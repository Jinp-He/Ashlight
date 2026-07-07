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
    ///     Img_Attack                 攻击图标
    ///   Coord/
    ///     Img_linkPiece              坐标连接块（横向拉伸）
    ///     Img_Coord0                 坐标点模板（prefab 里只放 1 个，运行时按需克隆）
    ///
    /// 颜色规范：
    ///   激活色  #9c660a
    ///   坐标点未激活色  #3f4447
    ///
    /// **Coord 语义（暗黑地牢式目标位置指示）：**
    /// - 总格子数 = 我方队伍上限（默认 4 格）
    /// - 激活的格子（橙色） = 本次技能锁定的目标位置
    /// - AOE 技能 → 4 格全亮 + 连接条铺满
    /// - 单体 → 仅目标位置一格亮，无连接条
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
        private static readonly Color InactiveDotColor = new Color32(0x3f, 0x44, 0x47, 0xff);

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

        [Header("坐标 (Coord/*) — 模板与连接块")]
        [Tooltip("Coord 容器，默认为子节点 'Coord'")]
        [SerializeField] private Transform _coordRoot;
        [Tooltip("坐标点模板。留空时取 Coord/Img_Coord0")]
        [SerializeField] private Image _coordTemplate;
        [Tooltip("坐标连接块。留空时取 Coord/Img_linkPiece")]
        [SerializeField] private Image _imgLinkPiece;

        [Header("动态生成参数")]
        [Tooltip("默认展示的坐标点总数（队伍上限，DD 式通常为 4）")]
        [SerializeField] private int _defaultTotalCoords = 4;
        [Tooltip("相邻坐标点之间的水平间距（像素）")]
        [SerializeField] private float _dotSpacing = 60f;

        // ===== 分区颜色（前排红 / 后排蓝）：被攻击的玩家点按其所在排上色 =====
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

        // 运行时生成的坐标点列表（含模板自身）
        private readonly List<Image> _coordDots = new List<Image>();
        // 当前总坐标点数（已生成且激活的数量）
        private int _currentTotalCoords;

        // tooltip 实例与当前对应的技能配置
        private DescriptionViewController _descriptionView;
        private EnemySkillInfo _currentSkillInfo;
        // 当前锁定目标 UnitId（供悬停抛物线指向；空区未锁人 / 思考态为 null）
        private string _currentTargetUnitId;

        // ===== 生命周期 =====

        private void Awake()
        {
            AutoBindIfMissing();
            EnsureCoordCount(_defaultTotalCoords);
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
        /// 改变坐标点总数（按需 Instantiate 或隐藏多余）。
        /// 不同队伍大小时调用。
        /// </summary>
        public void EnsureCoordCount(int n)
        {
            if (_coordTemplate == null) return;
            n = Mathf.Max(1, n);

            // 首次：从 CoordPoints（HorizontalLayoutGroup 容器）收集所有已预置的点
            if (_coordDots.Count == 0)
            {
                var layoutParent = _coordTemplate.transform.parent;
                if (layoutParent != null)
                {
                    foreach (Transform child in layoutParent)
                    {
                        var img = child.GetComponent<Image>();
                        if (img != null) _coordDots.Add(img);
                    }
                }
                // 兜底：至少把模板加进去
                if (_coordDots.Count == 0)
                    _coordDots.Add(_coordTemplate);
            }

            // 如果预置点不够，再克隆（HorizontalLayoutGroup 会自动排列）
            while (_coordDots.Count < n)
            {
                var clone = Instantiate(_coordTemplate, _coordTemplate.transform.parent);
                clone.name = $"Img_Coord{_coordDots.Count}";
                _coordDots.Add(clone);
            }

            // 前 n 个显示，超出的隐藏
            for (int i = 0; i < _coordDots.Count; i++)
            {
                bool on = i < n;
                if (_coordDots[i].gameObject.activeSelf != on)
                    _coordDots[i].gameObject.SetActive(on);
            }

            _currentTotalCoords = n;
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
        /// 含攻击效果 → Attack 图标 + 累计伤害
        /// 含防御效果 → Shield 图标 + 累计护甲
        /// 其他 → State 图标，无数值
        /// </summary>
        /// <param name="skillInfo">技能配置</param>
        /// <param name="dotStates">
        /// 每个玩家点的状态数组（长度=玩家数，顺序=名单序）：0=未被攻击(灰)、1=被攻击且在前排(红)、2=被攻击且在后排(蓝)。
        /// 为 null → 隐藏 Coord（自身/我方向技能）。
        /// </param>
        /// <param name="targetUnitId">当前锁定目标 UnitId（供悬停抛物线）</param>
        public void ShowFromSkill(EnemySkillInfo skillInfo, int[] dotStates, string targetUnitId = null)
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
            bool effectIsAoe = false;

            if (skillInfo.Effects != null)
            {
                foreach (var eff in skillInfo.Effects)
                {
                    switch (eff)
                    {
                        case AttackEffect atk:
                            attackDamage += atk.Damage;
                            hasAttack = true;
                            if (atk.IsAoe) effectIsAoe = true;
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

            // Coord 语义：3 个点 = 3 个玩家角色；被攻击的玩家点按其所在排上色（前排红/后排蓝），未被打为灰。
            // 具体每个点的状态由调用方(UI_BattleScene)按当前站位算好传入；null → 隐藏 Coord。
            if (hasAttack)
                ShowAttack(attackDamage, dotStates);
            else if (hasShield)
                ShowShield(shieldValue, dotStates);
            else
                ShowState(dotStates);
        }

        /// <summary>旧签名兜底：没有目标信息时隐藏 Coord。</summary>
        public void ShowFromSkill(EnemySkillInfo skillInfo)
        {
            ShowFromSkill(skillInfo, (int[])null, null);
        }

        public void ShowAttack(int damage, int[] dotStates)
        {
            Show();
            SetActiveIcon(_imgAttack);
            SetFigure(damage.ToString());
            ApplyCoordStates(dotStates);
        }

        public void ShowShield(int shieldValue, int[] dotStates)
        {
            Show();
            SetActiveIcon(_imgShield);
            SetFigure(shieldValue.ToString());
            ApplyCoordStates(dotStates);
        }

        public void ShowState(int[] dotStates)
        {
            Show();
            SetActiveIcon(_imgState);
            SetFigure(null);
            ApplyCoordStates(dotStates);
        }

        /// <summary>dotStates 为 null → 隐藏整个 Coord；否则显示并按每个玩家点的状态上色。</summary>
        private void ApplyCoordStates(int[] dotStates)
        {
            if (dotStates == null || dotStates.Length == 0)
            {
                SetCoordsVisible(false);
                return;
            }
            SetCoordsVisible(true);
            SetCoordStates(dotStates);
        }

        // ===== 坐标点：3 个玩家点 + 前排红/后排蓝上色 =====

        /// <summary>
        /// 按每个玩家点的状态上色：0=未被攻击(灰)、1=被攻击且前排(红)、2=被攻击且后排(蓝)。
        /// 全部点都显示（代表在场的玩家角色），只有被攻击的点染成红/蓝。
        /// </summary>
        private void SetCoordStates(int[] states)
        {
            int n = states.Length;
            EnsureCoordCount(n);

            for (int i = 0; i < n && i < _coordDots.Count; i++)
            {
                var dot = _coordDots[i];
                if (dot == null) continue;
                if (!dot.gameObject.activeSelf) dot.gameObject.SetActive(true);

                Color c;
                if (states[i] == 1) c = ReadCustomColor(FrontRowColorId, FrontRowFallback);
                else if (states[i] == 2) c = ReadCustomColor(BackRowColorId, BackRowFallback);
                else c = InactiveDotColor;
                dot.color = c;
            }

            // 新方案按玩家点染色，不再用连接条长条：始终隐藏它
            if (_imgLinkPiece != null && _imgLinkPiece.gameObject.activeSelf)
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
        /// 切换整个 Coord 区域（坐标点 + 连接条）的显示/隐藏。
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
            // 没有 _coordRoot 兜底：逐个 dot + linkPiece 切换
            for (int i = 0; i < _coordDots.Count; i++)
            {
                var dot = _coordDots[i];
                if (dot == null) continue;
                bool on = visible && i < _currentTotalCoords;
                if (dot.gameObject.activeSelf != on)
                    dot.gameObject.SetActive(on);
            }
            if (_imgLinkPiece != null && _imgLinkPiece.gameObject.activeSelf && !visible)
                _imgLinkPiece.gameObject.SetActive(false);
        }

        // ===== 内部：自动绑定 & 工具 =====

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
