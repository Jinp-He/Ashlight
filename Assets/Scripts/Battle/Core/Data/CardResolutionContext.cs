namespace Ashlight.Battle.Core.Data
{
    /// <summary>
    /// 单个在轨执行牌实例的结算上下文。与按 CardId 保存的永久升级修正分离，
    /// 用于顺延成长、余烬增压与回声等只属于这一枚引线的效果。
    /// </summary>
    public sealed class CardResolutionContext
    {
        public int AddedDelay { get; set; }
        public int DamageBonus { get; set; }
        public bool NumericOnly { get; set; }
        public float NumericScale { get; set; } = 1f;
        public bool MoraleConsumed { get; set; }
        public bool SuppressMoveHistory { get; set; }

        public CardResolutionContext Clone()
        {
            return new CardResolutionContext
            {
                AddedDelay = AddedDelay,
                DamageBonus = DamageBonus,
                NumericOnly = NumericOnly,
                NumericScale = NumericScale,
                MoraleConsumed = MoraleConsumed,
                SuppressMoveHistory = SuppressMoveHistory
            };
        }
    }
}
