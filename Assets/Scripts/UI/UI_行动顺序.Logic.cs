using UnityEngine;
using UnityEngine.EventSystems;
using cfg.Enemy;
using Ashlight.Config;

namespace Scripts.UI
{
    /// <summary>
    /// UI_ActionOrder 单张卡片的业务逻辑
    ///
    /// Prefab 子物体结构：
    ///   Img_highlight   — 高亮金框（激活态显示）
    ///   Img_BlackLayer  — 暗色背景
    ///   UnitsPiece      — 角色图片容器
    ///     Img_DogKnight / Img_Irene / Img_Rocket / Img_Zhouzhou / ...
    ///   Img_Attack      — 攻击图标
    ///
    /// 敌人进入执行轨后，hover 本卡会弹出「即将执行技能」的说明（复用 DescriptionViewController，与 IntentionView 同款）。
    /// </summary>
    public partial class UI_行动顺序 : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        // 当前执行技能（仅敌人进入执行轨时有值）；共享 tooltip 由 TurnOrderView 注入
        private EnemySkillInfo _executingSkill;
        private DescriptionViewController _tooltip;
        // 天气卡：本卡当前承载的天气（hover 显示天气说明）；与 _executingSkill 互斥
        private cfg.WeatherInfo _weatherInfo;
        // hover 联动：本卡承载的单位 + 通知回调（TurnOrderView 注入）——用于点亮战场上对应单位的选中标记
        private string _hoverUnitId;
        private System.Action<string, bool> _onHoverUnit;

        #region Unity 生命周期

        private void Awake()
        {
            InitUIBindings();

            // 初始状态：关闭高亮和攻击图标
            SetHighlight(false);
            SetAttacking(false);

            EnsureRaycastTarget();
        }

        #endregion

        #region 公共 API

        /// <summary>
        /// 根据 configId 显示对应的单位图片。
        /// 统一从 Resources 加载「行动顺序单位图标」(UI/ActionOrder/Img_{configId})，套到复用的 Img_Dynamic 上；
        /// 找不到时回退到旧的角色/敌人图标路径。prefab 里预置的命名子图一律隐藏（改为 Resources 驱动）。
        /// </summary>
        public void Setup(string configId, bool isPlayer)
        {
            if (UnitsPiece == null)
            {
                Debug.LogWarning("[UI_行动顺序] UnitsPiece 未绑定");
                return;
            }

            // 隐藏所有子图（含 prefab 预置命名子图与动态图），改由 Resources 图标驱动
            foreach (Transform child in UnitsPiece.transform)
                child.gameObject.SetActive(false);

            var dyn = EnsureDynamicImage();
            if (dyn == null)
            {
                Debug.LogWarning($"[UI_行动顺序] 无法为 {configId} 创建动态图标");
                return;
            }

            // 敌人的美术按 AlternativePath 命名（多个 EnemyInfo 共用一套美术），与 Enemy.Logic.cs 保持一致；
            // 玩家没有该字段，直接用 configId。
            string iconId = ResolveArtId(configId, isPlayer);

            // 优先：行动顺序专用图标（Assets/Resources/UI/ActionOrder/Img_{iconId}）
            var sprite = Resources.Load<Sprite>(Ashlight.Common.Utils.AssetPath.GetActionOrderIconPath(iconId));
            // 回退：角色/敌人通用图标——敌人的资源文件夹按美术名（AlternativePath）组织，先试美术名再试 configId
            if (sprite == null) sprite = LoadUnitIconSprite(iconId, isPlayer);
            if (sprite == null && iconId != configId) sprite = LoadUnitIconSprite(configId, isPlayer);

            if (sprite != null)
            {
                dyn.sprite = sprite;
                dyn.color = Color.white;
                dyn.gameObject.SetActive(true);
            }
            else
            {
                // 找不到任何图：绝不能带着池化复用残留的旧 sprite 激活（会显示成上一个单位），
                // 降级为显示单位名文字（与天气/引线缺图时的处理一致）。
                Debug.LogWarning($"[UI_行动顺序] 未找到单位图标: UI/ActionOrder/Img_{iconId}（configId={configId}, isPlayer={isPlayer}），降级为文字");
                dyn.sprite = null;
                dyn.gameObject.SetActive(false);
                var txt = EnsureDynamicText();
                if (txt != null)
                {
                    txt.text = ResolveDisplayName(configId, isPlayer);
                    txt.gameObject.SetActive(true);
                }
            }
        }

        /// <summary>兼容旧调用：默认按敌人处理（保留以防外部旧引用）。</summary>
        public void Setup(string configId) => Setup(configId, false);

        /// <summary>
        /// 按天气渲染本卡（天气虚拟单位的行动格）：图标走天气表 IconPath；
        /// 缺图降级为显示天气名文字（素材确认后再补图，见 docs/天气系统设计_v1.md）。
        /// </summary>
        public void SetupWeather(cfg.WeatherInfo weather)
        {
            if (UnitsPiece == null || weather == null)
            {
                return;
            }

            foreach (Transform child in UnitsPiece.transform)
                child.gameObject.SetActive(false);

            Sprite sprite = string.IsNullOrEmpty(weather.IconPath)
                ? null
                : Resources.Load<Sprite>(weather.IconPath.Replace('\\', '/'));

            if (sprite != null)
            {
                var dyn = EnsureDynamicImage();
                if (dyn == null) return;
                dyn.sprite = sprite;
                dyn.color = Color.white;
                dyn.gameObject.SetActive(true);
            }
            else
            {
                var txt = EnsureDynamicText();
                if (txt == null) return;
                txt.text = weather.Name;
                txt.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// 按在轨引线渲染本卡（玩家执行牌的结算格）：图标用卡牌 MiniSprite；
        /// 缺图降级为显示卡牌名文字。hover 说明暂无（后续可加卡牌 tooltip）。
        /// </summary>
        public void SetupCast(cfg.Character.CardInfo card)
        {
            if (UnitsPiece == null || card == null)
            {
                return;
            }

            foreach (Transform child in UnitsPiece.transform)
                child.gameObject.SetActive(false);

            var sprite = Resources.Load<Sprite>(
                Ashlight.Common.Utils.AssetPath.GetCardMiniSpriteAssetPath(card.Id).Replace('\\', '/'));

            if (sprite != null)
            {
                var dyn = EnsureDynamicImage();
                if (dyn == null) return;
                dyn.sprite = sprite;
                dyn.color = Color.white;
                dyn.gameObject.SetActive(true);
            }
            else
            {
                var txt = EnsureDynamicText();
                if (txt == null) return;
                txt.text = card.Name;
                txt.gameObject.SetActive(true);
            }
        }

        /// <summary>在 UnitsPiece 下懒创建（或复用）一个铺满容器的动态文字（天气卡缺图降级用）。</summary>
        private TMPro.TextMeshProUGUI EnsureDynamicText()
        {
            var existing = UnitsPiece.transform.Find("Txt_Dynamic");
            if (existing != null) return existing.GetComponent<TMPro.TextMeshProUGUI>();

            var go = new GameObject("Txt_Dynamic", typeof(RectTransform));
            go.transform.SetParent(UnitsPiece.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var txt = go.AddComponent<TMPro.TextMeshProUGUI>();
            txt.richText = true;
            txt.raycastTarget = false;
            txt.alignment = TMPro.TextAlignmentOptions.Center;
            txt.enableAutoSizing = true;
            txt.fontSizeMin = 8f;
            txt.fontSizeMax = 26f;
            return txt;
        }

        /// <summary>在 UnitsPiece 下懒创建（或复用）一个铺满容器的动态图标 Image。</summary>
        private UnityEngine.UI.Image EnsureDynamicImage()
        {
            var existing = UnitsPiece.transform.Find("Img_Dynamic");
            if (existing != null) return existing.GetComponent<UnityEngine.UI.Image>();

            var go = new GameObject("Img_Dynamic", typeof(RectTransform));
            go.transform.SetParent(UnitsPiece.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            return img;
        }

        /// <summary>
        /// 解析用于拼图标名的美术 Id：敌人优先取 AlternativePath（多个 EnemyInfo 共用一套美术，
        /// 与 Enemy.Logic.cs 的骨骼加载逻辑一致）；玩家无该字段，直接返回 configId。
        /// </summary>
        private static string ResolveArtId(string configId, bool isPlayer)
        {
            if (isPlayer) return configId;

            var info = ConfigLoader.Tables?.TbEnemyInfo?.GetOrDefault(configId);
            if (info != null && !string.IsNullOrEmpty(info.AlternativePath))
                return info.AlternativePath;
            return configId;
        }

        /// <summary>缺图降级文字：敌人取表里的 Name，玩家/查不到时用 configId。</summary>
        private static string ResolveDisplayName(string configId, bool isPlayer)
        {
            if (!isPlayer)
            {
                var info = ConfigLoader.Tables?.TbEnemyInfo?.GetOrDefault(configId);
                if (info != null && !string.IsNullOrEmpty(info.Name)) return info.Name;
            }
            return configId;
        }

        /// <summary>回退图标：按玩家/敌人取通用 Icon（Resources 路径统一转正斜杠）。</summary>
        private static Sprite LoadUnitIconSprite(string configId, bool isPlayer)
        {
            string path = isPlayer
                ? Ashlight.Common.Utils.AssetPath.GetCharacterIconAssetPath(configId)
                : Ashlight.Common.Utils.AssetPath.GetEnemyIconAssetPath(configId);
            if (string.IsNullOrEmpty(path)) return null;
            return Resources.Load<Sprite>(path.Replace('\\', '/'));
        }

        /// <summary>
        /// 打开/关闭高亮金框（当前行动单位）
        /// </summary>
        public void SetHighlight(bool active)
        {
            if (Img_highlight != null)
                Img_highlight.gameObject.SetActive(active);
        }

        /// <summary>
        /// 显示/隐藏攻击图标（单位进入执行轨时显示）
        /// </summary>
        public void SetAttacking(bool attacking)
        {
            if (Img_Attack != null)
                Img_Attack.gameObject.SetActive(attacking);
        }

        /// <summary>
        /// 设置"即将执行技能"与共享 tooltip。skill 为 null 表示清除（退出执行轨）。
        /// 仅在「有 → 无」的转变时收起 tooltip：RefreshOrder 每帧对所有无意图卡传 null，
        /// 若无条件 Hide 会把正在 hover 的共享 tooltip 每帧打灭（天气/技能说明都会闪没）。
        /// </summary>
        public void SetExecutingSkill(EnemySkillInfo skill, DescriptionViewController tooltip)
        {
            bool hadSkill = _executingSkill != null;
            _executingSkill = skill;
            _tooltip = tooltip;
            if (skill == null && hadSkill && _tooltip != null)
                _tooltip.Hide();
        }

        /// <summary>
        /// 设置本卡承载的天气（天气卡 hover 显示天气说明）。weather 为 null 表示清除（卡槽复用回普通单位卡）。
        /// </summary>
        public void SetWeatherInfo(cfg.WeatherInfo weather, DescriptionViewController tooltip)
        {
            _weatherInfo = weather;
            if (weather != null)
                _tooltip = tooltip;
        }

        /// <summary>
        /// 设置本卡承载的单位与 hover 通知回调（天气/引线卡传 null unitId）。
        /// hover 进入/离开时回调 (unitId, hovering)，由 UI_BattleScene 联动战场选中标记。
        /// </summary>
        public void SetHoverUnit(string unitId, System.Action<string, bool> callback)
        {
            _hoverUnitId = unitId;
            _onHoverUnit = callback;
        }

        #endregion

        #region Tooltip（悬停显示执行技能说明）

        public void OnPointerEnter(PointerEventData eventData)
        {
            // 战场联动：不管有没有 tooltip 都要点亮对应单位的选中标记
            if (!string.IsNullOrEmpty(_hoverUnitId))
                _onHoverUnit?.Invoke(_hoverUnitId, true);

            if (_tooltip == null) return;

            // 天气卡：hover 显示天气说明（与技能说明互斥，天气优先）。
            if (_weatherInfo != null)
            {
                _tooltip.Show(_weatherInfo);
                PositionTooltip(eventData);
                return;
            }

            if (_executingSkill == null) return;
            _tooltip.Show(_executingSkill);
            PositionTooltip(eventData);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!string.IsNullOrEmpty(_hoverUnitId))
                _onHoverUnit?.Invoke(_hoverUnitId, false);

            if (_tooltip != null) _tooltip.Hide();
        }

        /// <summary>把 tooltip 放到鼠标右侧（复刻 IntentionView 的定位逻辑，兼容 Overlay / Camera 画布）。</summary>
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

        /// <summary>确保卡根有可接收射线的 Graphic，否则收不到 hover 事件。</summary>
        private void EnsureRaycastTarget()
        {
            if (GetComponent<UnityEngine.UI.Graphic>() != null) return;
            var img = gameObject.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0, 0, 0, 0); // 透明，仅用于接收 hover
            img.raycastTarget = true;
        }

        #endregion
    }
}
