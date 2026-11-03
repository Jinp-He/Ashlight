using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    public class DefenseConditionalCommand : ICommand
    {
        public int DefenseValue { get; set; }
        public string ConditionType { get; set; }

        public DefenseConditionalCommand(int defenseValue, string conditionType)
        {
            DefenseValue = defenseValue;
            ConditionType = conditionType;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var owner = state.GetUnitById(ownerId);
            if (owner == null || owner.IsDead)
            {
                Debug.LogWarning($"[DefenseConditionalCommand] invalid owner: {ownerId}");
                return;
            }

            if (!CheckCondition(state, owner))
            {
                Debug.Log($"[DefenseConditionalCommand] condition not met: {ownerId}, {ConditionType}");
                return;
            }

            owner.AddDefense(DefenseValue);
            Debug.Log($"[DefenseConditionalCommand] {ownerId} gains {DefenseValue} defense by {ConditionType}");
        }

        private bool CheckCondition(BattleStateSnapshot state, UnitState owner)
        {
            switch (ConditionType)
            {
                case "SelfInFrontRow":
                    return state.IsFrontRow(owner);
                case "SelfInBackRow":
                    return !state.IsFrontRow(owner);
                default:
                    Debug.LogWarning($"[DefenseConditionalCommand] unknown condition: {ConditionType}");
                    return false;
            }
        }

        public int GetPriority()
        {
            return 100;
        }

        public string GetCommandType()
        {
            return "DefenseConditional";
        }

        public ICommand Clone()
        {
            return new DefenseConditionalCommand(DefenseValue, ConditionType);
        }
    }
}
