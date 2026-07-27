using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>注册一次性护甲耗尽移动；真正移动发生在护甲被伤害降为 0 的当次结算。</summary>
    public class MoveOnArmorBreakCommand : ICommand
    {
        public string Mode { get; set; }

        public MoveOnArmorBreakCommand(string mode)
        {
            Mode = mode;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var target = state?.GetUnitById(string.IsNullOrEmpty(targetId) ? ownerId : targetId);
            if (target == null || target.IsDead)
            {
                Debug.LogWarning($"[MoveOnArmorBreakCommand] 无效目标: {targetId}");
                return;
            }

            target.RegisterArmorBreakMove(Mode);
        }

        public int GetPriority() => 89;
        public string GetCommandType() => "MoveOnArmorBreak";
        public ICommand Clone() => new MoveOnArmorBreakCommand(Mode);
    }
}
