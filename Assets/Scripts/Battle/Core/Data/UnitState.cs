using System.Collections.Generic;
using cfg;
using cfg.Character;
using Ashlight.Config;
namespace Ashlight.Battle.Core.Data
{
    /// <summary>
    /// 单个战斗单位的状态（NvN架构）
    /// POCO类，不继承MonoBehaviour
    /// </summary>
    public class UnitState
    {
        /// <summary>
        /// 单位唯一ID
        /// </summary>
        public string UnitId { get; set; }

        /// <summary>
        /// 当前血量
        /// </summary>
        public int CurrentHp { get; set; }

        /// <summary>
        /// 最大血量
        /// </summary>
        public int MaxHp { get; set; }

        /// <summary>
        /// 当前护甲值（减伤）
        /// </summary>
        public int Defense { get; set; }

        /// <summary>
        /// Buff列表
        /// </summary>
        public List<BuffState> Buffs { get; set; }

        /// <summary>
        /// 该单位的独立时间轴（15格）
        /// </summary>
        public TimelineTrack Track { get; set; }

        /// <summary>
        /// 是否死亡
        /// </summary>
        public bool IsDead { get; set; }

        /// <summary>
        /// 是否为玩家方单位
        /// </summary>
        public bool IsPlayerUnit { get; set; }

        /// <summary>
        /// 角色/敌人配置ID（用于获取基础属性）
        /// </summary>
        public string ConfigId { get; set; }

        public UnitState()
        {
            Buffs = new List<BuffState>();
            Track = new TimelineTrack();
            IsDead = false;
        }

        /// <summary>
        /// 受到伤害
        /// </summary>
        /// <param name="damage">伤害值</param>
        /// <returns>实际受到的伤害</returns>
        public int TakeDamage(int damage)
        {
            // 先消耗护甲
            int actualDamage = damage;
            if (Defense > 0)
            {
                if (Defense >= damage)
                {
                    Defense -= damage;
                    return 0; // 完全被护甲吸收
                }
                else
                {
                    actualDamage = damage - Defense;
                    Defense = 0;
                }
            }

            // 扣除血量
            CurrentHp -= actualDamage;
            if (CurrentHp <= 0)
            {
                CurrentHp = 0;
                IsDead = true;
            }

            return actualDamage;
        }

        /// <summary>
        /// 治疗
        /// </summary>
        /// <param name="healAmount">治疗量</param>
        /// <returns>实际治疗量</returns>
        public int Heal(int healAmount)
        {
            if (IsDead)
            {
                return 0; // 死亡单位无法治疗
            }

            int actualHeal = healAmount;
            CurrentHp += healAmount;

            if (CurrentHp > MaxHp)
            {
                actualHeal = healAmount - (CurrentHp - MaxHp);
                CurrentHp = MaxHp;
            }

            return actualHeal;
        }

        /// <summary>
        /// 增加护甲
        /// </summary>
        public void AddDefense(int amount)
        {
            Defense += amount;
            if (Defense < 0)
            {
                Defense = 0;
            }
        }

        /// <summary>
        /// 添加Buff
        /// </summary>
        public void AddBuff(BuffState buff)
        {
            if (buff == null) return;

            // 简化版本：直接添加，不处理叠加逻辑
            Buffs.Add(buff);
        }

        /// <summary>
        /// 移除Buff
        /// </summary>
        public void RemoveBuff(string buffId)
        {
            Buffs.RemoveAll(b => b.BuffId == buffId);
        }

        /// <summary>
        /// 获取指定Buff
        /// </summary>
        public BuffState GetBuff(string buffId)
        {
            return Buffs.Find(b => b.BuffId == buffId);
        }

        /// <summary>
        /// 检查是否拥有指定Buff
        /// </summary>
        public bool HasBuff(string buffId)
        {
            return Buffs.Exists(b => b.BuffId == buffId);
        }

        /// <summary>
        /// 更新Buff状态（每回合末调用）
        /// 移除过期的Buff
        /// </summary>
        public void UpdateBuffs()
        {
            Buffs.RemoveAll(buff => buff.DecreaseDuration());
        }

        /// <summary>
        /// 获取角色ID（仅对玩家单位有效）
        /// </summary>
        /// <returns>角色枚举，如果不是玩家单位或解析失败则返回null</returns>
        public CharacterEnum? GetCharacterId()
        {
            if (!IsPlayerUnit || string.IsNullOrEmpty(ConfigId))
            {
                return null;
            }

            if (System.Enum.TryParse<CharacterEnum>(ConfigId, out var characterId))
            {
                return characterId;
            }

            return null;
        }

        /// <summary>
        /// 获取角色配置信息（仅对玩家单位有效）
        /// </summary>
        /// <returns>角色配置信息，如果不是玩家单位或未找到则返回null</returns>
        public CharaterInfo GetCharacterInfo()
        {
            var characterId = GetCharacterId();
            if (characterId == null)
            {
                return null;
            }

            return ConfigLoader.Tables?.TbCharaterInfo?.GetOrDefault(characterId.Value);
        }

        /// <summary>
        /// 深拷贝
        /// </summary>
        public UnitState Clone()
        {
            var clone = new UnitState
            {
                UnitId = this.UnitId,
                CurrentHp = this.CurrentHp,
                MaxHp = this.MaxHp,
                Defense = this.Defense,
                IsDead = this.IsDead,
                IsPlayerUnit = this.IsPlayerUnit,
                ConfigId = this.ConfigId,
                Buffs = new List<BuffState>(),
                Track = this.Track?.Clone()
            };

            // 深拷贝Buffs列表
            if (this.Buffs != null)
            {
                foreach (var buff in this.Buffs)
                {
                    clone.Buffs.Add(buff.Clone());
                }
            }

            return clone;
        }
    }
}

