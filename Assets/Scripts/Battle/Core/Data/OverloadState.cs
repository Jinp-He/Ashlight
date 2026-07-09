namespace Ashlight.Battle.Core.Data
{
    /// <summary>
    /// 过载状态
    /// 记录单位的过载次数和等级，影响下次行动的公共回合延迟（每次过载 +1 回合）。
    /// </summary>
    public class OverloadState
    {
        /// <summary>
        /// 是否处于过载状态
        /// </summary>
        public bool IsOverloaded { get; set; }

        /// <summary>
        /// 当前过载等级
        /// 0=无过载, 1=轻度, 2=中度, 3=重度（仅用于封顶与显示，不再决定时间延迟）
        /// </summary>
        public int OverloadLevel { get; set; }

        /// <summary>
        /// 本回合已过载次数（回合结束/开始时重置）。
        /// 【公共回合制】= 下次行动重排时额外延迟的回合数（每次过载 +1 回合）。
        /// </summary>
        public int OverloadCountThisTurn { get; set; }

        /// <summary>
        /// 本回合是否已用过「能量透支」型过载（每人每回合限 1 次）。
        /// 与卡牌 [过载] 效果分开计：打过载牌不占用透支额度。
        /// </summary>
        public bool EnergyOverdraftUsedThisTurn { get; set; }

        public OverloadState()
        {
            IsOverloaded = false;
            OverloadLevel = 0;
            OverloadCountThisTurn = 0;
            EnergyOverdraftUsedThisTurn = false;
        }

        /// <summary>
        /// 执行过载：本回合过载计数 +<paramref name="amount"/>（= 下次行动额外延迟的回合数），
        /// 并提升过载等级（封顶 3）。
        /// </summary>
        /// <returns>本次过载新增的回合延迟</returns>
        public int ApplyOverload(int amount = 1)
        {
            if (amount <= 0)
            {
                return 0;
            }

            OverloadCountThisTurn += amount;
            IsOverloaded = true;

            OverloadLevel = System.Math.Min(3, OverloadLevel + amount);

            return amount;
        }

        /// <summary>
        /// 回合结束后重置本回合过载计数与状态。
        /// </summary>
        public void OnTurnEnd()
        {
            OverloadCountThisTurn = 0;
            OverloadLevel = 0;
            IsOverloaded = false;
            EnergyOverdraftUsedThisTurn = false;
        }

        public OverloadState Clone()
        {
            return new OverloadState
            {
                IsOverloaded = this.IsOverloaded,
                OverloadLevel = this.OverloadLevel,
                OverloadCountThisTurn = this.OverloadCountThisTurn,
                EnergyOverdraftUsedThisTurn = this.EnergyOverdraftUsedThisTurn
            };
        }
    }
}
