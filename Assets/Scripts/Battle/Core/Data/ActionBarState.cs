namespace Ashlight.Battle.Core.Data
{
    /// <summary>
    /// 单位行动条状态
    /// 离散 ATB 系统核心数据结构，记录单位在行动条上的当前位置和相关参数
    /// </summary>
    public class ActionBarState
    {
        /// <summary>
        /// 当前段位（0 ~ MaxSegment）
        /// 到达 MaxSegment 时获得行动权
        /// </summary>
        public int CurrentSegment { get; set; }

        /// <summary>
        /// 行动条总段位数（到达此值时获得回合）
        /// </summary>
        public int MaxSegment { get; set; }

        /// <summary>
        /// 回合结束后的起跑段位
        /// 受过载负债影响，负债越高起跑段位越靠后
        /// </summary>
        public int RestartSegment { get; set; }

        /// <summary>
        /// 基础起跑段位（无过载负债时的起跑位置）
        /// </summary>
        public int BaseRestartSegment { get; set; }

        public const int DefaultMaxSegment = 100;
        public const int DefaultBaseRestartSegment = 0;

        public ActionBarState()
        {
            CurrentSegment = 0;
            MaxSegment = DefaultMaxSegment;
            RestartSegment = DefaultBaseRestartSegment;
            BaseRestartSegment = DefaultBaseRestartSegment;
        }

        /// <summary>
        /// 是否到达终点（可以获得行动权）
        /// </summary>
        public bool HasReachedEnd => CurrentSegment >= MaxSegment;

        /// <summary>
        /// 剩余段位数（距离获得行动权还差多少段）
        /// </summary>
        public int RemainingSegments => MaxSegment - CurrentSegment;

        /// <summary>
        /// 推进行动条
        /// </summary>
        /// <param name="segments">推进段数</param>
        public void Advance(int segments)
        {
            CurrentSegment += segments;
            if (CurrentSegment > MaxSegment)
            {
                CurrentSegment = MaxSegment;
            }
        }

        /// <summary>
        /// 偏移行动条（支持正向推进和反向拉回）
        /// </summary>
        /// <param name="shift">偏移段数，正数前进，负数后退</param>
        public void Shift(int shift)
        {
            CurrentSegment += shift;
            if (CurrentSegment < 0)
            {
                CurrentSegment = 0;
            }
            if (CurrentSegment > MaxSegment)
            {
                CurrentSegment = MaxSegment;
            }
        }

        /// <summary>
        /// 重置行动条到起跑段位（回合结束后调用）
        /// </summary>
        public void Restart()
        {
            CurrentSegment = RestartSegment;
        }

        /// <summary>
        /// 根据过载负债计算实际起跑段位
        /// </summary>
        /// <param name="overloadDebt">过载负债段数</param>
        public void ApplyOverloadPenalty(int overloadDebt)
        {
            RestartSegment = BaseRestartSegment - overloadDebt;
            if (RestartSegment < 0)
            {
                RestartSegment = 0;
            }
        }

        public ActionBarState Clone()
        {
            return new ActionBarState
            {
                CurrentSegment = this.CurrentSegment,
                MaxSegment = this.MaxSegment,
                RestartSegment = this.RestartSegment,
                BaseRestartSegment = this.BaseRestartSegment
            };
        }
    }
}
