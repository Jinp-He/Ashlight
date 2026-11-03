using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 清除目标的全部过载，对应 ClearOverloadEffect（肾上腺素）。两部分：
    ///   1. 清掉**尚未落账**的过载计数（当前回合行动者给自己清 → 回合结束重排不再加延迟）；
    ///   2. 把**已落账**的过载负债（上次重排多加的回合数，见 UnitState.AppliedOverloadRoundDelay）
    ///      作为负的 PendingRoundDelay 拉回来——UI 落账后该单位的下次行动提前相应回合数。
    /// </summary>
    public class ClearOverloadCommand : ICommand
    {
        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            string actualTargetId = string.IsNullOrEmpty(targetId) ? ownerId : targetId;
            var target = state.GetUnitById(actualTargetId);
            if (target == null || target.IsDead)
            {
                return;
            }

            int cleared = 0;
            if (target.Overload != null)
            {
                cleared = target.Overload.OverloadCountThisTurn;
                target.Overload.OverloadCountThisTurn = 0;
                target.Overload.OverloadLevel = 0;
                target.Overload.IsOverloaded = false;
            }

            // 拉回已落账的过载延迟（队友被过载推远后可被肾上腺素拉回）
            int pullback = target.AppliedOverloadRoundDelay;
            if (pullback > 0)
            {
                target.PendingRoundDelay -= pullback;
                target.AppliedOverloadRoundDelay = 0;
            }

            Debug.Log($"[ClearOverloadCommand] {actualTargetId} 过载清除 (未落账 {cleared} 次 → 0, 拉回已落账延迟 {pullback} 回合)");
        }

        public int GetPriority()
        {
            return 55;
        }

        public string GetCommandType()
        {
            return "ClearOverload";
        }

        public ICommand Clone()
        {
            return new ClearOverloadCommand();
        }
    }
}
