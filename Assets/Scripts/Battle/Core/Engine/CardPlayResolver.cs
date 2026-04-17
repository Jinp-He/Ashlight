using System.Collections.Generic;
using Ashlight.Battle.Core.Commands;
using Ashlight.Battle.Core.Data;
using cfg;
using cfg.Character;
using UnityEngine;

namespace Ashlight.Battle.Core.Engine
{
    /// <summary>
    /// 卡牌结算器
    /// ATB 系统中卡牌不再插入时间轴，而是直接转为 Command 在回合内结算
    /// 复用现有的 Effect -> Command 映射逻辑
    /// </summary>
    public class CardPlayResolver
    {
        /// <summary>
        /// 打出一张卡牌，直接结算其效果
        /// </summary>
        /// <param name="state">战场快照（会被修改）</param>
        /// <param name="card">卡牌配置</param>
        /// <param name="ownerId">施法者ID</param>
        /// <param name="targetId">目标ID</param>
        /// <returns>是否成功结算</returns>
        public bool PlayCard(BattleStateSnapshot state, CardInfo card, string ownerId, string targetId)
        {
            if (state == null || card == null)
            {
                Debug.LogError("[CardPlayResolver] state 或 card 为 null");
                return false;
            }

            var owner = state.GetUnitById(ownerId);
            if (owner == null || owner.IsDead)
            {
                Debug.LogWarning($"[CardPlayResolver] 施法者无效或已死亡: {ownerId}");
                return false;
            }

            var commands = ConvertEffectsToCommands(card.Effects);
            if (commands.Count == 0)
            {
                Debug.LogWarning($"[CardPlayResolver] 卡牌 {card.Name} 没有可执行的命令");
                return false;
            }

            Debug.Log($"[CardPlayResolver] {ownerId} 打出 {card.Name} -> {targetId} ({commands.Count} commands)");

            foreach (var command in commands)
            {
                if (command == null) continue;

                command.Execute(state, ownerId, targetId);

                if (state.IsBattleEnded)
                {
                    Debug.Log("[CardPlayResolver] 战斗结束，停止后续命令");
                    break;
                }
            }

            return true;
        }

        /// <summary>
        /// 仅生成命令列表但不执行（用于预测）
        /// </summary>
        public List<ICommand> GenerateCommands(CardInfo card)
        {
            if (card?.Effects == null)
            {
                return new List<ICommand>();
            }

            return ConvertEffectsToCommands(card.Effects);
        }

        /// <summary>
        /// 将 Effect 列表转换为 Command 列表
        /// 复用原 CardToTimelineConverter 的映射逻辑
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
        /// 将单个 Effect 转换为 Command
        /// </summary>
        private ICommand ConvertEffectToCommand(Effect effect)
        {
            if (effect == null)
            {
                return null;
            }

            if (effect is AttackEffect attackEffect)
            {
                return new DamageCommand(attackEffect.Damage, attackEffect.IsAoe);
            }

            if (effect is DefenseEffect defenseEffect)
            {
                return new DefenseCommand(defenseEffect.Value, defenseEffect.PerHit);
            }

            if (effect is HealEffect healEffect)
            {
                return new HealCommand(healEffect.Value);
            }

            if (effect is BuffEffect buffEffect)
            {
                return new BuffCommand(buffEffect.BuffId, buffEffect.Value);
            }

            // ATB 新增：PushCollisionEffect 映射到 ActionBarShiftCommand
            if (effect is PushCollisionEffect pushEffect)
            {
                return new ActionBarShiftCommand(pushEffect.ShiftValue);
            }

            // ATB 新增：TimeShiftAllEffect 映射到 ActionBarShiftCommand（AOE）
            if (effect is TimeShiftAllEffect timeShiftAllEffect)
            {
                return new ActionBarShiftCommand(timeShiftAllEffect.ShiftValue, isAoe: true);
            }

            // TODO: 待 Luban schema 新增 StunEffect 后启用
            // if (effect is StunEffect stunEffect)
            // {
            //     return new StunCommand(stunEffect.StunTicks);
            // }

            // TODO: 待 Luban schema 新增 InterruptEffect 后启用
            // if (effect is InterruptEffect interruptEffect)
            // {
            //     return new InterruptCommand();
            // }

            Debug.LogWarning($"[CardPlayResolver] 未处理的 Effect 类型: {effect.GetType().Name}");
            return null;
        }
    }
}
