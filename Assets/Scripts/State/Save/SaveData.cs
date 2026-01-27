using System;
using System.Collections.Generic;
using Ashlight.State.Runtime;
using cfg;

namespace Ashlight.State.Save
{
    /// <summary>
    /// 存档数据结构 - 包含所有需要持久化的状态
    /// 状态层：序列化友好，纯数据
    /// </summary>
    [Serializable]
    public class SaveData
    {
        /// <summary>
        /// 存档版本号（用于兼容性处理）
        /// </summary>
        public int Version;

        /// <summary>
        /// 存档时间戳
        /// </summary>
        public long SaveTimestamp;

        /// <summary>
        /// 玩家状态
        /// </summary>
        public PlayerState PlayerState;

        /// <summary>
        /// 所有角色的运行时状态列表
        /// 包含已解锁和未解锁的角色
        /// </summary>
        public List<CharacterRuntimeState> Characters;

        /// <summary>
        /// 已解锁的角色ID列表（用于快速查询）
        /// </summary>
        public List<CharacterEnum> UnlockedCharacters;

        /// <summary>
        /// 当前队伍中激活的角色ID列表
        /// 索引表示队伍槽位，槽位数量由游戏设计决定
        /// </summary>
        public List<CharacterEnum> ActiveTeam;

        // TODO: 添加其他需要存档的状态
        // public BuildingState[] Buildings;
        // public AdventurerState[] Adventurers;

        /// <summary>
        /// 创建新存档
        /// 注意：角色数据需要在上层（System层）通过访问配置表来初始化
        /// </summary>
        public static SaveData CreateNew()
        {
            return new SaveData
            {
                Version = 1,
                SaveTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                PlayerState = PlayerState.CreateDefault(),
                Characters = new List<CharacterRuntimeState>(),
                UnlockedCharacters = new List<CharacterEnum>(),
                ActiveTeam = new List<CharacterEnum>()
            };
        }
    }
}

