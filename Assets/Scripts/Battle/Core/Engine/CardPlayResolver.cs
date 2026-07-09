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

            var modifier = state.CardModifiers?.Get(card.Id);
            var commands = ConvertEffectsToCommands(card.Effects, modifier, card.TargetType, card.TargetZone);
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
            return GenerateCommands(card, null);
        }

        /// <summary>
        /// 仅生成命令列表但不执行（用于预测），并叠加卡牌修正
        /// </summary>
        public List<ICommand> GenerateCommands(CardInfo card, CardModifier modifier)
        {
            if (card?.Effects == null)
            {
                return new List<ICommand>();
            }

            return ConvertEffectsToCommands(card.Effects, modifier, card.TargetType, card.TargetZone);
        }

        /// <summary>
        /// 将 Effect 列表转换为 Command 列表
        /// 复用原 CardToTimelineConverter 的映射逻辑
        /// </summary>
        /// <param name="modifier">来自升级系统的卡牌修正（可空）</param>
        private List<ICommand> ConvertEffectsToCommands(List<Effect> effects, CardModifier modifier = null,
            TargetTypeEnum targetType = TargetTypeEnum.SingleEnemy, TargetZoneEnum targetZone = TargetZoneEnum.Any)
        {
            var commands = new List<ICommand>();
            if (effects == null || effects.Count == 0)
            {
                return commands;
            }

            foreach (var effect in effects)
            {
                var command = ConvertEffectToCommand(effect, modifier, targetType, targetZone);
                if (command != null)
                {
                    commands.Add(command);
                }
            }

            return commands;
        }

        /// <summary>
        /// 将单个 Effect 转换为 Command（叠加卡牌修正）
        /// <paramref name="targetType"/> / <paramref name="targetZone"/> 来自卡牌本身，用于把「打哪个区/群体友军」等
        /// 卡级信息带给自展开型 Command（AttackEffect 的 AOE 分区、DefenseEffect 的 AllAlly 群体护甲）。
        /// </summary>
        private ICommand ConvertEffectToCommand(Effect effect, CardModifier modifier = null,
            TargetTypeEnum targetType = TargetTypeEnum.SingleEnemy, TargetZoneEnum targetZone = TargetZoneEnum.Any)
        {
            if (effect == null)
            {
                return null;
            }

            if (effect is AttackEffect attackEffect)
            {
                int damage = attackEffect.Damage + (modifier?.DamageDelta ?? 0);
                if (damage < 0) damage = 0;
                bool isAoe = modifier?.ForceAoe ?? attackEffect.IsAoe;
                // 把卡牌声明的目标分区带给 AOE 扩散（如寒霜新星只打前排）；单体攻击忽略分区
                return new DamageCommand(damage, isAoe) { TargetZone = targetZone };
            }

            if (effect is AttackConditionalEffect attackConditionalEffect)
            {
                int bonusDamage = attackConditionalEffect.BonusDamage + (modifier?.DamageDelta ?? 0);
                if (bonusDamage < 0) bonusDamage = 0;
                return new AttackConditionalCommand(bonusDamage, attackConditionalEffect.ConditionType);
            }

            if (effect is DefenseEffect defenseEffect)
            {
                int value = defenseEffect.Value + (modifier?.DefenseDelta ?? 0);
                if (value < 0) value = 0;
                // AllAlly 卡（如奥术护罩）= 群体护甲，按卡牌分区给一整排友军加甲；其余仍是单体（自己/指定友军）
                bool defenseAoe = targetType == TargetTypeEnum.AllAlly;
                return new DefenseCommand(value, defenseEffect.PerHit)
                {
                    IsAoe = defenseAoe,
                    TargetZone = defenseAoe ? targetZone : TargetZoneEnum.Any
                };
            }

            if (effect is DefenseConditionalEffect defenseConditionalEffect)
            {
                int value = defenseConditionalEffect.Value + (modifier?.DefenseDelta ?? 0);
                if (value < 0) value = 0;
                return new DefenseConditionalCommand(value, defenseConditionalEffect.ConditionType);
            }

            if (effect is HealEffect healEffect)
            {
                return new HealCommand(healEffect.Value);
            }

            if (effect is BuffEffect buffEffect)
            {
                return new BuffCommand(buffEffect.BuffId, buffEffect.Value);
            }

            if (effect is BuffConditionalEffect buffConditionalEffect)
            {
                return new BuffConditionalCommand(
                    buffConditionalEffect.BuffId,
                    buffConditionalEffect.Value,
                    buffConditionalEffect.ConditionType);
            }

            if (effect is DrawEffect drawEffect)
            {
                return new DrawCommand(drawEffect.Count);
            }

            // 【过载】使用后自身过载 V 格：下次行动重排额外 +V 回合
            if (effect is OverloadEffect overloadEffect)
            {
                return new OverloadCommand(0, overloadEffect.Value);
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

            if (effect is MovePositionEffect movePositionEffect)
            {
                return new MovePositionCommand(movePositionEffect.Mode);
            }

            if (effect is AddToHandEffect addToHandEffect)
            {
                return new AddToHandCommand(addToHandEffect.CardId, addToHandEffect.Count);
            }

            // 迅猛打击：目标处于[执行]中（公共回合口径 = 本回合将行动）则伤害乘倍率
            if (effect is AttackExtraEffect attackExtraEffect)
            {
                int extraDamage = attackExtraEffect.Damage + (modifier?.DamageDelta ?? 0);
                if (extraDamage < 0) extraDamage = 0;
                return new AttackExtraCommand(extraDamage, attackExtraEffect.Conditions, attackExtraEffect.Multiplier);
            }

            // 挺身而出：嘲讽 = 给自己贴 Taunt buff（敌人索敌优先攻击持有者，见 BattleManager 嘲讽选目标）
            if (effect is TauntEffect)
            {
                return new BuffCommand("Taunt", 1);
            }

            // 杀戮时刻：立即获得能量（费用已在出牌前扣除，加量可用于本回合继续出牌）
            if (effect is EnergyEffect energyEffect)
            {
                return new EnergyCommand(energyEffect.Value);
            }

            // 肾上腺素：清除目标未落账的过载计数
            if (effect is ClearOverloadEffect)
            {
                return new ClearOverloadCommand();
            }

            // 掩护射击：施法者自身移动（区别于 MovePositionEffect 移动的是卡牌目标）
            if (effect is MoveSelfEffect moveSelfEffect)
            {
                return new MoveSelfCommand(moveSelfEffect.Mode);
            }

            // 紧急避险：施法者所在排整排翻转
            if (effect is MoveRowEffect)
            {
                return new MoveRowCommand();
            }

            // 铁蒺藜：本回合每次移动随机对一名敌人造成伤害
            if (effect is OnMoveDamageEffect onMoveDamageEffect)
            {
                return new RegisterMoveTriggerCommand(MoveTriggerState.TypeDamage, onMoveDamageEffect.Damage);
            }

            // 隧穿效应：本回合每次移动加牌进手
            if (effect is OnMoveAddCardEffect onMoveAddCardEffect)
            {
                return new RegisterMoveTriggerCommand(MoveTriggerState.TypeAddCard, onMoveAddCardEffect.Count, onMoveAddCardEffect.CardId);
            }

            // 预知一击：打「当前公共回合将行动」的所有敌人
            if (effect is AttackCurrentRoundEffect attackCurrentRoundEffect)
            {
                int crDamage = attackCurrentRoundEffect.Damage + (modifier?.DamageDelta ?? 0);
                if (crDamage < 0) crDamage = 0;
                return new AttackCurrentRoundCommand(crDamage);
            }

            // 示现/眩晕飞镖：晕眩当前回合敌人（random_one=true 随机一名）
            if (effect is StunCurrentRoundEffect stunCurrentRoundEffect)
            {
                return new StunCurrentRoundCommand(stunCurrentRoundEffect.Duration, stunCurrentRoundEffect.RandomOne);
            }

            // 全知全闪：按当前回合敌人数给自己叠 buff
            if (effect is BuffPerCurrentRoundEnemyEffect buffPerEffect)
            {
                return new BuffPerCurrentRoundEnemyCommand(buffPerEffect.BuffId, buffPerEffect.Value);
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
