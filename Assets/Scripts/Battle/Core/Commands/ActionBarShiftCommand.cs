using Ashlight.Battle.Core.Data;
using cfg;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 行动条位移指令
    /// 替代旧的 TimeShiftCommand，语义为前进/后退 N 个行动条段位
    /// </summary>
    public class ActionBarShiftCommand : ICommand
    {
        /// <summary>
        /// 位移段数（正数前进、负数后退）
        /// </summary>
        public int ShiftSegments { get; set; }

        /// <summary>
        /// 是否影响全体目标（true=AOE，false=单体）
        /// </summary>
        public bool IsAoe { get; set; }

        /// <summary>AOE 位移作用的目标分区。</summary>
        public TargetZoneEnum TargetZone { get; set; } = TargetZoneEnum.Any;

        /// <summary>单体位移撞上同阵营单位同一行动回合时触发的结果；None/空表示不处理碰撞。</summary>
        public string CollisionResult { get; set; }

        public ActionBarShiftCommand(int shiftSegments, bool isAoe = false, string collisionResult = null)
        {
            ShiftSegments = shiftSegments;
            IsAoe = isAoe;
            CollisionResult = collisionResult;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            if (IsAoe)
            {
                var owner = state.GetUnitById(ownerId);
                bool ownerIsPlayer = owner != null && owner.IsPlayerUnit;
                var targets = ownerIsPlayer ? state.GetAliveEnemyUnits() : state.GetAlivePlayerUnits();
                targets = ZoneTargeting.FilterByZone(state, targets, TargetZone, strict: true);

                foreach (var target in targets)
                {
                    ApplyShift(target);
                }
            }
            else
            {
                var target = state.GetUnitById(targetId);
                if (target == null || target.IsDead)
                {
                    Debug.LogWarning($"[ActionBarShiftCommand] 目标无效或已死亡: {targetId}");
                    return;
                }

                int beforeRound = GetProjectedRound(target);
                ApplyShift(target);
                int afterRound = GetProjectedRound(target);
                ResolveCollision(state, ownerId, target, beforeRound, afterRound);
            }
        }

        private void ApplyShift(UnitState target)
        {
            // 【公共回合制】推迟 = 目标下次行动延后 N 个公共回合。
            // Core 只累加 PendingRoundDelay；UI 层结算后调用 ATB.ApplyPendingDelays 把它落到真调度（NextRound += N）。
            // 正数 = 推迟（延后），负数 = 提前（当前无卡使用，落账时会被 clamp 到不早于当前回合）。
            target.PendingRoundDelay += ShiftSegments;
            string direction = ShiftSegments > 0 ? "推迟" : "提前";
            Debug.Log($"[ActionBarShiftCommand] {target.UnitId} 行动{direction} {System.Math.Abs(ShiftSegments)} 回合 (累计待落账 {target.PendingRoundDelay})");
        }

        private static int GetProjectedRound(UnitState unit)
        {
            return unit.NextActionRound < 0 ? -1 : unit.NextActionRound + unit.PendingRoundDelay;
        }

        private void ResolveCollision(
            BattleStateSnapshot state,
            string ownerId,
            UnitState shifted,
            int beforeRound,
            int afterRound)
        {
            if (string.IsNullOrWhiteSpace(CollisionResult)
                || string.Equals(CollisionResult, "None", StringComparison.OrdinalIgnoreCase)
                || beforeRound < 0
                || afterRound < 0
                || beforeRound == afterRound)
            {
                return;
            }

            var collided = new List<UnitState>();
            foreach (var other in state.GetAllUnits())
            {
                if (other == null || other == shifted || other.IsDead || other.IsPlayerUnit != shifted.IsPlayerUnit)
                {
                    continue;
                }

                if (GetProjectedRound(other) == afterRound)
                {
                    collided.Add(other);
                }
            }

            if (collided.Count == 0)
            {
                return;
            }

            if (!string.Equals(CollisionResult, "Stun", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[ActionBarShiftCommand] 未支持的碰撞结果: {CollisionResult}");
                return;
            }

            // 碰撞双方均晕眩 1 回合；通过 BuffCommand 统一走圣物抵消和 Buff 配置。
            new BuffCommand("Stun", 1).Execute(state, ownerId, shifted.UnitId);
            foreach (var other in collided)
            {
                new BuffCommand("Stun", 1).Execute(state, ownerId, other.UnitId);
            }

            Debug.Log($"[ActionBarShiftCommand] {shifted.UnitId} 在回合 {afterRound} 与 {collided.Count} 个同阵营单位碰撞，双方晕眩");
        }

        public int GetPriority()
        {
            return 60;
        }

        public string GetCommandType()
        {
            return "ActionBarShift";
        }

        public ICommand Clone()
        {
            return new ActionBarShiftCommand(ShiftSegments, IsAoe, CollisionResult)
            {
                TargetZone = TargetZone
            };
        }
    }
}
