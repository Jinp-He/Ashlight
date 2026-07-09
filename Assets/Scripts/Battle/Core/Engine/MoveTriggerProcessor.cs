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
        public static void OnUnitMoved(BattleStateSnapshot state, UnitState mover)
        {
            if (state == null || state.IsBattleEnded)
            {
                return;
            }

            // 全局移动计数（本回合）：无论有没有触发器都累计，
            // 供隧穿/铁蒺藜卡面动态显示「本回合已移动 N 次」（回合结束清零）。
            state.MovesThisTurn++;

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

                switch (trigger.TriggerType)
                {
                    case MoveTriggerState.TypeDamage:
                        FireDamage(state, owner, trigger);
                        break;
                    case MoveTriggerState.TypeAddCard:
                        FireAddCard(state, owner, trigger);
                        break;
                    default:
                        Debug.LogWarning($"[MoveTrigger] 未知触发类型: {trigger.TriggerType}");
                        break;
                }
            }
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
            Debug.Log($"[MoveTrigger] 移动触发伤害：{owner.UnitId} -> {target.UnitId} {dealt} 点 (第 {trigger.FireCount} 次)");

            GameEvent.Publish(new AttackExecutedEvent
            {
                AttackerId = owner.UnitId,
                TargetId = target.UnitId,
                ActualDamage = dealt,
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
    }
}
