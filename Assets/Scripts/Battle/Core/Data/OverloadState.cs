namespace Ashlight.Battle.Core.Data
{
    /// <summary>
    /// 过载状态
    /// 记录单位的过载负债和等级，影响下次行动条起跑位置
    /// </summary>
    public class OverloadState
    {
        /// <summary>
        /// 是否处于过载状态
        /// </summary>
        public bool IsOverloaded { get; set; }

        /// <summary>
        /// 过载负债（段位数），累积值
        /// 回合结束后会使 RestartSegment 后退对应段数
        /// </summary>
        public int OverloadDebt { get; set; }

        /// <summary>
        /// 当前过载等级
        /// 0=无过载, 1=轻度, 2=中度, 3=重度
        /// </summary>
        public int OverloadLevel { get; set; }

        /// <summary>
        /// 本回合已过载次数（回合结束后重置）
        /// </summary>
        public int OverloadCountThisTurn { get; set; }

        /// <summary>
        /// 轻度过载的负债增量（段位数）
        /// </summary>
        public const int LightDebtIncrement = 5;

        /// <summary>
        /// 中度过载的负债增量
        /// </summary>
        public const int MediumDebtIncrement = 12;

        /// <summary>
        /// 重度过载的负债增量
        /// </summary>
        public const int HeavyDebtIncrement = 25;

        public OverloadState()
        {
            IsOverloaded = false;
            OverloadDebt = 0;
            OverloadLevel = 0;
            OverloadCountThisTurn = 0;
        }

        /// <summary>
        /// 执行一次过载，增加负债
        /// </summary>
        /// <returns>本次过载增加的负债段数</returns>
        public int ApplyOverload()
        {
            OverloadCountThisTurn++;
            IsOverloaded = true;

            int debtIncrement;
            if (OverloadLevel < 1)
            {
                OverloadLevel = 1;
                debtIncrement = LightDebtIncrement;
            }
            else if (OverloadLevel < 2)
            {
                OverloadLevel = 2;
                debtIncrement = MediumDebtIncrement;
            }
            else
            {
                OverloadLevel = 3;
                debtIncrement = HeavyDebtIncrement;
            }

            OverloadDebt += debtIncrement;
            return debtIncrement;
        }

        /// <summary>
        /// 回合结束后重置本回合过载计数
        /// 过载等级和负债保留到下次行动条结算时消耗
        /// </summary>
        public void OnTurnEnd()
        {
            OverloadCountThisTurn = 0;
        }

        /// <summary>
        /// 消耗过载负债（行动条重启时调用）
        /// </summary>
        /// <returns>被消耗的负债段数</returns>
        public int ConsumeDebt()
        {
            int consumed = OverloadDebt;
            OverloadDebt = 0;
            OverloadLevel = 0;
            IsOverloaded = false;
            return consumed;
        }

        public OverloadState Clone()
        {
            return new OverloadState
            {
                IsOverloaded = this.IsOverloaded,
                OverloadDebt = this.OverloadDebt,
                OverloadLevel = this.OverloadLevel,
                OverloadCountThisTurn = this.OverloadCountThisTurn
            };
        }
    }
}
