using System.Collections.Generic;
using cfg;
using cfg.Enemy;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Scripts.UI
{
    /// <summary>
    /// 敌人意图指示物视图。
    /// 预制体层级（挂在本脚本的 GameObject 下）：
    ///   Img_IntentionBase            底图
    ///   Txt_figure                   数值文本（攻击伤害 / 护盾值；思考/状态态时隐藏）
    ///   IntentionIcon/
    ///     Img_Think                  思考图标
    ///     Img_State                  状态图标
    ///     Img_Shield                 护盾图标
    ///     Img_Attack                 攻击图标容器（按目标区切换近战/远程素材）
    ///   Coord/Img_Coord0             目标区标记容器（按单体/AOE切换素材）
    ///
    /// 颜色规范：
    ///   激活色  #9c660a
    ///
    /// **Coord 语义：**
    /// - 单体使用 Coord_Img_Monomer，AOE 使用 Coord_Img_Aoe
    /// - 前排为红色，后排为蓝色
    /// - Any 表示前后排同时生效，因此同时显示红、蓝两个标记
    ///
    /// 字段优先取 Inspector 拖入的引用；未拖入时按上述名字自动查找。
    /// </summary>
    public class IntentionView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        // ===== 颜色 =====
        // 注意：静态字段初始化阶段不能调用 ColorUtility.TryParseHtmlString，
        // 否则会抛 "DoTryParseHtmlColor is not allowed to be called from a MonoBehaviour constructor"。
        // 直接用 Color32 字面量。#9c660a / #3f4447
        private static readonly Color ActiveColor = new Color32(0x9c, 0x66, 0x0a, 0xff);
        private const string ResourceRoot = "UI/Intention/";
        private static readonly Dictionary<string, Sprite> RuntimeSprites = new Dictionary<string, Sprite>();

        // ===== Inspector 可拖入（可选，留空将按名字自动绑定）=====
        [Header("背景")]
        [SerializeField] private Image _imgBase;

        [Header("数值文本")]
        [SerializeField] private TextMeshProUGUI _txtFigure;

        [Header("图标 (IntentionIcon/*)")]
        [SerializeField] private Image _imgThink;
        [SerializeField] private Image _imgState;
        [SerializeField] private Image _imgShield;
        [SerializeField] private Image _imgAttack;

        [Header("坐标 (Coord/*) — 运行时复用旧节点显示新标记")]
        [Tooltip("Coord 容器，默认为子节点 'Coord'")]
        [SerializeField] private Transform _coordRoot;
        [Tooltip("目标区标记模板。留空时取 Coord/CoordPoints/Img_Coord0")]
        [SerializeField] private Image _coordTemplate;
        [Tooltip("旧坐标连接块；新方案始终隐藏")]
        [SerializeField] private Image _imgLinkPiece;

        // ===== 分区颜色（前排红 / 后排蓝） =====
        // 颜色数据来源(通道)：TbCustomColor（Luban 表 Id→hex）。读不到时用兜底常量。
        private const string FrontRowColorId = "FrontRow";
        private const string BackRowColorId  = "BackRow";
        private static readonly Color FrontRowFallback = new Color32(0xcc, 0x5a, 0x3c, 0xff); // 前排·红
        private static readonly Color BackRowFallback  = new Color32(0x3c, 0x8f, 0xcc, 0xff); // 后排·蓝

        [Header("Tooltip（敌人技能详情）")]
        [Tooltip("DescriptionViewController 预制体（与 CardViewController 用的是同一个）。鼠标悬停在意图上时弹出，显示当前 EnemySkillInfo 的详情")]
        [SerializeField] private GameObject _descriptionViewControllerPrefab;

        [Tooltip("Tooltip 相对鼠标位置的屏幕偏移（默认右侧 120px）")]
        [SerializeField] private Vector2 _tooltipMouseOffset = new Vector2(120f, 0f);

        [Tooltip("勾选时 tooltip 跟随鼠标位置出现；不勾选则锚定在 IntentionView 自身的右侧")]
        [SerializeField] private bool _tooltipFollowMouse = true;

        /// <summary>
        /// UnitId → 目标角色 UI Transform 的解析器（由 UI_BattleScene 在战斗初始化时注入）。
        /// 悬停意图时用它把「锁定目标 UnitId」解析为当前 UI 位置，画抛物线指向之。
        /// 角色移动后 Transform 仍有效，抛物线自动指向其新位置。
        /// </summary>
        public static System.Func<string, Transform> TargetTransformResolver;

        // 悬停时从意图指向锁定目标的抛物线（复用玩家出牌的 TargetArrowRenderer；红色示意来袭攻击）
        private TargetArrowRenderer _parabola;
        private Canvas _canvas;

        // 最多使用两个标记：Front/Back 各一个；Any 时二者同时显示。
        private readonly List<Image> _coordMarkers = new List<Image>();
        private float _coordMarkerY;

        private Sprite _meleeSprite;
        private Sprite _remoteSprite;
        private Sprite _coordMonomerSprite;
        private Sprite _coordAoeSprite;

        // tooltip 实例与当前对应的技能配置
        private DescriptionViewController _descriptionView;
        private EnemySkillInfo _currentSkillInfo;
        // 当前锁定目标 UnitId（供悬停抛物线指向；空区未锁人 / 思考态为 null）
        private string _currentTargetUnitId;

        // ===== 生命周期 =====

        private void Awake()
        {
            AutoBindIfMissing();
            ApplyNewArtwork();
            PrepareCoordMarkers();
            Hide();

            CreateDescriptionView();
        }

        private void OnDestroy()
        {
            if (_descriptionView != null)
            {
                Destroy(_descriptionView.gameObject);
                _descriptionView = null;
            }
            if (_parabola != null)
            {
                Destroy(_parabola.gameObject);
                _parabola = null;
            }
        }

        // ===== 悬停抛物线 =====

        /// <summary>懒创建一条填满 Canvas 的抛物线渲染器（不拦射线、初始隐藏）。</summary>
        private TargetArrowRenderer EnsureParabola()
        {
            if (_parabola != null) return _parabola;
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null) return null;

            var go = new GameObject("IntentionParabola");
            go.transform.SetParent(_canvas.transform, false);
            go.layer = _canvas.gameObject.layer;

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);

            _parabola = go.AddComponent<TargetArrowRenderer>();
            _parabola.raycastTarget = false;
            _parabola.Hide();
            return _parabola;
        }

        /// <summary>悬停时若已锁定目标，画一条抛物线从意图指向该目标当前 UI 位置。</summary>
        private void TryShowParabola()
        {
            if (string.IsNullOrEmpty(_currentTargetUnitId) || TargetTransformResolver == null) return;

            var targetT = TargetTransformResolver(_currentTargetUnitId);
            if (targetT == null) return;

            var arrow = EnsureParabola();
            if (arrow == null) return;

            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            Camera cam = (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? _canvas.worldCamera
                : null;

            Vector3 startScreen = RectTransformUtility.WorldToScreenPoint(cam, transform.position);
            Vector3 endScreen = RectTransformUtility.WorldToScreenPoint(cam, targetT.position);

            arrow.transform.SetAsLastSibling();      // 画在最上层
            arrow.UpdateLine(startScreen, endScreen, false); // false → 红色，示意来袭攻击
            arrow.Show();
        }

        private void HideParabola()
        {
            if (_parabola != null) _parabola.Hide();
        }

        private void CreateDescriptionView()
        {
            if (_descriptionViewControllerPrefab == null) return;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            var obj = Instantiate(_descriptionViewControllerPrefab, canvas.transform);
            _descriptionView = obj.GetComponent<DescriptionViewController>();
            if (_descriptionView != null)
            {
                _descriptionView.Hide();
            }
        }

        // ===== 公共 API =====

        /// <summary>隐藏整个意图指示物</summary>
        public void Hide()
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);

            _currentSkillInfo = null;
            _currentTargetUnitId = null;
            HideTooltip();
            HideParabola();
        }

        /// <summary>显示</summary>
        public void Show()
        {
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);
        }

        /// <summary>
        /// 复用 prefab 里的旧坐标点节点，整理成最多两个独立标记。
        /// 标记直接挂到 Coord 下并手动居中，不再受旧 HorizontalLayoutGroup 控制。
        /// </summary>
        private void PrepareCoordMarkers()
        {
            if (_coordTemplate == null || _coordRoot == null) return;

            var oldParent = _coordTemplate.transform.parent;
            if (oldParent != null)
            {
                var candidates = new List<Image>();
                foreach (Transform child in oldParent)
                {
                    if (child == null || !child.name.StartsWith("Img_Coord")) continue;
                    var image = child.GetComponent<Image>();
                    if (image != null) candidates.Add(image);
                }
                _coordMarkers.AddRange(candidates);
            }

            if (_coordMarkers.Count == 0)
                _coordMarkers.Add(_coordTemplate);

            _coordMarkerY = _coordRoot.InverseTransformPoint(_coordTemplate.transform.position).y;

            while (_coordMarkers.Count < 2)
            {
                var clone = Instantiate(_coordTemplate, _coordRoot);
                clone.name = $"Img_TargetZone{_coordMarkers.Count}";
                _coordMarkers.Add(clone);
            }

            for (int i = 0; i < _coordMarkers.Count; i++)
            {
                var marker = _coordMarkers[i];
                if (marker == null) continue;
                marker.transform.SetParent(_coordRoot, true);
                marker.raycastTarget = false;
                marker.gameObject.SetActive(false);
            }

            if (_imgLinkPiece != null)
                _imgLinkPiece.gameObject.SetActive(false);
        }

        /// <summary>思考中：只显示思考图标，无数值，整个 Coord 区域隐藏。思考态无技能详情可看</summary>
        public void ShowThinking()
        {
            Show();
            SetActiveIcon(_imgThink);
            SetFigure(null);
            SetCoordsVisible(false); // 思考阶段不展示目标位置

            _currentSkillInfo = null;
            _currentTargetUnitId = null;
            HideTooltip();
            HideParabola();
        }

        /// <summary>
        /// 根据敌人技能配置 + 目标信息自动显示意图。
        /// 含攻击效果 → Front/Any 使用 Melee，Back 使用 Remote，并显示累计伤害
        /// 含防御效果 → Shield 图标 + 累计护甲
        /// 其他 → State 图标，无数值
        /// </summary>
        /// <param name="skillInfo">技能配置</param>
        /// <param name="targetUnitId">当前锁定目标 UnitId（供悬停抛物线）</param>
        public void ShowFromSkill(EnemySkillInfo skillInfo, string targetUnitId = null)
        {
            if (skillInfo == null)
            {
                ShowThinking();
                return;
            }

            // 缓存当前技能 + 锁定目标，供 hover tooltip / 抛物线使用
            _currentSkillInfo = skillInfo;
            _currentTargetUnitId = targetUnitId;

            // 扫效果分类
            int attackDamage = 0;
            int shieldValue = 0;
            bool hasAttack = false;
            bool hasShield = false;
            bool isAoe = skillInfo.TargetType == TargetTypeEnum.AllEnemy
                         || skillInfo.TargetType == TargetTypeEnum.AllAlly;

            if (skillInfo.Effects != null)
            {
                foreach (var eff in skillInfo.Effects)
                {
                    switch (eff)
                    {
                        case AttackEffect atk:
                            attackDamage += atk.Damage;
                            hasAttack = true;
                            if (atk.IsAoe) isAoe = true;
                            break;
                        case AttackExtraEffect atkEx:
                            attackDamage += atkEx.Damage;
                            hasAttack = true;
                            break;
                        case AttackConditionalEffect atkCond:
                            attackDamage += atkCond.BonusDamage;
                            hasAttack = true;
                            break;
                        case DefenseEffect def:
                            shieldValue += def.Value;
                            hasShield = true;
                            break;
                        case InterceptEffect ic:
                            shieldValue += ic.ShieldValue;
                            hasShield = true;
                            break;
                    }
                }
            }

            bool targetsPlayers = skillInfo.TargetType == TargetTypeEnum.SingleEnemy
                                  || skillInfo.TargetType == TargetTypeEnum.AllEnemy;

            if (hasAttack)
                ShowAttack(attackDamage, skillInfo.TargetZone, isAoe, targetsPlayers);
            else if (hasShield)
                ShowShield(shieldValue);
            else
                ShowState();
        }

        public void ShowAttack(int damage, TargetZoneEnum targetZone, bool isAoe, bool targetsPlayers)
        {
            Show();
            ApplyImageSprite(_imgAttack, targetZone == TargetZoneEnum.Back ? _remoteSprite : _meleeSprite);
            SetActiveIcon(_imgAttack);
            SetFigure(damage.ToString());
            if (targetsPlayers)
                ShowTargetZone(targetZone, isAoe);
            else
                SetCoordsVisible(false);
        }

        public void ShowShield(int shieldValue)
        {
            Show();
            SetActiveIcon(_imgShield);
            SetFigure(shieldValue.ToString());
            SetCoordsVisible(false);
        }

        public void ShowState()
        {
            Show();
            SetActiveIcon(_imgState);
            SetFigure(null);
            SetCoordsVisible(false);
        }

        /// <summary>
        /// 单体/群体分别使用 Monomer/Aoe 素材；Front 红、Back 蓝；Any/Conditional 同时显示红蓝。
        /// </summary>
        private void ShowTargetZone(TargetZoneEnum targetZone, bool isAoe)
        {
            if (_coordMarkers.Count == 0) return;

            SetCoordsVisible(true);
            bool bothRows = targetZone == TargetZoneEnum.Any || targetZone == TargetZoneEnum.Conditional;
            int count = bothRows ? 2 : 1;
            Sprite sprite = isAoe ? _coordAoeSprite : _coordMonomerSprite;
            float markerWidth = sprite != null ? sprite.rect.width : (isAoe ? 23f : 10f);
            const float gap = 5f;
            float firstX = count == 1 ? 0f : -(markerWidth + gap) * 0.5f;

            for (int i = 0; i < _coordMarkers.Count; i++)
            {
                var marker = _coordMarkers[i];
                if (marker == null) continue;
                bool visible = i < count;
                marker.gameObject.SetActive(visible);
                if (!visible) continue;

                ApplyImageSprite(marker, sprite);
                bool front = bothRows ? i == 0 : targetZone != TargetZoneEnum.Back;
                marker.color = front
                    ? ReadCustomColor(FrontRowColorId, FrontRowFallback)
                    : ReadCustomColor(BackRowColorId, BackRowFallback);

                var markerTransform = marker.transform;
                markerTransform.localPosition = new Vector3(
                    firstX + i * (markerWidth + gap),
                    _coordMarkerY,
                    0f);
            }

            if (_imgLinkPiece != null)
                _imgLinkPiece.gameObject.SetActive(false);
        }

        /// <summary>从 TbCustomColor 读取指定 Id 的十六进制颜色；缺失/解析失败时返回 fallback。</summary>
        private static Color ReadCustomColor(string id, Color fallback)
        {
            try
            {
                var entry = Ashlight.Config.ConfigLoader.Tables?.TbCustomColor?.GetOrDefault(id);
                if (entry != null && !string.IsNullOrEmpty(entry.Color)
                    && ColorUtility.TryParseHtmlString("#" + entry.Color.TrimStart('#'), out var c))
                {
                    return c;
                }
            }
            catch
            {
                // 配置未加载等异常：静默回退
            }
            return fallback;
        }

        // ===== 内部：图标 / 数值 / 坐标 =====

        private void SetActiveIcon(Image active)
        {
            ToggleIcon(_imgThink, active);
            ToggleIcon(_imgState, active);
            ToggleIcon(_imgShield, active);
            ToggleIcon(_imgAttack, active);
        }

        private void ToggleIcon(Image img, Image active)
        {
            if (img == null) return;
            bool on = (img == active);
            if (img.gameObject.activeSelf != on)
                img.gameObject.SetActive(on);
            if (on) img.color = ActiveColor;
        }

        private void SetFigure(string text)
        {
            if (_txtFigure == null) return;
            if (string.IsNullOrEmpty(text))
            {
                if (_txtFigure.gameObject.activeSelf)
                    _txtFigure.gameObject.SetActive(false);
            }
            else
            {
                if (!_txtFigure.gameObject.activeSelf)
                    _txtFigure.gameObject.SetActive(true);
                _txtFigure.text = text;
                _txtFigure.color = ActiveColor;
            }
        }

        /// <summary>
        /// 切换整个 Coord 区域的显示/隐藏。
        /// 思考阶段调用 false；进入执行轨公示意图时调用 true。
        /// </summary>
        private void SetCoordsVisible(bool visible)
        {
            if (_coordRoot != null)
            {
                if (_coordRoot.gameObject.activeSelf != visible)
                    _coordRoot.gameObject.SetActive(visible);
                return;
            }
            // 没有 _coordRoot 兜底：逐个标记切换
            for (int i = 0; i < _coordMarkers.Count; i++)
            {
                var marker = _coordMarkers[i];
                if (marker == null) continue;
                if (!visible && marker.gameObject.activeSelf)
                    marker.gameObject.SetActive(false);
            }
            if (_imgLinkPiece != null && !visible)
                _imgLinkPiece.gameObject.SetActive(false);
        }

        // ===== 内部：自动绑定 & 工具 =====

        /// <summary>把 0717 导出的独立 PNG 覆盖到旧 PSB 节点上。</summary>
        private void ApplyNewArtwork()
        {
            ApplyImageSprite(_imgBase, LoadIntentionSprite("Img_IntentionBase"));
            ApplyImageSprite(_imgThink, LoadIntentionSprite("IntentionIcon_Img_Think"));
            ApplyImageSprite(_imgState, LoadIntentionSprite("IntentionIcon_Img_State"));
            ApplyImageSprite(_imgShield, LoadIntentionSprite("IntentionIcon_Img_Shield"));

            _meleeSprite = LoadIntentionSprite("IntentionIcon_Img_Melee");
            _remoteSprite = LoadIntentionSprite("IntentionIcon_Img_Remote");
            _coordMonomerSprite = LoadIntentionSprite("Coord_Img_Monomer");
            _coordAoeSprite = LoadIntentionSprite("Coord_Img_Aoe");
            ApplyImageSprite(_imgAttack, _meleeSprite);
        }

        /// <summary>
        /// PNG 尚无 .meta 时 Unity 会按 Texture2D 导入，因此先尝试 Sprite，再用 Texture2D 运行时创建 Sprite。
        /// </summary>
        private static Sprite LoadIntentionSprite(string assetName)
        {
            if (RuntimeSprites.TryGetValue(assetName, out var cached) && cached != null)
                return cached;

            string path = ResourceRoot + assetName;
            var sprite = Resources.Load<Sprite>(path);
            if (sprite == null)
            {
                var texture = Resources.Load<Texture2D>(path);
                if (texture != null)
                {
                    sprite = Sprite.Create(
                        texture,
                        new Rect(0f, 0f, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f),
                        100f);
                    sprite.name = assetName;
                }
            }

            if (sprite != null)
                RuntimeSprites[assetName] = sprite;
            else
                Debug.LogWarning($"[IntentionView] 无法加载新意图素材: Resources/{path}.png");
            return sprite;
        }

        private static void ApplyImageSprite(Image image, Sprite sprite)
        {
            if (image == null || sprite == null) return;
            image.sprite = sprite;
            image.preserveAspect = true;
            image.rectTransform.sizeDelta = new Vector2(sprite.rect.width, sprite.rect.height);
        }

        private void AutoBindIfMissing()
        {
            if (_imgBase == null)
                _imgBase = FindByName<Image>(transform, "Img_IntentionBase");
            if (_txtFigure == null)
                _txtFigure = FindByName<TextMeshProUGUI>(transform, "Txt_figure");

            var iconRoot = transform.Find("IntentionIcon");
            if (iconRoot != null)
            {
                if (_imgThink == null)
                    _imgThink = FindByName<Image>(iconRoot, "Img_Think");
                if (_imgState == null)
                    _imgState = FindByName<Image>(iconRoot, "Img_State");
                if (_imgShield == null)
                    _imgShield = FindByName<Image>(iconRoot, "Img_Shield");
                if (_imgAttack == null)
                    _imgAttack = FindByName<Image>(iconRoot, "Img_Attack");
            }

            if (_coordRoot == null)
                _coordRoot = transform.Find("Coord");
            if (_coordRoot != null)
            {
                // prefab 中连接块名字是 CoordLinkPiece，不是 Img_linkPiece
                if (_imgLinkPiece == null)
                    _imgLinkPiece = FindByName<Image>(_coordRoot, "CoordLinkPiece");

                // 坐标点在 Coord/CoordPoints/ 下，不在 Coord 直接子级
                if (_coordTemplate == null)
                {
                    var coordPoints = FindByName<Transform>(_coordRoot, "CoordPoints");
                    if (coordPoints != null)
                        _coordTemplate = FindByName<Image>(coordPoints, "Img_Coord0");
                }
            }
        }

        private static T FindByName<T>(Transform parent, string childName) where T : Component
        {
            var t = parent.Find(childName);
            return t == null ? null : t.GetComponent<T>();
        }

        // ===== Tooltip =====

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_currentSkillInfo == null)
            {
                return; // 思考态/无意图：既不显示 tooltip 也不画抛物线
            }

            // 抛物线指向锁定目标（即使没有 tooltip prefab 也要画）
            TryShowParabola();

            if (_descriptionView != null)
            {
                _descriptionView.Show(_currentSkillInfo);
                PositionTooltip(eventData);
            }
        }

        /// <summary>
        /// 把 tooltip 放到鼠标右侧（默认）或自身右侧。
        /// 用 ScreenPointToWorldPointInRectangle 兼容 Overlay / Camera 两种 Canvas 模式。
        /// </summary>
        private void PositionTooltip(PointerEventData eventData)
        {
            if (_descriptionView == null) return;

            var canvas = _descriptionView.GetComponentInParent<Canvas>();
            if (canvas == null) return;

            var canvasRect = canvas.transform as RectTransform;
            if (canvasRect == null) return;

            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : (eventData != null ? eventData.pressEventCamera ?? eventData.enterEventCamera : canvas.worldCamera);

            Vector2 screenPoint;
            if (_tooltipFollowMouse && eventData != null)
            {
                screenPoint = eventData.position + _tooltipMouseOffset;
            }
            else
            {
                // 锚定 IntentionView 自身的世界位置，转回屏幕再加偏移
                Camera screenCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                screenPoint = RectTransformUtility.WorldToScreenPoint(screenCam, transform.position) + _tooltipMouseOffset;
            }

            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(canvasRect, screenPoint, cam, out var worldPoint))
            {
                _descriptionView.SetPosition(worldPoint);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            HideTooltip();
            HideParabola();
        }

        private void HideTooltip()
        {
            if (_descriptionView != null)
            {
                _descriptionView.Hide();
            }
        }
    }
}
