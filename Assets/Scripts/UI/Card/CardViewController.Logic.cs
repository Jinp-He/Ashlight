using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using cfg;
using cfg.Character;
using TMPro;
using DG.Tweening;
using Ashlight.Common.Events;
using Ashlight.Common.Utils;
using Ashlight.Battle.Prototype;
using Ashlight.State.Runtime;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UI;
namespace Scripts.UI
{
    /// <summary>
    /// CardViewController的业务逻辑部分（手动编写）
    /// </summary>
    public partial class CardViewController : IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        
        #region 序列化字段

        [Header("描述面板")]
        [SerializeField]
        [Tooltip("DescriptionViewController预制体")]
        private GameObject descriptionViewControllerPrefab;

        [Header("悬停设置")]
        [SerializeField]
        [Tooltip("悬停时的缩放比例")]
        private float hoverScale = 1.4f;

        /// <summary>外部设置悬停放大比例（如三选一选卡面板用 1.1，避免大卡 hover 放得过大）。</summary>
        public void SetHoverScale(float scale) => hoverScale = scale;

        [SerializeField]
        [Tooltip("缩放动画时长")]
        private float scaleDuration = 0.2f;

        [SerializeField]
        [Tooltip("悬停时向上偏移距离（像素）")]
        private float hoverLiftDistance = 50f;

        [SerializeField]
        [Tooltip("描述面板相对于卡牌的偏移（像素）")]
        private Vector2 descriptionOffset = new Vector2(300f, 0f);

        [Header("层级设置")]
        [SerializeField]
        [Tooltip("悬停时的Canvas排序顺序")]
        private int hoverSortingOrder = 100;

        [Header("拖拽设置")]
        [SerializeField]
        [Tooltip("拖拽时的缩放比例（战斗模式）")]
        private float dragScale = 1.2f;

        [SerializeField]
        [Tooltip("拖拽时的透明度")]
        private float dragAlpha = 0.8f;

        [SerializeField]
        [Tooltip("位置恢复动画时长")]
        private float positionRestoreDuration = 0.3f;

        [SerializeField]
        [Tooltip("使用（拖动/选择）卡牌时，其余手牌向下位移的距离（像素），用于让出空间不遮挡信息")]
        private float otherCardsPushDownDistance = 100f;

        [Header("目标选择颜色设置")]
        [SerializeField]
        [Tooltip("合法目标的变暗颜色（拖动时合法目标会变暗）")]
        private Color validTargetDimColor = new Color(0.5f, 0.5f, 0.5f, 1f);

        [SerializeField]
        [Tooltip("能量不足时左上角能量文本的变色颜色")]
        private Color insufficientEnergyColor = new Color(1f, 0.25f, 0.25f, 1f);

        [SerializeField]
        [Tooltip("执行（延时）卡牌左上角能量底图颜色")]
        private Color executionCostTint = new Color(1f, 0.55f, 0.2f, 1f);

        [Header("条件牌高亮（Outline）")]
        [SerializeField]
        [Tooltip("条件牌：当前满足触发条件时的描边颜色（黄）")]
        private Color conditionMetOutlineColor = new Color(1f, 0.85f, 0.1f, 1f);

        [SerializeField]
        [Tooltip("条件牌：当前不满足触发条件时的描边颜色（白）")]
        private Color conditionUnmetOutlineColor = Color.white;

        /// <summary>本卡是否为「自身可判定的条件牌」（描边随条件黄/白，且常驻不随悬停消失）。RefreshConditionHighlight 时缓存。</summary>
        private bool _isSelfConditionCard;

        #endregion

        #region 私有字段

        private CardInfo _currentCard;
        private DescriptionMode _displayMode = DescriptionMode.View;

        // 记录 Txt_LeftCost 默认颜色，用于能量足够时恢复
        private Color _defaultLeftCostColor;
        private bool _defaultLeftCostColorCaptured = false;

        // 记录 Img_CostLeft 默认颜色，用于非执行牌时恢复
        private Color _defaultLeftCostImageColor;
        private bool _defaultLeftCostImageColorCaptured = false;

        /// <summary>
        /// 当前关联的 CardRuntimeState 的 InstanceId（用于对象池关联）
        /// </summary>
        private string _instanceId;
        private Vector3 _originalScale;
        private Vector3 _originalCardScale; // Card子对象的原始缩放
        private Tween _scaleTween;
        private Tween _hoverMoveTween;
        private DescriptionViewController _descriptionView;
        private Canvas _parentCanvas;
        private string _currentHoveredLink = string.Empty;
        
        // 层级管理相关
        private Transform _originalParent;
        private int _originalSiblingIndex;
        private Canvas _hoverCanvas;
        private bool _isHovering = false;
        private RectTransform _rectTransform;
        private Vector2 _hoverBaseAnchoredPosition;

        /// <summary>
        /// 手牌布局完成后同步悬停基准。布局系统是手牌坐标的唯一真相源，
        /// 避免取消选择后把 Tween 中间位置继续当作下一次悬停基准。
        /// </summary>
        public void RefreshHandLayoutBaseline()
        {
            if (_rectTransform == null || _isDragging || _isClickTargeting || _isCastTargeting)
                return;

            _hoverMoveTween?.Kill();
            _hoverBaseAnchoredPosition = _rectTransform.anchoredPosition;
        }

        // 拖拽相关
        private bool _isDragging = false;
        private bool _tempoActionPreviewStarted = false;
        private bool _isInTimeSlot = false; // 是否已切换到时间轴状态
        private bool _hasLoggedNoTimeSlot = false; // 避免日志刷屏
        private Vector3 _dragOffset;
        private Vector3 _originalDragPosition;
        private Tween _positionTween;
        private CanvasGroup _canvasGroup;
        private float _originalAlpha = 1f;

        // 使用本牌时被下移让位的其他手牌（记录其原始 anchoredPosition.y 以便恢复）
        private readonly System.Collections.Generic.List<RectTransform> _pushedSiblings = new System.Collections.Generic.List<RectTransform>();
        private readonly System.Collections.Generic.List<float> _pushedSiblingOriginalY = new System.Collections.Generic.List<float>();
        private readonly System.Collections.Generic.List<RectTransform> _hoverShiftedSiblings = new System.Collections.Generic.List<RectTransform>();
        private readonly System.Collections.Generic.List<float> _hoverShiftedSiblingOriginalX = new System.Collections.Generic.List<float>();
        private readonly System.Collections.Generic.List<Tween> _hoverNeighborTweens = new System.Collections.Generic.List<Tween>();
        private readonly System.Collections.Generic.List<GameObject> _selectedMultiTargetObjects = new System.Collections.Generic.List<GameObject>();
        
        // Card 拖拽状态
        private CardDragState _cardDragState = CardDragState.OnHand;
        
        // 卡片锁定状态（被执行后不可移动）
        private bool _isLocked = false;

        /// <summary>
        /// 本回合已打出执行牌后，其余执行牌被压制（变暗、不可交互）
        /// </summary>
        private bool _executionSuppressed = false;

        private readonly System.Collections.Generic.List<UnityEngine.UI.Graphic> _executionTintGraphics = new System.Collections.Generic.List<UnityEngine.UI.Graphic>();
        private readonly System.Collections.Generic.List<Color> _executionTintColors = new System.Collections.Generic.List<Color>();
        
        // 时间轴相关（当 Card 在时间轴上时使用）
        private Timeline.TimelineTrackView _parentTrack;
        private int _originalSlotIndex = -1;
        private Vector3 _originalTimePosition;
        private Vector2 _originalTimeAnchoredPosition; // 保存原始 anchoredPosition（本地坐标）
        private Transform _originalTimeParent;
        
        // CardTimeSlot 悬停相关（OnTime状态）
        private Vector2 _originalCardPosition; // Card 的原始位置（在时间轴上时）
        private bool _isCardTimeSlotHovering = false; // 是否正在悬停 CardTimeSlot
        
        // 高亮相关
        private Timeline.TimelineTrackView _currentHighlightedTrack = null;
        private int _currentHighlightedSlotIndex = -1;
        
        // 保存原始 raycastTarget 和 interactable 状态
        private bool _originalRaycastTarget = true;
        private bool _originalInteractable = true;
        private UnityEngine.UI.Selectable[] _selectables;
        
        // Raycast 调试相关（时间轴拖拽）
        private int _raycastDebugFrameCount = 0;
        private const int RAYCAST_DEBUG_INTERVAL = 30; // 每30帧输出一次（降低频率）
        
        // 性能优化：缓存和节流
        private Timeline.TimelineSlotView _cachedSlotUnderPointer = null;
        private Vector2 _cachedPointerPosition = Vector2.zero;
        private int _lastRaycastFrame = -1;
        private const int RAYCAST_CACHE_FRAMES = 2; // 每2帧更新一次缓存
        private const float RAYCAST_POSITION_THRESHOLD = 5f; // 鼠标移动超过5像素才重新检测
        
        // 调试开关（可在Inspector中控制）
        [Header("调试设置")]
        [SerializeField]
        [Tooltip("是否启用详细的调试日志（会影响性能）")]
        private bool enableDetailedDebugLogs = false;

        // 目标选择系统
        private TargetArrowRenderer _targetArrow;
        private TargetSelectionManager _targetManager;
        private GameObject _currentTargetObject;
        private GameObject _lastValidTargetObject;
        private bool _isTargeting = false;

        // 点击进入的目标选择模式（无需按住拖拽）：点击卡牌进入，移动鼠标选目标，再次左键确认，右键/Esc取消
        private bool _isClickTargeting = false;
        private bool _isCastTargeting = false;
        private TurnOrderView _castTargetView;

        // 目标颜色管理
        private Dictionary<Character, Color> _originalCharacterColors = new Dictionary<Character, Color>();
        private Dictionary<Enemy, Color> _originalEnemyColors = new Dictionary<Enemy, Color>();
        private GameObject _previousHoveredTarget = null; // 之前悬停的目标

        #endregion

        #region Unity生命周期

        /// <summary>
        /// 初始化
        /// </summary>
        private void Awake()
        {
            // 调用自动生成的UI绑定初始化方法
            InitUIBindings();

            // 保存原始缩放和父对象
            _originalScale = transform.localScale;
            _originalParent = transform.parent;
            _originalSiblingIndex = transform.GetSiblingIndex();

            // 保存Card子对象的原始缩放
            if (Card != null && Card.transform != null)
            {
                _originalCardScale = Card.transform.localScale;
            }

            // 获取父Canvas（用于定位描述面板）
            _parentCanvas = GetComponentInParent<Canvas>();

            // 创建或获取Canvas组件（用于控制渲染优先级）
            _hoverCanvas = gameObject.GetComponent<Canvas>();
            if (_hoverCanvas == null)
            {
                _hoverCanvas = gameObject.AddComponent<Canvas>();
            }
            _rectTransform = GetComponent<RectTransform>();
            if (_rectTransform != null)
            {
                _hoverBaseAnchoredPosition = _rectTransform.anchoredPosition;
            }
            
            // 添加GraphicRaycaster（用于接收鼠标事件）
            if (gameObject.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }
            
            // 默认不覆盖父Canvas
            _hoverCanvas.overrideSorting = false;

            // 创建或获取CanvasGroup（用于控制透明度）
            _canvasGroup = gameObject.GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
            _originalAlpha = _canvasGroup.alpha;
            
            // 保存原始 raycastTarget 状态（从第一个 Image 获取，假设所有 Image 初始状态一致）
            Image firstImage = GetComponent<Image>();
            if (firstImage != null)
            {
                _originalRaycastTarget = firstImage.raycastTarget;
            }
            
            // 查找所有 Selectable 组件（Button、Toggle 等）
            _selectables = GetComponentsInChildren<UnityEngine.UI.Selectable>(true);
            
            // 保存原始 interactable 状态（从第一个 Selectable 获取，假设所有 Selectable 初始状态一致）
            if (_selectables != null && _selectables.Length > 0)
            {
                _originalInteractable = _selectables[0].interactable;
            }

            // 创建描述面板实例
            if (descriptionViewControllerPrefab != null && _parentCanvas != null)
            {
                // tooltip 挂到专用的高层级 overlay Canvas 下，避免嵌套 Canvas 干扰父 Canvas 的射线检测
                Transform tooltipParent = GetOrCreateTooltipCanvas(_parentCanvas);
                GameObject descObj = Instantiate(descriptionViewControllerPrefab, tooltipParent);
                _descriptionView = descObj.GetComponent<DescriptionViewController>();
                if (_descriptionView != null)
                {
                    _descriptionView.Hide();
                }
            }

            // 初始化目标选择系统
            InitializeTargetSelection();
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        private void Update()
        {
            // 点击进入的目标选择模式：每帧更新箭头/高亮，并检测确认/取消输入
            if (_isClickTargeting)
            {
                UpdateClickTargeting();
            }
            if (_isCastTargeting && (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)))
            {
                CancelCastTargeting();
            }

            // 检测鼠标是否悬停在link上
            if (Txt_Effect != null)
            {
                CheckLinkHover(Txt_Effect);
            }
        }

        /// <summary>
        /// 销毁时清理
        /// </summary>
        private void OnDestroy()
        {
            // 清理动画
            _scaleTween?.Kill();
            _positionTween?.Kill();
            _hoverMoveTween?.Kill();

            // 清理描述面板
            if (_descriptionView != null)
            {
                Destroy(_descriptionView.gameObject);
            }
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 初始化卡牌数据
        /// </summary>
        /// <param name="card">卡牌配置数据</param>
        /// <param name="mode">显示模式（默认为阅览模式）</param>
        public void Initialize(CardInfo card, DescriptionMode mode = DescriptionMode.View)
        {
            if (card == null)
            {
                Debug.LogError("[CardViewController] 卡牌数据为空");
                return;
            }

            _currentCard = card;
            _displayMode = mode;
            UpdateCardDisplay();

            // 同时初始化 CardTimeSlot（如果存在）
            if (CardTimeSlot != null)
            {
                // 创建临时的 CardRuntimeState 用于初始化 CardTimeSlot
                var tempCardState = CardRuntimeState.CreateDefault(card.Id);
                CardTimeSlot.InitLoad(tempCardState);
            }
        }

        /// <summary>
        /// 获取当前关联的 CardRuntimeState 的 InstanceId
        /// </summary>
        public string InstanceId => _instanceId;

        /// <summary>
        /// 重新初始化为另一张卡牌（对象池复用）
        /// </summary>
        /// <param name="cardInfo">卡牌配置数据</param>
        /// <param name="instanceId">CardRuntimeState 的 InstanceId</param>
        /// <param name="mode">显示模式（默认为战斗模式）</param>
        public void Reinitialize(CardInfo cardInfo, string instanceId, DescriptionMode mode = DescriptionMode.Battle)
        {
            if (cardInfo == null)
            {
                Debug.LogError("[CardViewController] Reinitialize: 卡牌数据为空");
                return;
            }

            _instanceId = instanceId;
            _currentCard = cardInfo;
            _displayMode = mode;

            // 重置所有状态
            ResetForReuse();

            // 更新显示
            UpdateCardDisplay();

            // 同时初始化 CardTimeSlot
            if (CardTimeSlot != null)
            {
                var tempCardState = CardRuntimeState.CreateDefault(cardInfo.Id);
                CardTimeSlot.InitLoad(tempCardState);
            }

            Debug.Log($"[CardViewController] Reinitialize: {cardInfo.Name} (InstanceId: {instanceId})");
        }

        /// <summary>
        /// 重置卡牌状态（供对象池复用）
        /// </summary>
        public void ResetForReuse()
        {
            // 若仍处于点击式目标选择中，先收尾清理（隐藏箭头/高亮、复位其余手牌、恢复射线）
            if (_isClickTargeting)
            {
                ExitClickTargetingVisuals();
            }
            if (_isCastTargeting)
            {
                ExitCastTargetingVisuals();
            }

            // 重置拖拽状态为 OnHand
            SetCardDragState(CardDragState.OnHand);
            _lastValidTargetObject = null;

            // 清除时间轴信息
            _parentTrack = null;
            _originalSlotIndex = -1;
            _originalTimePosition = Vector3.zero;
            _originalTimeAnchoredPosition = Vector2.zero;
            _originalTimeParent = null;

            // 解锁
            if (_isLocked)
            {
                UnlockCard();
            }

            if (_executionSuppressed)
            {
                SetExecutionSuppressed(false);
            }

            // 重置视觉状态
            if (Card != null)
            {
                // CardTimeSlot / 预览等路径可能关闭完整卡面子节点；回到 OnHand 时必须一起恢复。
                Card.gameObject.SetActive(true);
                Card.alpha = 1f;
                Card.transform.localScale = _originalCardScale;
            }
            if (CardTimeSlot != null)
            {
                CardTimeSlot.gameObject.SetActive(false);
            }

            // 重置缩放
            transform.localScale = Vector3.one;

            // 重置其他状态
            _isDragging = false;
            _tempoActionPreviewStarted = false;
            _isHovering = false;
            _isInTimeSlot = false;
            _isTargeting = false;
            RestoreHoverNeighbors(true);
        }

        /// <summary>进入抽牌/打出流转层前，清除悬停、选牌和复用残留，但保留当前世界位置作为动画起点。</summary>
        public void PrepareForCardFlowAnimation()
        {
            _scaleTween?.Kill();
            _hoverMoveTween?.Kill();
            _positionTween?.Kill();
            RestoreHoverNeighbors(true);
            RestoreOtherHandCards(true);
            if (Card != null && Card.transform != null)
                Card.transform.localScale = _originalCardScale;
            SetCanvasGroupAlpha(1f);
            RestoreCard();
            HideDescription();
        }

        /// <summary>
        /// 显示卡牌（从对象池中取出时调用）
        /// </summary>
        public void Show()
        {
            gameObject.SetActive(true);
        }

        /// <summary>
        /// 隐藏卡牌（放回对象池时调用）
        /// </summary>
        public void Hide()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 切换显示模式
        /// </summary>
        /// <param name="mode">新的显示模式</param>
        public void SetDisplayMode(DescriptionMode mode)
        {
            if (_displayMode != mode)
            {
                _displayMode = mode;
                UpdateCardDisplay();
            }
        }
        
        /// <summary>
        /// 获取当前卡牌信息
        /// </summary>
        public CardInfo GetCurrentCard()
        {
            return _currentCard;
        }

        /// <summary>
        /// 获取当前卡牌信息
        /// </summary>
        /// <returns>卡牌信息</returns>
        public CardInfo GetCardInfo()
        {
            return _currentCard;
        }

        /// <summary>
        /// 锁定卡片（被执行后不可移动）
        /// </summary>
        public void LockCard()
        {
            if (_isLocked)
            {
                Debug.Log($"[CardViewController] 卡片已经是锁定状态: {_currentCard?.Name}");
                return;
            }

            _isLocked = true;
            SetLockedVisual();
            Debug.Log($"[CardViewController] 卡片已锁定: {_currentCard?.Name}");
        }

        /// <summary>
        /// 解锁卡片
        /// </summary>
        public void UnlockCard()
        {
            if (!_isLocked)
            {
                return;
            }

            _isLocked = false;
            RestoreNormalVisual();
            Debug.Log($"[CardViewController] 卡片已解锁: {_currentCard?.Name}");
        }

        /// <summary>
        /// 获取锁定状态
        /// </summary>
        public bool IsLocked()
        {
            return _isLocked;
        }

        /// <summary>
        /// 执行牌已打出后：将其余执行牌变暗并禁止悬停/拖拽（本回合内）
        /// </summary>
        public void SetExecutionSuppressed(bool suppressed)
        {
            if (suppressed && _executionSuppressed)
                return;
            if (!suppressed && !_executionSuppressed)
                return;

            if (suppressed)
            {
                _executionSuppressed = true;
                _isHovering = false;
                _scaleTween?.Kill();
                ForceResetHoverLift();
                if (Img_Outline != null)
                {
                    Img_Outline.gameObject.SetActive(false);
                }

                if (Card != null && Card.transform != null)
                {
                    Card.transform.localScale = _originalCardScale;
                }

                RestoreCard();
                HideDescription();

                _executionTintGraphics.Clear();
                _executionTintColors.Clear();
                foreach (var g in GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                {
                    if (g == null)
                        continue;
                    _executionTintGraphics.Add(g);
                    _executionTintColors.Add(g.color);
                    var c = g.color;
                    g.color = new Color(c.r * 0.32f, c.g * 0.32f, c.b * 0.32f, c.a);
                }

                if (_canvasGroup != null)
                    _canvasGroup.blocksRaycasts = false;
            }
            else
            {
                for (int i = 0; i < _executionTintGraphics.Count; i++)
                {
                    var g = _executionTintGraphics[i];
                    if (g != null && i < _executionTintColors.Count)
                        g.color = _executionTintColors[i];
                }

                _executionTintGraphics.Clear();
                _executionTintColors.Clear();
                _executionSuppressed = false;

                if (_canvasGroup != null)
                    _canvasGroup.blocksRaycasts = true;
            }
        }

        /// <summary>
        /// 设置锁定状态的视觉效果（变暗）
        /// </summary>
        private void SetLockedVisual()
        {
            // 降低CardTimeSlot的透明度
            if (CardTimeSlot != null)
            {
                var canvasGroup = CardTimeSlot.GetComponent<CanvasGroup>();
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = 0.5f;
                }
            }

            // 同时降低Card部分的透明度（如果可见）
            if (Card != null && Card.alpha > 0.1f)
            {
                Card.alpha = 0.5f;
            }
        }

        /// <summary>
        /// 恢复正常视觉效果
        /// </summary>
        private void RestoreNormalVisual()
        {
            // 恢复CardTimeSlot的透明度
            if (CardTimeSlot != null)
            {
                var canvasGroup = CardTimeSlot.GetComponent<CanvasGroup>();
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = 1.0f;
                }
            }

            // 恢复Card部分的透明度
            if (Card != null)
            {
                Card.alpha = _cardDragState == CardDragState.OnTime ? 0f : 1f;
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 更新卡牌显示
        /// </summary>
        private void UpdateCardDisplay()
        {
            if (_currentCard == null) return;
  
            // 设置卡牌名称
            if (Txt_CardName != null)
            {
                Txt_CardName.text = _currentCard.Name;
            }

            // 使用CardDescriptionParser解析描述和效果
            string parsedDescription = CardDescriptionParser.Parse(_currentCard, _displayMode);
            // 闪回是运行时实例属性（原卡仍保留自己的配置），因此在此补充标签。
            if (FindRuntimeState()?.IsFlashback == true && !parsedDescription.Contains("闪回"))
            {
                parsedDescription += "\n[闪回] [虚无]";
            }

            // 设置卡牌效果文本（解析后的完整描述）
            if (Txt_Effect != null)
            {
                Txt_Effect.text = parsedDescription;
            }

            // 设置卡牌描述（可选：显示原始描述或留空）
            if (Txt_Comment != null)
            {
                // 如果需要显示额外的说明文本，可以在这里设置
                // 当前留空，让Txt_Effect显示完整的解析后描述
                Txt_Comment.text = string.Empty;
            }

            // 设置卡牌标签（目标类型）
            if (Txt_CardTag != null)
            {
                Txt_CardTag.text = GetTargetTypeText(_currentCard.TargetType);
            }

            // 设置左侧消耗（能量）——用有效费用，游侠首张移动等免费效果直接体现在卡面
            if (Txt_LeftCost != null)
            {
                Txt_LeftCost.text = GetEffectiveEnergyCost().ToString();
            }

            // 根据玩家当前能量刷新左上角颜色
            RefreshEnergyAffordability();

            // 条件牌描边：按当前站位/移动状态着色（黄=满足 / 白=不满足）
            RefreshConditionHighlight();

            // 设置右侧消耗（卡牌类型）
            bool isExecution = _currentCard.CardType == cfg.CardTypeEnum.Execution;
            bool isCharge = _currentCard.CardType == cfg.CardTypeEnum.Charge;
            if (Txt_RightCost != null)
            {
                Txt_RightCost.text = isExecution ? "执" : isCharge ? "蓄" : "迅";
            }

            // 执行（延时）卡牌左上角能量底图变橙色
            if (Img_CostLeft != null)
            {
                if (!_defaultLeftCostImageColorCaptured)
                {
                    _defaultLeftCostImageColor = Img_CostLeft.color;
                    _defaultLeftCostImageColorCaptured = true;
                }
                Img_CostLeft.color = (isExecution || isCharge) ? executionCostTint : _defaultLeftCostImageColor;
                Debug.Log($"[CardViewController] {_currentCard.Name} CardType={_currentCard.CardType} isExecution={isExecution} Img_CostLeft.color={Img_CostLeft.color}");
            }
            else
            {
                Debug.LogWarning($"[CardViewController] {_currentCard.Name} Img_CostLeft 为 null，无法设置类型底色");
            }

            // 设置稀有度显示
            UpdateRarityDisplay();

            // 加载卡牌图片资源
            LoadCardSprite(_currentCard.Id);
        }

        /// <summary>
        /// 获取目标类型文本
        /// </summary>
        private string GetTargetTypeText(TargetTypeEnum targetType)
        {
            switch (targetType)
            {
                case TargetTypeEnum.SingleAlly:
                    return "队友";
                case TargetTypeEnum.AllAlly:
                    return "队友们";
                case TargetTypeEnum.Self:
                    return "自己";
                case TargetTypeEnum.SingleEnemy:
                    return "敌人";
                case TargetTypeEnum.AllEnemy:
                    return "敌人们";
                case TargetTypeEnum.TimeSlot:
                    return "时间轴";
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// 更新稀有度显示
        /// </summary>
        private void UpdateRarityDisplay()
        {
            // 隐藏所有稀有度图标
            if (Img_Rarity01 != null) Img_Rarity01.gameObject.SetActive(false);
            if (Img_Rarity02 != null) Img_Rarity02.gameObject.SetActive(false);
            if (Img_Rarity03 != null) Img_Rarity03.gameObject.SetActive(false);

            // 根据稀有度显示对应的星级
            int rarityLevel = (int)_currentCard.Rarity + 1; // -2=临时(0星), -1=基础(0星), 0=普通(1星), 1=稀有(2星), 2=史诗(3星)

            if (rarityLevel >= 1 && Img_Rarity01 != null)
            {
                Img_Rarity01.gameObject.SetActive(true);
            }

            if (rarityLevel >= 2 && Img_Rarity02 != null)
            {
                Img_Rarity02.gameObject.SetActive(true);
            }

            if (rarityLevel >= 3 && Img_Rarity03 != null)
            {
                Img_Rarity03.gameObject.SetActive(true);
            }
        }

        #endregion

        #region 鼠标事件处理

        /// <summary>
        /// 鼠标进入卡牌
        /// </summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            // 如果正在拖拽或处于点击目标选择中，不处理悬停效果
            if (_isDragging || _isClickTargeting)
                return;

            if (_executionSuppressed)
                return;

            _isHovering = true;
            // 仅在没有正在进行的悬停位移动画时才重新记录基准位置。
            // 否则快速切换/反复进出时，会把"上浮/回落中"的中间位置误当作基准，
            // 导致每次进入都在上一次的偏移基础上再抬升，卡牌不断往上走。
            if (_rectTransform != null && (_hoverMoveTween == null || !_hoverMoveTween.IsActive()))
            {
                _hoverBaseAnchoredPosition = _rectTransform.anchoredPosition;
            }

            // 显示轮廓
            if (Img_Outline != null)
            {
                Img_Outline.gameObject.SetActive(true);
            }

            // 提升渲染层级（脱离mask，显示在最前）
            ElevateCard();

            // 只放大Card子对象，不放大CardTimeSlot
            _scaleTween?.Kill();
            if (Card != null && Card.transform != null)
            {
                _scaleTween = Card.transform.DOScale(_originalCardScale * hoverScale, scaleDuration)
                    .SetEase(Ease.OutBack);
            }
            PlayHoverLift(true);
            ShiftHoverNeighbors();
        }

        /// <summary>
        /// 鼠标离开卡牌
        /// </summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            // 如果正在拖拽或处于点击目标选择中，不处理离开效果
            if (_isDragging || _isClickTargeting)
                return;

            _isHovering = false;

            // 隐藏轮廓——但条件牌的描边是常驻的（随条件黄/白），离开时不收起，改为按当前条件重着色
            if (Img_Outline != null)
            {
                if (_isSelfConditionCard)
                {
                    RefreshConditionHighlight();
                }
                else
                {
                    Img_Outline.gameObject.SetActive(false);
                }
            }

            // 恢复原始层级
            RestoreCard();

            // 恢复Card子对象的原始大小
            _scaleTween?.Kill();
            if (Card != null && Card.transform != null)
            {
                _scaleTween = Card.transform.DOScale(_originalCardScale, scaleDuration)
                    .SetEase(Ease.OutBack);
            }
            PlayHoverLift(false);
            RestoreHoverNeighbors();

            // 隐藏描述面板
            HideDescription();
        }

        /// <summary>
        /// 鼠标点击卡牌
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            // View模式：点击加入卡组
            if (_displayMode == DescriptionMode.View && _currentCard != null)
            {
                // 发送选择卡牌到卡组的事件
                GameEvent.Publish(new SelectCardToDeckEvent
                {
                    cardInfo = _currentCard
                });

                Debug.Log($"[CardViewController] 选择卡牌到卡组: {_currentCard.Name}");
                return;
            }

            // 战斗模式：点击卡牌进入目标选择模式（等同进入拖拽选目标），无需按住拖动
            if (_displayMode == DescriptionMode.Battle)
            {
                HandleBattleClick(eventData);
            }
        }

        /// <summary>
        /// 战斗模式下点击手牌：进入点击式目标选择模式
        /// </summary>
        private void HandleBattleClick(PointerEventData eventData)
        {
            // 拖拽过程中不响应点击
            if (_isDragging)
                return;

            // 已在点击选择中：再次点击卡牌视为取消
            if (_isClickTargeting)
            {
                CancelClickTargeting();
                return;
            }
            if (_isCastTargeting)
            {
                CancelCastTargeting();
                return;
            }

            if (_isLocked || _executionSuppressed)
                return;

            // 仅手牌中的卡牌可点选
            if (_cardDragState != CardDragState.OnHand)
                return;

            // 能量不足不允许使用
            if (!HasEnoughEnergyForCard())
            {
                Debug.Log($"[CardViewController] 能量不足，无法选择目标: {_currentCard?.Name} (需求={_currentCard?.Energy})");
                return;
            }

            // 【前排/后排】站位不满足不允许使用
            if (!IsCastZoneSatisfiedForOwner())
            {
                Debug.Log($"[CardViewController] 站位不满足，无法选择目标: {_currentCard?.Name} (CastZone={_currentCard?.CastZone})");
                return;
            }

            if (_currentCard != null && _currentCard.TargetType == cfg.TargetTypeEnum.TimeSlot)
            {
                BeginCastTargeting();
                return;
            }

            if (!UsesTargetSelection())
                return;

            BeginClickTargeting();
        }

        #endregion

        #region 层级管理

        /// <summary>
        /// 提升卡牌层级（脱离mask，显示在最前）
        /// </summary>
        private void ElevateCard()
        {
            if (_hoverCanvas == null) return;

            // 启用Canvas覆盖排序
            _hoverCanvas.overrideSorting = true;
            _hoverCanvas.sortingOrder = hoverSortingOrder;

            Debug.Log($"[CardViewController] 卡牌提升层级: sortingOrder={hoverSortingOrder}");
        }

        /// <summary>
        /// 恢复卡牌原始层级
        /// </summary>
        private void RestoreCard()
        {
            if (_hoverCanvas == null) return;

            // 禁用Canvas覆盖排序
            _hoverCanvas.overrideSorting = false;

            Debug.Log("[CardViewController] 卡牌恢复原始层级");
        }

        /// <summary>
        /// 播放悬停位移动画（向上/回落）
        /// </summary>
        private void PlayHoverLift(bool entering)
        {
            if (_rectTransform == null) return;

            _hoverMoveTween?.Kill();
            Vector2 targetPos = entering
                ? _hoverBaseAnchoredPosition + new Vector2(0f, hoverLiftDistance)
                : _hoverBaseAnchoredPosition;

            _hoverMoveTween = _rectTransform.DOAnchorPos(targetPos, scaleDuration)
                .SetEase(Ease.OutCubic);
        }

        /// <summary>
        /// 立即重置悬停位移，避免状态切换后残留偏移
        /// </summary>
        private void ForceResetHoverLift()
        {
            if (_rectTransform == null) return;
            _hoverMoveTween?.Kill();
            _rectTransform.anchoredPosition = _hoverBaseAnchoredPosition;
            RestoreHoverNeighbors(true);
        }

        /// <summary>按当前手牌槽位给左右相邻牌让出放大后的半宽，避免悬停牌覆盖邻牌。</summary>
        private void ShiftHoverNeighbors()
        {
            RestoreHoverNeighbors(true);
            Transform parent = transform.parent;
            RectTransform visualRect = Card != null ? Card.transform as RectTransform : null;
            if (parent == null || visualRect == null || hoverScale <= 1f) return;

            float overflow = visualRect.rect.width * Mathf.Abs(_originalCardScale.x) * (hoverScale - 1f) * 0.5f;
            if (overflow <= 0f) return;

            var handSiblings = new System.Collections.Generic.List<RectTransform>();
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (!child.gameObject.activeSelf || child.GetComponent<CardViewController>() == null) continue;
                if (child is RectTransform siblingRect) handSiblings.Add(siblingRect);
            }

            int slot = handSiblings.IndexOf(_rectTransform);
            if (slot < 0) return;
            ShiftHoverNeighbor(slot > 0 ? handSiblings[slot - 1] : null, -overflow);
            ShiftHoverNeighbor(slot + 1 < handSiblings.Count ? handSiblings[slot + 1] : null, overflow);
        }

        private void ShiftHoverNeighbor(RectTransform neighbor, float offset)
        {
            if (neighbor == null) return;
            float originalX = neighbor.anchoredPosition.x;
            _hoverShiftedSiblings.Add(neighbor);
            _hoverShiftedSiblingOriginalX.Add(originalX);
            _hoverNeighborTweens.Add(neighbor.DOAnchorPosX(originalX + offset, scaleDuration).SetEase(Ease.OutCubic));
        }

        private void RestoreHoverNeighbors(bool immediate = false)
        {
            foreach (Tween tween in _hoverNeighborTweens)
                tween?.Kill();
            _hoverNeighborTweens.Clear();

            for (int i = 0; i < _hoverShiftedSiblings.Count; i++)
            {
                RectTransform neighbor = _hoverShiftedSiblings[i];
                if (neighbor == null || i >= _hoverShiftedSiblingOriginalX.Count) continue;
                float originalX = _hoverShiftedSiblingOriginalX[i];
                if (immediate || scaleDuration <= 0f)
                    neighbor.anchoredPosition = new Vector2(originalX, neighbor.anchoredPosition.y);
                else
                    _hoverNeighborTweens.Add(neighbor.DOAnchorPosX(originalX, scaleDuration).SetEase(Ease.OutCubic));
            }

            // 缓动回位期间保留原始槽位；若玩家快速切到另一张牌，下一次 immediate 恢复仍能
            // 从真正的布局基准开始，避免把 Tween 中间值累计成新的偏移。
            if (immediate || scaleDuration <= 0f)
            {
                _hoverShiftedSiblings.Clear();
                _hoverShiftedSiblingOriginalX.Clear();
            }
        }

        /// <summary>
        /// 使用本牌时，将同一手牌容器下的其余卡牌向下位移让位（本牌保持原位/跟随鼠标），
        /// 避免其他手牌遮挡目标箭头、敌人意图等信息。
        /// </summary>
        private void PushDownOtherHandCards()
        {
            // 先立即完成上一轮回位。若在回位 Tween 尚未结束时再次选择，
            // 直接记录当前中间位置会让整排手牌每次都继续向下累积偏移。
            RestoreOtherHandCards(true);

            Transform parent = transform.parent;
            if (parent == null || otherCardsPushDownDistance <= 0f)
                return;

            int childCount = parent.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child == transform)
                    continue;
                if (!child.gameObject.activeSelf)
                    continue;
                if (child.GetComponent<CardViewController>() == null)
                    continue;

                RectTransform rt = child as RectTransform;
                if (rt == null)
                    continue;

                float originalY = rt.anchoredPosition.y;
                _pushedSiblings.Add(rt);
                _pushedSiblingOriginalY.Add(originalY);

                // 仅动画 Y，X 交由手牌布局负责，避免与 UpdateHandLayout 冲突
                rt.DOKill();
                rt.DOAnchorPosY(originalY - otherCardsPushDownDistance, positionRestoreDuration)
                    .SetEase(Ease.OutCubic);
            }
        }

        /// <summary>
        /// 恢复被 PushDownOtherHandCards 下移的其余手牌位置
        /// </summary>
        private void RestoreOtherHandCards(bool immediate = false)
        {
            for (int i = 0; i < _pushedSiblings.Count; i++)
            {
                RectTransform rt = _pushedSiblings[i];
                if (rt == null)
                    continue;

                rt.DOKill();
                if (immediate || positionRestoreDuration <= 0f)
                {
                    rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, _pushedSiblingOriginalY[i]);
                }
                else
                {
                    rt.DOAnchorPosY(_pushedSiblingOriginalY[i], positionRestoreDuration)
                        .SetEase(Ease.OutCubic);
                }
            }

            _pushedSiblings.Clear();
            _pushedSiblingOriginalY.Clear();
        }

        #endregion

        #region 点击式目标选择（点击卡牌进入，等同拖拽选目标）

        /// <summary>
        /// 进入点击式目标选择：与拖拽进入目标选择的视觉/状态一致，但卡牌停在手牌中，
        /// 由 Update 每帧根据鼠标更新箭头与高亮，左键确认、右键/Esc取消。
        /// </summary>
        private void BeginClickTargeting()
        {
            _isClickTargeting = true;
            _isTargeting = true;
            _currentTargetObject = null;
            _lastValidTargetObject = null;
            _selectedMultiTargetObjects.Clear();

            // 其余手牌让位
            PushDownOtherHandCards();

            // 设置所有目标的颜色（非法变黑，合法变暗）
            SetAllTargetsColor();

            // 提升层级
            ElevateCard();

            // 放大Card子对象
            _scaleTween?.Kill();
            if (Card != null && Card.transform != null)
            {
                _scaleTween = Card.transform.DOScale(_originalCardScale * dragScale, scaleDuration)
                    .SetEase(Ease.OutBack);
            }

            // 半透明
            SetCanvasGroupAlpha(dragAlpha);

            // 关闭自身射线：选择期间确认/取消统一由 Update 的全局鼠标输入处理，
            // 避免卡牌自身的 OnPointerClick 与之重复触发
            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = false;
            }

            // 隐藏描述面板，避免遮挡
            HideDescription();

            // 初始化并显示目标箭头
            if (_targetArrow != null)
            {
                _targetArrow.UpdateLine(GetCardScreenCenter(), Input.mousePosition, false);
                _targetArrow.Show();
            }

            Debug.Log($"[CardViewController] 点击进入目标选择模式: {_currentCard?.Name}");
        }

        /// <summary>
        /// 点击式目标选择的每帧逻辑：更新箭头/高亮，处理确认与取消输入
        /// </summary>
        private void UpdateClickTargeting()
        {
            if (_currentCard == null)
            {
                CancelClickTargeting();
                return;
            }

            // 多目标牌：右键确认已选择的目标；Esc 始终取消。
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelClickTargeting();
                return;
            }
            if (Input.GetMouseButtonDown(1))
            {
                if (UsesMultiAllySelection() && _selectedMultiTargetObjects.Count > 0)
                {
                    ConfirmMultiTargeting(GetOwnerCharacterId());
                }
                else
                {
                    CancelClickTargeting();
                }
                return;
            }

            PointerEventData ped = new PointerEventData(EventSystem.current)
            {
                position = Input.mousePosition
            };

            // 检测目标
            GameObject targetObj = _targetManager?.DetectTargetUnderMouse(ped);
            CharacterEnum ownerCharacterId = GetOwnerCharacterId();
            bool isValid = _targetManager?.IsValidTarget(targetObj, _currentCard.TargetType, ownerCharacterId, _currentCard.TargetZone) ?? false;

            // 更新箭头颜色
            if (_targetArrow != null)
            {
                _targetArrow.UpdateLine(GetCardScreenCenter(), Input.mousePosition, isValid);
            }

            // 更新高亮
            UpdateTargetHighlighting(targetObj, isValid);

            _currentTargetObject = targetObj;
            if (isValid && targetObj != null)
                _lastValidTargetObject = targetObj;

            // 确认：左键命中合法目标才出牌；点空白/非法目标不退出，继续选择（退出只用右键）
            if (Input.GetMouseButtonDown(0) && isValid)
            {
                if (UsesMultiAllySelection())
                {
                    ToggleMultiTarget(targetObj);
                    if (_selectedMultiTargetObjects.Count >= 3)
                    {
                        ConfirmMultiTargeting(ownerCharacterId);
                    }
                }
                else
                {
                    ConfirmClickTargeting(targetObj, ownerCharacterId);
                }
            }
        }

        private bool UsesMultiAllySelection() => _currentCard?.Id == "Zhouzhou023";

        private void ToggleMultiTarget(GameObject target)
        {
            if (target == null) return;
            if (_selectedMultiTargetObjects.Contains(target))
            {
                _selectedMultiTargetObjects.Remove(target);
                return;
            }
            if (_selectedMultiTargetObjects.Count < 3)
            {
                _selectedMultiTargetObjects.Add(target);
            }
        }

        private void ConfirmMultiTargeting(CharacterEnum ownerCharacterId)
        {
            string targetIds = string.Join("|", _selectedMultiTargetObjects
                .Select(GetTargetId)
                .Where(id => !string.IsNullOrEmpty(id)));
            if (string.IsNullOrEmpty(targetIds))
            {
                CancelClickTargeting();
                return;
            }

            ExitClickTargetingVisuals();
            PlaceCardOnTargetIds(targetIds, ownerCharacterId.ToString());
            _currentTargetObject = null;
            _lastValidTargetObject = null;
            _selectedMultiTargetObjects.Clear();
        }

        /// <summary>
        /// 左键命中合法目标后出牌
        /// </summary>
        private void ConfirmClickTargeting(GameObject resolvedTarget, CharacterEnum ownerCharacterId)
        {
            // 先收尾视觉与高亮（与拖拽结束顺序一致）
            ExitClickTargetingVisuals();

            string ownerId = ownerCharacterId.ToString();
            PlaceCardOnTargetTimeline(resolvedTarget, ownerId);

            _currentTargetObject = null;
            _lastValidTargetObject = null;
            _selectedMultiTargetObjects.Clear();
        }

        /// <summary>
        /// 取消点击式目标选择，恢复手牌
        /// </summary>
        private void CancelClickTargeting()
        {
            ExitClickTargetingVisuals();
            RestoreCardToHandState("点击选择：取消");
            _currentTargetObject = null;
            _lastValidTargetObject = null;
        }

        /// <summary>
        /// 收尾点击式目标选择的视觉/高亮（不含卡牌自身缩放/透明度恢复，交由调用方决定）
        /// </summary>
        private void ExitClickTargetingVisuals()
        {
            _isClickTargeting = false;
            _isTargeting = false;

            // 恢复自身射线
            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = true;
            }

            RestoreOtherHandCards();

            if (_targetArrow != null)
            {
                _targetArrow.Hide();
            }

            ClearAllTargetHighlighting();
            RestoreAllTargetsColor();
            _previousHoveredTarget = null;
            _selectedMultiTargetObjects.Clear();
        }

        /// <summary>选择一张己方在轨执行牌。TimeSlot 在当前 ATB 架构中专用于这一目标类型。</summary>
        private void BeginCastTargeting()
        {
            var manager = Ashlight.Battle.BattleManager.Instance;
            string ownerUnitId = ResolveOwnerUnitId(GetOwnerCharacterId().ToString());
            if (manager == null || string.IsNullOrEmpty(ownerUnitId) || _currentCard == null)
                return;

            bool requireDamage = _currentCard.Effects != null
                                 && _currentCard.Effects.Any(e => e is CastDamageBonusEffect);
            bool requireNumeric = _currentCard.Effects != null
                                  && _currentCard.Effects.Any(e => e is CastEchoEffect);
            var allowed = manager.GetPendingCasts()
                .Where(c => manager.IsFriendlyPendingCast(c.CastId, ownerUnitId, requireDamage, requireNumeric))
                .Select(c => c.CastId)
                .ToList();

            _castTargetView = FindObjectOfType<TurnOrderView>();
            if (_castTargetView == null || !_castTargetView.BeginCastSelection(allowed, OnCastTargetSelected))
            {
                Debug.LogWarning($"[CardViewController] 没有可选择的己方在轨执行牌: {_currentCard.Name}");
                _castTargetView?.EndCastSelection();
                _castTargetView = null;
                return;
            }

            _isCastTargeting = true;
            PushDownOtherHandCards();
            ElevateCard();
            SetCanvasGroupAlpha(dragAlpha);
            // 执行牌目标在行动顺序轴上。当前手牌被提升到最前层后若仍拦截射线，
            // 会遮住下方的引线卡，造成金框目标难以点击或完全点不到。
            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = false;
            }
            HideDescription();
            Debug.Log($"[CardViewController] 进入执行牌目标选择: {_currentCard.Name}, 候选={allowed.Count}");
        }

        private void OnCastTargetSelected(string castId)
        {
            if (!_isCastTargeting || _currentCard == null || string.IsNullOrEmpty(castId)) return;

            string ownerUnitId = ResolveOwnerUnitId(GetOwnerCharacterId().ToString());
            var manager = Ashlight.Battle.BattleManager.Instance;
            ExitCastTargetingVisuals();
            if (manager == null || string.IsNullOrEmpty(ownerUnitId)
                || !manager.TryPlayCardImmediately(_currentCard, ownerUnitId, castId, InstanceId))
            {
                RestoreCardToHandState("执行牌目标结算失败");
                return;
            }

            var battleScene = FindObjectOfType<UI_BattleScene>();
            battleScene?.ConsumeHandCard(this);
            battleScene?.RefreshHandFromData();
            battleScene?.ApplyPendingScheduleChanges();
            Debug.Log($"[CardViewController] 执行牌目标卡结算完成: {_currentCard.Name} -> {castId}");
        }

        private void CancelCastTargeting()
        {
            ExitCastTargetingVisuals();
            RestoreCardToHandState("执行牌目标选择取消");
        }

        private void ExitCastTargetingVisuals()
        {
            _isCastTargeting = false;
            _castTargetView?.EndCastSelection();
            _castTargetView = null;
            RestoreOtherHandCards();
            SetCanvasGroupAlpha(1f);
            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = true;
            }
            RestoreCard();
        }

        /// <summary>
        /// 获取卡牌中心的屏幕坐标（作为箭头起点）
        /// </summary>
        private Vector3 GetCardScreenCenter()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera cam = canvas?.renderMode == RenderMode.ScreenSpaceOverlay ? null : (canvas?.worldCamera ?? Camera.main);
            return RectTransformUtility.WorldToScreenPoint(cam, transform.position);
        }

        #endregion

        #region 拖拽处理（战斗模式）

        /// <summary>
        /// 开始拖拽（仅在战斗模式下启用）
        /// 根据 Card 的状态（OnHand 或 OnTime）决定拖拽行为
        /// </summary>
        public void OnBeginDrag(PointerEventData eventData)
        {
            // 只在战斗模式下启用拖拽
            if (_displayMode != DescriptionMode.Battle)
                return;

            // 如果卡片已被锁定，不允许拖拽
            if (_isLocked)
            {
                Debug.Log($"[CardViewController] 卡片已锁定，不允许拖拽: {_currentCard?.Name}");
                return;
            }

            if (_executionSuppressed)
            {
                return;
            }

            // 能量不足时禁止从手牌拖出（已经放到时间轴上的牌不受影响，那张牌的能量已被扣除）
            if (_cardDragState != CardDragState.OnTime && !HasEnoughEnergyForCard())
            {
                Debug.Log($"[CardViewController] 能量不足，无法拖拽: {_currentCard?.Name} (需求={_currentCard?.Energy})");
                return;
            }

            // 【前排/后排】站位不满足时禁止从手牌拖出
            if (_cardDragState != CardDragState.OnTime && !IsCastZoneSatisfiedForOwner())
            {
                Debug.Log($"[CardViewController] 站位不满足，无法拖拽: {_currentCard?.Name} (CastZone={_currentCard?.CastZone})");
                return;
            }

            if (_cardDragState == CardDragState.OnHand
                && _currentCard != null
                && _currentCard.TargetType == cfg.TargetTypeEnum.TimeSlot)
            {
                BeginCastTargeting();
                return;
            }

            _isDragging = true;
            ForceResetHoverLift();

            // 根据 Card 的状态决定拖拽行为
            if (_cardDragState == CardDragState.OnTime)
            {
                // 在时间轴上：处理时间轴拖拽
                OnBeginDragOnTime(eventData);
            }
            else
            {
                // 在手牌中：处理手牌拖拽
                OnBeginDragOnHand(eventData);
            }
        }
        
        /// <summary>
        /// 在手牌中开始拖拽
        /// </summary>
        private void OnBeginDragOnHand(PointerEventData eventData)
        {
            // 使用本牌时，把其余手牌向下让位，避免遮挡目标/箭头/信息
            PushDownOtherHandCards();

            // 节奏原型：拖起非 0 费牌便预览该角色下次行动的插入位置。
            // 此处只读印刷费用，不触碰真实 ATB；成功出牌后才会正式重排。
            if (!_tempoActionPreviewStarted
                && TempoPrototypeMode.IsActive
                && _currentCard != null
                && _currentCard.Energy > 0)
            {
                string previewOwnerId = ResolveOwnerUnitId(GetOwnerCharacterId().ToString());
                if (!string.IsNullOrEmpty(previewOwnerId))
                {
                    _tempoActionPreviewStarted = true;
                    FindObjectOfType<UI_BattleScene>()?.BeginTempoActionPreview(previewOwnerId, _currentCard.Energy);
                }
            }

            // 判断是否使用目标选择模式
            bool usesTargetSelection = UsesTargetSelection();

            Debug.Log($"[CardViewController] OnBeginDragOnHand: usesTargetSelection={usesTargetSelection}, TargetType={_currentCard?.TargetType}, _targetArrow={(_targetArrow != null ? "存在" : "null")}, _targetManager={(_targetManager != null ? "存在" : "null")}");

            if (usesTargetSelection)
            {
                // 目标选择模式：卡牌保持在手牌区
                _isTargeting = true;

                // 不保存原始位置，不移动卡牌

                // 设置所有目标的颜色（非法变黑，合法变暗）
                SetAllTargetsColor();

                // 初始化并显示目标箭头
                if (_targetArrow != null)
                {
                    Debug.Log("[CardViewController] 显示目标箭头");
                    // 先更新箭头位置,再显示
                    // 将卡牌中心转换为屏幕坐标
                    Canvas canvas = GetComponentInParent<Canvas>();
                    Camera cam = canvas?.renderMode == RenderMode.ScreenSpaceOverlay ? null : (canvas?.worldCamera ?? Camera.main);
                    Vector3 cardScreenPos = RectTransformUtility.WorldToScreenPoint(cam, transform.position);
                    Vector3 mousePos = eventData.position;

                    Debug.Log($"[CardViewController] 箭头起点(屏幕坐标): {cardScreenPos}, 终点(屏幕坐标): {mousePos}");

                    _targetArrow.UpdateLine(cardScreenPos, mousePos, false); // 初始为红色
                    _targetArrow.Show();
                }
                else
                {
                    Debug.LogWarning("[CardViewController] _targetArrow为null,无法显示箭头!");
                }

                // 提升层级
                ElevateCard();

                // 放大Card子对象
                _scaleTween?.Kill();
                if (Card != null && Card.transform != null)
                {
                    _scaleTween = Card.transform.DOScale(_originalCardScale * dragScale, scaleDuration)
                        .SetEase(Ease.OutBack);
                }

                // 设置透明度
                SetCanvasGroupAlpha(dragAlpha);

                Debug.Log($"[CardViewController] 进入目标选择模式: {_currentCard?.Name}");
            }
            else
            {
                // 时间轴拖拽模式(TimeSlot类型)
                // 保存原始位置
                _originalDragPosition = transform.localPosition;

                // 计算拖拽偏移量
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    transform.parent as RectTransform,
                    eventData.position,
                    eventData.pressEventCamera,
                    out Vector2 localPoint);

                _dragOffset = transform.localPosition - new Vector3(localPoint.x, localPoint.y, 0f);

                // 停止之前的位置动画（如果有）
                _positionTween?.Kill();

                // 提升层级
                ElevateCard();

                // 只放大Card子对象，不放大CardTimeSlot
                _scaleTween?.Kill();
                if (Card != null && Card.transform != null)
                {
                    _scaleTween = Card.transform.DOScale(_originalCardScale * dragScale, scaleDuration)
                        .SetEase(Ease.OutBack);
                }

                // 设置透明度（使用统一方法，同时设置 raycastTarget 和 interactable）
                SetCanvasGroupAlpha(dragAlpha);

                if (enableDetailedDebugLogs)
                {
                    Debug.Log($"[CardViewController] 开始拖拽卡牌（时间轴模式）: {_currentCard?.Name}");
                }
            }
        }
        
        /// <summary>
        /// 在时间轴上开始拖拽
        /// </summary>
        private void OnBeginDragOnTime(PointerEventData eventData)
        {
            if (_parentTrack == null)
            {
                Debug.LogWarning("[CardViewController] 在时间轴上但缺少父轨道，无法拖拽");
                _isDragging = false;
                return;
            }
            
            // 如果正在悬停 CardTimeSlot 或 Card 是显示的，立即恢复 Card 的位置和隐藏状态
            if (Card != null)
            {
                RectTransform cardRect = Card.GetComponent<RectTransform>();
                if (cardRect != null)
                {
                    // 如果 Card 是显示的（alpha > 0），说明正在悬停状态
                    bool isCardVisible = Card.alpha > 0.01f;
                    
                    if (isCardVisible || _isCardTimeSlotHovering)
                    {
                        // 如果有保存的原始位置，使用它；否则使用当前位置（可能是偏移后的位置）
                        if (_originalCardPosition != Vector2.zero)
                        {
                            cardRect.anchoredPosition = _originalCardPosition;
                        }
                        else
                        {
                            // 如果没有保存原始位置，尝试从当前位置减去偏移量
                            Vector2 currentPos = cardRect.anchoredPosition;
                            cardRect.anchoredPosition = currentPos - new Vector2(200f, 0f);
                            _originalCardPosition = cardRect.anchoredPosition; // 保存为原始位置
                        }
                        
                        // 立即隐藏 Card
                        Card.alpha = 0f;
                        _isCardTimeSlotHovering = false;
                        
                        // 停止可能正在进行的 DOTween 动画
                        DOTween.Kill(cardRect);
                        
                        if (enableDetailedDebugLogs)
                        {
                            Debug.Log($"[CardViewController] 开始拖拽：恢复 Card 位置并隐藏，原始位置={_originalCardPosition}");
                        }
                    }
                }
            }
            
            // 保存整个 CardViewController 的原始位置和父级
            _originalTimePosition = transform.position;
            RectTransform rect = GetComponent<RectTransform>();
            if (rect != null)
            {
                _originalTimeAnchoredPosition = rect.anchoredPosition;
            }
            _originalTimeParent = transform.parent;
            
            // 在 CardViewController 上提升层级（检查是否已存在）
            Canvas dragCanvas = gameObject.GetComponent<Canvas>();
            if (dragCanvas == null)
            {
                dragCanvas = gameObject.AddComponent<Canvas>();
            }
            dragCanvas.overrideSorting = true;
            dragCanvas.sortingOrder = 1000;
            
            if (gameObject.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }
            
            // 设置半透明并禁用射线检测（避免挡住下方的UI）
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0.6f;
                _canvasGroup.blocksRaycasts = false;
            }
            
            // 禁用所有 Image 的 raycastTarget（避免阻塞 raycast）
            Image[] images = GetComponentsInChildren<Image>(true);
            foreach (Image img in images)
            {
                if (img != null)
                {
                    img.raycastTarget = false;
                }
            }
            
            // 清除当前高亮
            _parentTrack.ClearHighlight();
            
            if (enableDetailedDebugLogs)
            {
                Debug.Log($"[CardViewController] 开始拖拽卡牌（时间轴）: {_currentCard?.Name}");
            }
        }

        /// <summary>
        /// 拖拽中（仅在战斗模式下启用）
        /// 根据 Card 的状态决定拖拽行为
        /// </summary>
        public void OnDrag(PointerEventData eventData)
        {
            // 只在战斗模式下启用拖拽
            if (_displayMode != DescriptionMode.Battle || !_isDragging)
                return;

            // 根据 Card 的状态决定拖拽行为
            if (_cardDragState == CardDragState.OnTime)
            {
                // 在时间轴上：处理时间轴拖拽
                OnDragOnTime(eventData);
            }
            else
            {
                // 在手牌中：处理手牌拖拽
                OnDragOnHand(eventData);
            }
        }
        
        /// <summary>
        /// 在手牌中拖拽
        /// </summary>
        private void OnDragOnHand(PointerEventData eventData)
        {
            if (_isTargeting)
            {
                // 目标选择模式：卡牌保持在原位，只更新箭头和高亮
                // 将卡牌中心转换为屏幕坐标
                Canvas canvas = GetComponentInParent<Canvas>();
                Camera cam = canvas?.renderMode == RenderMode.ScreenSpaceOverlay ? null : (canvas?.worldCamera ?? Camera.main);
                Vector3 cardScreenPos = RectTransformUtility.WorldToScreenPoint(cam, transform.position);
                Vector3 mousePos = eventData.position;

                // 检测目标
                GameObject targetObj = _targetManager?.DetectTargetUnderMouse(eventData);
                CharacterEnum ownerCharacterId = GetOwnerCharacterId();
                bool isValid = _targetManager?.IsValidTarget(targetObj, _currentCard.TargetType, ownerCharacterId, _currentCard.TargetZone) ?? false;

                // 更新箭头颜色
                if (_targetArrow != null)
                {
                    _targetArrow.UpdateLine(cardScreenPos, mousePos, isValid);
                }

                // 更新角色高亮
                UpdateTargetHighlighting(targetObj, isValid);

                _currentTargetObject = targetObj;
                if (isValid && targetObj != null)
                    _lastValidTargetObject = targetObj;
            }
            else
            {
                // 时间轴拖拽模式
                // 更新卡牌位置跟随鼠标
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    transform.parent as RectTransform,
                    eventData.position,
                    eventData.pressEventCamera,
                    out Vector2 localPoint);

                transform.localPosition = new Vector3(localPoint.x, localPoint.y, 0f) + _dragOffset;

                // 在拖拽过程中检测是否拖拽到时间轴
                bool isOverTimeSlot = CheckDragToTimeSlot(eventData);
            
            if (isOverTimeSlot && !_isInTimeSlot)
            {
                // 如果刚进入时间轴区域，立即切换到时间轴状态
                _isInTimeSlot = true;
                ShowCardTimeSlot();
                // 隐藏Card子对象（设置Card的alpha为0）
                if (Card != null)
                {
                    Card.alpha = 0f;
                }
                
                // 调整拖拽偏移量，使鼠标指针正好在 CardTimeSlot 的中心
                if (CardTimeSlot != null && _currentCard != null)
                {
                    // 获取 CardTimeSlot 的 RectTransform
                    RectTransform cardTimeSlotRect = CardTimeSlot.GetComponent<RectTransform>();
                    if (cardTimeSlotRect != null)
                    {
                        // 获取 CardTimeSlot 中心相对于 CardViewController 的本地位置
                        Vector3 cardTimeSlotCenterLocal = cardTimeSlotRect.localPosition;
                        
                        // 将鼠标位置转换为 CardViewController 父级的本地坐标
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            transform.parent as RectTransform,
                            eventData.position,
                            eventData.pressEventCamera,
                            out Vector2 mouseLocalPoint);
                        
                        // 调整偏移量，使鼠标指针在 CardTimeSlot 的中心
                        // 当 CardViewController 的位置是 mouseLocalPoint + _dragOffset 时，
                        // CardTimeSlot 的中心应该在 mouseLocalPoint
                        // 所以：mouseLocalPoint + _dragOffset + cardTimeSlotCenterLocal = mouseLocalPoint
                    // 因此：_dragOffset = -cardTimeSlotCenterLocal
                    _dragOffset = -new Vector3(cardTimeSlotCenterLocal.x, cardTimeSlotCenterLocal.y, 0f);
                    
                    if (enableDetailedDebugLogs)
                    {
                        Debug.Log($"[CardViewController] 调整拖拽偏移: CardTimeSlot中心={cardTimeSlotCenterLocal}, 新偏移={_dragOffset}");
                    }
                }
            }
            
            if (enableDetailedDebugLogs)
            {
                Debug.Log($"[CardViewController] 拖拽到时间轴，切换到时间轴状态: {_currentCard?.Name}");
            }
            }
            else if (!isOverTimeSlot && _isInTimeSlot)
            {
                // 如果离开时间轴区域，恢复卡牌显示（恢复拖拽时的透明度）
                _isInTimeSlot = false;
                HideCardTimeSlot();
                // 恢复Card的透明度（1.0，因为Card本身不受拖拽透明度影响）
                if (Card != null)
                {
                    Card.alpha = 1f;
                }
                // 清除高亮
                ClearTimelineHighlight();
                if (enableDetailedDebugLogs)
                {
                    Debug.Log($"[CardViewController] 离开时间轴区域，恢复卡牌显示: {_currentCard?.Name}");
                }
            }
            
                // 如果在时间轴区域，更新高亮显示
                if (_isInTimeSlot)
                {
                    UpdateTimelineHighlight(eventData);
                }
            }
        }
        
        /// <summary>
        /// 在时间轴上拖拽
        /// </summary>
        private void OnDragOnTime(PointerEventData eventData)
        {
            if (_parentTrack == null) return;
            
            // 整个 CardViewController 跟随鼠标移动
            transform.position = eventData.position;
            
            // 显示 raycast 穿透的对象（仅在调试模式下，避免性能问题）
            if (enableDetailedDebugLogs)
            {
                ShowRaycastDebugInfo(eventData);
            }
            
            // 更新高亮显示
            UpdateDragHighlightOnTime(eventData);
        }

        /// <summary>
        /// 结束拖拽（仅在战斗模式下启用）
        /// 根据 Card 的状态决定拖拽行为
        /// </summary>
        public void OnEndDrag(PointerEventData eventData)
        {
            // 只在战斗模式下启用拖拽
            if (_displayMode != DescriptionMode.Battle || !_isDragging)
            {
                Debug.LogWarning($"[CardViewController] OnEndDrag 被跳过: 显示模式={_displayMode}, 拖拽中={_isDragging}");
                return;
            }

            _isDragging = false;

            // 根据 Card 的状态决定拖拽行为
            if (_cardDragState == CardDragState.OnTime)
            {
                // 在时间轴上：处理时间轴拖拽结束
                OnEndDragOnTime(eventData);
            }
            else
            {
                // 在手牌中：处理手牌拖拽结束
                OnEndDragOnHand(eventData);
                // 成功出牌会先把预览标记为 committed；其他结束路径在这里统一反向收起。
                FindObjectOfType<UI_BattleScene>()?.EndTempoActionPreviewDrag();
                _tempoActionPreviewStarted = false;
            }
        }
        
        /// <summary>
        /// 在手牌中结束拖拽
        /// </summary>
        private void OnEndDragOnHand(PointerEventData eventData)
        {
            // 拖拽结束（无论放置成功与否）先把让位的手牌复位
            RestoreOtherHandCards();

            // 共用规则：只要松手点回到手牌区，就必须撤回，不能使用拖拽途中缓存的目标或时间槽。
            // 该入口同时服务普通 BattleScene 与 TempoPrototype。
            if (IsOverHandArea(eventData))
            {
                _isTargeting = false;
                _isInTimeSlot = false;
                _currentTargetObject = null;
                _lastValidTargetObject = null;
                _previousHoveredTarget = null;
                _cachedSlotUnderPointer = null;
                _lastRaycastFrame = -1;

                if (_targetArrow != null) _targetArrow.Hide();
                ClearAllTargetHighlighting();
                RestoreAllTargetsColor();
                ClearTimelineHighlight();
                HideCardTimeSlot();
                ShowCard();
                RestoreDragPosition();
                RestoreCardToHandState("在手牌区松手");
                return;
            }

            if (_isTargeting)
            {
                // 目标选择模式
                _isTargeting = false;

                // 隐藏箭头
                if (_targetArrow != null)
                {
                    _targetArrow.Hide();
                }

                // 清除高亮
                ClearAllTargetHighlighting();

                // 恢复所有目标的原始颜色
                RestoreAllTargetsColor();
                
                // 清除之前悬停的目标
                _previousHoveredTarget = null;

                // 只认松手瞬间指针下的目标。若松手位置在手牌区，即使拖拽途中经过合法目标也必须返回手牌。
                GameObject resolvedTarget = _currentTargetObject;
                if (resolvedTarget != null)
                {
                    CharacterEnum ownerCharacterId = GetOwnerCharacterId();
                    bool isValid = _targetManager?.IsValidTarget(resolvedTarget, _currentCard.TargetType, ownerCharacterId, _currentCard.TargetZone) ?? false;
                    Debug.Log($"[CardViewController] OnEndDragOnHand 目标选择: target={resolvedTarget.name}, isValid={isValid}, ownerId={ownerCharacterId}, targetType={_currentCard?.TargetType}");

                    if (isValid)
                    {
                        // 放置到目标时间轴的slot 0
                        string ownerId = ownerCharacterId.ToString();
                        PlaceCardOnTargetTimeline(resolvedTarget, ownerId);
                    }
                    else
                    {
                        Debug.LogWarning($"[CardViewController] 目标无效，已恢复到手牌 (target={resolvedTarget.name}, targetType={_currentCard?.TargetType})");
                        RestoreCardToHandState($"目标非法 target={resolvedTarget.name} targetType={_currentCard?.TargetType}");
                    }
                }
                else
                {
                    Debug.LogWarning("[CardViewController] 松手位置不是合法目标，已恢复到手牌");
                    RestoreCardToHandState("拖拽结束时未检测到任何目标");
                }

                _currentTargetObject = null;
                _lastValidTargetObject = null;
            }
            else
            {
                // 时间轴拖拽模式
                bool wasInTimeSlot = _isInTimeSlot;
                _isInTimeSlot = false; // 重置时间轴状态

                // 清除高亮
                ClearTimelineHighlight();

                if (enableDetailedDebugLogs)
                {
                    Debug.Log($"[CardViewController] 结束拖拽（手牌）: wasInTimeSlot={wasInTimeSlot}, 卡牌={_currentCard?.Name}");
                }

                // 清除缓存（拖拽结束后重置）
                _cachedSlotUnderPointer = null;
                _lastRaycastFrame = -1;

                // 检查是否在TimeSlot区域上释放
                // 优先使用已缓存的高亮 slotIndex，确保与高亮显示的位置一致
                Timeline.TimelineSlotView targetSlot = null;
                int slotIndex = -1;
                Timeline.TimelineTrackView track = null;
                
                if (wasInTimeSlot)
                {
                    // 如果之前有高亮显示，优先使用高亮的 slotIndex（确保位置一致）
                    if (_currentHighlightedTrack != null && _currentHighlightedSlotIndex >= 0)
                    {
                        track = _currentHighlightedTrack;
                        slotIndex = _currentHighlightedSlotIndex;
                        
                        // 尝试获取对应的 slot（用于验证）
                        targetSlot = GetTimeSlotUnderPointer(eventData);
                        
                        if (enableDetailedDebugLogs)
                        {
                            Debug.Log($"[CardViewController] 使用高亮显示的 slotIndex: {slotIndex}, 当前检测到的slotIndex: {(targetSlot != null ? targetSlot.SlotIndex.ToString() : "null")}");
                        }
                    }
                    else
                    {
                        // 如果没有高亮显示，则重新检测
                        targetSlot = GetTimeSlotUnderPointer(eventData);
                        if (targetSlot != null)
                        {
                            track = targetSlot.GetParentTrack();
                            slotIndex = targetSlot.SlotIndex;
                        }
                        
                        if (enableDetailedDebugLogs)
                        {
                            Debug.Log($"[CardViewController] 重新检测到的目标格子: {(targetSlot != null ? $"索引 {slotIndex}" : "null")}");
                        }
                    }
                }

                // 如果在TimeSlot上释放，尝试放置卡牌
                if (track != null && _currentCard != null && slotIndex >= 0)
            {
                if (enableDetailedDebugLogs)
                {
                    Debug.Log($"[CardViewController] 在TimeSlot上释放卡牌: {_currentCard.Name}, 索引: {slotIndex}, 轨道: {track.name}");
                }
                
                int totalSlots = 1;
                
                // 检查位置（使用与高亮显示相同的 slotIndex）
                bool canPlaceByPosition = track.GetTrack().CanPlaceCard(slotIndex, totalSlots);
                
                // 检查角色匹配
                bool canPlaceByCharacter = CheckCharacterMatch(track);
                
                // 只有位置和角色都匹配才能放置
                bool canPlace = canPlaceByPosition && canPlaceByCharacter;
                
                if (!canPlace)
                {
                    if (enableDetailedDebugLogs)
                    {
                        string reason = !canPlaceByPosition ? "位置已被占用或超出范围" : "角色不匹配";
                        Debug.LogWarning($"[CardViewController] 位置 {slotIndex} 不可放置（{reason}）");
                    }
                    // 不能放置，继续执行恢复原位的代码
                }
                else
                {
                    // 获取所属角色ID
                    string ownerId = GetOwnerCharacterId().ToString();
                    
                    // 获取目标ID（TODO: 实现目标选择逻辑，暂时使用默认值）
                    string targetId = "enemy_0";
                    
                    // 确保使用与高亮显示相同的 slotIndex（避免索引不一致导致位置偏移）
                    // 直接调用 OnCardDropped，传入正确的 slotIndex
                    Debug.Log($"[CardViewController] 准备放置卡牌: {_currentCard.Name}, slotIndex={slotIndex}, 高亮显示的slotIndex={_currentHighlightedSlotIndex}, 轨道={track.name}");
                    track.OnCardDropped(_currentCard.Id, slotIndex, ownerId, targetId, this);
                    
                    if (enableDetailedDebugLogs)
                    {
                        Debug.Log($"[CardViewController] 整个CardViewController已移动到时间轴: {_currentCard.Name}, slotIndex={slotIndex}");
                    }
                    
                    return;
                }
            }

                // 如果不在TimeSlot上或放置失败，恢复原状
                // 恢复Card子对象的原始大小
                _scaleTween?.Kill();
                if (Card != null && Card.transform != null)
                {
                    _scaleTween = Card.transform.DOScale(_originalCardScale, scaleDuration)
                        .SetEase(Ease.OutBack);
                }

                // 恢复透明度（使用统一方法，同时设置 raycastTarget 和 interactable）
                SetCanvasGroupAlpha(_originalAlpha);

                // 恢复原始层级（如果不在悬停状态）
                if (!_isHovering)
                {
                    RestoreCard();
                }

                // 隐藏CardTimeSlot，显示卡牌（恢复原状）
                HideCardTimeSlot();
                ShowCard();

                // 恢复位置到CardContainer
                RestoreDragPosition();

                if (enableDetailedDebugLogs)
                {
                    Debug.Log($"[CardViewController] 结束拖拽卡牌，已恢复原状: {_currentCard?.Name}");
                }
            }
        }
        
        /// <summary>
        /// 在时间轴上结束拖拽
        /// </summary>
        private void OnEndDragOnTime(PointerEventData eventData)
        {
            if (_parentTrack == null) return;
            
            // 重要：先进行检测，此时 blocksRaycasts 和 raycastTarget 仍然是 false，不会阻塞 raycast
            // 清除高亮
            _parentTrack.ClearHighlight();
            
            // 清除缓存，强制重新检测
            _cachedSlotUnderPointer = null;
            _lastRaycastFrame = -1;
            
            // 检测拖拽目标（在恢复 raycast 之前）
            // 强制输出日志，方便调试
            Debug.Log($"[CardViewController] OnEndDragOnTime 开始检测，鼠标位置: {eventData.position}");
            
            Timeline.TimelineSlotView targetSlot = GetTimeSlotUnderPointer(eventData);
            bool isOverHand = IsOverHandArea(eventData);
            
            // 强制输出检测结果
            Debug.Log($"[CardViewController] OnEndDragOnTime 检测结果: targetSlot={(targetSlot != null ? $"索引 {targetSlot.SlotIndex}, 轨道={targetSlot.GetParentTrack()?.name}" : "null")}, isOverHand={isOverHand}");
            
            // 现在恢复透明度和射线检测（检测完成后）
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.blocksRaycasts = true;
            }
            
            // 恢复所有 Image 的 raycastTarget
            Image[] images = GetComponentsInChildren<Image>(true);
            foreach (Image img in images)
            {
                if (img != null)
                {
                    img.raycastTarget = _originalRaycastTarget;
                }
            }
            
            // 移除拖拽 Canvas
            Canvas dragCanvas = gameObject.GetComponent<Canvas>();
            if (dragCanvas != null && dragCanvas.overrideSorting)
            {
                dragCanvas.overrideSorting = false;
            }
            
            // 重置 raycast 调试计数器
            _raycastDebugFrameCount = 0;
            
            // 根据检测结果处理
            if (targetSlot != null)
            {
                // 拖到时间轴格子上 - 调整位置
                HandleRepositionOnTimeline(targetSlot);
            }
            else if (isOverHand)
            {
                // 拖回手牌区域 - 撤回
                HandleRecallToHand();
            }
            else
            {
                // 其他位置 - 恢复原位
                RestorePositionOnTime();
            }
        }

        /// <summary>
        /// 获取指针下的TimelineSlotView（带缓存优化）
        /// </summary>
        private Timeline.TimelineSlotView GetTimeSlotUnderPointer(PointerEventData eventData)
        {
            // 性能优化：使用缓存机制，避免每帧都执行昂贵的 RaycastAll
            int currentFrame = Time.frameCount;
            Vector2 currentPosition = eventData.position;
            
            // 检查是否需要更新缓存
            bool needUpdate = false;
            if (_lastRaycastFrame < 0 || 
                currentFrame - _lastRaycastFrame >= RAYCAST_CACHE_FRAMES ||
                Vector2.Distance(currentPosition, _cachedPointerPosition) > RAYCAST_POSITION_THRESHOLD)
            {
                needUpdate = true;
            }
            
            // 如果不需要更新，直接返回缓存结果
            if (!needUpdate && _cachedSlotUnderPointer != null)
            {
                // 验证缓存的对象仍然有效
                if (_cachedSlotUnderPointer != null && _cachedSlotUnderPointer.gameObject.activeInHierarchy)
                {
                    return _cachedSlotUnderPointer;
                }
                else
                {
                    // 缓存失效，需要重新检测
                    _cachedSlotUnderPointer = null;
                    needUpdate = true;
                }
            }
            
            // 执行射线检测
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);
            
            // 强制输出日志（至少输出数量）
            Debug.Log($"[CardViewController] GetTimeSlotUnderPointer: 射线检测到 {results.Count} 个UI对象");
            
            Timeline.TimelineSlotView foundSlot = null;
            
            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                GameObject go = result.gameObject;
                
                // 跳过 CardViewController 自身及其子对象（避免检测到自己）
                if (go.transform.IsChildOf(transform) || go == gameObject)
                {
                    if (enableDetailedDebugLogs)
                    {
                        Debug.Log($"[CardViewController]   [{i}] 跳过自身: {go.name}");
                    }
                    continue;
                }
                
                if (enableDetailedDebugLogs)
                {
                    Debug.Log($"[CardViewController]   [{i}] {go.name} (Tag: {go.tag})");
                }
                
                Timeline.TimelineSlotView slot = go.GetComponent<Timeline.TimelineSlotView>();
                if (slot != null)
                {
                    foundSlot = slot;
                    if (enableDetailedDebugLogs)
                    {
                        Debug.Log($"[CardViewController] 找到 TimelineSlotView: 索引 {slot.SlotIndex}");
                    }
                    break; // 找到后立即退出循环
                }
            }
            
            // 更新缓存
            _cachedSlotUnderPointer = foundSlot;
            _cachedPointerPosition = currentPosition;
            _lastRaycastFrame = currentFrame;
            
            if (foundSlot == null && enableDetailedDebugLogs)
            {
                Debug.LogWarning("[CardViewController] 未在射线检测结果中找到 TimelineSlotView 组件");
            }
            
            return foundSlot;
        }

        /// <summary>
        /// 获取所属角色ID
        /// </summary>
        private CharacterEnum GetOwnerCharacterId()
        {
            // 优先用运行时实例上的动态 owner（如飞刀在生成时按生成者写入）；无则回退卡牌静态 BelongTo
            var rt = FindRuntimeState();
            if (rt != null && rt.OwnerCharacterId.HasValue)
            {
                return rt.OwnerCharacterId.Value;
            }
            return _currentCard.BelongTo;
        }

        /// <summary>按 InstanceId 在手牌里找到本卡的运行时状态（找不到返回 null）。</summary>
        private CardRuntimeState FindRuntimeState()
        {
            if (string.IsNullOrEmpty(_instanceId))
            {
                return null;
            }
            var hand = Ashlight.Battle.BattleManager.Instance?.CurrentState?.DeckSystem?.Hand;
            return hand?.Find(c => c != null && c.InstanceId == _instanceId);
        }

        /// <summary>本卡对其施法者的有效能量费用（含游侠首张移动免费）；数据不可用时回退卡牌静态 Energy。</summary>
        private int GetEffectiveEnergyCost()
        {
            if (_currentCard == null)
            {
                return 0;
            }
            var bm = Ashlight.Battle.BattleManager.Instance;
            if (bm == null || bm.CurrentState == null)
            {
                return _currentCard.Energy;
            }
            string ownerUnitId = ResolveOwnerUnitId(GetOwnerCharacterId().ToString());
            if (string.IsNullOrEmpty(ownerUnitId))
            {
                return _currentCard.Energy;
            }
            return bm.GetEffectiveEnergyCost(_currentCard, ownerUnitId, _instanceId);
        }

        /// <summary>
        /// 判断该卡牌的所属单位是否拥有足够的能量
        /// 解析失败或战斗数据不可用时返回 true（不误禁拖拽）
        /// </summary>
        private bool HasEnoughEnergyForCard()
        {
            if (_currentCard == null)
            {
                return true;
            }

            var battleManager = Ashlight.Battle.BattleManager.Instance;
            if (battleManager == null || battleManager.CurrentState == null)
            {
                return true;
            }

            string ownerUnitId = ResolveOwnerUnitId(GetOwnerCharacterId().ToString());
            if (string.IsNullOrEmpty(ownerUnitId))
            {
                return true;
            }

            var unit = battleManager.CurrentState.GetUnitById(ownerUnitId);
            if (unit == null)
            {
                return true;
            }

            return unit.CurrentEnergy >= battleManager.GetEffectiveEnergyCost(_currentCard, ownerUnitId, _instanceId);
        }

        /// <summary>
        /// 【前排/后排】卡牌声明打出排限制（CastZone=Front/Back）时，施法者当前站位是否满足。
        /// 解析失败或战斗数据不可用时返回 true（不误禁使用，交由 BattleManager 出牌校验兜底）。
        /// </summary>
        private bool IsCastZoneSatisfiedForOwner()
        {
            if (_currentCard == null)
            {
                return true;
            }

            var zone = _currentCard.CastZone;
            if (zone != cfg.TargetZoneEnum.Front && zone != cfg.TargetZoneEnum.Back)
            {
                return true;
            }

            var state = Ashlight.Battle.BattleManager.Instance?.CurrentState;
            if (state == null)
            {
                return true;
            }

            string ownerUnitId = ResolveOwnerUnitId(GetOwnerCharacterId().ToString());
            if (string.IsNullOrEmpty(ownerUnitId))
            {
                return true;
            }

            var owner = state.GetUnitById(ownerUnitId);
            if (owner == null)
            {
                return true;
            }

            return Ashlight.Battle.Core.Data.ZoneTargeting.IsUnitInZone(owner, zone);
        }

        /// <summary>
        /// 根据玩家能量刷新左上角能量文本颜色
        /// 能量不足：变为 <see cref="insufficientEnergyColor"/>
        /// 能量足够：恢复默认颜色
        /// </summary>
        public void RefreshEnergyAffordability()
        {
            if (Txt_LeftCost == null || _currentCard == null)
            {
                return;
            }

            if (!_defaultLeftCostColorCaptured)
            {
                _defaultLeftCostColor = Txt_LeftCost.color;
                _defaultLeftCostColorCaptured = true;
            }

            // 有效费用可能因手牌中的解签等全局修正动态变化，刷新颜色时同步刷新数字。
            Txt_LeftCost.text = GetEffectiveEnergyCost().ToString();
            bool affordable = HasEnoughEnergyForCard();
            Txt_LeftCost.color = affordable ? _defaultLeftCostColor : insufficientEnergyColor;
        }

        /// <summary>
        /// 刷新「条件牌」描边：带自身可判定条件（如 SelfInBackRow / SelfInFrontRow / MovedThisTurn）的牌，
        /// 当前满足条件 → 描边黄、常驻显示；不满足 → 描边白、常驻显示；非条件牌 → 不接管描边（仍由悬停控制）。
        /// 站位/移动会改变判定结果，故在状态刷新（能量刷新循环）与移动换位后都应调用。
        /// </summary>
        /// <summary>
        /// 移动触发牌（隧穿/铁蒺藜）的动态计数刷新：有单位移动后重解析描述，
        /// 更新卡面「本回合已移动 N 次」。非移动触发牌零开销直接返回。
        /// </summary>
        public void RefreshDynamicDescription()
        {
            if (_currentCard?.Effects == null || Txt_Effect == null)
            {
                return;
            }

            bool hasMoveTrigger = false;
            foreach (var effect in _currentCard.Effects)
            {
                if (effect is cfg.OnMoveAddCardEffect || effect is cfg.OnMoveDamageEffect)
                {
                    hasMoveTrigger = true;
                    break;
                }
            }
            if (!hasMoveTrigger)
            {
                return;
            }

            Txt_Effect.text = CardDescriptionParser.Parse(_currentCard, _displayMode);
        }

        public void RefreshConditionHighlight()
        {
            if (Img_Outline == null || _currentCard == null)
            {
                return;
            }

            string conditionType = GetSelfEvaluableConditionType();
            // 【前排/后排】声明打出排限制的牌也按条件牌处理：描边常驻提示当前站位能否打出
            bool hasCastZoneLimit = _currentCard.CastZone == cfg.TargetZoneEnum.Front
                                    || _currentCard.CastZone == cfg.TargetZoneEnum.Back;
            if (string.IsNullOrEmpty(conditionType) && !hasCastZoneLimit)
            {
                // 非（自身可判定）条件牌：把描边交回悬停控制——非悬停时收起
                _isSelfConditionCard = false;
                if (!_isHovering)
                {
                    Img_Outline.gameObject.SetActive(false);
                }
                return;
            }

            _isSelfConditionCard = true;
            bool met = (string.IsNullOrEmpty(conditionType) || EvaluateSelfCondition(conditionType))
                       && (!hasCastZoneLimit || IsCastZoneSatisfiedForOwner());
            Img_Outline.gameObject.SetActive(true); // 条件牌描边常驻
            Img_Outline.color = met ? conditionMetOutlineColor : conditionUnmetOutlineColor;
        }

        /// <summary>
        /// 取本卡第一个「自身可判定」的条件类型（Attack/Defense/Buff 三种 ConditionalEffect 都算）；
        /// 目标类条件（如 IsAttacking/InExecution，手牌里无目标、判不了）返回 null，不接管描边。
        /// </summary>
        private string GetSelfEvaluableConditionType()
        {
            var effects = _currentCard?.Effects;
            if (effects == null)
            {
                return null;
            }

            foreach (var effect in effects)
            {
                string cond = null;
                if (effect is AttackConditionalEffect atk) cond = atk.ConditionType;
                else if (effect is DefenseConditionalEffect def) cond = def.ConditionType;
                else if (effect is BuffConditionalEffect buff) cond = buff.ConditionType;

                if (!string.IsNullOrEmpty(cond) && IsSelfEvaluableCondition(cond))
                {
                    return cond;
                }
            }
            return null;
        }

        private static bool IsSelfEvaluableCondition(string conditionType)
        {
            switch (conditionType)
            {
                case "SelfInFrontRow":
                case "SelfInBackRow":
                case "MovedThisTurn":
                    return true;
                default:
                    return false; // 目标类等条件在手牌里无法判定
            }
        }

        /// <summary>按施法者当前状态判定自身条件是否满足。数据不可用时按「不满足」处理（描边白）。</summary>
        private bool EvaluateSelfCondition(string conditionType)
        {
            var bm = Ashlight.Battle.BattleManager.Instance;
            var state = bm?.CurrentState;
            if (state == null)
            {
                return false;
            }

            string ownerUnitId = ResolveOwnerUnitId(GetOwnerCharacterId().ToString());
            if (string.IsNullOrEmpty(ownerUnitId))
            {
                return false;
            }

            var owner = state.GetUnitById(ownerUnitId);
            if (owner == null)
            {
                return false;
            }

            switch (conditionType)
            {
                case "SelfInFrontRow":
                    return state.IsFrontRow(owner);
                case "SelfInBackRow":
                    return !state.IsFrontRow(owner);
                case "MovedThisTurn":
                    return owner.HasMovedThisTurn;
                default:
                    return false;
            }
        }
        
        /// <summary>
        /// 检查卡牌所属角色是否与时间轴匹配
        /// </summary>
        /// <param name="track">目标时间轴</param>
        /// <returns>如果匹配或时间轴是敌人时间轴则返回true，否则返回false</returns>
        private bool CheckCharacterMatch(Timeline.TimelineTrackView track)
        {
            if (_currentCard == null || track == null)
            {
                return false;
            }
            
            var timelineTrack = track.GetTrack();
            if (timelineTrack == null)
            {
                return false;
            }
            
            // 如果是敌人时间轴，不允许玩家卡牌放置
            if (timelineTrack.IsEnemyTrack)
            {
                if (enableDetailedDebugLogs)
                {
                    Debug.LogWarning($"[CardViewController] 敌人时间轴不允许放置玩家卡牌: {_currentCard.Name}");
                }
                return false;
            }
            
            // 如果是玩家角色时间轴，检查卡牌的 BelongTo 是否匹配
            if (timelineTrack.IsPlayerTrack && timelineTrack.OwnerCharacterId.HasValue)
            {
                var trackCharacterId = timelineTrack.OwnerCharacterId.Value;
                var cardBelongTo = _currentCard.BelongTo;
                
                bool matches = trackCharacterId == cardBelongTo;
                
                if (enableDetailedDebugLogs && !matches)
                {
                    Debug.LogWarning($"[CardViewController] 角色不匹配: 卡牌属于 {cardBelongTo}，时间轴属于 {trackCharacterId}");
                }
                
                return matches;
            }
            
            // 其他情况（时间轴没有设置角色ID），不允许放置
            return false;
        }
        
        /// <summary>
        /// 更新时间轴高亮显示
        /// </summary>
        private void UpdateTimelineHighlight(PointerEventData eventData)
        {
            if (_currentCard == null) return;
            
            // 获取鼠标下的 TimelineSlotView
            var targetSlot = GetTimeSlotUnderPointer(eventData);
            if (targetSlot == null)
            {
                ClearTimelineHighlight();
                return;
            }
            
            var track = targetSlot.GetParentTrack();
            if (track == null)
            {
                ClearTimelineHighlight();
                return;
            }
            
            int slotIndex = targetSlot.SlotIndex;
            
            // 如果高亮位置没变，不需要重复更新
            if (_currentHighlightedTrack == track && _currentHighlightedSlotIndex == slotIndex)
            {
                return;
            }
            
            // 清除旧高亮
            ClearTimelineHighlight();
            
            // 计算卡牌占用的格子数
            int totalSlots = 1;
            
            // 检查是否可以放置（位置检查）
            bool canPlaceByPosition = track.GetTrack().CanPlaceCard(slotIndex, totalSlots);
            
            // 检查角色匹配（卡牌的 BelongTo 必须与时间轴的 OwnerCharacterId 匹配）
            bool canPlaceByCharacter = CheckCharacterMatch(track);
            
            // 如果角色不匹配，不显示任何颜色
            if (!canPlaceByCharacter)
            {
                // 清除高亮，不显示任何颜色
                _currentHighlightedTrack = null;
                _currentHighlightedSlotIndex = -1;
                return;
            }
            
            // 只有位置和角色都匹配才能放置
            bool canPlace = canPlaceByPosition && canPlaceByCharacter;
            
            // 显示新高亮
            track.HighlightPlacementArea(slotIndex, totalSlots, canPlace);
            
            // 记录当前高亮状态
            _currentHighlightedTrack = track;
            _currentHighlightedSlotIndex = slotIndex;
            
            if (enableDetailedDebugLogs)
            {
                Debug.Log($"[CardViewController] 高亮格子 {slotIndex}-{slotIndex + totalSlots - 1}, 可放置: {canPlace}");
            }
        }
        
        /// <summary>
        /// 清除时间轴高亮
        /// </summary>
        private void ClearTimelineHighlight()
        {
            if (_currentHighlightedTrack != null)
            {
                _currentHighlightedTrack.ClearHighlight();
                _currentHighlightedTrack = null;
                _currentHighlightedSlotIndex = -1;
            }
        }

        /// <summary>
        /// 恢复拖拽前的位置
        /// </summary>
        private void RestoreDragPosition()
        {
            // 停止之前的位置动画
            _positionTween?.Kill();

            // 使用 DOTween 平滑回到原始位置
            _positionTween = transform.DOLocalMove(_originalDragPosition, positionRestoreDuration)
                .SetEase(Ease.OutCubic)
                .OnComplete(() =>
                {
                    _positionTween = null;
                });
        }

        /// <summary>
        /// 检测是否拖拽到时间轴
        /// 使用RectTransform直接检测UI位置，适合UI环境
        /// </summary>
        /// <param name="eventData">拖拽事件数据</param>
        /// <returns>是否拖拽到时间轴</returns>
        private bool CheckDragToTimeSlot(PointerEventData eventData)
        {
            // 查找所有Tag为"TimeSlot"的GameObject
            GameObject[] timeSlotObjects = GameObject.FindGameObjectsWithTag("TimeSlot");
            
            if (timeSlotObjects == null || timeSlotObjects.Length == 0)
            {
                // 仅第一次输出警告，避免日志刷屏
                if (!_hasLoggedNoTimeSlot)
                {
                    Debug.LogWarning("[CardViewController] 场景中未找到Tag为'TimeSlot'的对象");
                    _hasLoggedNoTimeSlot = true;
                }
                return false;
            }

            // 获取鼠标位置（屏幕坐标）
            Vector2 screenPosition = eventData.position;
            Camera eventCamera = eventData.pressEventCamera;

            // 遍历所有TimeSlot对象，检查鼠标位置是否在其RectTransform范围内
            foreach (GameObject timeSlotObj in timeSlotObjects)
            {
                if (timeSlotObj == null || !timeSlotObj.activeInHierarchy)
                    continue;

                RectTransform rectTransform = timeSlotObj.GetComponent<RectTransform>();
                if (rectTransform == null)
                {
                    // 如果没有RectTransform，尝试从子对象获取
                    rectTransform = timeSlotObj.GetComponentInChildren<RectTransform>();
                }

                if (rectTransform != null)
                {
                    // 使用RectTransformUtility检查屏幕点是否在RectTransform范围内
                    bool containsPoint = RectTransformUtility.RectangleContainsScreenPoint(
                        rectTransform, 
                        screenPosition, 
                        eventCamera
                    );

                    if (containsPoint)
                    {
                        Debug.Log($"[CardViewController] 检测到拖拽到TimeSlot: {timeSlotObj.name}");
                        return true;
                    }
                }
            }

            Debug.Log("[CardViewController] 鼠标位置不在任何TimeSlot对象范围内");
            return false;
        }

        /// <summary>
        /// 显示CardTimeSlot并隐藏卡牌
        /// </summary>
        private void ShowCardTimeSlot()
        {
            // 显示CardTimeSlot
            if (CardTimeSlot != null)
            {
                CardTimeSlot.Show();
            }
        }

        /// <summary>
        /// 隐藏CardTimeSlot
        /// </summary>
        private void HideCardTimeSlot()
        {
            // 隐藏CardTimeSlot
            if (CardTimeSlot != null)
            {
                CardTimeSlot.Hide();
            }
        }

        /// <summary>
        /// 隐藏Card子对象
        /// </summary>
        private void HideCard()
        {
            if (Card != null)
            {
                Card.alpha = 0f;
            }
        }

        /// <summary>
        /// 显示Card子对象
        /// </summary>
        private void ShowCard()
        {
            if (Card != null)
            {
                Card.alpha = 1f;
            }
        }
        
        /// <summary>
        /// CardTimeSlot 悬停处理（仅在 OnTime 状态下有效）
        /// </summary>
        /// <param name="isHovering">是否正在悬停</param>
        public void OnCardTimeSlotHover(bool isHovering)
        {
            // 只在 OnTime 状态下处理
            if (_cardDragState != CardDragState.OnTime)
            {
                return;
            }
            
            // 如果正在拖拽，不处理悬停
            if (_isDragging)
            {
                return;
            }
            
            _isCardTimeSlotHovering = isHovering;
            
            if (Card == null)
            {
                return;
            }
            
            RectTransform cardRect = Card.GetComponent<RectTransform>();
            if (cardRect == null)
            {
                return;
            }
            
            if (isHovering)
            {
                // 保存原始位置（只在第一次悬停时保存）
                if (_originalCardPosition == Vector2.zero)
                {
                    _originalCardPosition = cardRect.anchoredPosition;
                }
                
                // 确保 CardViewController 的 CanvasGroup alpha = 1（确保整个对象可见）
                if (_canvasGroup != null)
                {
                    _canvasGroup.alpha = 1f;
                }
                
                // 显示 Card（设置 Card 的 alpha）
                if (Card != null)
                {
                    Card.alpha = 1f;
                }
                
                // 直接设置到右边位置（取消移动动画）
                Vector2 newPosition = _originalCardPosition + new Vector2(200f, 0f);
                cardRect.anchoredPosition = newPosition;
                
                // 停止可能正在进行的动画
                DOTween.Kill(cardRect);
                
                if (enableDetailedDebugLogs)
                {
                    Debug.Log($"[CardViewController] CardTimeSlot 悬停：显示 Card 并直接出现在右边");
                }
            }
            else
            {
                // 直接恢复原始位置（不使用动画）
                cardRect.anchoredPosition = _originalCardPosition;
                
                // 隐藏 Card（但保持 CardViewController 的 CanvasGroup alpha = 1，因为 CardTimeSlot 需要显示）
                if (Card != null)
                {
                    Card.alpha = 0f;
                }
                
                // 确保 CardViewController 的 CanvasGroup alpha = 1（CardTimeSlot 需要显示）
                if (_canvasGroup != null)
                {
                    _canvasGroup.alpha = 1f;
                }
                
                if (enableDetailedDebugLogs)
                {
                    Debug.Log($"[CardViewController] CardTimeSlot 离开：隐藏 Card 并恢复位置");
                }
            }
        }
        
        /// <summary>
        /// 统一设置 CanvasGroup alpha，同时设置所有 Image 的 raycastTarget 和所有 Selectable 的 interactable
        /// </summary>
        /// <param name="alpha">透明度值（0-1）</param>
        private void SetCanvasGroupAlpha(float alpha)
        {
            if (_canvasGroup == null) return;
            
            _canvasGroup.alpha = alpha;
            
            // 根据 alpha 设置 raycastTarget 和 interactable
            bool shouldBeActive = alpha > 0f;
            
            // 设置所有 Image 的 raycastTarget
            Image[] images = GetComponentsInChildren<Image>(true);
            foreach (Image img in images)
            {
                if (img != null)
                {
                    img.raycastTarget = shouldBeActive ? _originalRaycastTarget : false;
                }
            }
            
            // 设置所有 Selectable 的 interactable
            if (_selectables != null)
            {
                foreach (var selectable in _selectables)
                {
                    if (selectable != null)
                    {
                        selectable.interactable = shouldBeActive ? _originalInteractable : false;
                    }
                }
            }
            
            if (enableDetailedDebugLogs)
            {
                Debug.Log($"[CardViewController] 设置 CanvasGroup alpha={alpha}, raycastTarget={shouldBeActive}, interactable={shouldBeActive}");
            }
        }

        #endregion
        
        #region 时间轴拖拽辅助方法
        
        /// <summary>
        /// 显示 raycast 穿透的对象信息（用于调试）
        /// 仅在调试模式下启用，避免性能问题
        /// </summary>
        private void ShowRaycastDebugInfo(PointerEventData eventData)
        {
            // 如果未启用详细调试日志，直接返回
            if (!enableDetailedDebugLogs)
            {
                return;
            }
            
            // 节流：每 N 帧输出一次，避免日志刷屏
            _raycastDebugFrameCount++;
            if (_raycastDebugFrameCount % RAYCAST_DEBUG_INTERVAL != 0)
            {
                return;
            }
            
            var results = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);
            
            if (results.Count == 0)
            {
                Debug.Log("[CardViewController] Raycast: 未检测到任何对象");
                return;
            }
            
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"[CardViewController] Raycast 穿透了 {results.Count} 个对象:");
            
            bool foundCardContainer = false;
            bool foundTimeSlot = false;
            
            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                GameObject go = result.gameObject;
                
                sb.Append($"  [{i}] {go.name}");
                
                // 检查 Tag
                if (!string.IsNullOrEmpty(go.tag))
                {
                    sb.Append($" (Tag: {go.tag})");
                    
                    // 特别标注重要标签
                    if (go.CompareTag("HandArea"))
                    {
                        sb.Append(" [✓手牌区域]");
                        foundCardContainer = true;
                    }
                    else if (go.CompareTag("TimeSlot"))
                    {
                        sb.Append(" [✓时间轴格子]");
                        foundTimeSlot = true;
                    }
                }
                
                // 检查组件
                var slot = go.GetComponent<Timeline.TimelineSlotView>();
                if (slot != null)
                {
                    sb.Append($" [✓TimelineSlotView: 索引 {slot.SlotIndex}]");
                    foundTimeSlot = true;
                }
                
                // 检查是否有 CardContainer 相关的组件或名称
                if (go.name.Contains("CardContainer") || go.name.Contains("Hand"))
                {
                    sb.Append(" [✓卡牌容器/手牌区域]");
                    foundCardContainer = true;
                }
                
                // 检查 CanvasGroup 的 blocksRaycasts 设置
                var canvasGroup = go.GetComponent<CanvasGroup>();
                if (canvasGroup != null)
                {
                    sb.Append($" [CanvasGroup: blocksRaycasts={canvasGroup.blocksRaycasts}]");
                }
                
                // 检查 Image 的 raycastTarget
                var image = go.GetComponent<Image>();
                if (image != null)
                {
                    sb.Append($" [Image: raycastTarget={image.raycastTarget}]");
                }
                
                sb.AppendLine();
            }
            
            // 总结
            sb.AppendLine($"总结: CardContainer={(foundCardContainer ? "✓" : "✗")}, TimeSlot={(foundTimeSlot ? "✓" : "✗")}");
            
            Debug.Log(sb.ToString());
        }
        
        /// <summary>
        /// 更新时间轴拖拽时的高亮显示
        /// </summary>
        private void UpdateDragHighlightOnTime(PointerEventData eventData)
        {
            Timeline.TimelineSlotView targetSlot = GetTimeSlotUnderPointer(eventData);
            
            if (targetSlot != null)
            {
                var track = targetSlot.GetParentTrack();
                if (track != null && _currentCard != null)
                {
                    int totalSlots = 1;
                    
                    // 检查是否可以放置（位置检查）
                    bool canPlaceByPosition = track.GetTrack().CanPlaceCard(targetSlot.SlotIndex, totalSlots);
                    
                    // 检查角色匹配（卡牌的 BelongTo 必须与时间轴的 OwnerCharacterId 匹配）
                    bool canPlaceByCharacter = CheckCharacterMatch(track);
                    
                    // 如果角色不匹配，不显示任何颜色
                    if (!canPlaceByCharacter)
                    {
                        // 清除高亮，不显示任何颜色
                        if (_parentTrack != null)
                        {
                            _parentTrack.ClearHighlight();
                        }
                        return;
                    }
                    
                    // 如果是在同一轨道上移动，需要先清除原位置再检测
                    bool canPlace;
                    if (track == _parentTrack && targetSlot.SlotIndex != _originalSlotIndex)
                    {
                        // 在同一轨道上移动时，位置检查需要排除原位置
                        // 但角色匹配仍然需要检查（因为已经在自己的轨道上了）
                        canPlace = canPlaceByPosition && canPlaceByCharacter;
                    }
                    else
                    {
                        // 只有位置和角色都匹配才能放置
                        canPlace = canPlaceByPosition && canPlaceByCharacter;
                    }
                    
                    track.HighlightPlacementArea(targetSlot.SlotIndex, totalSlots, canPlace);
                }
            }
            else
            {
                // 不在任何格子上，清除高亮
                if (_parentTrack != null)
                {
                    _parentTrack.ClearHighlight();
                }
            }
        }
        

        
        /// <summary>
        /// 检测是否在手牌区域上方
        /// </summary>
        private bool IsOverHandArea(PointerEventData eventData)
        {
            var results = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);
            
            if (enableDetailedDebugLogs)
            {
                Debug.Log($"[CardViewController] IsOverHandArea: 检测到 {results.Count} 个对象");
            }
            
            foreach (var result in results)
            {
                GameObject go = result.gameObject;
                
                // 跳过 CardViewController 自身及其子对象
                if (go.transform.IsChildOf(transform) || go == gameObject)
                {
                    if (enableDetailedDebugLogs)
                    {
                        Debug.Log($"[CardViewController] IsOverHandArea: 跳过自身: {go.name}");
                    }
                    continue;
                }
                
                // 检测是否有 Tag="HandArea" 的对象
                try
                {
                    if (go.CompareTag("HandArea"))
                    {
                        if (enableDetailedDebugLogs)
                        {
                            Debug.Log($"[CardViewController] IsOverHandArea: 找到手牌区域: {go.name}");
                        }
                        return true;
                    }
                }
                catch (UnityException)
                {
                    // Tag不存在时会抛出异常，忽略
                }
                
                // 也检查名称中是否包含 Hand 或 CardContainer
                if (go.name.Contains("Hand") || go.name.Contains("CardContainer"))
                {
                    if (enableDetailedDebugLogs)
                    {
                        Debug.Log($"[CardViewController] IsOverHandArea: 找到手牌相关对象: {go.name}");
                    }
                    return true;
                }
            }
            
            if (enableDetailedDebugLogs)
            {
                Debug.Log("[CardViewController] IsOverHandArea: 未找到手牌区域");
            }
            
            return false;
        }
        
        /// <summary>
        /// 处理在时间轴上调整位置
        /// </summary>
        private void HandleRepositionOnTimeline(Timeline.TimelineSlotView targetSlot)
        {
            if (_parentTrack == null || _currentCard == null)
            {
                Debug.LogWarning($"[CardViewController] HandleRepositionOnTimeline 失败: _parentTrack={(_parentTrack != null ? _parentTrack.name : "null")}, _currentCard={(_currentCard != null ? _currentCard.Name : "null")}");
                RestorePositionOnTime();
                return;
            }
            
            int newSlotIndex = targetSlot.SlotIndex;
            var targetTrack = targetSlot.GetParentTrack();
            
            Debug.Log($"[CardViewController] HandleRepositionOnTimeline: 当前轨道={_parentTrack.name}, 当前索引={_originalSlotIndex}, 目标轨道={targetTrack?.name}, 目标索引={newSlotIndex}");
            
            // 如果是同一个轨道的同一个位置，恢复原位（避免位置偏移）
            if (targetTrack == _parentTrack && newSlotIndex == _originalSlotIndex)
            {
                Debug.Log($"[CardViewController] 放置位置与原位置相同，恢复原位");
                RestorePositionOnTime();
                return;
            }
            
            Debug.Log($"[CardViewController] 尝试调整位置: 从轨道 {_parentTrack.name}[{_originalSlotIndex}] -> 轨道 {targetTrack?.name}[{newSlotIndex}]");
            
            // 调用 TimelineTrackView 的重新放置方法
            bool success = _parentTrack.RepositionCard(this, _originalSlotIndex, newSlotIndex, targetTrack);
            
            if (success)
            {
                // 更新索引和轨道
                _originalSlotIndex = newSlotIndex;
                _parentTrack = targetTrack;
                Debug.Log($"[CardViewController] 成功调整位置到轨道 {targetTrack.name}[{newSlotIndex}]");
            }
            else
            {
                // 放置失败，恢复原位
                Debug.LogWarning($"[CardViewController] 调整位置失败，恢复原位");
                RestorePositionOnTime();
            }
        }
        
        /// <summary>
        /// 处理撤回到手牌
        /// </summary>
        private void HandleRecallToHand()
        {
            if (_parentTrack == null || _currentCard == null)
            {
                RestorePositionOnTime();
                return;
            }
            
            Debug.Log($"[CardViewController] 撤回到手牌: {_currentCard.Id}");
            
            // 调用 TimelineTrackView 的撤回方法
            bool success = _parentTrack.RecallCardToHand(this, _originalSlotIndex);
            
            if (!success)
            {
                // 撤回失败，恢复原位
                RestorePositionOnTime();
            }
            else
            {
                // 如果成功，设置状态为 OnHand
                SetCardDragState(CardDragState.OnHand);
            }
        }
        
        /// <summary>
        /// 恢复到时间轴上的原始位置
        /// </summary>
        private void RestorePositionOnTime()
        {
            RectTransform rect = GetComponent<RectTransform>();
            if (rect == null)
            {
                Debug.LogError("[CardViewController] RestorePositionOnTime: 没有 RectTransform 组件");
                return;
            }
            
            Debug.Log($"[CardViewController] RestorePositionOnTime: 从 anchoredPosition={rect.anchoredPosition} -> {_originalTimeAnchoredPosition}, 父对象: {transform.parent?.name} -> {_originalTimeParent?.name}, slotIndex={_originalSlotIndex}");
            
            // 恢复父对象
            if (_originalTimeParent != null)
            {
                rect.SetParent(_originalTimeParent, false);
            }
            
            // 确保锚点正确
            rect.anchorMin = new Vector2(0, 0.5f);
            rect.anchorMax = new Vector2(0, 0.5f);
            rect.pivot = new Vector2(0, 0.5f);
            
            // 恢复 anchoredPosition（本地坐标），而不是 position（世界坐标）
            rect.anchoredPosition = _originalTimeAnchoredPosition;
            
            Debug.Log($"[CardViewController] 恢复CardViewController到原始位置（时间轴）完成: anchoredPosition={rect.anchoredPosition}, 预期位置={_originalSlotIndex * 100f}");
        }
        
        /// <summary>
        /// 设置 Card 的拖拽状态
        /// </summary>
        public void SetCardDragState(CardDragState state)
        {
            _cardDragState = state;
            Debug.Log($"[CardViewController] 设置 Card 拖拽状态: {state}");

            // 状态改变时重新加载对应的图片
            // 注意：在 OnTime 状态下，Card 应该被隐藏，所以不需要改变图片
            // 只有在 OnHand 状态下才需要加载完整 Sprite
            if (_currentCard != null && state == CardDragState.OnHand)
            {
                LoadCardSprite(_currentCard.Id);
            }
        }
        
        /// <summary>
        /// 设置时间轴信息（在放置到时间轴时调用）
        /// </summary>
        public void SetTimelineInfo(Timeline.TimelineTrackView parentTrack, int slotIndex)
        {
            Debug.Log($"[CardViewController] SetTimelineInfo 开始: 旧轨道={_parentTrack?.name}[{_originalSlotIndex}] -> 新轨道={parentTrack?.name}[{slotIndex}]");
            
            _parentTrack = parentTrack;
            _originalSlotIndex = slotIndex;
            
            // 同时更新 CardTimeSlot 的时间轴信息
            if (CardTimeSlot != null)
            {
                CardTimeSlot.SetTimelineInfo(parentTrack, slotIndex);
            }
            else
            {
                Debug.LogWarning($"[CardViewController] CardTimeSlot 为 null！");
            }
            
            SetCardDragState(CardDragState.OnTime);
            
            Debug.Log($"[CardViewController] SetTimelineInfo 完成: 轨道={parentTrack?.name}, 格子索引={slotIndex}");
        }
        
        #endregion

        #region 标签悬停检测

        /// <summary>
        /// 检测鼠标是否悬停在link上
        /// </summary>
        private void CheckLinkHover(TextMeshProUGUI textComponent)
        {
            if (textComponent == null || _descriptionView == null || !textComponent.isActiveAndEnabled)
                return;

            // 卡牌预览切换时会在本帧替换文本，而 TMP 的 textInfo 通常要到 Canvas
            // rebuild 才更新。FindIntersectingLink 直接读取 linkInfo / characterInfo，
            // 在两者尚未生成时会在 TMP 内部抛 NullReferenceException。
            if (textComponent.havePropertiesChanged)
            {
                textComponent.ForceMeshUpdate();
            }

            TMP_TextInfo textInfo = textComponent.textInfo;
            if (textInfo == null || textInfo.linkCount <= 0 ||
                textInfo.linkInfo == null || textInfo.characterInfo == null ||
                textInfo.linkCount > textInfo.linkInfo.Length)
            {
                if (!string.IsNullOrEmpty(_currentHoveredLink))
                {
                    _currentHoveredLink = string.Empty;
                    HideDescription();
                }
                return;
            }

            // 防止文本刚变化时 linkInfo 已更新、characterInfo 仍是旧数组。
            for (int i = 0; i < textInfo.linkCount; i++)
            {
                TMP_LinkInfo info = textInfo.linkInfo[i];
                int first = info.linkTextfirstCharacterIndex;
                int length = info.linkTextLength;
                if (first < 0 || length <= 0 || first + length > textInfo.characterInfo.Length)
                {
                    return;
                }
            }

            // 获取鼠标位置
            Vector3 mousePosition = Input.mousePosition;

            // 检测鼠标位置是否在link上
            Canvas textCanvas = textComponent.canvas;
            Camera eventCamera = textCanvas != null && textCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? (textCanvas.worldCamera != null ? textCanvas.worldCamera : Camera.main)
                : null;
            int linkIndex = TMP_TextUtilities.FindIntersectingLink(textComponent, mousePosition, eventCamera);

            if (linkIndex != -1)
            {
                // 鼠标在link上
                TMP_LinkInfo linkInfo = textComponent.textInfo.linkInfo[linkIndex];
                string linkId = linkInfo.GetLinkID();

                // 如果是新的link，显示描述
                if (linkId != _currentHoveredLink)
                {
                    _currentHoveredLink = linkId;
                    ShowDescription(linkId);
                }
            }
            else
            {
                // 鼠标不在link上
                if (!string.IsNullOrEmpty(_currentHoveredLink))
                {
                    _currentHoveredLink = string.Empty;
                    HideDescription();
                }
            }
        }

        /// <summary>
        /// 显示标签描述
        /// </summary>
        private void ShowDescription(string nounName)
        {
            if (_descriptionView == null || string.IsNullOrEmpty(nounName))
                return;

            // 从当前卡牌 Effects 里找到与该名词对应的 BuffEffect value
            float? buffValue = FindBuffValueForNoun(nounName);

            _descriptionView.Show(nounName, buffValue);

            // 设置描述面板位置（卡牌右侧）
            Vector3 cardPosition = transform.position;
            Vector3 descPosition = cardPosition + new Vector3(descriptionOffset.x, descriptionOffset.y, 0f);
            _descriptionView.SetPosition(descPosition);
        }

        /// <summary>
        /// 从卡牌 Effects 中查找与 nounName 对应的 BuffEffect 数值
        /// 通过 TbBuffInfo.Name == nounName 找到 BuffInfo.Id，再匹配 BuffEffect.BuffId
        /// </summary>
        private float? FindBuffValueForNoun(string nounName)
        {
            if (_currentCard?.Effects == null) return null;

            var tables = Ashlight.Config.ConfigLoader.Tables;
            if (tables?.TbBuffInfo == null) return null;

            // 找到名称对应的 BuffInfo
            cfg.BuffInfo targetBuffInfo = null;
            foreach (var buff in tables.TbBuffInfo.DataList)
            {
                if (buff.Name == nounName) { targetBuffInfo = buff; break; }
            }
            if (targetBuffInfo == null) return null;

            // 在卡牌 Effects 里找第一个匹配 BuffId 的 BuffEffect
            foreach (var effect in _currentCard.Effects)
            {
                if (effect is cfg.BuffEffect buffEffect && buffEffect.BuffId == targetBuffInfo.Id)
                    return buffEffect.Value;
            }
            return null;
        }

        /// <summary>
        /// 隐藏标签描述
        /// </summary>
        private void HideDescription()
        {
            if (_descriptionView != null)
            {
                _descriptionView.Hide();
            }
        }

        /// <summary>
        /// 获取或创建专用 tooltip Canvas（sortingOrder 高于卡牌悬停层级）。
        /// 挂在场景根级，避免嵌套 Canvas 干扰父 Canvas 的射线检测。
        /// </summary>
        private Transform GetOrCreateTooltipCanvas(Canvas referenceCanvas)
        {
            const string tooltipCanvasName = "TooltipCanvas";
            const int tooltipSortingOrder = 500;

            // 先从场景里找已有的 TooltipCanvas
            var existing = GameObject.Find(tooltipCanvasName);
            if (existing != null)
                return existing.transform;

            // 不存在则创建
            var go = new GameObject(tooltipCanvasName);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = referenceCanvas != null ? referenceCanvas.renderMode : RenderMode.ScreenSpaceOverlay;
            if (referenceCanvas != null && referenceCanvas.renderMode == RenderMode.ScreenSpaceCamera)
                canvas.worldCamera = referenceCanvas.worldCamera;
            canvas.sortingOrder = tooltipSortingOrder;

            go.AddComponent<UnityEngine.UI.CanvasScaler>();
            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            return go.transform;
        }

        /// <summary>
        /// 加载卡牌 Sprite（Card 组件始终使用完整 Sprite）
        /// 注意：MiniSprite 只在 CardTimeSlot 中使用，Card 组件应该始终显示完整 Sprite
        /// 在 OnTime 状态下，Card 应该被隐藏，所以不修改图片
        /// </summary>
        /// <param name="cardId">卡牌ID</param>
        private void LoadCardSprite(string cardId)
        {
            // 如果卡牌在时间轴上，Card 应该被隐藏，不需要修改图片
            if (_cardDragState == CardDragState.OnTime)
            {
                Debug.Log($"[CardViewController] 卡牌在时间轴上，跳过加载图片（Card 应该被隐藏）");
                return;
            }

            if (Img_CardPicture == null)
            {
                Debug.LogWarning("[CardViewController] Img_CardPicture 为 null，无法加载卡牌图片");
                return;
            }

            if (string.IsNullOrEmpty(cardId))
            {
                Debug.LogWarning("[CardViewController] cardId 为空，无法加载卡牌图片");
                return;
            }

            // Card 组件始终加载完整的 Sprite（MiniSprite 只在 CardTimeSlot 中使用）
            string visualCardId = TempoPrototypeMode.ResolveVisualCardId(cardId);
            string spritePath = AssetPath.GetCardSpriteAssetPath(visualCardId);

            // 从 Resources 加载 Sprite
            Sprite sprite = Resources.Load<Sprite>(spritePath);

            if (sprite != null)
            {
                Img_CardPicture.sprite = sprite;
                Debug.Log($"[CardViewController] 成功加载卡牌完整图片: {spritePath}");
            }
            else
            {
                // 找不到对应卡图时，回退到默认卡图 Resources/Cards/Sprites/Default
                string defaultPath = AssetPath.GetCardSpriteAssetPath("Default");
                Sprite defaultSprite = Resources.Load<Sprite>(defaultPath);
                if (defaultSprite != null)
                {
                    Img_CardPicture.sprite = defaultSprite;
                    Debug.LogWarning($"[CardViewController] 无法加载卡牌图片: {spritePath}，已回退到默认卡图: {defaultPath}");
                }
                else
                {
                    Debug.LogWarning($"[CardViewController] 无法加载卡牌图片: {spritePath}，且默认卡图也缺失: {defaultPath}");
                }
            }
        }

        #region 目标选择系统方法

        /// <summary>
        /// 初始化目标选择系统
        /// </summary>
        private void InitializeTargetSelection()
        {
            // 创建TargetArrowRenderer作为子对象,放在Canvas层级
            Canvas parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas == null)
            {
                Debug.LogWarning("[CardViewController] 无法找到父Canvas,目标选择系统初始化失败");
                return;
            }

            GameObject arrowObj = new GameObject("TargetArrow");
            arrowObj.transform.SetParent(parentCanvas.transform, false);
            arrowObj.layer = parentCanvas.gameObject.layer;

            RectTransform arrowRect = arrowObj.AddComponent<RectTransform>();
            // 填满整个Canvas
            arrowRect.anchorMin = Vector2.zero;
            arrowRect.anchorMax = Vector2.one;
            arrowRect.sizeDelta = Vector2.zero;
            arrowRect.anchoredPosition = Vector2.zero;
            arrowRect.pivot = new Vector2(0.5f, 0.5f);

            _targetArrow = arrowObj.AddComponent<TargetArrowRenderer>();
            _targetArrow.raycastTarget = false; // 不拦截射线
            _targetArrow.Hide();

            Debug.Log($"[CardViewController] TargetArrowRenderer已创建: Canvas={parentCanvas.name}, RenderMode={parentCanvas.renderMode}");

            // 查找或创建TargetSelectionManager
            _targetManager = FindObjectOfType<TargetSelectionManager>();
            if (_targetManager == null)
            {
                var battleScene = FindObjectOfType<UI_BattleScene>();
                if (battleScene != null)
                {
                    _targetManager = battleScene.gameObject.AddComponent<TargetSelectionManager>();
                    _targetManager.Initialize(battleScene.GetAllPlayerCharacters(), battleScene.GetAllEnemies());
                    Debug.Log("[CardViewController] TargetSelectionManager已创建并初始化");
                }
                else
                {
                    Debug.LogWarning("[CardViewController] 无法找到UI_BattleScene,TargetSelectionManager初始化失败");
                }
            }
            else
            {
                Debug.Log("[CardViewController] TargetSelectionManager已存在");
            }
        }

        /// <summary>
        /// 判断是否使用目标选择模式
        /// </summary>
        private bool UsesTargetSelection()
        {
            if (_currentCard == null)
            {
                return false;
            }

            // TimeSlot类型使用原有的时间轴拖拽模式
            return _currentCard.TargetType != cfg.TargetTypeEnum.TimeSlot;
        }

        /// <summary>
        /// 设置所有目标的颜色（根据合法性）
        /// 非法目标变黑，合法目标变暗
        /// </summary>
        private void SetAllTargetsColor()
        {
            if (_currentCard == null || _targetManager == null)
            {
                return;
            }

            // 清空之前的颜色记录
            _originalCharacterColors.Clear();
            _originalEnemyColors.Clear();

            CharacterEnum ownerCharacterId = GetOwnerCharacterId();
            var battleScene = FindObjectOfType<UI_BattleScene>();
            if (battleScene == null)
            {
                return;
            }

            // 处理所有角色
            var characters = battleScene.GetAllPlayerCharacters();
            foreach (var character in characters)
            {
                if (character == null) continue;

                // 保存原始颜色（如果 Skeleton_Unit 为 null，使用默认白色）
                Color originalColor = Color.white;
                if (character.Skeleton_Unit != null)
                {
                    originalColor = character.Skeleton_Unit.color;
                }
                _originalCharacterColors[character] = originalColor;

                // 判断是否为合法目标
                bool isValid = _targetManager.IsValidTarget(character.gameObject, _currentCard.TargetType, ownerCharacterId, _currentCard.TargetZone);

                // 设置颜色：非法变黑，合法变暗
                character.SetColor(isValid ? validTargetDimColor : Color.black);
            }

            // 处理所有敌人
            var enemies = battleScene.GetAllEnemies();
            foreach (var enemy in enemies)
            {
                if (enemy == null) continue;

                // 保存原始颜色（如果 Skeleton_Unit 为 null，使用默认白色）
                Color originalColor = Color.white;
                if (enemy.Skeleton_Unit != null)
                {
                    originalColor = enemy.Skeleton_Unit.color;
                }
                _originalEnemyColors[enemy] = originalColor;

                // 判断是否为合法目标
                bool isValid = _targetManager.IsValidTarget(enemy.gameObject, _currentCard.TargetType, ownerCharacterId, _currentCard.TargetZone);

                // 设置颜色：非法变黑，合法变暗
                enemy.SetColor(isValid ? validTargetDimColor : Color.black);
            }
        }

        /// <summary>
        /// 恢复所有目标的原始颜色
        /// </summary>
        private void RestoreAllTargetsColor()
        {
            // 恢复所有角色的颜色
            foreach (var kvp in _originalCharacterColors)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.SetColor(kvp.Value);
                }
            }

            // 恢复所有敌人的颜色
            foreach (var kvp in _originalEnemyColors)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.SetColor(kvp.Value);
                }
            }

            // 清空颜色记录
            _originalCharacterColors.Clear();
            _originalEnemyColors.Clear();
        }

        /// <summary>
        /// 更新目标高亮
        /// </summary>
        private void UpdateTargetHighlighting(GameObject targetObj, bool isValid)
        {
            // 如果目标改变了，恢复之前选中目标的颜色
            if (_previousHoveredTarget != null && _previousHoveredTarget != targetObj)
            {
                RestoreTargetColor(_previousHoveredTarget);
            }

            // 清除之前的高亮
            ClearAllTargetHighlighting();

            if (targetObj == null || _currentCard == null)
            {
                // 群体目标类型：鼠标离开时，把所有合法目标恢复成 dim 状态
                if (_currentCard != null)
                {
                    if (_currentCard.TargetType == cfg.TargetTypeEnum.AllAlly)
                    {
                        CharacterEnum ownerCharacterId = GetOwnerCharacterId();
                        var allies = _targetManager?.GetAllValidAllies(ownerCharacterId);
                        if (allies != null)
                        {
                            foreach (var ally in allies)
                            {
                                if (ally != null)
                                {
                                    ally.SetColor(validTargetDimColor);
                                }
                            }
                        }
                    }
                    else if (_currentCard.TargetType == cfg.TargetTypeEnum.AllEnemy)
                    {
                        var enemies = _targetManager?.GetAllValidEnemies();
                        if (enemies != null)
                        {
                            foreach (var enemy in enemies)
                            {
                                if (enemy != null)
                                {
                                    enemy.SetColor(validTargetDimColor);
                                }
                            }
                        }
                    }
                }
                _previousHoveredTarget = null;
                return;
            }

            // 保存当前悬停的目标
            _previousHoveredTarget = targetObj;

            // 检查是否为群体目标类型
            if (_currentCard.TargetType == cfg.TargetTypeEnum.AllAlly)
            {
                // 高亮所有合法队友：全部变白 + 绿色指示
                CharacterEnum ownerCharacterId = GetOwnerCharacterId();
                var allies = _targetManager?.GetAllValidAllies(ownerCharacterId);
                if (allies != null)
                {
                    foreach (var ally in allies)
                    {
                        if (ally != null)
                        {
                            ally.SetColor(Color.white);
                            ally.ShowIndicator(Color.green);
                        }
                    }
                }
            }
            else if (_currentCard.TargetType == cfg.TargetTypeEnum.AllEnemy)
            {
                // 高亮所有合法敌人：全部变白 + 绿色指示
                var enemies = _targetManager?.GetAllValidEnemies();
                if (enemies != null)
                {
                    foreach (var enemy in enemies)
                    {
                        if (enemy != null)
                        {
                            enemy.SetColor(Color.white);
                            enemy.ShowIndicator(Color.green);
                        }
                    }
                }
            }
            else
            {
                // 单目标 - 如果目标合法，设为白色并显示绿色指示
                if (isValid)
                {
                    var character = targetObj.GetComponent<Character>();
                    var enemy = targetObj.GetComponent<Enemy>();

                    if (character != null)
                    {
                        character.SetColor(Color.white);
                        character.ShowIndicator(Color.green);
                    }
                    else if (enemy != null)
                    {
                        enemy.SetColor(Color.white);
                        enemy.ShowIndicator(Color.green);
                    }
                }
                else
                {
                    // 非法目标不显示Indicator
                    return;
                }
            }
        }

        /// <summary>
        /// 恢复单个目标的颜色（根据合法性）
        /// </summary>
        private void RestoreTargetColor(GameObject targetObj)
        {
            if (targetObj == null || _currentCard == null || _targetManager == null)
            {
                return;
            }

            CharacterEnum ownerCharacterId = GetOwnerCharacterId();
            bool isValid = _targetManager.IsValidTarget(targetObj, _currentCard.TargetType, ownerCharacterId, _currentCard.TargetZone);

            var character = targetObj.GetComponent<Character>();
            var enemy = targetObj.GetComponent<Enemy>();

            if (character != null)
            {
                // 恢复颜色：非法变黑，合法变暗
                character.SetColor(isValid ? validTargetDimColor : Color.black);
            }
            else if (enemy != null)
            {
                // 恢复颜色：非法变黑，合法变暗
                enemy.SetColor(isValid ? validTargetDimColor : Color.black);
            }
        }

        /// <summary>
        /// 清除所有目标高亮
        /// </summary>
        private void ClearAllTargetHighlighting()
        {
            if (_targetManager == null)
            {
                return;
            }

            // 清除所有角色高亮
            var battleScene = FindObjectOfType<UI_BattleScene>();
            if (battleScene != null)
            {
                var characters = battleScene.GetAllPlayerCharacters();
                if (characters != null)
                {
                    foreach (var character in characters)
                    {
                        if (character != null)
                        {
                            character.HideIndicator();
                        }
                    }
                }

                var enemies = battleScene.GetAllEnemies();
                if (enemies != null)
                {
                    foreach (var enemy in enemies)
                    {
                        if (enemy != null)
                        {
                            enemy.HideIndicator();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 将卡牌放置到目标时间轴
        /// </summary>
        private void PlaceCardOnTargetTimeline(GameObject targetObj, string ownerId)
        {
            if (_currentCard == null || targetObj == null)
            {
                return;
            }

            // 获取目标ID
            string targetId = GetTargetId(targetObj);
            PlaceCardOnTargetIds(targetId, ownerId);
        }

        private void PlaceCardOnTargetIds(string targetId, string ownerId)
        {
            if (string.IsNullOrEmpty(targetId))
            {
                Debug.LogWarning("[CardViewController] 无法确定目标ID");
                RestoreCardToHandState("无法从目标对象解析出 UnitId");
                return;
            }

            string ownerUnitId = ResolveOwnerUnitId(ownerId);
            if (string.IsNullOrEmpty(ownerUnitId))
            {
                Debug.LogWarning($"[CardViewController] 无法解析施法者单位ID: ownerId={ownerId}");
                RestoreCardToHandState($"无法解析施法者单位ID ownerId={ownerId}");
                return;
            }

            var battleManager = Ashlight.Battle.BattleManager.Instance;
            if (battleManager == null)
            {
                Debug.LogWarning("[CardViewController] BattleManager 不存在，无法立即执行卡牌");
                RestoreCardToHandState("BattleManager.Instance 为 null");
                return;
            }

            bool isExecutionCard = _currentCard != null && _currentCard.CardType == CardTypeEnum.Execution;
            bool isChargeCard = _currentCard != null && _currentCard.CardType == CardTypeEnum.Charge;
            bool success;
            if (isExecutionCard)
            {
                success = battleManager.TryQueuePlayerExecutionCard(_currentCard, ownerUnitId, targetId, InstanceId, out _);
            }
            else if (isChargeCard)
            {
                success = battleManager.TryStartPlayerChargeCard(_currentCard, ownerUnitId, targetId, InstanceId);
            }
            else
            {
                success = battleManager.TryPlayCardImmediately(_currentCard, ownerUnitId, targetId, InstanceId);
            }

            if (!success)
            {
                Debug.LogWarning($"[CardViewController] 打牌失败，恢复手牌状态 (execution={isExecutionCard}, owner={ownerUnitId}, target={targetId}) —— 真正原因见上方 [BattleManager] 告警");
                string entry = isExecutionCard ? "TryQueuePlayerExecutionCard"
                    : isChargeCard ? "TryStartPlayerChargeCard" : "TryPlayCardImmediately";
                RestoreCardToHandState($"{entry} 返回 false，详见上方 [BattleManager] 告警");
                return;
            }

            var battleScene = FindObjectOfType<UI_BattleScene>();
            battleScene?.CommitTempoActionPreview(ownerUnitId);
            if (isExecutionCard)
            {
                battleScene?.OnPlayerPlayedExecutionCard(this, ownerUnitId);
            }
            else if (isChargeCard)
            {
                battleScene?.OnPlayerPlayedChargeCard(this);
            }

            battleScene?.ConsumeHandCard(this);

            // 秒放牌可能产出新卡（如刀扇产出飞刀）：从数据层补齐这些卡的手牌 UI
            if (!isExecutionCard)
            {
                battleScene?.RefreshHandFromData();
                // 【推迟落账】推迟类效果立即反映到 ATB 调度和行动顺序视图
                battleScene?.ApplyPendingScheduleChanges();
            }

            battleScene?.OnTempoPrototypeCardPlayed(ownerUnitId);

            Debug.Log($"[CardViewController] 出牌完成: card={_currentCard?.Name}, ownerId={ownerUnitId}, targetId={targetId}, execution={isExecutionCard}");
        }

        /// <summary>
        /// 从GameObject获取目标ID
        /// </summary>
        private string GetTargetId(GameObject targetObj)
        {
            if (targetObj == null)
            {
                return null;
            }

            // 目标可能是角色/敌人的子节点，优先向父级查找
            var character = targetObj.GetComponentInParent<Character>();
            if (character != null)
            {
                return character.GetUnitState()?.UnitId;
            }

            var enemy = targetObj.GetComponentInParent<Enemy>();
            if (enemy != null)
            {
                return enemy.GetUnitState()?.UnitId;
            }

            return null;
        }

        /// <summary>
        /// 查找目标的时间轴
        /// </summary>
        private Timeline.TimelineTrackView FindTimelineForTarget(GameObject targetObj)
        {
            string targetId = GetTargetId(targetObj);
            if (string.IsNullOrEmpty(targetId))
            {
                return null;
            }

            var battleScene = FindObjectOfType<UI_BattleScene>();
            if (battleScene == null)
            {
                return null;
            }

            // 玩家角色：查找对应的时间轴
            var character = targetObj.GetComponent<Character>();
            if (character != null)
            {
                return battleScene.FindTimelineByUnitId(targetId);
            }

            // 敌人：使用共享时间轴
            var enemy = targetObj.GetComponent<Enemy>();
            if (enemy != null)
            {
                return battleScene.FindTimelineByUnitId(targetId);
            }

            return null;
        }

        /// <summary>
        /// 查找下一个可用slot
        /// </summary>
        private int FindNextAvailableSlot(Timeline.TimelineTrackView timeline, int requiredSlots)
        {
            for (int i = 0; i < Ashlight.Battle.Core.Data.TimelineTrack.TrackLength - requiredSlots; i++)
            {
                if (timeline.GetTrack().CanPlaceCard(i, requiredSlots))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 解析施法者单位ID（优先返回 player_x）
        /// </summary>
        private string ResolveOwnerUnitId(string ownerId)
        {
            if (string.IsNullOrEmpty(ownerId))
            {
                return null;
            }

            if (ownerId.StartsWith("player_"))
            {
                return ownerId;
            }

            var battleScene = FindObjectOfType<UI_BattleScene>();
            if (battleScene == null)
            {
                return ownerId;
            }

            var characters = battleScene.GetAllPlayerCharacters();
            foreach (var character in characters)
            {
                if (character == null)
                {
                    continue;
                }

                var unitState = character.GetUnitState();
                if (unitState == null)
                {
                    continue;
                }

                if (unitState.ConfigId == ownerId)
                {
                    return unitState.UnitId;
                }
            }

            return ownerId;
        }

        /// <summary>
        /// 恢复卡牌到手牌状态
        /// </summary>
        private void RestoreCardToHandState(string reason = null)
        {
            // 取消目标选择时必须先回到布局记录的基准位置，不能保留悬停抬升或其 Tween 中间值。
            ForceResetHoverLift();

            // 恢复缩放
            _scaleTween?.Kill();
            if (Card != null && Card.transform != null)
            {
                _scaleTween = Card.transform.DOScale(_originalCardScale, scaleDuration)
                    .SetEase(Ease.OutBack);
            }

            // 恢复层级
            if (!_isHovering)
            {
                RestoreCard();
            }

            // 恢复透明度
            SetCanvasGroupAlpha(_originalAlpha);

            Debug.Log($"[CardViewController] 卡牌已恢复到手牌状态：{(string.IsNullOrEmpty(reason) ? "未知原因" : reason)} (card={_currentCard?.Name})");
        }

        #endregion

        #endregion
    }
}

