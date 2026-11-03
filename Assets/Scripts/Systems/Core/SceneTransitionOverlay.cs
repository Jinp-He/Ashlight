using System;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace Ashlight.Systems.Core
{
    /// <summary>
    /// Persistent loading transition. It keeps the YAYA and BlackCurtain frame sequences
    /// independent so scene loading can continue while the curtain holds on its last frame.
    /// </summary>
    public sealed class SceneTransitionOverlay : MonoBehaviour
    {
        private const float YayaFrameDuration = 1f / 24f;
        private const float YayaFadeDuration = 0.4f;
        private const float CurtainFrameDuration = 0.1f;
        private const string YayaResourcePath = "Cutscene/Anim_YAYA";
        private const string CurtainResourcePath = "Cutscene/Anim_BlackCurtain";

        private static readonly Regex FrameNumberPattern = new Regex(@"(\d+)$", RegexOptions.Compiled);
        private static SceneTransitionOverlay _instance;

        private CanvasGroup _canvasGroup;
        private Image _yaya;
        private Image _blackCurtain;
        private Sprite[] _yayaFrames;
        private Sprite[] _blackCurtainFrames;
        private bool _isYayaLooping;
        private float _yayaLoopElapsed;

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

        /// <summary>Plays YAYA's fade-in and the curtain frames 01 → 05, then holds.</summary>
        public IEnumerator FadeToBlack()
        {
            if (!HasFrames()) yield break;

            _canvasGroup.alpha = 1f;
            _canvasGroup.blocksRaycasts = true;
            yield return PlayOpening();
            _isYayaLooping = true;
            _yayaLoopElapsed = 0f;
        }

        /// <summary>Plays YAYA's fade-out and the curtain frames 05 → 01, then hides.</summary>
        public IEnumerator FadeFromBlack()
        {
            if (!HasFrames()) yield break;

            _isYayaLooping = false;
            yield return PlayClosing();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
        }

        private void Update()
        {
            if (!_isYayaLooping || !HasFrames()) return;

            _yayaLoopElapsed += Time.unscaledDeltaTime;
            int frame = Mathf.FloorToInt(_yayaLoopElapsed / YayaFrameDuration) % _yayaFrames.Length;
            _yaya.sprite = _yayaFrames[frame];
        }

        private void CreateUi()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;

            LoadFrames();
            _blackCurtain = CreateImage("BlackCurtain", transform);
            _blackCurtain.preserveAspect = false;
            _blackCurtain.raycastTarget = true;
            Stretch(_blackCurtain.rectTransform);

            // The curtain's final frame is fully opaque. YAYA must therefore be created
            // after it so the loading indicator remains visible during the hold phase.
            _yaya = CreateImage("YAYA", transform);
            _yaya.preserveAspect = true;
            _yaya.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _yaya.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _yaya.rectTransform.anchoredPosition = Vector2.zero;
            _yaya.rectTransform.sizeDelta = new Vector2(186f, 220f);
            _yaya.color = Color.clear;

            if (HasFrames())
            {
                _yaya.sprite = _yayaFrames[0];
                _blackCurtain.sprite = _blackCurtainFrames[0];
            }
        }

        private IEnumerator PlayOpening()
        {
            float elapsed = 0f;
            int yayaFrame = 0;
            _blackCurtain.sprite = _blackCurtainFrames[0];

            while (elapsed < CurtainSequenceDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                _yaya.sprite = NextYayaFrame(ref yayaFrame, elapsed);
                _yaya.color = new Color(1f, 1f, 1f, Mathf.Clamp01(elapsed / YayaFadeDuration));
                _blackCurtain.sprite = _blackCurtainFrames[FrameAt(elapsed, false)];
                yield return null;
            }

            _yaya.color = Color.white;
            _blackCurtain.sprite = _blackCurtainFrames[_blackCurtainFrames.Length - 1];
        }

        private IEnumerator PlayClosing()
        {
            float elapsed = 0f;
            int yayaFrame = 0;
            _blackCurtain.sprite = _blackCurtainFrames[_blackCurtainFrames.Length - 1];

            while (elapsed < CurtainSequenceDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                _yaya.sprite = NextYayaFrame(ref yayaFrame, elapsed);
                _yaya.color = new Color(1f, 1f, 1f, 1f - Mathf.Clamp01(elapsed / YayaFadeDuration));
                _blackCurtain.sprite = _blackCurtainFrames[FrameAt(elapsed, true)];
                yield return null;
            }

            _yaya.color = Color.clear;
            _blackCurtain.sprite = _blackCurtainFrames[0];
        }

        private Sprite NextYayaFrame(ref int frame, float elapsed)
        {
            frame = Mathf.FloorToInt(elapsed / YayaFrameDuration) % _yayaFrames.Length;
            return _yayaFrames[frame];
        }

        private int FrameAt(float elapsed, bool reverse)
        {
            int frame = Mathf.Min(Mathf.FloorToInt(elapsed / CurtainFrameDuration), _blackCurtainFrames.Length - 1);
            return reverse ? _blackCurtainFrames.Length - 1 - frame : frame;
        }

        private float CurtainSequenceDuration => _blackCurtainFrames.Length * CurtainFrameDuration;

        private void LoadFrames()
        {
            _yayaFrames = Resources.LoadAll<Sprite>(YayaResourcePath);
            _blackCurtainFrames = Resources.LoadAll<Sprite>(CurtainResourcePath);
            SortFrames(_yayaFrames);
            SortFrames(_blackCurtainFrames);

            if (!HasFrames())
                Debug.LogError("[SceneTransitionOverlay] Cutscene frames could not be loaded from Resources/Cutscene.", this);
        }

        private bool HasFrames()
        {
            return _yayaFrames != null && _yayaFrames.Length > 0 &&
                   _blackCurtainFrames != null && _blackCurtainFrames.Length > 0;
        }

        private static void SortFrames(Sprite[] frames)
        {
            Array.Sort(frames, (left, right) => GetFrameNumber(left.name).CompareTo(GetFrameNumber(right.name)));
        }

        private static int GetFrameNumber(string frameName)
        {
            Match match = FrameNumberPattern.Match(frameName);
            return match.Success ? int.Parse(match.Value) : 0;
        }

        private static Image CreateImage(string name, Transform parent)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(Image));
            child.transform.SetParent(parent, false);
            var image = child.GetComponent<Image>();
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
    }
}
