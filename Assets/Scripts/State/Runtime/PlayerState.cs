using System;

namespace Ashlight.State.Runtime
{
    /// <summary>
    /// 玩家状态 - 运行时可变数据
    /// 状态层：支持 Save/Load，纯 C# 数据结构
    /// </summary>
    [Serializable]
    public class PlayerState
    {
        /// <summary>
        /// 玩家等级
        /// </summary>
        public int Level;

        /// <summary>
        /// 玩家经验值
        /// </summary>
        public int Experience;

        /// <summary>
        /// 玩家金币
        /// </summary>
        public long Gold;

        /// <summary>
        /// 创建默认初始状态
        /// </summary>
        public static PlayerState CreateDefault()
        {
            return new PlayerState
            {
                Level = 1,
                Experience = 0,
                Gold = 0
            };
        }
    }
}

