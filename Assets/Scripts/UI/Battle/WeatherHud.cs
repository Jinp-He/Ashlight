using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Scripts.UI
{
    /// <summary>
    /// 天气 HUD：常驻角标（画布左上角，hover 弹天气说明）+ 开场横幅（淡入→停留→淡出，一次性预告）。
    /// v1 全部代码构建、不依赖 prefab——素材/美术确认后可整体替换。见 docs/天气系统设计_v1.md。
    /// </summary>
    public class WeatherHud : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private cfg.WeatherInfo _weather;
        private DescriptionViewController _tooltip;

        /// <summary>
        /// 在画布上创建常驻天气角标并播一次开场横幅。canvas / weather 为 null 时返回 null。
        /// </summary>
        public static WeatherHud Create(Canvas canvas, cfg.WeatherInfo weather)
        {
            if (canvas == null || weather == null) return null;

            var go = new GameObject("WeatherHud", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);

            var hud = go.AddComponent<WeatherHud>();
            hud._weather = weather;
            hud.BuildBadge(weather);
            hud.StartCoroutine(hud.BannerRoutine(weather, canvas));
            return hud;
        }

        // ────────────────────────────────────────────────────────────────
        #region 常驻角标

        /// <summary>角标本体：左上角 底板 + 天气图标（缺图省略）+ 天气名。根节点接收 hover。</summary>
        private void BuildBadge(cfg.WeatherInfo weather)
        {
            var rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(16f, -16f);
            rt.sizeDelta = new Vector2(160f, 42f);

            // 底板（同时是 hover 的射线接收体）
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);
            bg.raycastTarget = true;

            float textLeft = 10f;

            var sprite = LoadWeatherSprite(weather);
            if (sprite != null)
            {
                var iconGo = new GameObject("Img_Icon", typeof(RectTransform));
                iconGo.transform.SetParent(transform, false);
                var iconRt = (RectTransform)iconGo.transform;
                iconRt.anchorMin = new Vector2(0f, 0.5f);
                iconRt.anchorMax = new Vector2(0f, 0.5f);
                iconRt.pivot = new Vector2(0f, 0.5f);
                iconRt.anchoredPosition = new Vector2(6f, 0f);
                iconRt.sizeDelta = new Vector2(30f, 30f);

                var iconImg = iconGo.AddComponent<Image>();
                iconImg.sprite = sprite;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;

                textLeft = 42f;
            }

            var txtGo = new GameObject("Txt_Name", typeof(RectTransform));
            txtGo.transform.SetParent(transform, false);
            var txtRt = (RectTransform)txtGo.transform;
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = new Vector2(textLeft, 2f);
            txtRt.offsetMax = new Vector2(-6f, -2f);

            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            txt.richText = true;
            txt.text = weather.Name;
            txt.alignment = TextAlignmentOptions.MidlineLeft;
            txt.enableAutoSizing = true;
            txt.fontSizeMin = 12f;
            txt.fontSizeMax = 24f;
            txt.raycastTarget = false;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_weather == null) return;
            EnsureTooltip();
            if (_tooltip == null) return;

            _tooltip.Show(_weather);
            PositionTooltip(eventData);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_tooltip != null) _tooltip.Hide();
        }

        /// <summary>懒创建自己的说明面板实例（与 TurnOrderView 同款 prefab）。</summary>
        private void EnsureTooltip()
        {
            if (_tooltip != null) return;

            var prefab = Resources.Load<GameObject>("UI/常用UI/PageViewer/DescriptionViewController");
            if (prefab == null)
            {
                Debug.LogWarning("[WeatherHud] 未找到 DescriptionViewController prefab，角标无 hover 说明");
                return;
            }

            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            var obj = Instantiate(prefab, canvas.transform);
            _tooltip = obj.GetComponent<DescriptionViewController>();
            _tooltip?.Hide();
        }

        /// <summary>把 tooltip 放到鼠标右侧（复刻 UI_行动顺序 的定位逻辑，兼容 Overlay / Camera 画布）。</summary>
        private void PositionTooltip(PointerEventData eventData)
        {
            if (_tooltip == null) return;

            var canvas = _tooltip.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var canvasRect = canvas.transform as RectTransform;
            if (canvasRect == null) return;

            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : (eventData != null ? (eventData.pressEventCamera ?? eventData.enterEventCamera) : canvas.worldCamera);

            Vector2 screenPoint = (eventData != null ? eventData.position : (Vector2)Input.mousePosition)
                                  + new Vector2(120f, 0f);

            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(canvasRect, screenPoint, cam, out var world))
                _tooltip.SetPosition(world);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────
        #region 开场横幅

        /// <summary>开场横幅：画布中上方，淡入 0.3s → 停留 2.2s → 淡出 0.6s → 销毁。不阻塞操作。</summary>
        private IEnumerator BannerRoutine(cfg.WeatherInfo weather, Canvas canvas)
        {
            var go = new GameObject("WeatherBanner", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -130f);
            rt.sizeDelta = new Vector2(720f, 92f);

            var cg = go.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.blocksRaycasts = false;
            cg.interactable = false;

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.65f);
            bg.raycastTarget = false;

            var txtGo = new GameObject("Txt_Banner", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, false);
            var txtRt = (RectTransform)txtGo.transform;
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = new Vector2(16f, 6f);
            txtRt.offsetMax = new Vector2(-16f, -6f);

            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            txt.richText = true; // 横幅用 <size>/<b> 富文本标签，必须显式开启解析（运行时 new 的 TMP 不保证默认开）
            string desc = DescriptionViewController.FormatWeatherDescription(weather, colored: false);
            txt.text = $"<size=34><b>{weather.Name}</b></size>\n<size=20>{desc}</size>";
            txt.alignment = TextAlignmentOptions.Center;
            txt.raycastTarget = false;

            const float fadeIn = 0.3f, hold = 2.2f, fadeOut = 0.6f;

            for (float t = 0f; t < fadeIn; t += Time.deltaTime)
            {
                cg.alpha = Mathf.Clamp01(t / fadeIn);
                yield return null;
            }
            cg.alpha = 1f;

            yield return new WaitForSeconds(hold);

            for (float t = 0f; t < fadeOut; t += Time.deltaTime)
            {
                cg.alpha = 1f - Mathf.Clamp01(t / fadeOut);
                yield return null;
            }

            Destroy(go);
        }

        #endregion

        private static Sprite LoadWeatherSprite(cfg.WeatherInfo weather)
        {
            if (weather == null || string.IsNullOrEmpty(weather.IconPath)) return null;
            return Resources.Load<Sprite>(weather.IconPath.Replace('\\', '/'));
        }
    }
}
