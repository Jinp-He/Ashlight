using System;
using System.Collections.Generic;
using cfg;

namespace Ashlight.State.Runtime
{
    /// <summary>
    /// 角色运行时状态 - 存储角色的动态数据
    /// 状态层：支持 Save/Load，纯 C# 数据结构
    /// </summary>
    [Serializable]
    public class CharacterRuntimeState
    {
        /// <summary>
        /// 角色ID（枚举类型，引用配置表 CharaterInfo）
        /// </summary>
        public CharacterEnum CharacterId;

        /// <summary>
        /// 当前血量
        /// </summary>
        public int CurrentHp;

        /// <summary>
        /// 角色等级
        /// </summary>
        public int Level;

        /// <summary>
        /// 角色经验值
        /// </summary>
        public int Experience;

        /// <summary>
        /// 是否已解锁
        /// </summary>
        public bool IsUnlocked;

        /// <summary>
        /// 当前卡组（存储卡牌的运行时状态）
        /// </summary>
        public List<CardRuntimeState> CurrentDeck;

        /// <summary>
        /// 装备槽（预留，存储装备ID列表）
        /// </summary>
        public List<string> EquipmentSlots;

        /// <summary>
        /// 创建默认角色状态
        /// 注意：需要通过 ConfigLoader 获取配置表数据来初始化 BaseHp
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <param name="baseHp">基础血量（从配置表读取）</param>
        /// <param name="isUnlocked">是否解锁</param>
        /// <returns>默认初始状态的角色</returns>
        public static CharacterRuntimeState CreateDefault(CharacterEnum characterId, int baseHp, bool isUnlocked = false)
        {
            return new CharacterRuntimeState
            {
                CharacterId = characterId,
                CurrentHp = baseHp,
                Level = 1,
                Experience = 0,
                IsUnlocked = isUnlocked,
                CurrentDeck = new List<CardRuntimeState>(),
                EquipmentSlots = new List<string>()
            };
        }

        /// <summary>
        /// 从配置表创建角色状态
        /// </summary>
        /// <param name="config">角色配置信息</param>
        /// <param name="isUnlocked">是否解锁</param>
        /// <returns>基于配置创建的角色状态</returns>
        public static CharacterRuntimeState CreateFromConfig(cfg.Character.CharaterInfo config, bool isUnlocked = false)
        {
            var characterState = new CharacterRuntimeState
            {
                CharacterId = config.Character,
                CurrentHp = config.BaseHp,
                Level = 1,
                Experience = 0,
                IsUnlocked = isUnlocked,
                CurrentDeck = new List<CardRuntimeState>(),
                EquipmentSlots = new List<string>()
            };

            // 从BaseDeck初始化默认卡组
            if (config.BaseDeck != null && config.BaseDeck.Count > 0)
            {
                foreach (var cardId in config.BaseDeck)
                {
                    var cardState = CardRuntimeState.CreateDefault(cardId);
                    characterState.CurrentDeck.Add(cardState);
                }
            }

            return characterState;
        }

        /// <summary>
        /// 添加卡牌到卡组
        /// </summary>
        /// <param name="card">要添加的卡牌</param>
        public void AddCardToDeck(CardRuntimeState card)
        {
            if (CurrentDeck == null)
            {
                CurrentDeck = new List<CardRuntimeState>();
            }
            CurrentDeck.Add(card);
        }

        /// <summary>
        /// 从卡组移除卡牌
        /// </summary>
        /// <param name="cardId">要移除的卡牌ID</param>
        /// <returns>是否成功移除</returns>
        public bool RemoveCardFromDeck(string cardId)
        {
            if (CurrentDeck == null) return false;
            
            for (int i = 0; i < CurrentDeck.Count; i++)
            {
                if (CurrentDeck[i].CardId == cardId)
                {
                    CurrentDeck.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 恢复血量到最大值
        /// </summary>
        /// <param name="maxHp">最大血量（从配置表获取）</param>
        public void RestoreHp(int maxHp)
        {
            CurrentHp = maxHp;
        }

        /// <summary>
        /// 受到伤害
        /// </summary>
        /// <param name="damage">伤害值</param>
        /// <returns>角色是否死亡</returns>
        public bool TakeDamage(int damage)
        {
            CurrentHp -= damage;
            if (CurrentHp < 0) CurrentHp = 0;
            return CurrentHp <= 0;
        }

        /// <summary>
        /// 治疗
        /// </summary>
        /// <param name="healAmount">治疗量</param>
        /// <param name="maxHp">最大血量（从配置表获取）</param>
        public void Heal(int healAmount, int maxHp)
        {
            CurrentHp += healAmount;
            if (CurrentHp > maxHp) CurrentHp = maxHp;
        }
    }
}

