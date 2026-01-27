using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 碰撞效果指令
    /// 对应CollisionEffect
    /// 处理时间轴碰撞时的特殊效果（如Stun、Interrupt等）
    /// </summary>
    public class CollisionCommand : ICommand
    {
        /// <summary>
        /// 碰撞结果类型（如"Stun"、"Interrupt"、"Knockback"）
        /// </summary>
        public string Result { get; set; }

        public CollisionCommand(string result)
        {
            Result = result;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var target = state.GetUnitById(targetId);
            if (target == null)
            {
                Debug.LogWarning($"[CollisionCommand] 目标不存在: {targetId}");
                return;
            }

            if (target.IsDead)
            {
                Debug.Log($"[CollisionCommand] 目标已死亡，跳过: {targetId}");
                return;
            }

            if (string.IsNullOrEmpty(Result))
            {
                Debug.LogWarning($"[CollisionCommand] 碰撞结果为空");
                return;
            }

            // 根据碰撞结果类型执行不同的效果
            switch (Result)
            {
                case "Stun":
                    ApplyStun(state, target);
                    break;

                case "Interrupt":
                    ApplyInterrupt(target);
                    break;

                case "Knockback":
                    ApplyKnockback(state, target);
                    break;

                default:
                    Debug.LogWarning($"[CollisionCommand] 未知的碰撞结果类型: {Result}");
                    break;
            }
        }

        /// <summary>
        /// 应用眩晕效果（添加Stun Buff）
        /// </summary>
        private void ApplyStun(BattleStateSnapshot state, UnitState target)
        {
            var stunBuff = new BuffState
            {
                BuffId = "Stun",
                Value = 1,
                RemainingDuration = 1 // 持续1回合
            };

            target.AddBuff(stunBuff);
            Debug.Log($"[CollisionCommand] {target.UnitId} 被眩晕（Stun）");
        }

        /// <summary>
        /// 应用打断效果（清除时间轴上的后续Block）
        /// </summary>
        private void ApplyInterrupt(UnitState target)
        {
            if (target.Track == null)
            {
                return;
            }

            // 清除当前时间之后的所有Block
            int clearedCount = 0;
            for (int i = 0; i < TimelineTrack.TrackLength; i++)
            {
                var block = target.Track.GetBlock(i);
                if (block != null && !block.IsEmpty())
                {
                    target.Track.ClearBlock(i);
                    clearedCount++;
                }
            }

            Debug.Log($"[CollisionCommand] {target.UnitId} 被打断（Interrupt），清除 {clearedCount} 个时间块");
        }

        /// <summary>
        /// 应用击退效果（推迟时间轴）
        /// </summary>
        private void ApplyKnockback(BattleStateSnapshot state, UnitState target)
        {
            if (target.Track == null)
            {
                return;
            }

            int knockbackAmount = 2; // 击退2格
            int startIndex = state.CurrentTimeIndex + 1;
            
            if (startIndex >= TimelineTrack.TrackLength)
            {
                Debug.Log($"[CollisionCommand] {target.UnitId} 时间轴已到末尾，无法击退");
                return;
            }

            target.Track.ShiftBlocks(startIndex, knockbackAmount);
            Debug.Log($"[CollisionCommand] {target.UnitId} 被击退（Knockback） {knockbackAmount} 格");
        }

        public int GetPriority()
        {
            return 60; // 与TimeShift相同的优先级
        }

        public string GetCommandType()
        {
            return "Collision";
        }

        public ICommand Clone()
        {
            return new CollisionCommand(Result);
        }
    }
}
