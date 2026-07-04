using System.Collections.Generic;
using System.Linq;
using cfg;

namespace Ashlight.Battle.Core.Data
{
    /// <summary>
    /// 分区索敌工具：按技能声明的 <see cref="TargetZoneEnum"/> 把候选单位过滤到「前排/后排」区。
    /// 供敌人索敌（载体目标选择）与 AOE 扩散（<see cref="Commands.DamageCommand"/>）共用，保证两处口径一致。
    /// 前后排由 <see cref="BattleStateSnapshot.GetRowPosition"/> 现算（列表索引 0 = 前排），不再读单位字段。
    /// </summary>
    public static class ZoneTargeting
    {
        /// <summary>
        /// 从一组单位里筛出目标分区内的存活单位。
        /// · Front → 前排区；Back → 后排区；
        /// · Any / Conditional（未实装）→ 不过滤，返回全部存活。
        /// 目标区为空（该区被清空/全员移走）时**暂时回退到全体存活**，避免敌人无目标空转。
        /// TODO(索敌): 待「空区落空 / 闪避」表现就绪后，空区应改为真正 miss，而非回退全体。
        /// </summary>
        public static List<UnitState> FilterByZone(BattleStateSnapshot state, IEnumerable<UnitState> units, TargetZoneEnum zone)
        {
            var alive = units == null
                ? new List<UnitState>()
                : units.Where(u => u != null && !u.IsDead).ToList();

            if (alive.Count == 0 || state == null)
            {
                return alive;
            }

            BattleRowPosition wanted;
            if (zone == TargetZoneEnum.Front)
            {
                wanted = BattleRowPosition.FrontRow;
            }
            else if (zone == TargetZoneEnum.Back)
            {
                wanted = BattleRowPosition.BackRow;
            }
            else
            {
                // Any / Conditional：不做分区过滤
                return alive;
            }

            var inZone = alive.Where(u => state.GetRowPosition(u) == wanted).ToList();
            return inZone.Count > 0 ? inZone : alive;
        }
    }
}
