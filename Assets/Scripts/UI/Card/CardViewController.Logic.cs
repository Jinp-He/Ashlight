using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using cfg;
using cfg.Character;
using TMPro;
using DG.Tweening;
using Ashlight.Common.Events;
using Ashlight.Common.Utils;
using Ashlight.State.Runtime;
using System.Collections.Generic;
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
        private float hoverScale = 1.8f;

        [SerializeField]
        [Tooltip("缩放动画时长")]
        private float scaleDuration = 0.2f;

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

        [Header("目标选择颜色设置")]
        [SerializeField]
        [Tooltip("合法目标的变暗颜色（拖动时合法目标会变暗）")]
        private Color validTargetDimColor = new Color(0.5f, 0.5f, 0.5f, 1f);

        #endregion

        #region 私有字段

        private CardInfo _currentCard;
        private DescriptionMode _displayMode = DescriptionMode.View;

        /// <summary>
        /// 当前关联的 CardRuntimeState 的 InstanceId（用于对象池关联）
        /// </summary>
        private string _instanceId;
        private Vector3 _originalScale;
        private Vector3 _originalCardScale; // Card子对象的原始缩放
        private Tween _scaleTween;
        private DescriptionViewController _descriptionView;
        private Canvas _parentCanvas;
        private string _currentHoveredLink = string.Empty;
        
        // 层级管理相关
        private Transform _originalParent;
        private int _originalSiblingIndex;
        private Canvas _hoverCanvas;
        private bool _isHovering = false;

        // 拖拽相关
        private bool _isDragging = false;
        private bool _isInTimeSlot = false; // 是否已切换到时间轴状态
        private bool _hasLoggedNoTimeSlot = false; // 避免日志刷屏
        private Vector3 _dragOffset;
        private Vector3 _originalDragPosition;
        private Tween _positionTween;
        private CanvasGroup _canvasGroup;
        private float _originalAlpha = 1f;
        
        // Card 拖拽状态
        private CardDragState _cardDragState = CardDragState.OnHand;
        
        // 卡片锁定状态（被执行后不可移动）
        private bool _isLocked = false;
        
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
        private bool _isTargeting = false;
        
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
                GameObject descObj = Instantiate(descriptionViewControllerPrefab, _parentCanvas.transform);
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
            // 重置拖拽状态为 OnHand
            SetCardDragState(CardDragState.OnHand);

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

            // 重置视觉状态
            if (Card != null)
            {
                Card.alpha = 1f;
            }
            if (CardTimeSlot != null)
            {
                CardTimeSlot.gameObject.SetActive(false);
            }

            // 重置缩放
            transform.localScale = Vector3.one;

            // 重置其他状态
            _isDragging = false;
            _isHovering = false;
            _isInTimeSlot = false;
            _isTargeting = false;
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

            // 设置左侧消耗（引导时间）
            if (Txt_LeftCost != null)
            {
                Txt_LeftCost.text = _currentCard.Channeling.ToString();
            }

            // 设置右侧消耗（后摇）
            if (Txt_RightCost != null)
            {
                Txt_RightCost.text = _currentCard.Recoil.ToString();
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
            int rarityLevel = (int)_currentCard.Rarity + 1; // 0=普通(1星), 1=稀有(2星), 2=史诗(3星)

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
            // 如果正在拖拽，不处理悬停效果
            if (_isDragging)
                return;

            _isHovering = true;

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
        }

        /// <summary>
        /// 鼠标离开卡牌
        /// </summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            // 如果正在拖拽，不处理离开效果
            if (_isDragging)
                return;

            _isHovering = false;

            // 隐藏轮廓
            if (Img_Outline != null)
            {
                Img_Outline.gameObject.SetActive(false);
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

            // 隐藏描述面板
            HideDescription();
        }

        /// <summary>
        /// 鼠标点击卡牌
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            // 只在View模式下响应点击
            if (_displayMode == DescriptionMode.View && _currentCard != null)
            {
                // 发送选择卡牌到卡组的事件
                GameEvent.Publish(new SelectCardToDeckEvent
                {
                    cardInfo = _currentCard
                });

                Debug.Log($"[CardViewController] 选择卡牌到卡组: {_currentCard.Name}");
            }
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

            _isDragging = true;

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
                bool isValid = _targetManager?.IsValidTarget(targetObj, _currentCard.TargetType, ownerCharacterId) ?? false;

                // 更新箭头颜色
                if (_targetArrow != null)
                {
                    _targetArrow.UpdateLine(cardScreenPos, mousePos, isValid);
                }

                // 更新角色高亮
                UpdateTargetHighlighting(targetObj, isValid);

                _currentTargetObject = targetObj;
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
            }
        }
        
        /// <summary>
        /// 在手牌中结束拖拽
        /// </summary>
        private void OnEndDragOnHand(PointerEventData eventData)
        {
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

                // 验证并放置卡牌
                if (_currentTargetObject != null)
                {
                    CharacterEnum ownerCharacterId = GetOwnerCharacterId();
                    bool isValid = _targetManager?.IsValidTarget(_currentTargetObject, _currentCard.TargetType, ownerCharacterId) ?? false;
                    Debug.Log($"[CardViewController] OnEndDragOnHand 目标选择: target={_currentTargetObject.name}, isValid={isValid}, ownerId={ownerCharacterId}, targetType={_currentCard?.TargetType}");

                    if (isValid)
                    {
                        // 放置到目标时间轴的slot 0
                        string ownerId = ownerCharacterId.ToString();
                        PlaceCardOnTargetTimeline(_currentTargetObject, ownerId);
                    }
                    else
                    {
                        Debug.LogWarning("[CardViewController] 目标无效，已恢复到手牌");
                        // 非法目标,恢复到手牌状态
                        RestoreCardToHandState();
                    }
                }
                else
                {
                    Debug.LogWarning("[CardViewController] 未选择目标，已恢复到手牌");
                    // 未选择目标,恢复到手牌状态
                    RestoreCardToHandState();
                }

                _currentTargetObject = null;
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
                
                int totalSlots = _currentCard.Channeling + _currentCard.Duration + _currentCard.Recoil;
                
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
            return _currentCard.BelongTo;
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
            int totalSlots = _currentCard.Channeling + _currentCard.Duration + _currentCard.Recoil;
            
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
                    int totalSlots = _currentCard.Channeling + _currentCard.Duration + _currentCard.Recoil;
                    
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
            if (textComponent == null || _descriptionView == null)
                return;

            // 获取鼠标位置
            Vector3 mousePosition = Input.mousePosition;

            // 检测鼠标位置是否在link上
            int linkIndex = TMP_TextUtilities.FindIntersectingLink(textComponent, mousePosition, null);

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

            // 显示描述
            _descriptionView.Show(nounName);

            // 设置描述面板位置（卡牌右侧）
            Vector3 cardPosition = transform.position;
            Vector3 descPosition = cardPosition + new Vector3(descriptionOffset.x, descriptionOffset.y, 0f);
            _descriptionView.SetPosition(descPosition);
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
            string spritePath = AssetPath.GetCardSpriteAssetPath(cardId);

            // 从 Resources 加载 Sprite
            Sprite sprite = Resources.Load<Sprite>(spritePath);

            if (sprite != null)
            {
                Img_CardPicture.sprite = sprite;
                Debug.Log($"[CardViewController] 成功加载卡牌完整图片: {spritePath}");
            }
            else
            {
                Debug.LogWarning($"[CardViewController] 无法加载卡牌图片: {spritePath}");
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
                bool isValid = _targetManager.IsValidTarget(character.gameObject, _currentCard.TargetType, ownerCharacterId);
                
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
                bool isValid = _targetManager.IsValidTarget(enemy.gameObject, _currentCard.TargetType, ownerCharacterId);
                
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
                _previousHoveredTarget = null;
                return;
            }

            // 如果目标合法，恢复其颜色为白色（选中状态）
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

            // 保存当前悬停的目标
            _previousHoveredTarget = targetObj;

            // 检查是否为群体目标类型
            if (_currentCard.TargetType == cfg.TargetTypeEnum.AllAlly)
            {
                // 高亮所有合法队友（群体目标类型总是显示合法的目标，所以总是显示绿色）
                CharacterEnum ownerCharacterId = GetOwnerCharacterId();
                var allies = _targetManager?.GetAllValidAllies(ownerCharacterId);
                if (allies != null)
                {
                    foreach (var ally in allies)
                    {
                        if (ally != null)
                        {
                            ally.ShowIndicator(Color.green);
                        }
                    }
                }
            }
            else if (_currentCard.TargetType == cfg.TargetTypeEnum.AllEnemy)
            {
                // 高亮所有合法敌人（群体目标类型总是显示合法的目标，所以总是显示绿色）
                var enemies = _targetManager?.GetAllValidEnemies();
                if (enemies != null)
                {
                    foreach (var enemy in enemies)
                    {
                        if (enemy != null)
                        {
                            enemy.ShowIndicator(Color.green);
                        }
                    }
                }
            }
            else
            {
                // 单目标 - 如果目标非法，不显示Indicator
                if (!isValid)
                {
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
            bool isValid = _targetManager.IsValidTarget(targetObj, _currentCard.TargetType, ownerCharacterId);

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
            if (string.IsNullOrEmpty(targetId))
            {
                Debug.LogWarning("[CardViewController] 无法确定目标ID");
                RestoreCardToHandState();
                return;
            }

            // 查找施法者时间轴（放置到打出者的时间轴）
            Timeline.TimelineTrackView targetTimeline = null;
            if (!string.IsNullOrEmpty(ownerId))
            {
                var battleScene = FindObjectOfType<UI_BattleScene>();
                if (battleScene != null)
                {
                    targetTimeline = battleScene.FindTimelineByUnitId(ownerId);
                }
            }
            if (targetTimeline == null)
            {
                Debug.LogWarning("[CardViewController] 无法找到施法者时间轴");
                RestoreCardToHandState();
                return;
            }

            // 获取正确的UnitId（用于解算系统），而不是CharacterEnum字符串
            string unitIdForResolver = targetTimeline.GetUnitId();
            if (string.IsNullOrEmpty(unitIdForResolver))
            {
                // 如果无法获取UnitId，回退到使用ownerId（兼容旧代码）
                unitIdForResolver = ownerId;
                Debug.LogWarning($"[CardViewController] 无法获取UnitId，使用ownerId: {ownerId}");
            }

            // 检查slot 0是否可用
            int slotIndex = 0;
            int totalSlots = _currentCard.Channeling + _currentCard.Duration + _currentCard.Recoil;

            if (!targetTimeline.GetTrack().CanPlaceCard(slotIndex, totalSlots))
            {
                // slot 0被占用,查找下一个可用slot
                slotIndex = FindNextAvailableSlot(targetTimeline, totalSlots);
                if (slotIndex < 0)
                {
                    Debug.LogWarning("[CardViewController] 时间轴已满,无可用位置");
                    RestoreCardToHandState();
                    return;
                }
                Debug.Log($"[CardViewController] Slot 0已被占用,使用slot {slotIndex}");
            }

            // 放置卡牌（使用UnitId而不是CharacterEnum字符串，确保解算系统能正确找到单位）
            targetTimeline.OnCardDropped(_currentCard.Id, slotIndex, unitIdForResolver, targetId, this);

            Debug.Log($"[CardViewController] 放置卡牌完成: card={_currentCard?.Name}, ownerId={unitIdForResolver}, targetId={targetId}, track={targetTimeline?.name}, slot={slotIndex}");
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
        /// 恢复卡牌到手牌状态
        /// </summary>
        private void RestoreCardToHandState()
        {
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

            Debug.Log("[CardViewController] 卡牌已恢复到手牌状态（非法目标）");
        }

        #endregion

        #endregion
    }
}

