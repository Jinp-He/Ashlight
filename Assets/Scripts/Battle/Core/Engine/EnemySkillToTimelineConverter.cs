using System.Collections.Generic;
using Ashlight.Battle.Core.Commands;
using Ashlight.Battle.Core.Data;
using cfg;
using cfg.Enemy;
using UnityEngine;

namespace Ashlight.Battle.Core.Engine
{
    /// <summary>
    /// 敌人技能到时间轴转换器
    /// 将EnemySkillInfo转换为TimelineBlock列表
    /// </summary>
    public class EnemySkillToTimelineConverter
    {
        /// <summary>
        /// 施法者自施型 Buff（不随技能命中目标，而是在进入意图轴时挂给施法者本身）。
        /// 例如 Stagger（破韧）：大招引导期间给自己挂上，玩家打够伤害即可打断该次施法。
        /// </summary>
        public static readonly System.Collections.Generic.HashSet<string> SelfCastBuffIds =
            new System.Collections.Generic.HashSet<string> { "Stagger", "Block", "Channeling" };

        /// <summary>
        /// 将敌人技能转换为TimelineBlock列表
        /// </summary>
        /// <param name="skillInfo">敌人技能配置</param>
        /// <param name="ownerId">执行者ID</param>
        /// <param name="targetId">目标ID</param>
        /// <returns>TimelineBlock列表（按时间顺序）</returns>
        public List<TimelineBlock> ConvertEnemySkill(EnemySkillInfo skillInfo, string ownerId, string targetId)
        {
            if (skillInfo == null)
            {
                Debug.LogError("[EnemySkillToTimelineConverter] 敌人技能配置为null");
                return new List<TimelineBlock>();
            }

            var blocks = new List<TimelineBlock>();

            var commands = ConvertEffectsToCommands(skillInfo.Effects, skillInfo.TargetZone);
            blocks.Add(CreateBlock(PhaseEnum.Active, ownerId, targetId, skillInfo.Id, commands));
            blocks[0].IsLastBlock = true;

            Debug.Log($"[EnemySkillToTimelineConverter] 敌人技能 {skillInfo.Name} 转换为 {blocks.Count} 个Blocks (ExecutingCost:{skillInfo.ExecutingCost})");

            return blocks;
        }

        /// <summary>
        /// 创建TimelineBlock
        /// </summary>
        private TimelineBlock CreateBlock(PhaseEnum phase, string ownerId, string targetId, string sourceSkillId, List<ICommand> commands)
        {
            var block = new TimelineBlock
            {
                Phase = phase,
                OwnerId = ownerId,
                TargetId = targetId,
                SourceCardId = sourceSkillId, // 使用技能ID作为SourceCardId
                Commands = commands ?? new List<ICommand>(),
                Priority = CalculatePriority(commands)
            };

            return block;
        }

        /// <summary>
        /// 计算Block的优先级（取Commands中最高优先级）
        /// </summary>
        private int CalculatePriority(List<ICommand> commands)
        {
            if (commands == null || commands.Count == 0)
            {
                return 0;
            }

            int maxPriority = 0;
            foreach (var command in commands)
            {
                if (command != null)
                {
                    int priority = command.GetPriority();
                    if (priority > maxPriority)
                    {
                        maxPriority = priority;
                    }
                }
            }

            return maxPriority;
        }

        /// <summary>
        /// 将Effect列表转换为Command列表
        /// </summary>
        private List<ICommand> ConvertEffectsToCommands(List<Effect> effects, TargetZoneEnum targetZone)
        {
            var commands = new List<ICommand>();

            if (effects == null || effects.Count == 0)
            {
                return commands;
            }

            foreach (var effect in effects)
            {
                var command = ConvertEffectToCommand(effect, targetZone);
                if (command != null)
                {
                    commands.Add(command);
                }
            }

            return commands;
        }

        /// <summary>
        /// 将单个Effect转换为Command
        /// 复用CardToTimelineConverter的逻辑
        /// </summary>
        private ICommand ConvertEffectToCommand(Effect effect, TargetZoneEnum targetZone)
        {
            if (effect == null)
            {
                return null;
            }

            // AttackEffect -> DamageCommand（把技能的目标分区带给 AOE 扩散）
            if (effect is AttackEffect attackEffect)
            {
                return new DamageCommand(attackEffect.Damage, attackEffect.IsAoe) { TargetZone = targetZone };
            }

            // DefenseEffect -> DefenseCommand
            if (effect is DefenseEffect defenseEffect)
            {
                return new DefenseCommand(defenseEffect.Value, defenseEffect.PerHit);
            }

            // HealEffect -> HealCommand
            if (effect is HealEffect healEffect)
            {
                return new HealCommand(healEffect.Value);
            }

            // PushCollisionEffect -> ActionBarShiftCommand（单体推迟，ShiftValue 为正数表示延后格数，转为负值）
            if (effect is PushCollisionEffect pushEffect)
            {
                return new ActionBarShiftCommand(-pushEffect.ShiftValue, isAoe: false);
            }

            // TimeShiftAllEffect -> ActionBarShiftCommand（全体推迟）
            if (effect is TimeShiftAllEffect timeShiftAllEffect)
            {
                return new ActionBarShiftCommand(-timeShiftAllEffect.ShiftValue, isAoe: true);
            }

            // BuffEffect -> BuffCommand
            if (effect is BuffEffect buffEffect)
            {
                // 自施型 Buff（如 Stagger）不落在技能目标身上：改由 BattleManager 在进入意图轴时挂给施法者
                if (SelfCastBuffIds.Contains(buffEffect.BuffId))
                {
                    return null;
                }
                return new BuffCommand(buffEffect.BuffId, buffEffect.Value);
            }

            // 其他Effect暂不处理
            Debug.LogWarning($"[EnemySkillToTimelineConverter] 未处理的Effect类型: {effect.GetType().Name}");
            return null;
        }
    }
}



