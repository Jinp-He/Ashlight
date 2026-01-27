using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 时间推迟指令
    /// 对应PushCollisionEffect
    /// </summary>
    public class TimeShiftCommand : ICommand
    {
        /// <summary>
        /// 推迟格数（正数向后推，负数向前拉）
        /// </summary>
        public int ShiftAmount { get; set; }

        /// <summary>
        /// 碰撞结果（如"Stun"，预留）
        /// </summary>
        public string CollisionResult { get; set; }

        public TimeShiftCommand(int shiftAmount, string collisionResult = null)
        {
            ShiftAmount = shiftAmount;
            CollisionResult = collisionResult;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            // TimeShiftCommand 已暂时禁用
            return;
            
            /* 注释掉的原始代码
            var target = state.GetUnitById(targetId);
            if (target == null)
            {
                Debug.LogWarning($"[TimeShiftCommand] 目标不存在: {targetId}");
                return;
            }

            if (target.IsDead)
            {
                Debug.Log($"[TimeShiftCommand] 目标已死亡，跳过: {targetId}");
                return;
            }

            // 推迟目标的时间轴
            // 从当前时间索引+1开始推迟（避免影响当前正在执行的格子）
            int startIndex = state.CurrentTimeIndex + 1;
            if (startIndex >= TimelineTrack.TrackLength)
            {
                Debug.Log($"[TimeShiftCommand] 时间轴已到末尾，无法推迟");
                return;
            }

            target.Track.ShiftBlocks(startIndex, ShiftAmount);
            Debug.Log($"[TimeShiftCommand] {targetId} 的时间轴从索引 {startIndex} 开始推迟 {ShiftAmount} 格");

            // TODO: 处理碰撞逻辑（Stun等）
            if (!string.IsNullOrEmpty(CollisionResult))
            {
                Debug.Log($"[TimeShiftCommand] 碰撞效果: {CollisionResult} (暂未实现)");
            }
            */
        }

        public int GetPriority()
        {
            return 60; // TimeShift优先级
        }

        public string GetCommandType()
        {
            return "TimeShift";
        }

        public ICommand Clone()
        {
            return new TimeShiftCommand(ShiftAmount, CollisionResult);
        }
    }
}

