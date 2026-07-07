using Ashlight.Battle.Core.Data;
using cfg;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 防御指令
    /// 对应DefenseEffect
    /// </summary>
    public class DefenseCommand : ICommand
    {
        /// <summary>
        /// 护甲值
        /// </summary>
        public int DefenseValue { get; set; }

        /// <summary>
        /// 是否按命中次数叠加（预留）
        /// </summary>
        public bool PerHit { get; set; }

        /// <summary>
        /// 是否为群体护甲（对施法者所在阵营的全体生效）。玩家卡 TargetType=AllAlly 时置 true（如奥术护罩）。
        /// </summary>
        public bool IsAoe { get; set; }

        /// <summary>
        /// 群体护甲的目标分区限制（仅在 <see cref="IsAoe"/> 时生效）。默认 Any = 全体友军；Front/Back 只给某一区。
        /// </summary>
        public TargetZoneEnum TargetZone { get; set; } = TargetZoneEnum.Any;

        public DefenseCommand(int defenseValue, bool perHit = false)
        {
            DefenseValue = defenseValue;
            PerHit = perHit;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            if (IsAoe)
            {
                ExecuteAoeDefense(state, ownerId);
                return;
            }

            // 如果没有指定目标，则对自己生效
            string actualTargetId = string.IsNullOrEmpty(targetId) ? ownerId : targetId;

            var target = state.GetUnitById(actualTargetId);
            if (target == null)
            {
                Debug.LogWarning($"[DefenseCommand] 目标不存在: {actualTargetId}");
                return;
            }

            if (target.IsDead)
            {
                Debug.Log($"[DefenseCommand] 目标已死亡，跳过: {actualTargetId}");
                return;
            }

            target.AddDefense(DefenseValue);
            Debug.Log($"[DefenseCommand] {actualTargetId} 获得 {DefenseValue} 点护甲 (当前护甲: {target.Defense})");
        }

        /// <summary>群体护甲：给施法者阵营（按分区过滤）的每名存活友军各加护甲。</summary>
        private void ExecuteAoeDefense(BattleStateSnapshot state, string ownerId)
        {
            var owner = state.GetUnitById(ownerId);
            if (owner == null)
            {
                Debug.LogWarning($"[DefenseCommand] AOE 施法者不存在: {ownerId}");
                return;
            }

            var allies = owner.IsPlayerUnit
                ? state.GetAlivePlayerUnits()
                : state.GetAliveEnemyUnits();

            // 分区过滤：Front 只护前排、Back 只护后排、Any 全体。strict=true：目标区无人则不回退全体。
            allies = ZoneTargeting.FilterByZone(state, allies, TargetZone, strict: true);

            foreach (var ally in allies)
            {
                ally.AddDefense(DefenseValue);
                Debug.Log($"[DefenseCommand] AOE: {ally.UnitId} 获得 {DefenseValue} 点护甲 (当前护甲: {ally.Defense})");
            }
        }

        public int GetPriority()
        {
            return 100; // Defense优先级最高
        }

        public string GetCommandType()
        {
            return "Defense";
        }

        public ICommand Clone()
        {
            return new DefenseCommand(DefenseValue, PerHit) { IsAoe = IsAoe, TargetZone = TargetZone };
        }
    }
}

