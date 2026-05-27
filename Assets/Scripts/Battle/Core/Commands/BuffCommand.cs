using Ashlight.Battle.Core.Data;
using Ashlight.Config;
using cfg;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// Buff指令（占位实现）
    /// 对应BuffEffect
    /// </summary>
    public class BuffCommand : ICommand
    {
        /// <summary>调用方未显式指定 Duration 时的占位值；Execute 时会按 BuffInfo.DefaultDuration 落地</summary>
        public const int UseConfigDefaultDuration = int.MinValue;

        public string BuffId { get; set; }
        public float Value { get; set; }
        public int Duration { get; set; }

        public BuffCommand(string buffId, float value, int duration = UseConfigDefaultDuration)
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

            var info = ConfigLoader.Tables?.TbBuffInfo?.GetOrDefault(BuffId);

            // Artifact 抵消：来自他人的 debuff 被 Artifact 优先吸收，每层消耗 1 次
            bool isDebuff = info != null && info.Polarity == BuffPolarityEnum.Debuff;
            bool fromOther = !string.Equals(ownerId, actualTargetId, System.StringComparison.Ordinal);
            if (isDebuff && fromOther && BuffId != "Artifact")
            {
                var artifact = target.GetBuff("Artifact");
                if (artifact != null)
                {
                    artifact.Value -= 1f;
                    if (artifact.Value <= 0f)
                    {
                        target.RemoveBuff("Artifact");
                    }
                    Debug.Log($"[BuffCommand] {actualTargetId} 圣物抵消 debuff: {BuffId} (剩余圣物: {Mathf.Max(0, (int)artifact.Value)})");
                    return;
                }
            }

            // Duration 未显式指定时回退到 BuffInfo.DefaultDuration（如易伤/减伤配置 2 回合）
            int duration = Duration;
            if (duration == UseConfigDefaultDuration)
            {
                duration = info != null ? info.DefaultDuration : -1;
            }

            var buff = new BuffState
            {
                BuffId = BuffId,
                Value = Value,
                RemainingDuration = duration
            };

            target.AddBuff(buff);
            Debug.Log($"[BuffCommand] {actualTargetId} 获得Buff: {BuffId} (数值: {Value}, 持续: {duration})");
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

