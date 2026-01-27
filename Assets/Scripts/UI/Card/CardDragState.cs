namespace Scripts.UI
{
    /// <summary>
    /// Card 的拖拽状态
    /// </summary>
    public enum CardDragState
    {
        /// <summary>
        /// 在手牌中（可以拖到时间轴）
        /// </summary>
        OnHand,
        
        /// <summary>
        /// 在时间轴上（可以移动位置或撤回）
        /// </summary>
        OnTime
    }
}

