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

        /// <summary>
        /// 单位当前是否处于指定分区（Any / Conditional 恒为 true，null 单位为 false）。
        /// 出牌站位限制（CastZone）与单体索敌限制（近战/远程）共用此口径。
        /// </summary>
        public static bool IsUnitInZone(UnitState unit, TargetZoneEnum zone)
        {
            if (unit == null)
            {
                return false;
            }

            if (zone == TargetZoneEnum.Front)
            {
                return unit.RowPosition == BattleRowPosition.FrontRow;
            }

            if (zone == TargetZoneEnum.Back)
            {
                return unit.RowPosition == BattleRowPosition.BackRow;
            }

            return true;
        }

        /// <summary>
        /// 【前排/后排】卡牌声明打出排限制（CastZone=Front/Back）时，施法者是否站在对应排。
        /// 未声明（Any）恒为 true。
        /// </summary>
        public static bool CanCastFromCurrentRow(cfg.Character.CardInfo card, UnitState caster)
        {
            if (card == null)
            {
                return true;
            }

            return IsUnitInZone(caster, card.CastZone);
        }

        /// <summary>
        /// 【近战/远程】单体牌（SingleEnemy/SingleAlly）声明 TargetZone=Front/Back 时，
        /// 目标必须站在对应排。群体牌的 TargetZone 是 AOE 扩散分区，不在此限制载体目标。
        /// 目标为 null 时放行（由调用方自行校验目标存在性）。
        /// </summary>
        public static bool IsSingleTargetZoneValid(cfg.Character.CardInfo card, UnitState target)
        {
            if (card == null || target == null)
            {
                return true;
            }

            if (card.TargetType != TargetTypeEnum.SingleEnemy && card.TargetType != TargetTypeEnum.SingleAlly)
            {
                return true;
            }

            return IsUnitInZone(target, card.TargetZone);
        }
    }
}
