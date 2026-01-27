using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace Scripts.UI.Timeline
{
    /// <summary>
    /// 时间轴格子视图
    /// 表示时间轴上的单个格子（0-14）
    /// 必须设置 Tag="TimeSlot" 以供 CardViewController 检测
    /// 必须有 Image 组件用于射线检测
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class TimelineSlotView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        #region 序列化字段
        
        [Header("UI引用")]
        [SerializeField] 
        [Tooltip("背景图片")]
        private Image background;
        
        [SerializeField] 
        [Tooltip("索引文本显示")]
        private TextMeshProUGUI indexText;
        
        [SerializeField] 
        [Tooltip("高亮遮罩")]
        private Image highlightOverlay;
        
        [Header("颜色设置")]
        [SerializeField] 
        [Tooltip("空格子颜色")]
        private Color emptyColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
        
        [SerializeField] 
        [Tooltip("已占用格子颜色")]
        private Color occupiedColor = new Color(0.6f, 0.1f, 0.1f, 0.5f);
        
        [SerializeField] 
        [Tooltip("可放置高亮颜色")]
        private Color canPlaceColor = new Color(0.1f, 0.8f, 0.1f, 0.3f);
        
        [SerializeField] 
        [Tooltip("不可放置高亮颜色")]
        private Color cannotPlaceColor = new Color(0.8f, 0.1f, 0.1f, 0.3f);
        
        #endregion
        
        #region 私有字段
        
        private int _slotIndex;
        private bool _isOccupied = false;
        private TimelineTrackView _parentTrack;
        
        #endregion
        
        #region 属性
        
        /// <summary>
        /// 格子索引（0-14）
        /// </summary>
        public int SlotIndex => _slotIndex;
        
        /// <summary>
        /// 是否已被占用
        /// </summary>
        public bool IsOccupied => _isOccupied;
        
        #endregion
        
        #region 公共方法
        
        /// <summary>
        /// 初始化时间轴格子
        /// </summary>
        /// <param name="index">格子索引（0-14）</param>
        /// <param name="parent">父时间轴视图</param>
        public void Initialize(int index, TimelineTrackView parent)
        {
            _slotIndex = index;
            _parentTrack = parent;
            
            // 确保Tag设置为"TimeSlot"（供CardViewController检测）
            if (!gameObject.CompareTag("TimeSlot"))
            {
                gameObject.tag = "TimeSlot";
                Debug.Log($"[TimelineSlotView] 自动设置 Tag='TimeSlot' (索引: {index})");
            }
            
            // 确保 background Image 的 raycastTarget 启用（关键！）
            if (background != null)
            {
                background.raycastTarget = true;
                Debug.Log($"[TimelineSlotView] 启用 background.raycastTarget (索引: {index})");
            }
            else
            {
                Debug.LogError($"[TimelineSlotView] background 为 null！无法启用 raycastTarget (索引: {index})");
            }
            
            // 显示索引
            if (indexText != null)
            {
                indexText.text = index.ToString();
            }
            
            // 初始化为空状态
            SetOccupied(false);
            ClearHighlight();
            
            Debug.Log($"[TimelineSlotView] 初始化完成，索引: {index}");
        }
        
        /// <summary>
        /// 设置格子占用状态
        /// </summary>
        /// <param name="occupied">是否被占用</param>
        public void SetOccupied(bool occupied)
        {
            _isOccupied = occupied;
            
            // 平常保持透明，不在 SetOccupied 时显示颜色
            // 颜色只在拖拽时通过 SetHighlight 显示
            if (background != null)
            {
                // 保持透明（不显示占用状态的颜色）
                background.color = new Color(0f, 0f, 0f, 0f);
            }
        }
        
        /// <summary>
        /// 设置高亮显示（拖拽时显示颜色）
        /// </summary>
        /// <param name="canPlace">是否可以放置</param>
        public void SetHighlight(bool canPlace)
        {
            // 显示背景颜色（拖拽时）
            if (background != null)
            {
                Color highlightColor = canPlace ? canPlaceColor : cannotPlaceColor;
                // 保持 RGB，但使用更高的 alpha 值使其更明显
                highlightColor.a = canPlace ? 0.5f : 0.5f;
                background.color = highlightColor;
            }
            
            // 同时显示高亮遮罩（可选，用于更明显的视觉效果）
            if (highlightOverlay != null)
            {
                highlightOverlay.gameObject.SetActive(true);
                highlightOverlay.color = canPlace ? canPlaceColor : cannotPlaceColor;
            }
        }
        
        /// <summary>
        /// 清除高亮（恢复透明）
        /// </summary>
        public void ClearHighlight()
        {
            // 恢复背景为透明
            if (background != null)
            {
                background.color = new Color(0f, 0f, 0f, 0f);
            }
            
            // 隐藏高亮遮罩
            if (highlightOverlay != null)
            {
                highlightOverlay.gameObject.SetActive(false);
            }
        }
        
        /// <summary>
        /// 通知父TimelineTrackView有卡牌被放置
        /// 由CardViewController的OnEndDrag调用
        /// </summary>
        /// <param name="cardId">卡牌ID</param>
        /// <param name="ownerId">所属角色ID</param>
        /// <param name="targetId">目标ID</param>
        /// <param name="cardViewController">要转移的整个CardViewController</param>
        public void NotifyCardDropped(string cardId, string ownerId, string targetId, CardViewController cardViewController)
        {
            if (_parentTrack != null)
            {
                Debug.Log($"[TimelineSlotView] 通知父轨道：卡牌 {cardId} 被放置到索引 {_slotIndex}");
                _parentTrack.OnCardDropped(cardId, _slotIndex, ownerId, targetId, cardViewController);
            }
            else
            {
                Debug.LogError("[TimelineSlotView] 父轨道为null，无法通知卡牌放置");
            }
        }
        
        /// <summary>
        /// 获取父时间轴视图
        /// </summary>
        public TimelineTrackView GetParentTrack()
        {
            return _parentTrack;
        }
        
        #endregion
        
        #region IPointerHandler 实现（用于调试）
        
        /// <summary>
        /// 鼠标进入事件（用于调试）
        /// </summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            //Debug.Log($"[TimelineSlotView] 鼠标进入格子 {_slotIndex}");
        }
        
        /// <summary>
        /// 鼠标离开事件（用于调试）
        /// </summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            //Debug.Log($"[TimelineSlotView] 鼠标离开格子 {_slotIndex}");
        }
        
        #endregion
    }
}


