using System.Collections.Generic;
using Ashlight.Battle.Core.Data;
using Ashlight.Battle.Core.Engine;
using Ashlight.Common.Events;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 整排移动，对应 MoveRowEffect（紧急避险）：把施法者**所在排**的所有己方存活单位（含自己）翻转到另一排。
    /// 每个被移动的单位都记 HasMovedThisTurn、发 PositionSwappedEvent、并触发回合内移动触发器。
    /// </summary>
    public class MoveRowCommand : ICommand
    {
        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var owner = state.GetUnitById(ownerId);
            if (owner == null || owner.IsDead)
            {
                Debug.LogWarning($"[MoveRowCommand] 施法者无效: {ownerId}");
                return;
            }

            var sourceRow = owner.RowPosition;
            var destRow = sourceRow == BattleRowPosition.FrontRow
                ? BattleRowPosition.BackRow
                : BattleRowPosition.FrontRow;

            var allies = owner.IsPlayerUnit ? state.PlayerUnits : state.EnemyUnits;
            var movers = new List<UnitState>();
            foreach (var ally in allies)
            {
                if (ally != null && !ally.IsDead && ally.RowPosition == sourceRow)
                {
                    movers.Add(ally);
                }
            }

            Debug.Log($"[MoveRowCommand] {ownerId} 整排移动 {sourceRow} -> {destRow}，共 {movers.Count} 人");

            foreach (var mover in movers)
            {
                mover.RowPosition = destRow;
                mover.HasMovedThisTurn = true;

                GameEvent.Publish(new PositionSwappedEvent
                {
                    UnitIdA = mover.UnitId,
                    UnitIdB = null,
                    IsPrediction = state.IsPrediction
                });

                MoveTriggerProcessor.OnUnitMoved(state, mover);
                if (state.IsBattleEnded) return;
            }
        }

        public int GetPriority()
        {
            return 90;
        }

        public string GetCommandType()
        {
            return "MoveRow";
        }

        public ICommand Clone()
        {
            return new MoveRowCommand();
        }
    }
}
