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
        public bool PlayCard(
            BattleStateSnapshot state,
            CardInfo card,
            string ownerId,
            string targetId,
            CardResolutionContext context = null)
        {
            if (state == null || card == null)
            {
                Debug.LogError("[CardPlayResolver] state 或 card 为 null");
                return false;
            }

            // 解签的唯一主动效果是支付费用并消耗自身；没有需要转换成 Command 的表内 Effect。
            bool allowEmpty = card.Id == "Extra006";
            return PlayEffects(state, card, card.Effects, ownerId, targetId, 1, allowEmpty, context);
        }

        /// <summary>结算卡牌的一组阶段效果。蓄力开始/期间/完成共用此入口。</summary>
        public bool PlayEffects(
            BattleStateSnapshot state,
            CardInfo card,
            List<Effect> effects,
            string ownerId,
            string targetId,
            int chargeLevel = 1,
            bool allowEmpty = true,
            CardResolutionContext context = null)
        {
            if (state == null || card == null)
            {
                return false;
            }

            var owner = state.GetUnitById(ownerId);
            if (owner == null || owner.IsDead)
            {
                Debug.LogWarning($"[CardPlayResolver] 施法者无效或已死亡: {ownerId}");
                return false;
            }

            state.LastDamageHitCount = 0;
            int movesBefore = state.MovesThisTurn;
            context = ApplyMoraleDamageBonus(state, card, effects, owner, targetId, context);
            var modifier = state.CardModifiers?.Get(card.Id);
            var commands = ConvertEffectsToCommands(
                effects, modifier, card.TargetType, card.TargetZone, Mathf.Max(1, chargeLevel), context);
            AppendZhouzhouSpecialCommands(commands, card, effects, context);
            if (commands.Count == 0)
            {
                bool hasConfiguredEffects = effects != null && effects.Count > 0;
                if (!allowEmpty || hasConfiguredEffects)
                {
                    Debug.LogWarning($"[CardPlayResolver] 卡牌 {card.Name} 没有可执行的命令");
                }
                return allowEmpty && !hasConfiguredEffects;
            }

            foreach (var command in commands)
            {
                command?.Execute(state, ownerId, targetId);
                if (state.IsBattleEnded)
                {
                    break;
                }
            }
            if (state.MovesThisTurn > movesBefore
                && IsZhouzhouMoveMainCard(card)
                && context?.SuppressMoveHistory != true)
            {
                if (state.LastMoveMainCardByOwner == null)
                {
                    state.LastMoveMainCardByOwner = new Dictionary<string, string>();
                }
                state.LastMoveMainCardByOwner[ownerId] = card.Id;
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
        public List<ICommand> GenerateCommands(
            CardInfo card,
            CardModifier modifier,
            int chargeLevel = 1,
            CardResolutionContext context = null)
        {
            if (card?.Effects == null)
            {
                return new List<ICommand>();
            }

            var commands = ConvertEffectsToCommands(card.Effects, modifier, card.TargetType, card.TargetZone, chargeLevel, context);
            AppendZhouzhouSpecialCommands(commands, card, card.Effects, context);
            return commands;
        }

        /// <summary>
        /// 将 Effect 列表转换为 Command 列表
        /// 复用原 CardToTimelineConverter 的映射逻辑
        /// </summary>
        /// <param name="modifier">来自升级系统的卡牌修正（可空）</param>
        private List<ICommand> ConvertEffectsToCommands(List<Effect> effects, CardModifier modifier = null,
            TargetTypeEnum targetType = TargetTypeEnum.SingleEnemy, TargetZoneEnum targetZone = TargetZoneEnum.Any,
            int chargeLevel = 1, CardResolutionContext context = null)
        {
            var commands = new List<ICommand>();
            if (effects == null || effects.Count == 0)
            {
                return commands;
            }

            foreach (var effect in effects)
            {
                if (context?.NumericOnly == true && !IsNumericPayloadEffect(effect))
                {
                    continue;
                }

                var command = ConvertEffectToCommand(effect, modifier, targetType, targetZone, chargeLevel, context);
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
            TargetTypeEnum targetType = TargetTypeEnum.SingleEnemy, TargetZoneEnum targetZone = TargetZoneEnum.Any,
            int chargeLevel = 1, CardResolutionContext context = null)
        {
            if (effect == null)
            {
                return null;
            }

            if (effect is AttackEffect attackEffect)
            {
                int damage = attackEffect.Damage + (modifier?.DamageDelta ?? 0) + (context?.DamageBonus ?? 0);
                if (damage < 0) damage = 0;
                damage = ScaleNumeric(damage, context);
                bool isAoe = modifier?.ForceAoe ?? attackEffect.IsAoe;
                // 把卡牌声明的目标分区带给 AOE 扩散（如寒霜新星只打前排）；单体攻击忽略分区
                return new DamageCommand(damage, isAoe) { TargetZone = targetZone };
            }

            if (effect is AttackConditionalEffect attackConditionalEffect)
            {
                int bonusDamage = attackConditionalEffect.BonusDamage
                                  + (modifier?.DamageDelta ?? 0)
                                  + (context?.DamageBonus ?? 0);
                if (bonusDamage < 0) bonusDamage = 0;
                return new AttackConditionalCommand(bonusDamage, attackConditionalEffect.ConditionType);
            }

            if (effect is DefenseEffect defenseEffect)
            {
                int value = defenseEffect.Value + (modifier?.DefenseDelta ?? 0);
                if (value < 0) value = 0;
                value = ScaleNumeric(value, context);
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
                return new HealCommand(ScaleNumeric(healEffect.Value, context));
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
                return new ActionBarShiftCommand(pushEffect.ShiftValue, collisionResult: pushEffect.CollisionResult);
            }

            // ATB 新增：TimeShiftAllEffect 映射到 ActionBarShiftCommand（AOE）
            if (effect is TimeShiftAllEffect timeShiftAllEffect)
            {
                return new ActionBarShiftCommand(timeShiftAllEffect.ShiftValue, isAoe: true)
                {
                    TargetZone = targetZone
                };
            }

            if (effect is MovePositionEffect movePositionEffect)
            {
                return new MovePositionCommand(movePositionEffect.Mode);
            }

            if (effect is AddToHandEffect addToHandEffect)
            {
                return new AddToHandCommand(addToHandEffect.CardId, addToHandEffect.Count);
            }

            if (effect is AddRandomToHandEffect addRandomToHandEffect)
            {
                return new AddRandomToHandCommand(
                    addRandomToHandEffect.CardIds,
                    addRandomToHandEffect.Count,
                    addRandomToHandEffect.ConditionType,
                    addRandomToHandEffect.ConditionMultiplier);
            }

            // 迅猛打击：目标处于[执行]中（公共回合口径 = 本回合将行动）则伤害乘倍率
            if (effect is AttackExtraEffect attackExtraEffect)
            {
                int extraDamage = attackExtraEffect.Damage
                                  + (modifier?.DamageDelta ?? 0)
                                  + (context?.DamageBonus ?? 0);
                if (extraDamage < 0) extraDamage = 0;
                bool isAoe = targetType == TargetTypeEnum.AllEnemy || targetType == TargetTypeEnum.AllAlly;
                return new AttackExtraCommand(
                    extraDamage,
                    attackExtraEffect.Conditions,
                    attackExtraEffect.Multiplier,
                    isAoe,
                    targetZone);
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

            if (effect is MoveOnArmorBreakEffect moveOnArmorBreakEffect)
            {
                return new MoveOnArmorBreakCommand(moveOnArmorBreakEffect.Mode);
            }

            if (effect is ChargedAttackEffect chargedAttackEffect)
            {
                int perCharge = chargedAttackEffect.DamagePerCharge + (modifier?.DamageDelta ?? 0) + (context?.DamageBonus ?? 0);
                int damage = Mathf.Max(0, perCharge) * Mathf.Max(1, chargeLevel);
                damage = ScaleNumeric(damage, context);
                bool isAoe = modifier?.ForceAoe ?? chargedAttackEffect.IsAoe;
                return new DamageCommand(damage, isAoe) { TargetZone = targetZone };
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

            // 对「当前公共回合将行动」的所有敌人造成伤害
            if (effect is AttackCurrentRoundEffect attackCurrentRoundEffect)
            {
                int crDamage = attackCurrentRoundEffect.Damage
                               + (modifier?.DamageDelta ?? 0)
                               + (context?.DamageBonus ?? 0);
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

            if (effect is CastShiftEffect castShiftEffect)
            {
                return new CastShiftCommand(castShiftEffect.ShiftValue);
            }

            if (effect is CastDamageBonusEffect castDamageBonusEffect)
            {
                return new CastDamageBonusCommand(castDamageBonusEffect.DamageBonus);
            }

            if (effect is CastResolveDrawEffect castResolveDrawEffect)
            {
                return new CastResolveDrawCommand(castResolveDrawEffect.Count);
            }

            if (effect is CastResolveBuffEffect castResolveBuffEffect)
            {
                return new CastResolveBuffCommand(castResolveBuffEffect.BuffId, castResolveBuffEffect.Value);
            }

            if (effect is CastImmediateEffect)
            {
                return new CastImmediateCommand();
            }

            if (effect is CastEchoEffect castEchoEffect)
            {
                return new CastEchoCommand(castEchoEffect.Delay, castEchoEffect.Multiplier);
            }

            if (effect is CastShiftAllEffect castShiftAllEffect)
            {
                return new CastShiftAllCommand(castShiftAllEffect.ShiftValue);
            }

            if (effect is WeatherConditionalAttackEffect weatherAttackEffect)
            {
                int damage = weatherAttackEffect.Damage + (modifier?.DamageDelta ?? 0) + (context?.DamageBonus ?? 0);
                damage = ScaleNumeric(Mathf.Max(0, damage), context);
                bool isAoe = targetType == TargetTypeEnum.AllEnemy || targetType == TargetTypeEnum.AllAlly;
                return new WeatherConditionalDamageCommand(damage, weatherAttackEffect.Multiplier, isAoe)
                {
                    TargetZone = targetZone
                };
            }

            if (effect is DelayScaledAttackEffect delayAttackEffect)
            {
                int delayBonus = Mathf.Min(
                    Mathf.Max(0, delayAttackEffect.MaxBonus),
                    Mathf.Max(0, context?.AddedDelay ?? 0) * Mathf.Max(0, delayAttackEffect.DamagePerDelay));
                int damage = delayAttackEffect.BaseDamage + delayBonus
                             + (modifier?.DamageDelta ?? 0) + (context?.DamageBonus ?? 0);
                damage = ScaleNumeric(Mathf.Max(0, damage), context);
                bool isAoe = modifier?.ForceAoe ?? delayAttackEffect.IsAoe;
                return new DamageCommand(damage, isAoe) { TargetZone = targetZone };
            }

            if (effect is WeatherSyncEnergyEffect weatherSyncEnergyEffect)
            {
                return new WeatherSyncEnergyCommand(weatherSyncEnergyEffect.Value);
            }

            if (effect is AlignToWeatherEffect alignToWeatherEffect)
            {
                return new AlignToWeatherCommand(alignToWeatherEffect.MaxShift);
            }

            if (effect is WeatherGuardEffect weatherGuardEffect)
            {
                return new WeatherGuardCommand(weatherGuardEffect.Value);
            }

            if (effect is WeatherShiftEffect weatherShiftEffect)
            {
                return new WeatherShiftCommand(weatherShiftEffect.ShiftValue);
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

        private static int ScaleNumeric(int value, CardResolutionContext context)
        {
            if (context == null || Mathf.Approximately(context.NumericScale, 1f))
            {
                return value;
            }
            return Mathf.Max(1, Mathf.RoundToInt(value * Mathf.Max(0f, context.NumericScale)));
        }

        /// <summary>
        /// 周周 v3 中少数无法由单一通用 Effect 表达的组合牌。
        /// 保留普通效果在 CardInfo 内，组合目标、触发上限与运行时副本在这里拼接为明确 Command，
        /// 避免把行为写进 UI 或让卡面描述与实际结算分离。
        /// </summary>
        private static void AppendZhouzhouSpecialCommands(
            List<ICommand> commands,
            CardInfo card,
            List<Effect> effects,
            CardResolutionContext context)
        {
            if (commands == null || card == null || card.BelongTo != CharacterEnum.Zhouzhou)
            {
                return;
            }

            switch (card.Id)
            {
                case "Zhouzhou010": // 稳住阵脚：群甲后只给施法者士气
                    commands.Add(new DefenseCommand(3) { IsAoe = true, TargetZone = TargetZoneEnum.Any });
                    commands.Add(new GrantOwnerBuffCommand("Morale", 1));
                    break;
                case "Zhouzhou012": // 回锋：伤害已在表内，消费士气后再移动
                    commands.Add(new GrantDefenseIfMoraleSpentCommand(context?.MoraleConsumed == true, 4));
                    break;
                case "Zhouzhou013":
                    commands.Add(new RegisterMoveTriggerCommand(
                        MoveTriggerState.TypeDraw, 1, null, 1, MoveTriggerState.ScopeOwner));
                    break;
                case "Zhouzhou015":
                    commands.Add(new MoveSelfAndAllyDefenseCommand(4));
                    break;
                case "Zhouzhou016":
                    commands.Add(new RegisterMoveTriggerCommand(
                        MoveTriggerState.TypeAddCard, 1, "Extra001", 3, MoveTriggerState.ScopeFriendly));
                    break;
                case "Zhouzhou017":
                    commands.Add(new RegisterMoveTriggerCommand(
                        MoveTriggerState.TypeDamage, 4, null, 3, MoveTriggerState.ScopeAny));
                    break;
                case "Zhouzhou018":
                    commands.Add(new RegisterMoveTriggerCommand(
                        MoveTriggerState.TypeDraw, 1, null, 2, MoveTriggerState.ScopeOwner));
                    commands.Add(new AddToHandCommand("Extra001", 1));
                    break;
                case "Zhouzhou019":
                    commands.Add(new GrantTeamArmorAndConsumeMoraleCommand(5, 2));
                    break;
                case "Zhouzhou020":
                    commands.Add(new RegisterMoveTriggerCommand(
                        MoveTriggerState.TypeAddCard, 1, "Extra001", 1, MoveTriggerState.ScopeOwner));
                    break;
                case "Zhouzhou021":
                    commands.Add(new MakeMoveCardsFreeAndAddStepCommand(0));
                    commands.Add(new RegisterMoveTriggerCommand(
                        MoveTriggerState.TypeAddCard, 1, "Extra001", 2, MoveTriggerState.ScopeOwner));
                    break;
                case "Zhouzhou022":
                    commands.Add(new RepeatAttackByOwnMoveCommand(8 + (context?.DamageBonus ?? 0), 2));
                    break;
                case "Zhouzhou023":
                    commands.Add(new MoveSelectedAlliesAndGrantDodgeCommand(2, 1));
                    break;
            }
        }

        /// <summary>
        /// 士气：持有者使用会直接造成伤害的卡牌时，消耗全部士气；
        /// 每层使该牌的每一段基础伤害 +2。加成写入本次结算上下文，
        /// 因而同一张卡的多段 AttackEffect、群体攻击和条件攻击都会共享该加成，
        /// 但移动触发伤害、状态伤害不会进入这里。
        /// </summary>
        private static CardResolutionContext ApplyMoraleDamageBonus(
            BattleStateSnapshot state,
            CardInfo card,
            List<Effect> effects,
            UnitState owner,
            string targetId,
            CardResolutionContext context)
        {
            bool isSpecialDamageCard = card?.Id == "Zhouzhou022";
            if ((!isSpecialDamageCard && !HasDirectDamageEffect(effects))
                || !HasLegalDamageTarget(state, owner, targetId, card.TargetType, card.TargetZone, effects))
            {
                return context;
            }

            var morale = owner.GetBuff("Morale");
            int layers = morale == null ? 0 : Mathf.Max(0, Mathf.FloorToInt(morale.Value));
            if (layers <= 0)
            {
                return context;
            }

            owner.RemoveBuff("Morale");
            var result = context?.Clone() ?? new CardResolutionContext();
            result.DamageBonus += layers * 2;
            result.MoraleConsumed = true;
            Debug.Log($"[CardPlayResolver] {owner.UnitId} 消耗 {layers} 层士气：{card.Name} 的每段基础伤害 +{layers * 2}");
            return result;
        }

        private static bool HasDirectDamageEffect(List<Effect> effects)
        {
            if (effects == null)
            {
                return false;
            }

            foreach (var effect in effects)
            {
                if (effect is AttackEffect
                    || effect is AttackConditionalEffect
                    || effect is AttackExtraEffect
                    || effect is AttackCurrentRoundEffect
                    || effect is ChargedAttackEffect
                    || effect is WeatherConditionalAttackEffect
                    || effect is DelayScaledAttackEffect)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasLegalDamageTarget(
            BattleStateSnapshot state,
            UnitState owner,
            string targetId,
            TargetTypeEnum targetType,
            TargetZoneEnum targetZone,
            List<Effect> effects)
        {
            if (effects.Exists(effect => effect is AttackCurrentRoundEffect))
            {
                return state.GetCurrentRoundOpponents(owner).Count > 0;
            }

            bool isAoe = targetType == TargetTypeEnum.AllEnemy
                         || effects.Exists(effect => effect is AttackEffect attack && attack.IsAoe)
                         || effects.Exists(effect => effect is ChargedAttackEffect charged && charged.IsAoe)
                         || effects.Exists(effect => effect is DelayScaledAttackEffect delayed && delayed.IsAoe);
            if (isAoe)
            {
                var targets = owner.IsPlayerUnit ? state.GetAliveEnemyUnits() : state.GetAlivePlayerUnits();
                return ZoneTargeting.FilterByZone(state, targets, targetZone, strict: true).Count > 0;
            }

            var target = state.GetUnitById(targetId);
            return target != null && !target.IsDead;
        }

        private static bool IsNumericPayloadEffect(Effect effect)
        {
            return effect is AttackEffect
                   || effect is WeatherConditionalAttackEffect
                   || effect is DelayScaledAttackEffect
                   || effect is DefenseEffect
                   || effect is HealEffect;
        }

        private static bool IsZhouzhouMoveMainCard(CardInfo card)
        {
            if (card == null || card.BelongTo != CharacterEnum.Zhouzhou)
            {
                return false;
            }

            switch (card.Id)
            {
                case "Zhouzhou000":
                case "Zhouzhou002":
                case "Zhouzhou003":
                case "Zhouzhou004":
                case "Zhouzhou005":
                case "Zhouzhou009":
                case "Zhouzhou011":
                case "Zhouzhou012":
                case "Zhouzhou013":
                case "Zhouzhou014":
                case "Zhouzhou015":
                case "Zhouzhou018":
                case "Zhouzhou023":
                    return true;
                default:
                    return false;
            }
        }
    }
}
