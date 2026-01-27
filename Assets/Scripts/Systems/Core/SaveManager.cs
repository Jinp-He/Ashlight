using System;
using UnityEngine;
using Ashlight.State.Save;

namespace Ashlight.Systems.Core
{
    /// <summary>
    /// 存档管理器 - 负责存档的保存和加载
    /// 系统层：处理持久化逻辑
    /// </summary>
    public static class SaveManager
    {
        private const string SAVE_KEY = "GameSave";

        /// <summary>
        /// 保存游戏
        /// </summary>
        public static void Save(SaveData data)
        {
            if (data == null)
            {
                Debug.LogError("[SaveManager] 存档数据为空");
                return;
            }

            data.SaveTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string json = JsonUtility.ToJson(data);
            PlayerPrefs.SetString(SAVE_KEY, json);
            PlayerPrefs.Save();

            Debug.Log("[SaveManager] 游戏已保存");
        }

        /// <summary>
        /// 加载存档
        /// </summary>
        public static SaveData Load()
        {
            if (!PlayerPrefs.HasKey(SAVE_KEY))
            {
                Debug.Log("[SaveManager] 未找到存档");
                return null;
            }

            string json = PlayerPrefs.GetString(SAVE_KEY);
            SaveData data = JsonUtility.FromJson<SaveData>(json);

            // 数据迁移和验证
            data = MigrateAndValidate(data);

            Debug.Log("[SaveManager] 存档加载成功");
            return data;
        }

        /// <summary>
        /// 迁移和验证存档数据
        /// 处理版本升级和数据完整性检查
        /// </summary>
        private static SaveData MigrateAndValidate(SaveData data)
        {
            if (data == null)
            {
                Debug.LogError("[SaveManager] 存档数据为空");
                return null;
            }

            // 版本迁移逻辑
            if (data.Version < 1)
            {
                Debug.Log("[SaveManager] 迁移存档从版本 " + data.Version + " 到版本 1");
                // 如果有旧版本，这里处理迁移逻辑
                data.Version = 1;
            }

            // 数据完整性验证和修复
            ValidateAndRepairData(data);

            return data;
        }

        /// <summary>
        /// 验证和修复数据完整性
        /// 确保所有必需的字段都已初始化
        /// </summary>
        private static void ValidateAndRepairData(SaveData data)
        {
            // 验证玩家状态
            if (data.PlayerState == null)
            {
                Debug.LogWarning("[SaveManager] 玩家状态为空，创建默认状态");
                data.PlayerState = Ashlight.State.Runtime.PlayerState.CreateDefault();
            }

            // 验证角色列表（新增字段，旧存档可能为null）
            if (data.Characters == null)
            {
                Debug.LogWarning("[SaveManager] 角色列表为空，初始化为空列表");
                data.Characters = new System.Collections.Generic.List<Ashlight.State.Runtime.CharacterRuntimeState>();
            }

            if (data.UnlockedCharacters == null)
            {
                Debug.LogWarning("[SaveManager] 已解锁角色列表为空，初始化为空列表");
                data.UnlockedCharacters = new System.Collections.Generic.List<cfg.CharacterEnum>();
            }

            if (data.ActiveTeam == null)
            {
                Debug.LogWarning("[SaveManager] 激活队伍列表为空，初始化为空列表");
                data.ActiveTeam = new System.Collections.Generic.List<cfg.CharacterEnum>();
            }

            // 验证角色数据的完整性
            foreach (var character in data.Characters)
            {
                if (character.CurrentDeck == null)
                {
                    character.CurrentDeck = new System.Collections.Generic.List<Ashlight.State.Runtime.CardRuntimeState>();
                }
                if (character.EquipmentSlots == null)
                {
                    character.EquipmentSlots = new System.Collections.Generic.List<string>();
                }
            }
        }

        /// <summary>
        /// 加载存档，如果不存在则创建新存档
        /// </summary>
        public static SaveData LoadOrCreateNew()
        {
            SaveData data = Load();
            if (data == null)
            {
                data = SaveData.CreateNew();
                Debug.Log("[SaveManager] 创建新存档");
            }
            return data;
        }

        /// <summary>
        /// 删除存档
        /// </summary>
        public static void Delete()
        {
            PlayerPrefs.DeleteKey(SAVE_KEY);
            PlayerPrefs.Save();
            Debug.Log("[SaveManager] 存档已删除");
        }

        /// <summary>
        /// 检查是否存在存档
        /// </summary>
        public static bool HasSave()
        {
            return PlayerPrefs.HasKey(SAVE_KEY);
        }
    }
}

