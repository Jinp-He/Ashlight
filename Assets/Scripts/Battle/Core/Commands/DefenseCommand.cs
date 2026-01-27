using Ashlight.Battle.Core.Data;
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

        public DefenseCommand(int defenseValue, bool perHit = false)
        {
            DefenseValue = defenseValue;
            PerHit = perHit;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
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
            return new DefenseCommand(DefenseValue, PerHit);
        }
    }
}

