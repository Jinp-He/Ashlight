using System.Collections.Generic;
using Ashlight.Battle.Core.Commands;
using Ashlight.Battle.Core.Data;
using cfg;
using cfg.Character;
using UnityEngine;

namespace Ashlight.Battle.Core.Engine
{
    /// <summary>
    /// 卡牌到时间轴转换器
    /// 将CardInfo转换为TimelineBlock列表
    /// </summary>
    public class CardToTimelineConverter
    {
        /// <summary>
        /// 将卡牌转换为TimelineBlock列表
        /// </summary>
        /// <param name="card">卡牌配置</param>
        /// <param name="ownerId">执行者ID</param>
        /// <param name="targetId">目标ID</param>
        /// <returns>TimelineBlock列表（按时间顺序）</returns>
        public List<TimelineBlock> ConvertCard(CardInfo card, string ownerId, string targetId)
        {
            if (card == null)
            {
                Debug.LogError("[CardToTimelineConverter] 卡牌配置为null");
                return new List<TimelineBlock>();
            }

            var blocks = new List<TimelineBlock>();

            var commands = ConvertEffectsToCommands(card.Effects);
            blocks.Add(CreateBlock(PhaseEnum.Active, ownerId, targetId, card.Id, commands));
            blocks[0].IsLastBlock = true;

            Debug.Log($"[CardToTimelineConverter] 卡牌 {card.Name} 转换为 {blocks.Count} 个Blocks (CardType:{card.CardType})");

            return blocks;
        }

        /// <summary>
        /// 创建TimelineBlock
        /// </summary>
        private TimelineBlock CreateBlock(PhaseEnum phase, string ownerId, string targetId, string sourceCardId, List<ICommand> commands)
        {
            var block = new TimelineBlock
            {
                Phase = phase,
                OwnerId = ownerId,
                TargetId = targetId,
                SourceCardId = sourceCardId,
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

            if (effect is AttackConditionalEffect attackConditionalEffect)
            {
                return new AttackConditionalCommand(attackConditionalEffect.BonusDamage, attackConditionalEffect.ConditionType);
            }

            // DefenseEffect -> DefenseCommand
            if (effect is DefenseEffect defenseEffect)
            {
                return new DefenseCommand(defenseEffect.Value, defenseEffect.PerHit);
            }

            if (effect is DefenseConditionalEffect defenseConditionalEffect)
            {
                return new DefenseConditionalCommand(defenseConditionalEffect.Value, defenseConditionalEffect.ConditionType);
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
                return new BuffCommand(buffEffect.BuffId, buffEffect.Value);
            }

            if (effect is MovePositionEffect movePositionEffect)
            {
                return new MovePositionCommand(movePositionEffect.Mode);
            }

            if (effect is AddToHandEffect addToHandEffect)
            {
                return new AddToHandCommand(addToHandEffect.CardId, addToHandEffect.Count);
            }

            // 其他Effect暂不处理
            Debug.LogWarning($"[CardToTimelineConverter] 未处理的Effect类型: {effect.GetType().Name}");
            return null;
        }
    }
}

