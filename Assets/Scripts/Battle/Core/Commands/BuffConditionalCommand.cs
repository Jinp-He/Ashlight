using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 条件 Buff 指令
    /// 对应 BuffConditionalEffect：满足条件时才把 Buff 施加到目标身上。
    /// 目前支持条件：
    /// · "MovedThisTurn" —— 施法者本回合移动过（换过区），用于游侠影袭「本回合移动过则叠毒」。
    /// 满足条件后复用 <see cref="BuffCommand"/> 的施加逻辑（含圣物抵消、默认持续时间等）。
    /// </summary>
    public class BuffConditionalCommand : ICommand
    {
        public string BuffId { get; set; }
        public float Value { get; set; }
        public string ConditionType { get; set; }

        public BuffConditionalCommand(string buffId, float value, string conditionType)
        {
            BuffId = buffId;
            Value = value;
            ConditionType = conditionType;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var owner = state.GetUnitById(ownerId);
            if (owner == null || owner.IsDead)
            {
                Debug.LogWarning($"[BuffConditionalCommand] 执行者不存在或已死亡: {ownerId}");
                return;
            }

            if (!CheckCondition(owner))
            {
                Debug.Log($"[BuffConditionalCommand] 施法者 {ownerId} 不满足条件 [{ConditionType}]，不施加 {BuffId}");
                return;
            }

            // 复用 BuffCommand 的施加逻辑（圣物抵消 / 默认持续时间等）
            new BuffCommand(BuffId, Value).Execute(state, ownerId, targetId);
        }

        private bool CheckCondition(UnitState owner)
        {
            if (string.IsNullOrEmpty(ConditionType))
            {
                return false;
            }

            switch (ConditionType)
            {
                case "MovedThisTurn":
                    return owner.HasMovedThisTurn;

                case "HasMorale":
                    return owner.GetBuff("Morale") != null && owner.GetBuff("Morale").Value > 0f;

                default:
                    Debug.LogWarning($"[BuffConditionalCommand] 未知的条件类型: {ConditionType}");
                    return false;
            }
        }

        public int GetPriority()
        {
            return 50; // 与 Buff 同级
        }

        public string GetCommandType()
        {
            return "BuffConditional";
        }

        public ICommand Clone()
        {
            return new BuffConditionalCommand(BuffId, Value, ConditionType);
        }
    }
}
