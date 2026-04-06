using Ashlight.Battle.Core.Data;
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

        public ActionBarShiftCommand(int shiftSegments, bool isAoe = false)
        {
            ShiftSegments = shiftSegments;
            IsAoe = isAoe;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            if (IsAoe)
            {
                var owner = state.GetUnitById(ownerId);
                bool ownerIsPlayer = owner != null && owner.IsPlayerUnit;
                var targets = ownerIsPlayer ? state.GetAliveEnemyUnits() : state.GetAlivePlayerUnits();

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

                ApplyShift(target);
            }
        }

        private void ApplyShift(UnitState target)
        {
            if (target.ActionBar == null)
            {
                return;
            }

            int before = target.ActionBar.CurrentSegment;
            target.ActionBar.Shift(ShiftSegments);
            int after = target.ActionBar.CurrentSegment;

            string direction = ShiftSegments > 0 ? "前进" : "后退";
            Debug.Log($"[ActionBarShiftCommand] {target.UnitId} 行动条{direction} {System.Math.Abs(ShiftSegments)} 段 ({before} -> {after})");
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
            return new ActionBarShiftCommand(ShiftSegments, IsAoe);
        }
    }
}
