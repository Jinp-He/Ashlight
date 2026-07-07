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
        ///
        /// <paramref name="strict"/> 控制目标区为空（该区被清空/全员移走）时的行为：
        /// · false（默认，向后兼容）→ 回退到全体存活，避免调用方无目标。
        /// · true → 返回空列表，表示「该区当前无人」。敌人索敌/执行索敌用此语义实现
        ///   「攻击回合目标区空排 → 打空 miss」；AOE 扩散同理只铺到区内剩余单位。
        /// </summary>
        public static List<UnitState> FilterByZone(BattleStateSnapshot state, IEnumerable<UnitState> units, TargetZoneEnum zone, bool strict = false)
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
            if (inZone.Count > 0)
            {
                return inZone;
            }
            // 空区：strict 下如实返回空（触发 miss / 只打剩余）；否则回退全体（旧行为）
            return strict ? inZone : alive;
        }
    }
}
