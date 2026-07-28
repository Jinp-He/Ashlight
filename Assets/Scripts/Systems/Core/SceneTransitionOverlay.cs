using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Ashlight.Systems.Core
{
    /// <summary>Persistent spiral-black transition used while gameplay scenes are swapped.</summary>
    public sealed class SceneTransitionOverlay : MonoBehaviour
    {
        private const float TransitionDuration = 0.38f;
        private static SceneTransitionOverlay _instance;

        private CanvasGroup _canvasGroup;
        private Image _blackScreen;
        private RawImage _spiral;

        public static SceneTransitionOverlay GetOrCreate()
        {
            if (_instance != null) return _instance;
            var root = new GameObject("SceneTransitionOverlay");
            _instance = root.AddComponent<SceneTransitionOverlay>();
            return _instance;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            CreateUi();
        }

        public IEnumerator FadeToBlack()
        {
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.alpha = 1f;
            yield return Animate(0f, 1f, false);
        }

        public IEnumerator FadeFromBlack()
        {
            yield return Animate(1f, 0f, true);
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
        }

        private void CreateUi()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            gameObject.AddComponent<CanvasScaler>();
            gameObject.AddComponent<GraphicRaycaster>();
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;

            _blackScreen = CreateImage("BlackScreen", transform, Color.black);
            Stretch(_blackScreen.rectTransform);

            _spiral = CreateRawImage("SpiralMask", transform, Color.black);
            _spiral.texture = CreateSpiralTexture(256);
            Stretch(_spiral.rectTransform);
        }

        private IEnumerator Animate(float from, float to, bool opening)
        {
            float elapsed = 0f;
            while (elapsed < TransitionDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / TransitionDuration);
                SetProgress(Mathf.Lerp(from, to, progress), opening);
                yield return null;
            }
            SetProgress(to, opening);
        }

        private void SetProgress(float progress, bool opening)
        {
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            _blackScreen.color = new Color(0f, 0f, 0f, eased);
            float scale = opening ? Mathf.Lerp(2.4f, 0.15f, eased) : Mathf.Lerp(0.15f, 2.4f, eased);
            _spiral.rectTransform.localScale = Vector3.one * scale;
            _spiral.rectTransform.localRotation = Quaternion.Euler(0f, 0f, (opening ? 1f : -1f) * eased * 140f);
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(Image));
            child.transform.SetParent(parent, false);
            var image = child.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static RawImage CreateRawImage(string name, Transform parent, Color color)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            child.transform.SetParent(parent, false);
            var image = child.GetComponent<RawImage>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Texture2D CreateSpiralTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Atan2(dy, dx);
                    float wave = Mathf.Sin(angle * 5f + radius * 18f);
                    byte alpha = radius <= 1f && wave > -0.18f ? (byte)255 : (byte)0;
                    pixels[y * size + x] = new Color32(0, 0, 0, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
