using System.Collections.Generic;
using cfg;

namespace Ashlight.Battle.Core.Data
{
    /// <summary>
    /// 战斗初始化参数
    /// 用于传递战斗开始所需的配置信息
    /// </summary>
    public class BattleInfo
    {
        /// <summary>
        /// 参战角色ID列表
        /// 从SaveData.ActiveTeam获取
        /// </summary>
        public List<CharacterEnum> PlayerCharacters { get; set; }

        /// <summary>
        /// 遭遇战ID
        /// 用于从TbEncounter加载敌人配置
        /// </summary>
        public string EncounterId { get; set; }

        /// <summary>
        /// 初始抽牌数量
        /// 默认为5张
        /// </summary>
        public int InitialDrawCount { get; set; }

        /// <summary>
        /// 战斗难度系数（可选，预留）
        /// </summary>
        public float DifficultyMultiplier { get; set; }

        /// <summary>
        /// 创建默认战斗信息
        /// </summary>
        public BattleInfo()
        {
            PlayerCharacters = new List<CharacterEnum>();
            EncounterId = string.Empty;
            InitialDrawCount = 5;
            DifficultyMultiplier = 1.0f;
        }

        /// <summary>
        /// 创建战斗信息
        /// </summary>
        /// <param name="playerCharacters">参战角色列表</param>
        /// <param name="encounterId">遭遇战ID</param>
        /// <param name="initialDrawCount">初始抽牌数量</param>
        public static BattleInfo Create(
            List<CharacterEnum> playerCharacters, 
            string encounterId, 
            int initialDrawCount = 5)
        {
            return new BattleInfo
            {
                PlayerCharacters = playerCharacters ?? new List<CharacterEnum>(),
                EncounterId = encounterId ?? string.Empty,
                InitialDrawCount = initialDrawCount,
                DifficultyMultiplier = 1.0f
            };
        }

        /// <summary>
        /// 验证战斗信息是否有效
        /// </summary>
        public bool IsValid()
        {
            if (PlayerCharacters == null || PlayerCharacters.Count == 0)
            {
                return false;
            }

            if (string.IsNullOrEmpty(EncounterId))
            {
                return false;
            }

            return true;
        }
    }
}

