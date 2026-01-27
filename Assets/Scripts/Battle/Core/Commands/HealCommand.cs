using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 治疗指令
    /// 对应HealEffect
    /// </summary>
    public class HealCommand : ICommand
    {
        /// <summary>
        /// 治疗量
        /// </summary>
        public int HealValue { get; set; }

        public HealCommand(int healValue)
        {
            HealValue = healValue;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            // 如果没有指定目标，则治疗自己
            string actualTargetId = string.IsNullOrEmpty(targetId) ? ownerId : targetId;

            var target = state.GetUnitById(actualTargetId);
            if (target == null)
            {
                Debug.LogWarning($"[HealCommand] 目标不存在: {actualTargetId}");
                return;
            }

            if (target.IsDead)
            {
                Debug.Log($"[HealCommand] 目标已死亡，无法治疗: {actualTargetId}");
                return;
            }

            int actualHeal = target.Heal(HealValue);
            Debug.Log($"[HealCommand] {actualTargetId} 恢复 {actualHeal} 点生命值 (当前HP: {target.CurrentHp}/{target.MaxHp})");
        }

        public int GetPriority()
        {
            return 70; // Heal优先级
        }

        public string GetCommandType()
        {
            return "Heal";
        }

        public ICommand Clone()
        {
            return new HealCommand(HealValue);
        }
    }
}

