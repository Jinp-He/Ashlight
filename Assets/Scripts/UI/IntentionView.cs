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

        [Header("Tooltip（敌人技能详情）")]
        [Tooltip("DescriptionViewController 预制体（与 CardViewController 用的是同一个）。鼠标悬停在意图上时弹出，显示当前 EnemySkillInfo 的详情")]
        [SerializeField] private GameObject _descriptionViewControllerPrefab;

        [Tooltip("Tooltip 相对鼠标位置的屏幕偏移（默认右侧 120px）")]
        [SerializeField] private Vector2 _tooltipMouseOffset = new Vector2(120f, 0f);

        [Tooltip("勾选时 tooltip 跟随鼠标位置出现；不勾选则锚定在 IntentionView 自身的右侧")]
        [SerializeField] private bool _tooltipFollowMouse = true;

        // 运行时生成的坐标点列表（含模板自身）
        private readonly List<Image> _coordDots = new List<Image>();
        // 当前总坐标点数（已生成且激活的数量）
        private int _currentTotalCoords;

        // tooltip 实例与当前对应的技能配置
        private DescriptionViewController _descriptionView;
        private EnemySkillInfo _currentSkillInfo;

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
            HideTooltip();
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

            // 模板自身算第 0 个
            if (_coordDots.Count == 0)
            {
                _coordDots.Add(_coordTemplate);
                PlaceCoordAt(_coordTemplate, 0);
            }

            // 不够则克隆
            while (_coordDots.Count < n)
            {
                int idx = _coordDots.Count;
                var clone = Instantiate(_coordTemplate, _coordTemplate.transform.parent);
                clone.name = $"Img_Coord{idx}";
                PlaceCoordAt(clone, idx);
                _coordDots.Add(clone);
            }

            // 已有的：前 n 个显示，超出的隐藏
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
            HideTooltip();
        }

        /// <summary>
        /// 根据敌人技能配置 + 目标信息自动显示意图。
        /// 含攻击效果 → Attack 图标 + 累计伤害
        /// 含防御效果 → Shield 图标 + 累计护甲
        /// 其他 → State 图标，无数值
        /// </summary>
        /// <param name="skillInfo">技能配置</param>
        /// <param name="targetSlot">目标在队伍中的位置索引（0 起；AOE 时忽略）</param>
        /// <param name="totalSlots">队伍总人数（决定 Coord 总格数；&lt;=0 时保留当前）</param>
        /// <param name="isAoe">是否 AOE（全员）</param>
        public void ShowFromSkill(EnemySkillInfo skillInfo, int targetSlot, int totalSlots, bool isAoe)
        {
            if (skillInfo == null)
            {
                ShowThinking();
                return;
            }

            // 缓存当前技能，供 hover tooltip 使用
            _currentSkillInfo = skillInfo;

            // 调整总格数
            if (totalSlots > 0 && totalSlots != _currentTotalCoords)
            {
                EnsureCoordCount(totalSlots);
            }

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

            // 综合判定 AOE：调用方传入 || 任一效果 IsAoe || TargetType 为全体
            bool aoe = isAoe || effectIsAoe
                || skillInfo.TargetType == TargetTypeEnum.AllEnemy
                || skillInfo.TargetType == TargetTypeEnum.AllAlly;

            bool[] mask = BuildTargetMask(targetSlot, _currentTotalCoords, aoe);

            if (hasAttack)
                ShowAttack(attackDamage, mask);
            else if (hasShield)
                ShowShield(shieldValue, mask);
            else
                ShowState(mask);
        }

        /// <summary>旧签名兜底：没有目标信息时，所有坐标灰。</summary>
        public void ShowFromSkill(EnemySkillInfo skillInfo)
        {
            ShowFromSkill(skillInfo, targetSlot: -1, totalSlots: -1, isAoe: false);
        }

        public void ShowAttack(int damage, bool[] mask)
        {
            Show();
            SetActiveIcon(_imgAttack);
            SetFigure(damage.ToString());
            SetCoordsVisible(true);
            SetCoordMask(mask);
        }

        public void ShowShield(int shieldValue, bool[] mask)
        {
            Show();
            SetActiveIcon(_imgShield);
            SetFigure(shieldValue.ToString());
            SetCoordsVisible(true);
            SetCoordMask(mask);
        }

        public void ShowState(bool[] mask)
        {
            Show();
            SetActiveIcon(_imgState);
            SetFigure(null);
            SetCoordsVisible(true);
            SetCoordMask(mask);
        }

        // ===== 内部：图标 / 数值 / 坐标 =====

        private static bool[] BuildTargetMask(int targetSlot, int totalSlots, bool isAoe)
        {
            if (totalSlots <= 0) return null;
            var mask = new bool[totalSlots];
            if (isAoe)
            {
                for (int i = 0; i < totalSlots; i++) mask[i] = true;
            }
            else if (targetSlot >= 0 && targetSlot < totalSlots)
            {
                mask[targetSlot] = true;
            }
            return mask;
        }

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

        /// <summary>
        /// 按 mask 点亮 / 熄灭坐标点。mask 为 null 时全部置灰。
        /// </summary>
        private void SetCoordMask(bool[] mask)
        {
            int total = _currentTotalCoords;
            int firstActive = -1, lastActive = -1;

            for (int i = 0; i < total; i++)
            {
                bool on = mask != null && i < mask.Length && mask[i];
                if (on)
                {
                    if (firstActive < 0) firstActive = i;
                    lastActive = i;
                }
                var dot = _coordDots[i];
                if (dot == null) continue;
                dot.color = on ? ActiveColor : InactiveDotColor;
            }

            UpdateLinkPieceRange(firstActive, lastActive);
        }

        /// <summary>
        /// 连接条覆盖第 firstActive 到 lastActive 个坐标点之间的区间。
        /// 假设 link piece pivot = (0.5, 0.5)。
        /// </summary>
        private void UpdateLinkPieceRange(int firstActive, int lastActive)
        {
            if (_imgLinkPiece == null) return;

            // 无高亮或单点：隐藏连接条
            if (firstActive < 0 || firstActive == lastActive)
            {
                if (_imgLinkPiece.gameObject.activeSelf)
                    _imgLinkPiece.gameObject.SetActive(false);
                return;
            }

            if (!_imgLinkPiece.gameObject.activeSelf)
                _imgLinkPiece.gameObject.SetActive(true);
            _imgLinkPiece.color = ActiveColor;

            var rt = _imgLinkPiece.rectTransform;
            var tplPos = _coordTemplate.rectTransform.anchoredPosition;

            // 居中放在 firstActive..lastActive 之间
            float centerX = tplPos.x + (firstActive + lastActive) * 0.5f * _dotSpacing;
            float width = (lastActive - firstActive) * _dotSpacing;

            rt.anchoredPosition = new Vector2(centerX, tplPos.y);
            var size = rt.sizeDelta;
            size.x = width;
            rt.sizeDelta = size;
        }

        // 把第 idx 个坐标点放在 (template.x + idx * spacing, template.y)
        private void PlaceCoordAt(Image dot, int idx)
        {
            if (dot == null || _coordTemplate == null) return;
            var tpl = _coordTemplate.rectTransform;
            var rt = dot.rectTransform;
            var pos = tpl.anchoredPosition;
            pos.x += idx * _dotSpacing;
            rt.anchoredPosition = pos;
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
                if (_imgLinkPiece == null)
                    _imgLinkPiece = FindByName<Image>(_coordRoot, "Img_linkPiece");
                if (_coordTemplate == null)
                    _coordTemplate = FindByName<Image>(_coordRoot, "Img_Coord0");
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
            if (_descriptionView == null || _currentSkillInfo == null)
            {
                return; // 没有技能信息（思考态）或没绑 prefab，不显示
            }
            _descriptionView.Show(_currentSkillInfo);
            PositionTooltip(eventData);
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
