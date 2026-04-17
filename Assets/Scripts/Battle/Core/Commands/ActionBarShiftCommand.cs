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
            // 如果目标处于意图轴/执行轴且为负向位移（延后），作用于轴进度而非行动条
            if (ShiftSegments < 0 && target.CurrentPhase != EnemyPhase.None)
            {
                int delayAmount = System.Math.Abs(ShiftSegments);
                if (target.CurrentPhase == EnemyPhase.IntentAxis)
                {
                    int before = target.IntentAxisProgress;
                    target.IntentAxisProgress = Mathf.Max(0, target.IntentAxisProgress - delayAmount);
                    Debug.Log($"[ActionBarShiftCommand] {target.UnitId} 意图轴延后 {delayAmount} 格 ({before} -> {target.IntentAxisProgress}/{target.IntentAxisLength})");
                }
                else if (target.CurrentPhase == EnemyPhase.ExecuteAxis)
                {
                    int before = target.ExecuteAxisProgress;
                    target.ExecuteAxisProgress = Mathf.Max(0, target.ExecuteAxisProgress - delayAmount);
                    Debug.Log($"[ActionBarShiftCommand] {target.UnitId} 执行轴延后 {delayAmount} 格 ({before} -> {target.ExecuteAxisProgress}/{target.ExecuteAxisLength})");
                }
                return;
            }

            if (target.ActionBar == null)
            {
                return;
            }

            int beforeSeg = target.ActionBar.CurrentSegment;
            target.ActionBar.Shift(ShiftSegments);
            int afterSeg = target.ActionBar.CurrentSegment;

            string direction = ShiftSegments > 0 ? "前进" : "后退";
            Debug.Log($"[ActionBarShiftCommand] {target.UnitId} 行动条{direction} {System.Math.Abs(ShiftSegments)} 段 ({beforeSeg} -> {afterSeg})");
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
