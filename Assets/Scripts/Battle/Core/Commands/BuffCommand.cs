using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// Buff指令（占位实现）
    /// 对应BuffEffect
    /// </summary>
    public class BuffCommand : ICommand
    {
        public string BuffId { get; set; }
        public float Value { get; set; }
        public int Duration { get; set; }

        public BuffCommand(string buffId, float value, int duration = -1)
        {
            BuffId = buffId;
            Value = value;
            Duration = duration;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            string actualTargetId = string.IsNullOrEmpty(targetId) ? ownerId : targetId;

            var target = state.GetUnitById(actualTargetId);
            if (target == null || target.IsDead)
            {
                return;
            }

            var buff = new BuffState
            {
                BuffId = BuffId,
                Value = Value,
                RemainingDuration = Duration
            };

            target.AddBuff(buff);
            Debug.Log($"[BuffCommand] {actualTargetId} 获得Buff: {BuffId} (数值: {Value})");
        }

        public int GetPriority()
        {
            return 50; // Buff优先级
        }

        public string GetCommandType()
        {
            return "Buff";
        }

        public ICommand Clone()
        {
            return new BuffCommand(BuffId, Value, Duration);
        }
    }
}

