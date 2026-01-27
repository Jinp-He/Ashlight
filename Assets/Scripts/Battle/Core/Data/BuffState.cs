namespace Ashlight.Battle.Core.Data
{
    /// <summary>
    /// 简化的Buff状态
    /// 存储Buff的基本信息和剩余持续时间
    /// </summary>
    public class BuffState
    {
        /// <summary>
        /// Buff ID（对应配置表中的Buff标识）
        /// </summary>
        public string BuffId { get; set; }

        /// <summary>
        /// Buff数值（如减伤0.5表示50%减伤）
        /// </summary>
        public float Value { get; set; }

        /// <summary>
        /// 剩余持续时间（回合数）
        /// -1表示永久，0表示本回合结束后消失
        /// </summary>
        public int RemainingDuration { get; set; }

        /// <summary>
        /// Buff叠加层数（预留，简化版本可忽略）
        /// </summary>
        public int StackCount { get; set; }

        public BuffState()
        {
            StackCount = 1;
        }

        /// <summary>
        /// 深拷贝
        /// </summary>
        public BuffState Clone()
        {
            return new BuffState
            {
                BuffId = this.BuffId,
                Value = this.Value,
                RemainingDuration = this.RemainingDuration,
                StackCount = this.StackCount
            };
        }

        /// <summary>
        /// 减少持续时间（每回合调用）
        /// </summary>
        /// <returns>是否已过期（应移除）</returns>
        public bool DecreaseDuration()
        {
            if (RemainingDuration == -1)
            {
                // 永久Buff
                return false;
            }

            RemainingDuration--;
            return RemainingDuration < 0;
        }
    }
}

