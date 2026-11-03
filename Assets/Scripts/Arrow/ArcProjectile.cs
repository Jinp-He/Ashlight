using UnityEngine;
using UnityEngine.UI;

namespace Scripts.UI
{
    /// <summary>
    /// UI 弧线箭头动效。
    /// 箭头头部沿二次贝塞尔曲线移动；拖尾由 ParticleSystem 负责生命周期、颜色和尺寸衰减，
    /// 再通过 Graphic 网格绘制，以兼容 Screen Space - Overlay Canvas。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer), typeof(ParticleSystem))]
    public sealed class ArcProjectile : MaskableGraphic
    {
        [Header("箭头")]
        [Range(6f, 40f)] public float arrowSize = 13f;
        [Range(0.3f, 3f)] public float flightSpeed = 0.5f;
        [Range(0f, 1.5f)] public float fadeOutTime = 0.25f;
        public Color arrowColor = new Color(1f, 0.12f, 0.08f, 1f);

        [Header("弧线")]
        [Range(0f, 600f)] public float arcHeight = 200f;
        [Range(-1f, 1f)] public float arcDir = -0.7f;

        [Header("粒子拖尾")]
        [Range(0, 100)] public int trailLength = 18;
        [Range(0.05f, 1.5f)] public float trailLife = 0.3f;
        [Range(1f, 24f)] public float trailWidth = 8f;
        [Range(0f, 60f)] public float glow = 5f;
        [Range(15f, 120f)] public float trailEmissionRate = 60f;
        public bool trailTaper = true;

        private ParticleSystem _particles;
        private ParticleSystem.Particle[] _particleBuffer = new ParticleSystem.Particle[128];
        private Texture2D _softParticleTexture;

        private Vector2 _startLocal;
        private Vector2 _endLocal;
        private Vector2 _currentLocal;
        private Vector2 _lastEmitLocal;
        private float _currentAngle;
        private float _flightProgress;
        private float _fadeTimer;
        private float _arrowAlpha;
        private float _emissionAccumulator;
        private bool _isFlying;
        private bool _isFading;
        private bool _wasVisibleLastFrame;

        public bool IsPlaying => _isFlying || _isFading || (_particles != null && _particles.particleCount > 0);
        public override Texture mainTexture => _softParticleTexture != null ? _softParticleTexture : s_WhiteTexture;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            CreateSoftParticleTexture();
            ConfigureParticleSystem();
            canvasRenderer.cullTransparentMesh = true;
        }

        protected override void OnDestroy()
        {
            if (_softParticleTexture != null)
            {
                Destroy(_softParticleTexture);
                _softParticleTexture = null;
            }
            base.OnDestroy();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            UpdateArrow(dt);

            bool isVisible = IsPlaying;
            if (isVisible || _wasVisibleLastFrame)
                SetVerticesDirty();
            _wasVisibleLastFrame = isVisible;
        }

        /// <summary>使用屏幕坐标发射，自动兼容 Overlay / Camera Canvas。</summary>
        public bool Fire(Vector2 startScreen, Vector2 endScreen, Canvas targetCanvas)
        {
            if (targetCanvas == null) return false;

            Camera eventCamera = targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : targetCanvas.worldCamera;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, startScreen, eventCamera, out _startLocal) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, endScreen, eventCamera, out _endLocal))
            {
                return false;
            }

            FireLocal(_startLocal, _endLocal);
            return true;
        }

        /// <summary>使用当前 Graphic 的本地坐标发射。</summary>
        public void FireLocal(Vector2 startLocal, Vector2 endLocal)
        {
            EnsureParticleCapacity();
            _particles.Clear(true);

            _startLocal = startLocal;
            _endLocal = endLocal;
            Vector3 start = SampleCurve(0f);
            _currentLocal = new Vector2(start.x, start.y);
            _lastEmitLocal = _currentLocal;
            _currentAngle = start.z;
            _flightProgress = 0f;
            _fadeTimer = fadeOutTime;
            _arrowAlpha = 1f;
            _emissionAccumulator = 0f;
            _isFlying = true;
            _isFading = false;
            _wasVisibleLastFrame = true;

            if (trailLength > 0)
                EmitTrailParticle(_currentLocal);
            SetVerticesDirty();
        }

        public void StopAndClear()
        {
            _isFlying = false;
            _isFading = false;
            _arrowAlpha = 0f;
            _wasVisibleLastFrame = false;
            if (_particles != null) _particles.Clear(true);
            SetVerticesDirty();
        }

        private void UpdateArrow(float dt)
        {
            if (_isFlying)
            {
                _flightProgress = Mathf.Min(1f, _flightProgress + dt / Mathf.Max(0.0001f, flightSpeed));
                Vector3 sampled = SampleCurve(_flightProgress);
                Vector2 nextPosition = new Vector2(sampled.x, sampled.y);
                _currentAngle = sampled.z;
                EmitBetween(_lastEmitLocal, nextPosition, dt);
                _currentLocal = nextPosition;
                _lastEmitLocal = nextPosition;

                if (_flightProgress >= 1f)
                {
                    _isFlying = false;
                    _isFading = fadeOutTime > 0f;
                    _fadeTimer = fadeOutTime;
                    if (!_isFading) _arrowAlpha = 0f;
                }
            }
            else if (_isFading)
            {
                _fadeTimer -= dt;
                _arrowAlpha = Mathf.Clamp01(_fadeTimer / Mathf.Max(0.0001f, fadeOutTime));
                if (_fadeTimer <= 0f)
                {
                    _isFading = false;
                    _arrowAlpha = 0f;
                }
            }
        }

        private void EmitBetween(Vector2 from, Vector2 to, float dt)
        {
            if (trailLength <= 0 || trailEmissionRate <= 0f) return;

            _emissionAccumulator += dt * trailEmissionRate;
            int count = Mathf.FloorToInt(_emissionAccumulator);
            if (count <= 0) return;
            _emissionAccumulator -= count;

            count = Mathf.Min(count, 8);
            for (int i = 1; i <= count; i++)
            {
                EmitTrailParticle(Vector2.Lerp(from, to, i / (float)count));
            }
        }

        private void EmitTrailParticle(Vector2 position)
        {
            var emit = new ParticleSystem.EmitParams
            {
                position = position,
                startLifetime = Mathf.Max(0.01f, trailLife),
                startSize = Mathf.Max(0.1f, trailWidth),
                startColor = arrowColor,
                velocity = Vector3.zero
            };
            _particles.Emit(emit, 1);
        }

        private Vector2 GetControlPoint()
        {
            Vector2 delta = _endLocal - _startLocal;
            float length = Mathf.Max(0.0001f, delta.magnitude);
            Vector2 normal = new Vector2(-delta.y / length, delta.x / length);
            return (_startLocal + _endLocal) * 0.5f + normal * arcHeight * arcDir;
        }

        /// <summary>返回 x/y 与切线角度（弧度）。</summary>
        private Vector3 SampleCurve(float t)
        {
            Vector2 control = GetControlPoint();
            float u = 1f - t;
            Vector2 position = u * u * _startLocal + 2f * u * t * control + t * t * _endLocal;
            Vector2 tangent = 2f * u * (control - _startLocal) + 2f * t * (_endLocal - control);
            return new Vector3(position.x, position.y, Mathf.Atan2(tangent.y, tangent.x));
        }

        private void ConfigureParticleSystem()
        {
            _particles = GetComponent<ParticleSystem>();
            var main = _particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = Mathf.Max(32, trailLength * 3);
            main.startSpeed = 0f;
            main.startLifetime = trailLife;
            main.startSize = trailWidth;
            main.startColor = arrowColor;

            var emission = _particles.emission;
            emission.enabled = false;
            var shape = _particles.shape;
            shape.enabled = false;

            var colorOverLifetime = _particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var alphaGradient = new Gradient();
            alphaGradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(alphaGradient);

            var sizeOverLifetime = _particles.sizeOverLifetime;
            sizeOverLifetime.enabled = trailTaper;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0.08f)));

            var renderer = _particles.GetComponent<ParticleSystemRenderer>();
            if (renderer != null) renderer.enabled = false;

            _particles.Play(false);
        }

        private void EnsureParticleCapacity()
        {
            int required = Mathf.Max(32, trailLength * 3);
            var main = _particles.main;
            main.maxParticles = required;
            main.startLifetime = trailLife;
            main.startSize = trailWidth;
            main.startColor = arrowColor;

            var sizeOverLifetime = _particles.sizeOverLifetime;
            sizeOverLifetime.enabled = trailTaper;

            if (_particleBuffer.Length < required)
                _particleBuffer = new ParticleSystem.Particle[required];
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_particles == null) return;

            int particleCount = _particles.GetParticles(_particleBuffer);
            SortParticlesFromTailToHead(particleCount);
            if (particleCount >= 2)
            {
                AddTrailStrip(vh, particleCount, glow * 2f, 0.22f);
                AddTrailStrip(vh, particleCount, 0f, 1f);
            }

            if (_arrowAlpha > 0f)
                AddArrow(vh, _currentLocal, _currentAngle, arrowSize, arrowColor * _arrowAlpha);
        }

        private static Color32 WithAlpha(Color32 color, float multiplier)
        {
            color.a = (byte)Mathf.Clamp(Mathf.RoundToInt(color.a * multiplier), 0, 255);
            return color;
        }

        /// <summary>ParticleSystem 返回顺序不保证稳定；按剩余寿命从旧到新排序后才能连接成连续拖尾。</summary>
        private void SortParticlesFromTailToHead(int count)
        {
            for (int i = 1; i < count; i++)
            {
                ParticleSystem.Particle current = _particleBuffer[i];
                int previous = i - 1;
                while (previous >= 0 && _particleBuffer[previous].remainingLifetime > current.remainingLifetime)
                {
                    _particleBuffer[previous + 1] = _particleBuffer[previous];
                    previous--;
                }
                _particleBuffer[previous + 1] = current;
            }
        }

        /// <summary>把历史粒子连接为一条带状网格；粒子仍负责每个节点的寿命、宽度和透明度。</summary>
        private void AddTrailStrip(VertexHelper vh, int particleCount, float extraWidth, float alphaMultiplier)
        {
            int firstVertex = vh.currentVertCount;
            Vector2 fallbackDirection = (_endLocal - _startLocal).normalized;
            if (fallbackDirection.sqrMagnitude < 0.0001f) fallbackDirection = Vector2.right;

            for (int i = 0; i < particleCount; i++)
            {
                Vector2 position = _particleBuffer[i].position;
                Vector2 previous = i > 0 ? (Vector2)_particleBuffer[i - 1].position : position;
                Vector2 next = i < particleCount - 1 ? (Vector2)_particleBuffer[i + 1].position : position;
                Vector2 tangent = (next - previous).normalized;
                if (tangent.sqrMagnitude < 0.0001f) tangent = fallbackDirection;
                Vector2 normal = new Vector2(-tangent.y, tangent.x);

                float width = Mathf.Max(0.1f, _particleBuffer[i].GetCurrentSize(_particles) + extraWidth);
                Color32 color = WithAlpha(_particleBuffer[i].GetCurrentColor(_particles), alphaMultiplier);
                Vector2 offset = normal * (width * 0.5f);

                // 复用软圆纹理的中轴切片：横向中心最亮、两侧透明，形成柔边连续线。
                vh.AddVert(position + offset, color, new Vector2(0.5f, 1f));
                vh.AddVert(position - offset, color, new Vector2(0.5f, 0f));
            }

            for (int i = 0; i < particleCount - 1; i++)
            {
                int currentTop = firstVertex + i * 2;
                int currentBottom = currentTop + 1;
                int nextTop = currentTop + 2;
                int nextBottom = currentTop + 3;
                vh.AddTriangle(currentTop, currentBottom, nextTop);
                vh.AddTriangle(nextTop, currentBottom, nextBottom);
            }
        }

        private static void AddArrow(VertexHelper vh, Vector2 center, float angle, float size, Color color)
        {
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);
            Vector2 tip = TransformArrowPoint(new Vector2(size * 1.4f, 0f));
            Vector2 top = TransformArrowPoint(new Vector2(-size * 0.65f, size * 0.62f));
            Vector2 notch = TransformArrowPoint(new Vector2(-size * 0.18f, 0f));
            Vector2 bottom = TransformArrowPoint(new Vector2(-size * 0.65f, -size * 0.62f));
            Color32 color32 = color;

            int index = vh.currentVertCount;
            Vector2 solidUv = new Vector2(0.5f, 0.5f);
            vh.AddVert(tip, color32, solidUv);
            vh.AddVert(top, color32, solidUv);
            vh.AddVert(notch, color32, solidUv);
            vh.AddVert(bottom, color32, solidUv);
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);

            Vector2 TransformArrowPoint(Vector2 local)
            {
                return center + new Vector2(local.x * cos - local.y * sin, local.x * sin + local.y * cos);
            }
        }

        private void CreateSoftParticleTexture()
        {
            const int textureSize = 32;
            _softParticleTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
            {
                name = "ArcProjectile_SoftParticle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[textureSize * textureSize];
            Vector2 center = new Vector2((textureSize - 1) * 0.5f, (textureSize - 1) * 0.5f);
            float radius = textureSize * 0.5f;
            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    float normalized = Vector2.Distance(new Vector2(x, y), center) / radius;
                    float alpha = Mathf.Pow(Mathf.Clamp01(1f - normalized), 1.8f);
                    pixels[y * textureSize + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            _softParticleTexture.SetPixels32(pixels);
            _softParticleTexture.Apply(false, true);
        }
    }
}
