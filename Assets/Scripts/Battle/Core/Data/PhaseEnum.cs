namespace Ashlight.Battle.Core.Data
{
    /// <summary>
    /// 时间轴阶段枚举
    /// </summary>
    public enum PhaseEnum
    {
        /// <summary>
        /// 前摇阶段（引导期）
        /// </summary>
        Startup = 0,

        /// <summary>
        /// 生效阶段（伤害/效果生效）
        /// </summary>
        Active = 1,

        /// <summary>
        /// 僵直阶段（后摇期）
        /// </summary>
        Recoil = 2
    }
}

