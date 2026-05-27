using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using cfg.Character;
using Ashlight.Config;
using Ashlight.State.Runtime;
using Ashlight.Common.Utils;
using Scripts.UI.Timeline;

namespace Scripts.UI
{
    /// <summary>
    /// CardTimeSlot的业务逻辑部分（手动编写）
    /// 卡牌时间槽控制器，用于显示卡牌的引导时间、持续时间和后摇
    /// 注意：拖拽功能已转移到 CardViewController，根据 Card 的状态（OnHand/OnTime）决定行为
    /// </summary>
    public partial class CardTimeSlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        #region 私有字段

        private CanvasGroup _canvasGroup;
        
        // 注意：拖拽相关字段已移除，拖拽功能已转移到 CardViewController
        // 保留以下字段用于其他功能
        private Timeline.TimelineTrackView _parentTrack;
        [SerializeField]
        [Header("起始槽位索引")]
        private int _slotIndex = -1;  // 当前槽位索引（与EnemyTimeSlot保持一致）
        private CardRuntimeState _cardState;

        #endregion

        #region Unity生命周期

        /// <summary>
        /// 初始化
        /// </summary>
        private void Awake()
        {
            // 调用自动生成的UI绑定初始化方法
            InitUIBindings();

            // 创建或获取CanvasGroup组件
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            // 默认隐藏（alpha = 0）
            _canvasGroup.alpha = 0f;
            
            // 确保可以接收鼠标事件（用于悬停检测）
            // 检查是否有 Image 组件，如果没有则添加一个
            Image image = GetComponent<Image>();
            if (image == null)
            {
                image = gameObject.AddComponent<Image>();
                image.color = new Color(1f, 1f, 1f, 0f); // 透明但可接收事件
            }
            image.raycastTarget = true; // 确保可以接收射线检测
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 初始化并加载卡牌数据
        /// </summary>
        /// <param name="cardRuntimeState">卡牌运行时状态</param>
        public void InitLoad(CardRuntimeState cardRuntimeState)
        {
            if (cardRuntimeState == null)
            {
                Debug.LogError("[CardTimeSlot] CardRuntimeState为空");
                return;
            }
            
            // 保存卡牌状态
            _cardState = cardRuntimeState;

            // 从配置表获取卡牌信息
            var cardInfo = ConfigLoader.Tables?.TbCardInfo?.GetOrDefault(cardRuntimeState.CardId);
            if (cardInfo == null)
            {
                Debug.LogError($"[CardTimeSlot] 未找到卡牌配置: {cardRuntimeState.CardId}");
                return;
            }

            float slotLength = 100f;

            SetImageLength(Channeling, 0f);
            SetImageLength(Duration, slotLength);
            SetImageLength(Recoil, 0f);

            UpdateCardName(cardInfo.Name);
            UpdateMiniSprite(cardRuntimeState.CardId);

            Debug.Log($"[CardTimeSlot] 初始化完成，卡牌ID: {cardRuntimeState.CardId}, 名称: {cardInfo.Name}, CardType: {cardInfo.CardType}");
        }
        
        /// <summary>
        /// 设置父时间轴和格子索引（在放置到时间轴时调用）
        /// </summary>
        public void SetTimelineInfo(TimelineTrackView parentTrack, int slotIndex)
        {
            _parentTrack = parentTrack;
            _slotIndex = slotIndex;
            
            Debug.Log($"[CardTimeSlot] 设置时间轴信息: 轨道={parentTrack?.name}, 格子索引={slotIndex}");
        }
        
        /// <summary>
        /// 获取当前格子索引
        /// </summary>
        public int GetSlotIndex()
        {
            return _slotIndex;
        }
        
        /// <summary>
        /// 获取父时间轴
        /// </summary>
        public TimelineTrackView GetParentTrack()
        {
            return _parentTrack;
        }
        
        /// <summary>
        /// 获取卡牌状态
        /// </summary>
        public CardRuntimeState GetCardState()
        {
            return _cardState;
        }

        /// <summary>
        /// 显示时间槽（设置alpha为1）
        /// </summary>
        public void Show()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
            }
        }

        /// <summary>
        /// 隐藏时间槽（设置alpha为0）
        /// </summary>
        public void Hide()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
            }
        }

        #endregion
        
        #region 鼠标悬停处理（OnTime状态）
        
        /// <summary>
        /// 鼠标进入 CardTimeSlot（仅在 OnTime 状态下有效）
        /// </summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            // 获取父级 CardViewController
            CardViewController cardViewController = GetComponentInParent<CardViewController>();
            if (cardViewController != null)
            {
                // 通知 CardViewController 显示 Card 并向右偏移
                cardViewController.OnCardTimeSlotHover(true);
            }
        }
        
        /// <summary>
        /// 鼠标离开 CardTimeSlot（仅在 OnTime 状态下有效）
        /// </summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            // 获取父级 CardViewController
            CardViewController cardViewController = GetComponentInParent<CardViewController>();
            if (cardViewController != null)
            {
                // 通知 CardViewController 隐藏 Card 并恢复位置
                cardViewController.OnCardTimeSlotHover(false);
            }
        }
        
        #endregion
        
        // 注意：拖拽功能已转移到 CardViewController，根据 Card 的状态（OnHand/OnTime）决定行为
        // 所有拖拽相关代码已移除

        #region 私有方法

        /// <summary>
        /// 设置Image的长度
        /// </summary>
        /// <param name="image">要设置的Image组件</param>
        /// <param name="length">目标长度</param>
        private void SetImageLength(Image image, float length)
        {
            if (image == null)
            {
                Debug.LogWarning("[CardTimeSlot] Image组件为空，跳过设置");
                return;
            }

            RectTransform rectTransform = image.rectTransform;
            if (rectTransform == null)
            {
                Debug.LogWarning("[CardTimeSlot] RectTransform为空，跳过设置");
                return;
            }

            // 设置宽度为指定长度，保持高度不变
            Vector2 currentSize = rectTransform.sizeDelta;
            rectTransform.sizeDelta = new Vector2(length, currentSize.y);
        }

        /// <summary>
        /// 更新卡牌名称文本
        /// </summary>
        /// <param name="cardName">卡牌名称</param>
        private void UpdateCardName(string cardName)
        {
            if (CardNameText == null)
            {
                Debug.LogWarning("[CardTimeSlot] CardNameText为空，跳过设置卡牌名称");
                return;
            }

            CardNameText.text = cardName;
            Debug.Log($"[CardTimeSlot] 已更新卡牌名称: {cardName}");
        }

        /// <summary>
        /// 更新 MiniSprite 图片
        /// </summary>
        /// <param name="cardId">卡牌ID</param>
        public void UpdateMiniSprite(string cardId)
        {
            if (MiniSprite == null)
            {
                Debug.LogWarning("[CardTimeSlot] MiniSprite为空，无法加载卡牌图片");
                return;
            }

            if (string.IsNullOrEmpty(cardId))
            {
                Debug.LogWarning("[CardTimeSlot] cardId为空，无法加载卡牌图片");
                return;
            }

            // 获取 MiniSprite 资源路径
            string spritePath = AssetPath.GetCardMiniSpriteAssetPath(cardId);

            // 从 Resources 加载 Sprite
            Sprite sprite = Resources.Load<Sprite>(spritePath);

            if (sprite != null)
            {
                MiniSprite.sprite = sprite;
                Debug.Log($"[CardTimeSlot] 成功加载卡牌 MiniSprite: {spritePath}");
            }
            else
            {
                Debug.LogWarning($"[CardTimeSlot] 无法加载卡牌 MiniSprite: {spritePath}");
            }
        }

        #endregion
    }
}

