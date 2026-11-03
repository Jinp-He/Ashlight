using Ashlight.Battle.Core.Data;
using Ashlight.Common.Events;
using UnityEngine;

namespace Ashlight.Battle.Core.Engine
{
    /// <summary>
    /// 回合内移动触发器结算：任何单位完成一次移动（换排）后调用 <see cref="OnUnitMoved"/>，
    /// 逐个触发 <see cref="BattleStateSnapshot.MoveTriggers"/> 里登记的效果（铁蒺藜/隧穿效应）。
    /// 「随机」用确定性伪随机（按 FireCount 取模），保证预测快照与实战结果一致。
    /// </summary>
    public static class MoveTriggerProcessor
    {
        public const string MoveAllyGrantMoraleTrait = "MoveAllyGrantMorale";
        public const string MoraleBuffId = "Morale";

        public static void OnUnitMoved(BattleStateSnapshot state, UnitState mover, string sourceOwnerId = null)
        {
            if (state == null || state.IsBattleEnded)
            {
                return;
            }

            // 全局移动计数（本回合）：无论有没有触发器都累计，
            // 供隧穿/铁蒺藜卡面动态显示「本回合已移动 N 次」（回合结束清零）。
            state.MovesThisTurn++;
            if (mover != null)
            {
                if (state.MovesByUnitThisTurn == null)
                {
                    state.MovesByUnitThisTurn = new System.Collections.Generic.Dictionary<string, int>();
                }
                state.MovesByUnitThisTurn.TryGetValue(mover.UnitId, out int count);
                state.MovesByUnitThisTurn[mover.UnitId] = count + 1;
            }

            ApplyMovementTrait(state, mover, sourceOwnerId);

            if (state.MoveTriggers == null || state.MoveTriggers.Count == 0)
            {
                return;
            }

            // 触发中可能致死/结束战斗，遍历副本
            var triggers = new System.Collections.Generic.List<MoveTriggerState>(state.MoveTriggers);
            foreach (var trigger in triggers)
            {
                if (state.IsBattleEnded) return;

                var owner = state.GetUnitById(trigger.OwnerId);
                if (owner == null || owner.IsDead)
                {
                    continue;
                }

                if (trigger.MaxFireCount > 0 && trigger.FireCount >= trigger.MaxFireCount)
                {
                    continue;
                }

                if (!MatchesMoverScope(owner, mover, trigger.MoverScope))
                {
                    continue;
                }

                switch (trigger.TriggerType)
                {
                    case MoveTriggerState.TypeDamage:
                        FireDamage(state, owner, trigger);
                        break;
                    case MoveTriggerState.TypeAddCard:
                        FireAddCard(state, owner, trigger);
                        break;
                    case MoveTriggerState.TypeDraw:
                        FireDraw(state, owner, trigger);
                        break;
                    default:
                        Debug.LogWarning($"[MoveTrigger] 未知触发类型: {trigger.TriggerType}");
                        break;
                }
            }
        }

        private static bool MatchesMoverScope(UnitState owner, UnitState mover, string scope)
        {
            switch (scope)
            {
                case MoveTriggerState.ScopeOwner:
                    return owner.UnitId == mover?.UnitId;
                case MoveTriggerState.ScopeFriendly:
                    return mover != null && owner.IsPlayerUnit == mover.IsPlayerUnit;
                default:
                    return mover != null;
            }
        }

        /// <summary>
        /// 周周初始百相：每当周周通过卡牌成功移动一名友方角色时，使该角色获得 1 层士气。
        /// sourceOwnerId 是移动效果的施法者；只有同阵营目标会触发，移动敌人不会触发。
        /// 士气的叠层上限由 BuffInfo.Morale.MaxStack 统一控制。
        /// </summary>
        private static void ApplyMovementTrait(BattleStateSnapshot state, UnitState mover, string sourceOwnerId)
        {
            if (mover == null || string.IsNullOrEmpty(sourceOwnerId))
            {
                return;
            }

            var source = state.GetUnitById(sourceOwnerId);
            if (source == null || source.IsDead || source.IsPlayerUnit != mover.IsPlayerUnit)
            {
                return;
            }

            var characterInfo = source.GetCharacterInfo();
            if (characterInfo == null || characterInfo.Trait != MoveAllyGrantMoraleTrait)
            {
                return;
            }

            mover.AddBuff(new BuffState
            {
                BuffId = MoraleBuffId,
                Value = 1f,
                RemainingDuration = -1
            });

            Debug.Log($"[MoveTrigger] {source.UnitId} 的初始百相触发：移动友方 {mover.UnitId}，使其获得 1 层士气（当前 {mover.GetBuff(MoraleBuffId)?.Value ?? 0f} 层）");
        }

        /// <summary>铁蒺藜：随机（确定性）对一名敌对单位造成 Amount 伤害。</summary>
        private static void FireDamage(BattleStateSnapshot state, UnitState owner, MoveTriggerState trigger)
        {
            var pool = owner.IsPlayerUnit ? state.GetAliveEnemyUnits() : state.GetAlivePlayerUnits();
            if (pool.Count == 0)
            {
                return;
            }

            var target = pool[(state.TurnCount * 31 + trigger.FireCount) % pool.Count];
            trigger.FireCount++;

            int dealt = target.TakeDamage(trigger.Amount);
            ArmorBreakMoveProcessor.ResolvePending(state, target);
            Debug.Log($"[MoveTrigger] 移动触发伤害：{owner.UnitId} -> {target.UnitId} {dealt} 点 (第 {trigger.FireCount} 次)");

            GameEvent.Publish(new AttackExecutedEvent
            {
                AttackerId = owner.UnitId,
                TargetId = target.UnitId,
                ActualDamage = dealt,
                ArmorDamage = target.LastArmorDamage,
                IsAoe = false,
                IsPrediction = state.IsPrediction
            });

            state.CheckBattleEnd();
        }

        /// <summary>隧穿效应：把 CardId 加入登记者手牌 Amount 张。</summary>
        private static void FireAddCard(BattleStateSnapshot state, UnitState owner, MoveTriggerState trigger)
        {
            if (state.DeckSystem == null || string.IsNullOrEmpty(trigger.CardId))
            {
                return;
            }

            trigger.FireCount++;
            int added = state.DeckSystem.AddCardToHand(trigger.CardId, trigger.Amount, owner.GetCharacterId());
            Debug.Log($"[MoveTrigger] 移动触发加牌：{owner.UnitId} 获得 {added} 张 {trigger.CardId} (第 {trigger.FireCount} 次)");
        }

        private static void FireDraw(BattleStateSnapshot state, UnitState owner, MoveTriggerState trigger)
        {
            trigger.FireCount++;
            new Commands.DrawCommand(trigger.Amount).Execute(state, owner.UnitId, owner.UnitId);
            Debug.Log($"[MoveTrigger] 移动触发抽牌：{owner.UnitId} 抽 {trigger.Amount} 张 (第 {trigger.FireCount} 次)");
        }
    }
}
