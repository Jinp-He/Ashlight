using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Ashlight.Config;
using Ashlight.State.Runtime;
using Ashlight.Systems.Core;
using cfg;
using Ashlight.State.Save;
namespace Ashlight.Systems.Character
{
    /// <summary>
    /// 角色系统 - 负责角色数据的管理和业务逻辑
    /// 系统层：驱动规则，修改状态，不直接操作UI
    /// </summary>
    public static class CharacterSystem
    {
        /// <summary>
        /// 获取存档数据（内部辅助方法）
        /// </summary>
        /// <returns>存档数据，如果无效则返回null</returns>
        private static SaveData GetSaveData()
        {
            if (GameManager.Instance == null)
            {
                // GameManager 尚未初始化（如直接从 BattleScene 启动的测试场景）
                // 调用方需自行处理返回 null 的情况
                Debug.LogWarning("[CharacterSystem] GameManager.Instance 为空，跳过存档读取");
                return null;
            }

            var saveData = GameManager.Instance.CurrentSave;
            if (saveData == null)
            {
                Debug.LogError("[CharacterSystem] CurrentSave 为空");
                return null;
            }

            return saveData;
        }
        /// <summary>
        /// 初始化默认角色数据
        /// 从配置表读取所有角色并创建默认状态
        /// </summary>
        /// <param name="unlockFirst">是否解锁第一个角色</param>
        public static void InitializeCharacters(bool unlockFirst = true)
        {
            var saveData = GetSaveData();
            if (saveData == null)
            {
                Debug.LogError("[CharacterSystem] 无法获取存档数据，无法初始化角色");
                return;
            }

            // 初始化必要的列表
            if (saveData.Characters == null)
            {
                saveData.Characters = new List<CharacterRuntimeState>();
            }
            if (saveData.UnlockedCharacters == null)
            {
                saveData.UnlockedCharacters = new List<CharacterEnum>();
            }

            // 清空现有角色数据
            saveData.Characters.Clear();
            saveData.UnlockedCharacters.Clear();

            // 从配置表读取所有角色
            var characterConfigs = ConfigLoader.Tables.TbCharaterInfo.DataList;
            
            foreach (var config in characterConfigs)
            {
                bool shouldUnlock = unlockFirst && saveData.Characters.Count == 0;
                var characterState = CharacterRuntimeState.CreateFromConfig(config, shouldUnlock);
                saveData.Characters.Add(characterState);

                if (shouldUnlock)
                {
                    saveData.UnlockedCharacters.Add(config.Character);
                    Debug.Log($"[CharacterSystem] 初始化并解锁角色: {config.Name}");
                }
                else
                {
                    Debug.Log($"[CharacterSystem] 初始化角色: {config.Name}");
                }
            }
        }

        /// <summary>
        /// 解锁角色
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <returns>是否解锁成功</returns>
        public static bool UnlockCharacter(CharacterEnum characterId)
        {
            var saveData = GetSaveData();
            if (saveData == null) return false;
            
            // 检查角色是否已解锁
            if (saveData.UnlockedCharacters != null && saveData.UnlockedCharacters.Contains(characterId))
            {
                Debug.LogWarning($"[CharacterSystem] 角色 {characterId} 已经解锁");
                return false;
            }

            // 查找角色状态
            var characterState = GetCharacterState(characterId);
            if (characterState == null)
            {
                Debug.LogError($"[CharacterSystem] 未找到角色 {characterId}");
                return false;
            }

            // 确保列表已初始化
            if (saveData.UnlockedCharacters == null)
            {
                saveData.UnlockedCharacters = new List<CharacterEnum>();
            }

            // 解锁角色
            characterState.IsUnlocked = true;
            saveData.UnlockedCharacters.Add(characterId);

            Debug.Log($"[CharacterSystem] 解锁角色成功: {characterId}");
            return true;
        }

        /// <summary>
        /// 获取角色状态
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <returns>角色运行时状态，如果不存在则返回null</returns>
        public static CharacterRuntimeState GetCharacterState(CharacterEnum characterId)
        {
            var saveData = GetSaveData();
            if (saveData == null) return null;

            // 检查角色列表
            if (saveData.Characters == null)
            {
                Debug.LogError("[CharacterSystem] Characters 列表为空");
                return null;
            }

            return saveData.Characters.FirstOrDefault(c => c.CharacterId == characterId);
        }

        /// <summary>
        /// 获取所有已解锁的角色
        /// </summary>
        /// <returns>已解锁角色列表</returns>
        public static List<CharacterRuntimeState> GetUnlockedCharacters()
        {
            var saveData = GetSaveData();
            if (saveData == null || saveData.Characters == null)
            {
                return new List<CharacterRuntimeState>();
            }

            return saveData.Characters.Where(c => c.IsUnlocked).ToList();
        }

        /// <summary>
        /// 更新角色血量
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <param name="newHp">新的血量值</param>
        /// <returns>是否更新成功</returns>
        public static bool UpdateCharacterHp(CharacterEnum characterId, int newHp)
        {
            var characterState = GetCharacterState(characterId);
            if (characterState == null)
            {
                Debug.LogError($"[CharacterSystem] 未找到角色 {characterId}");
                return false;
            }

            characterState.CurrentHp = newHp;
            if (characterState.CurrentHp < 0) characterState.CurrentHp = 0;

            return true;
        }

        /// <summary>
        /// 恢复角色血量到最大值
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <returns>是否恢复成功</returns>
        public static bool RestoreCharacterHp(CharacterEnum characterId)
        {
            var characterState = GetCharacterState(characterId);
            if (characterState == null)
            {
                Debug.LogError($"[CharacterSystem] 未找到角色 {characterId}");
                return false;
            }

            // 从配置表读取最大血量
            var config = ConfigLoader.Tables.TbCharaterInfo.GetOrDefault(characterId);
            if (config == null)
            {
                Debug.LogError($"[CharacterSystem] 未找到角色配置 {characterId}");
                return false;
            }

            characterState.RestoreHp(config.BaseHp);
            Debug.Log($"[CharacterSystem] 角色 {characterId} 血量已恢复到 {config.BaseHp}");
            return true;
        }

        /// <summary>
        /// 增加角色经验值
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <param name="exp">经验值</param>
        /// <returns>是否添加成功</returns>
        public static bool AddExperience(CharacterEnum characterId, int exp)
        {
            var characterState = GetCharacterState(characterId);
            if (characterState == null)
            {
                Debug.LogError($"[CharacterSystem] 未找到角色 {characterId}");
                return false;
            }

            characterState.Experience += exp;
            Debug.Log($"[CharacterSystem] 角色 {characterId} 获得经验 {exp}，当前经验: {characterState.Experience}");

            // TODO: 实现升级逻辑
            return true;
        }

        /// <summary>
        /// 添加卡牌到角色卡组
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <param name="cardId">卡牌ID</param>
        /// <returns>是否添加成功</returns>
        public static bool AddCardToDeck(CharacterEnum characterId, string cardId)
        {
            var characterState = GetCharacterState(characterId);
            if (characterState == null)
            {
                Debug.LogError($"[CharacterSystem] 未找到角色 {characterId}");
                return false;
            }

            // 验证卡牌是否存在
            var cardConfig = ConfigLoader.Tables.TbCardInfo.GetOrDefault(cardId);
            if (cardConfig == null)
            {
                Debug.LogError($"[CharacterSystem] 未找到卡牌配置 {cardId}");
                return false;
            }

            // 创建卡牌运行时状态并添加到卡组
            var cardState = CardRuntimeState.CreateDefault(cardId);
            characterState.AddCardToDeck(cardState);

            Debug.Log($"[CharacterSystem] 为角色 {characterId} 添加卡牌 {cardConfig.Name}");
            return true;
        }

        /// <summary>
        /// 从角色卡组移除卡牌
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <param name="cardId">卡牌ID</param>
        /// <returns>是否移除成功</returns>
        public static bool RemoveCardFromDeck(CharacterEnum characterId, string cardId)
        {
            var characterState = GetCharacterState(characterId);
            if (characterState == null)
            {
                Debug.LogError($"[CharacterSystem] 未找到角色 {characterId}");
                return false;
            }

            bool removed = characterState.RemoveCardFromDeck(cardId);
            if (removed)
            {
                Debug.Log($"[CharacterSystem] 从角色 {characterId} 卡组中移除卡牌 {cardId}");
            }
            else
            {
                Debug.LogWarning($"[CharacterSystem] 角色 {characterId} 卡组中未找到卡牌 {cardId}");
            }

            return removed;
        }

        /// <summary>
        /// 添加角色到激活队伍
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <param name="slotIndex">队伍槽位索引（可选，-1表示添加到末尾）</param>
        /// <returns>是否添加成功</returns>
        public static bool AddToActiveTeam(CharacterEnum characterId, int slotIndex = -1)
        {
            var saveData = GetSaveData();
            if (saveData == null) return false;

            // 确保列表已初始化
            if (saveData.UnlockedCharacters == null)
            {
                saveData.UnlockedCharacters = new List<CharacterEnum>();
            }
            if (saveData.ActiveTeam == null)
            {
                saveData.ActiveTeam = new List<CharacterEnum>();
            }

            // 检查角色是否已解锁
            if (!saveData.UnlockedCharacters.Contains(characterId))
            {
                Debug.LogError($"[CharacterSystem] 角色 {characterId} 尚未解锁");
                return false;
            }

            // 检查角色是否已在队伍中
            if (saveData.ActiveTeam.Contains(characterId))
            {
                Debug.LogWarning($"[CharacterSystem] 角色 {characterId} 已在队伍中");
                return false;
            }

            if (slotIndex < 0)
            {
                // 添加到末尾
                saveData.ActiveTeam.Add(characterId);
            }
            else
            {
                // 插入到指定位置
                if (slotIndex > saveData.ActiveTeam.Count)
                {
                    slotIndex = saveData.ActiveTeam.Count;
                }
                saveData.ActiveTeam.Insert(slotIndex, characterId);
            }

            Debug.Log($"[CharacterSystem] 角色 {characterId} 加入队伍，槽位: {slotIndex}");
            return true;
        }

        /// <summary>
        /// 从激活队伍移除角色
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <returns>是否移除成功</returns>
        public static bool RemoveFromActiveTeam(CharacterEnum characterId)
        {
            var saveData = GetSaveData();
            if (saveData == null) return false;

            if (saveData.ActiveTeam == null)
            {
                saveData.ActiveTeam = new List<CharacterEnum>();
                return false;
            }

            bool removed = saveData.ActiveTeam.Remove(characterId);
            if (removed)
            {
                Debug.Log($"[CharacterSystem] 角色 {characterId} 离开队伍");
            }
            else
            {
                Debug.LogWarning($"[CharacterSystem] 角色 {characterId} 不在队伍中");
            }

            return removed;
        }

        /// <summary>
        /// 获取激活队伍
        /// </summary>
        /// <returns>激活队伍中的角色状态列表</returns>
        public static List<CharacterRuntimeState> GetActiveTeam()
        {
            var saveData = GetSaveData();
            if (saveData == null || saveData.ActiveTeam == null)
            {
                return new List<CharacterRuntimeState>();
            }

            var activeTeam = new List<CharacterRuntimeState>();

            foreach (var characterId in saveData.ActiveTeam)
            {
                var characterState = GetCharacterState(characterId);
                if (characterState != null)
                {
                    activeTeam.Add(characterState);
                }
            }

            return activeTeam;
        }
    }
}

