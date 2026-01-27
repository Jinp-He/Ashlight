using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 嘲讽指令
    /// 对应TauntEffect
    /// 强制敌方单位将攻击目标改为嘲讽者
    /// </summary>
    public class TauntCommand : ICommand
    {
        /// <summary>
        /// 嘲讽目标范围（如"All"表示所有敌人）
        /// </summary>
        public string Target { get; set; }

        public TauntCommand(string target)
        {
            Target = target;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var owner = state.GetUnitById(ownerId);
            if (owner == null || owner.IsDead)
            {
                Debug.LogWarning($"[TauntCommand] 执行者不存在或已死亡: {ownerId}");
                return;
            }

            // 添加嘲讽Buff到执行者身上
            var tauntBuff = new BuffState
            {
                BuffId = "Taunt",
                Value = 1,
                RemainingDuration = 1 // 持续1回合
            };

            owner.AddBuff(tauntBuff);
            Debug.Log($"[TauntCommand] {ownerId} 施加嘲讽效果，目标范围: {Target}");

            // 根据目标范围确定被嘲讽的单位
            var tauntedUnits = owner.IsPlayerUnit
                ? state.GetAliveEnemyUnits()
                : state.GetAlivePlayerUnits();

            if (Target != "All")
            {
                // 如果不是全体嘲讽，可以根据Target字段进一步筛选
                // 这里暂时只实现全体嘲讽
                Debug.LogWarning($"[TauntCommand] 目前只支持全体嘲讽（All），忽略目标: {Target}");
            }

            // 标记被嘲讽的单位（实际的目标重定向逻辑需要在攻击执行时处理）
            foreach (var unit in tauntedUnits)
            {
                var tauntedBuff = new BuffState
                {
                    BuffId = "Taunted",
                    Value = 1,
                    RemainingDuration = 1,
                    // 可以在这里存储嘲讽者的ID，用于目标重定向
                    // 但BuffState目前没有这个字段，需要扩展
                };

                unit.AddBuff(tauntedBuff);
                Debug.Log($"[TauntCommand] {unit.UnitId} 被嘲讽，将攻击 {ownerId}");
            }

            Debug.Log($"[TauntCommand] 嘲讽完成，影响 {tauntedUnits.Count} 个单位");
        }

        public int GetPriority()
        {
            return 50; // 与Buff相同的优先级
        }

        public string GetCommandType()
        {
            return "Taunt";
        }

        public ICommand Clone()
        {
            return new TauntCommand(Target);
        }
    }
}
