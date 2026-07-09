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
            // 【公共回合制】推迟 = 目标下次行动延后 N 个公共回合。
            // Core 只累加 PendingRoundDelay；UI 层结算后调用 ATB.ApplyPendingDelays 把它落到真调度（NextRound += N）。
            // 正数 = 推迟（延后），负数 = 提前（当前无卡使用，落账时会被 clamp 到不早于当前回合）。
            target.PendingRoundDelay += ShiftSegments;
            string direction = ShiftSegments > 0 ? "推迟" : "提前";
            Debug.Log($"[ActionBarShiftCommand] {target.UnitId} 行动{direction} {System.Math.Abs(ShiftSegments)} 回合 (累计待落账 {target.PendingRoundDelay})");
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
