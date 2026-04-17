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

            var commands = ConvertEffectsToCommands(skillInfo.Effects);
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
        private List<ICommand> ConvertEffectsToCommands(List<Effect> effects)
        {
            var commands = new List<ICommand>();

            if (effects == null || effects.Count == 0)
            {
                return commands;
            }

            foreach (var effect in effects)
            {
                var command = ConvertEffectToCommand(effect);
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
        private ICommand ConvertEffectToCommand(Effect effect)
        {
            if (effect == null)
            {
                return null;
            }

            // AttackEffect -> DamageCommand
            if (effect is AttackEffect attackEffect)
            {
                return new DamageCommand(attackEffect.Damage, attackEffect.IsAoe);
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

            // PushCollisionEffect -> TimeShiftCommand
            if (effect is PushCollisionEffect pushEffect)
            {
                return new TimeShiftCommand(pushEffect.ShiftValue, pushEffect.CollisionResult);
            }

            // BuffEffect -> BuffCommand
            if (effect is BuffEffect buffEffect)
            {
                return new BuffCommand(buffEffect.BuffId, buffEffect.Value);
            }

            // 其他Effect暂不处理
            Debug.LogWarning($"[EnemySkillToTimelineConverter] 未处理的Effect类型: {effect.GetType().Name}");
            return null;
        }
    }
}



