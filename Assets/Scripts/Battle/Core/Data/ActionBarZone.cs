namespace Ashlight.Battle.Core.Data
{
    /// <summary>
    /// 行动条区段枚举
    /// 定义单位在行动条上所处的区段，用于触发区段效果和 UI 表现
    /// </summary>
    public enum ActionBarZone
    {
        /// <summary>
        /// 起步区（0 ~ 24%）
        /// 刚从回合结束重启，过载惩罚主要体现在此区段
        /// </summary>
        Start = 0,

        /// <summary>
        /// 正常区（25% ~ 74%）
        /// 标准推进阶段
        /// </summary>
        Normal = 1,

        /// <summary>
        /// 临界区（75% ~ 99%）
        /// 即将获得行动权，某些效果在此区段触发
        /// </summary>
        Critical = 2,

        /// <summary>
        /// 行动中（已到达终点，正在执行回合）
        /// </summary>
        Acting = 3
    }

    /// <summary>
    /// 行动条区段工具类
    /// </summary>
    public static class ActionBarZoneHelper
    {
        public const float StartZoneEnd = 0.25f;
        public const float NormalZoneEnd = 0.75f;

        /// <summary>
        /// 根据当前段位和最大段位判定所处区段
        /// </summary>
        public static ActionBarZone GetZone(int currentSegment, int maxSegment)
        {
            if (currentSegment >= maxSegment)
            {
                return ActionBarZone.Acting;
            }

            float progress = (float)currentSegment / maxSegment;

            if (progress < StartZoneEnd)
            {
                return ActionBarZone.Start;
            }

            if (progress < NormalZoneEnd)
            {
                return ActionBarZone.Normal;
            }

            return ActionBarZone.Critical;
        }

        /// <summary>
        /// 判定给定段位是否在临界区
        /// </summary>
        public static bool IsInCriticalZone(int currentSegment, int maxSegment)
        {
            return GetZone(currentSegment, maxSegment) == ActionBarZone.Critical;
        }

        /// <summary>
        /// 判定给定段位是否在起步区
        /// </summary>
        public static bool IsInStartZone(int currentSegment, int maxSegment)
        {
            return GetZone(currentSegment, maxSegment) == ActionBarZone.Start;
        }
    }
}
