using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 引导指令
    /// 对应ChannelEffect
    /// 标记单位进入引导状态，引导期间可能受到打断等特殊效果
    /// </summary>
    public class ChannelCommand : ICommand
    {
        /// <summary>
        /// 引导持续时间（格数）
        /// </summary>
        public int Duration { get; set; }

        public ChannelCommand(int duration)
        {
            Duration = duration;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            // 如果没有指定目标，则对自己生效
            string actualTargetId = string.IsNullOrEmpty(targetId) ? ownerId : targetId;

            var target = state.GetUnitById(actualTargetId);
            if (target == null)
            {
                Debug.LogWarning($"[ChannelCommand] 目标不存在: {actualTargetId}");
                return;
            }

            if (target.IsDead)
            {
                Debug.Log($"[ChannelCommand] 目标已死亡，跳过: {actualTargetId}");
                return;
            }

            // 添加引导状态Buff
            var channelBuff = new BuffState
            {
                BuffId = "Channeling",
                Value = Duration,
                RemainingDuration = Duration
            };

            target.AddBuff(channelBuff);
            Debug.Log($"[ChannelCommand] {actualTargetId} 进入引导状态，持续 {Duration} 格");

            // 引导状态的主要作用：
            // 1. 标记单位正在引导（可被AttackExtraCommand等检测）
            // 2. 引导期间可能受到打断（CollisionCommand的Interrupt效果）
            // 3. 引导完成后触发特殊效果（需要在其他系统中实现）
        }

        public int GetPriority()
        {
            return 50; // 与Buff相同的优先级
        }

        public string GetCommandType()
        {
            return "Channel";
        }

        public ICommand Clone()
        {
            return new ChannelCommand(Duration);
        }
    }
}
