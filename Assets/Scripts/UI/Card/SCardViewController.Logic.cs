using UnityEngine;
using UnityEngine.EventSystems;
using cfg;
using cfg.Character;
using Ashlight.Common.Events;

namespace Scripts.UI
{
    /// <summary>
    /// SCardViewController的业务逻辑部分（手动编写）
    /// 简化版卡牌视图控制器，用于卡组显示
    /// </summary>
    public partial class SCardViewController : IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        #region 序列化字段

        [Header("层级设置")]
        [SerializeField]
        [Tooltip("ExpanseCardViewController悬停时的Canvas排序顺序")]
        private int hoverSortingOrder = 100;

        #endregion

        #region 私有字段

        private CardInfo _currentCard;
        private DescriptionMode _displayMode = DescriptionMode.View;
        
        // ExpanseCardViewController的Canvas引用
        private Canvas _expanseCanvas;
        
        // ExpanseCardViewController的CanvasGroup引用（用于控制透明度）
        private CanvasGroup _expanseCanvasGroup;

        #endregion
  
        #region Unity生命周期

        /// <summary>
        /// 初始化
        /// </summary>
        private void Awake()
        {
            // 调用自动生成的UI绑定初始化方法
            InitUIBindings();

            // 确保根对象有 Image 组件来接收射线投射（用于鼠标事件检测）
            EnsureRaycastTarget();

            // 初始化ExpanseCardViewController
            if (ExpanseCardViewController != null)
            {
                // 为ExpanseCardViewController添加Canvas组件（用于控制渲染优先级）
                _expanseCanvas = ExpanseCardViewController.gameObject.GetComponent<Canvas>();
                if (_expanseCanvas == null)
                {
                    _expanseCanvas = ExpanseCardViewController.gameObject.AddComponent<Canvas>();
                }
                
                // 添加GraphicRaycaster（确保UI交互正常）
                if (ExpanseCardViewController.gameObject.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                {
                    ExpanseCardViewController.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                }
                
                // 添加CanvasGroup组件（用于控制透明度）
                _expanseCanvasGroup = ExpanseCardViewController.gameObject.GetComponent<CanvasGroup>();
                if (_expanseCanvasGroup == null)
                {
                    _expanseCanvasGroup = ExpanseCardViewController.gameObject.AddComponent<CanvasGroup>();
                }
                
                // 初始设置为完全透明，且不阻挡射线
                _expanseCanvasGroup.alpha = 0f;
                _expanseCanvasGroup.blocksRaycasts = false;
                _expanseCanvasGroup.interactable = false;
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
                Debug.LogError("[SCardViewController] 卡牌数据为空");
                return;
            }

            _currentCard = card;
            _displayMode = mode;
            UpdateCardDisplay();

            // 同时初始化ExpanseCardViewController（如果存在）
            if (ExpanseCardViewController != null)
            {
                ExpanseCardViewController.GetComponent<CardViewController>().Initialize(card, mode);
            }
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
        /// <returns>卡牌信息</returns>
        public CardInfo GetCardInfo()
        {
            return _currentCard;
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 确保根对象有可以接收射线投射的组件
        /// </summary>
        private void EnsureRaycastTarget()
        {
            // 检查根对象上是否有 Image 组件
            var image = GetComponent<UnityEngine.UI.Image>();
            if (image == null)
            {
                // 如果没有，添加一个透明的 Image 组件
                image = gameObject.AddComponent<UnityEngine.UI.Image>();
                image.color = new Color(0, 0, 0, 0); // 完全透明
                Debug.Log("[SCardViewController] 添加透明Image组件以接收鼠标事件");
            }
            
            // 确保 raycastTarget 启用
            image.raycastTarget = true;
        }

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

            // 设置左侧消耗（能量）
            if (Txt_LeftCost != null)
            {
                Txt_LeftCost.text = _currentCard.Energy.ToString();
            }

            // 设置右侧消耗（卡牌类型）
            if (Txt_RightCost != null)
            {
                Txt_RightCost.text = _currentCard.CardType == cfg.CardTypeEnum.Swift ? "迅" : "执";
            }

            // 根据卡牌稀有度显示对应的框架
            UpdateFrameDisplay();

            // TODO: 加载卡牌图片资源
            // LoadCardPicture(_currentCard.AssetPath);
        }

        /// <summary>
        /// 更新框架显示（根据稀有度或升级状态）
        /// </summary>
        private void UpdateFrameDisplay()
        {
            if (_currentCard == null) return;

            // 默认显示未升级框架
            bool isUpgraded = false; // TODO: 从卡牌数据或其他来源获取升级状态

            if (Img_CardFrameUpgraded != null)
            {
                Img_CardFrameUpgraded.gameObject.SetActive(isUpgraded);
            }

            if (Img_CardFrameUnUpgraded != null)
            {
                Img_CardFrameUnUpgraded.gameObject.SetActive(!isUpgraded);
            }
        }

        /// <summary>
        /// 提升ExpanseCardViewController层级（脱离mask，显示在最前）
        /// </summary>
        private void ElevateExpanseCard()
        {
            if (_expanseCanvas == null) return;

            // 启用Canvas覆盖排序
            _expanseCanvas.overrideSorting = true;
            _expanseCanvas.sortingOrder = hoverSortingOrder;

            Debug.Log($"[SCardViewController] ExpanseCard提升层级: sortingOrder={hoverSortingOrder}");
        }

        /// <summary>
        /// 恢复ExpanseCardViewController原始层级
        /// </summary>
        private void RestoreExpanseCard()
        {
            if (_expanseCanvas == null) return;

            // 禁用Canvas覆盖排序
            _expanseCanvas.overrideSorting = false;

            Debug.Log("[SCardViewController] ExpanseCard恢复原始层级");
        }

        #endregion

        #region 鼠标事件处理

        /// <summary>
        /// 鼠标进入卡牌（悬停）
        /// </summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            // 显示轮廓
            if (Img_Outline != null)
            {
                Img_Outline.gameObject.SetActive(true);
            }

            // 显示展开的卡牌视图（使用CanvasGroup控制透明度）
            if (ExpanseCardViewController != null && _expanseCanvasGroup != null)
            {
                // 设置位置在鼠标右侧
                if (ExpanseCardViewController.transform is RectTransform rectTransform)
                {
                    Vector3 position = transform.position + new Vector3(200f, 0f, 0f);
                    ExpanseCardViewController.transform.position = position;
                }
                
                // 提升ExpanseCardViewController的渲染层级（无视mask，显示在最前）
                ElevateExpanseCard();
                
                // 使用CanvasGroup显示（透明度设为1，但不阻挡射线）
                _expanseCanvasGroup.alpha = 1f;
                _expanseCanvasGroup.blocksRaycasts = false;
                _expanseCanvasGroup.interactable = false;
            }
        }

        /// <summary>
        /// 鼠标离开卡牌
        /// </summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            // 隐藏轮廓
            if (Img_Outline != null)
            {
                Img_Outline.gameObject.SetActive(false);
            }

            // 恢复ExpanseCardViewController的原始层级
            RestoreExpanseCard();
            
            // 隐藏展开的卡牌视图（使用CanvasGroup控制透明度）
            if (_expanseCanvasGroup != null)
            {
                _expanseCanvasGroup.alpha = 0f;
                _expanseCanvasGroup.blocksRaycasts = false;
                _expanseCanvasGroup.interactable = false;
            }
        }

        /// <summary>
        /// 鼠标点击卡牌
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            Debug.Log($"[SCardViewController] 收到点击事件 - 按钮: {eventData.button}, 卡牌: {_currentCard?.Name ?? "null"}");
            
            // 右键点击删除卡牌
            if (eventData.button == PointerEventData.InputButton.Right && _currentCard != null)
            {
                Debug.Log($"[SCardViewController] 右键点击，准备删除卡牌: {_currentCard.Name}");

                // 发送删除卡牌事件
                GameEvent.Publish(new DeleteCardFromDeckEvent
                {
                    sCardView = this,
                    cardInfo = _currentCard
                });
                
                Debug.Log($"[SCardViewController] 已发布删除事件");
            }
            else if (eventData.button != PointerEventData.InputButton.Right)
            {
                Debug.Log($"[SCardViewController] 非右键点击，忽略");
            }
        }

        #endregion
    }
}

