using System.Collections.Generic;
using cfg;
using cfg.Character;
using Ashlight.Config;
using UnityEngine;
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

        // ========== ATB 系统新增字段 ==========

        /// <summary>
        /// 单位速度，决定行动条推进效率
        /// </summary>
        public int Speed { get; set; }

        /// <summary>
        /// 基础能量（每回合刷新值）
        /// </summary>
        public int BaseEnergy { get; set; }

        /// <summary>
        /// 基础抽牌数（每回合抽牌值）
        /// </summary>
        public int BaseDrawCount { get; set; }

        /// <summary>
        /// 当前回合剩余能量
        /// </summary>
        public int CurrentEnergy { get; set; }

        /// <summary>
        /// 行动条状态
        /// </summary>
        public ActionBarState ActionBar { get; set; }

        /// <summary>
        /// 过载状态
        /// </summary>
        public OverloadState Overload { get; set; }

        // ========== 敌人意图轴/执行轴字段 ==========

        /// <summary>
        /// 当前敌人行动阶段（仅敌人单位使用）
        /// </summary>
        public EnemyPhase CurrentPhase { get; set; }

        /// <summary>
        /// 意图轴总长度（格数，来自技能配置）
        /// </summary>
        public int IntentAxisLength { get; set; }

        /// <summary>
        /// 意图轴当前进度（0 到 IntentAxisLength）
        /// </summary>
        public int IntentAxisProgress { get; set; }

        /// <summary>
        /// 执行轴总长度（格数，默认1）
        /// </summary>
        public int ExecuteAxisLength { get; set; }

        /// <summary>
        /// 执行轴当前进度（0 到 ExecuteAxisLength）
        /// </summary>
        public int ExecuteAxisProgress { get; set; }

        /// <summary>
        /// 是否处于硬直状态（所有推进冻结）
        /// </summary>
        public bool IsStunned { get; set; }

        /// <summary>
        /// 硬直剩余tick数
        /// </summary>
        public int StunRemainingTicks { get; set; }

        /// <summary>
        /// 待执行技能ID（敌人意图轴中暂存）
        /// </summary>
        public string PendingSkillId { get; set; }

        /// <summary>
        /// 待执行技能的目标ID（敌人意图轴中暂存）
        /// </summary>
        public string PendingTargetId { get; set; }

        public UnitState()
        {
            Buffs = new List<BuffState>();
            Track = new TimelineTrack();
            ActionBar = new ActionBarState();
            Overload = new OverloadState();
            IsDead = false;
            Speed = 10;
            BaseEnergy = 3;
            BaseDrawCount = 5;
            CurrentEnergy = 0;
            // 敌人意图轴/执行轴默认值
            CurrentPhase = EnemyPhase.None;
            IntentAxisLength = 0;
            IntentAxisProgress = 0;
            ExecuteAxisLength = 1;
            ExecuteAxisProgress = 0;
            IsStunned = false;
            StunRemainingTicks = 0;
            PendingSkillId = null;
            PendingTargetId = null;
        }

        /// <summary>
        /// 受到伤害
        /// </summary>
        /// <param name="damage">伤害值</param>
        /// <returns>实际受到的伤害</returns>
        public int TakeDamage(int damage)
        {
            if (damage <= 0)
            {
                return 0;
            }

            // 1. Buff 修正：先放大（易伤），再衰减（减伤）；配置里 Value 是百分比，如 50 表示 50%
            float modified = damage;

            var vulnerable = GetBuff("Vulnerable");
            if (vulnerable != null)
            {
                modified *= 1f + vulnerable.Value / 100f;
            }

            var reduceDmg = GetBuff("ReduceDmg");
            if (reduceDmg != null)
            {
                modified *= Mathf.Max(0f, 1f - reduceDmg.Value / 100f);
            }

            int adjusted = Mathf.Max(0, Mathf.RoundToInt(modified));

            // 2. 护甲吸收
            int actualDamage = adjusted;
            if (Defense > 0)
            {
                if (Defense >= adjusted)
                {
                    Defense -= adjusted;
                    return 0; // 完全被护甲吸收
                }
                else
                {
                    actualDamage = adjusted - Defense;
                    Defense = 0;
                }
            }

            // 3. 扣血
            CurrentHp -= actualDamage;
            if (CurrentHp <= 0)
            {
                CurrentHp = 0;
                IsDead = true;
            }

            // 4. 打断系 buff 计数（Stagger=累计伤害, Block=受击次数）
            if (!IsDead)
            {
                ProcessStaggerOnHit(actualDamage);
                ProcessBlockOnHit(actualDamage);
            }

            return actualDamage;
        }

        /// <summary>
        /// Stagger（破韧）：每次受到伤害扣对应 Value，归 0 时打断行动
        /// </summary>
        private void ProcessStaggerOnHit(int actualDamage)
        {
            if (actualDamage <= 0) return;
            var stagger = GetBuff("Stagger");
            if (stagger == null) return;

            stagger.Value -= actualDamage;
            if (stagger.Value <= 0)
            {
                RemoveBuff("Stagger");
                TryInterruptByBuff("Stagger");
            }
        }

        /// <summary>
        /// Block（格挡）：每次成功受到伤害扣 1 次数，归 0 时打断行动
        /// </summary>
        private void ProcessBlockOnHit(int actualDamage)
        {
            if (actualDamage <= 0) return;
            var block = GetBuff("Block");
            if (block == null) return;

            block.Value -= 1f;
            if (block.Value <= 0)
            {
                RemoveBuff("Block");
                TryInterruptByBuff("Block");
            }
        }

        /// <summary>
        /// 由打断系 buff 触发的中断（仅对处于意图轴/执行轴的敌人生效）
        /// </summary>
        private void TryInterruptByBuff(string source)
        {
            if (CurrentPhase == EnemyPhase.None) return;

            Debug.Log($"[UnitState] {UnitId} 被 [{source}] 打断 (原阶段={CurrentPhase}, 技能={PendingSkillId})");
            CurrentPhase = EnemyPhase.None;
            IntentAxisLength = 0;
            IntentAxisProgress = 0;
            ExecuteAxisLength = 1;
            ExecuteAxisProgress = 0;
            PendingSkillId = null;
            PendingTargetId = null;
            IsStunned = false;
            StunRemainingTicks = 0;
            ActionBar?.Restart();
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
            // 仅对正向加护甲应用 Dex/Frail（负值是直接扣，比如卡片清空护甲等）
            if (amount > 0)
            {
                float modified = amount;
                var dex = GetBuff("Dexterity");
                if (dex != null)
                {
                    modified += dex.Value;
                }
                var frail = GetBuff("Frail");
                if (frail != null)
                {
                    modified *= Mathf.Max(0f, 1f - frail.Value / 100f);
                }
                amount = Mathf.Max(0, Mathf.RoundToInt(modified));
            }

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
                Speed = this.Speed,
                BaseEnergy = this.BaseEnergy,
                BaseDrawCount = this.BaseDrawCount,
                CurrentEnergy = this.CurrentEnergy,
                Buffs = new List<BuffState>(),
                Track = this.Track?.Clone(),
                ActionBar = this.ActionBar?.Clone() ?? new ActionBarState(),
                Overload = this.Overload?.Clone() ?? new OverloadState(),
                // 敌人意图轴/执行轴
                CurrentPhase = this.CurrentPhase,
                IntentAxisLength = this.IntentAxisLength,
                IntentAxisProgress = this.IntentAxisProgress,
                ExecuteAxisLength = this.ExecuteAxisLength,
                ExecuteAxisProgress = this.ExecuteAxisProgress,
                IsStunned = this.IsStunned,
                StunRemainingTicks = this.StunRemainingTicks,
                PendingSkillId = this.PendingSkillId,
                PendingTargetId = this.PendingTargetId
            };

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

