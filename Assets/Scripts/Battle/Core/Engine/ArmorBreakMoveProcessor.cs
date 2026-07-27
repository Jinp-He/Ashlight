using Ashlight.Battle.Core.Commands;
using Ashlight.Battle.Core.Data;

namespace Ashlight.Battle.Core.Engine
{
    public static class ArmorBreakMoveProcessor
    {
        public static void ResolvePending(BattleStateSnapshot state, UnitState target)
        {
            if (state == null || target == null || target.IsDead)
            {
                return;
            }

            string mode = target.ConsumePendingArmorBreakMove();
            if (!string.IsNullOrEmpty(mode))
            {
                new MovePositionCommand(mode).Execute(state, target.UnitId, target.UnitId);
            }
        }
    }
}
