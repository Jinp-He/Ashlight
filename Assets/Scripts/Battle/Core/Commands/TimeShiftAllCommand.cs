using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 全体时间推迟指令
    /// 对应TimeShiftAllEffect
    /// 推迟所有敌方单位的时间轴
    /// </summary>
    public class TimeShiftAllCommand : ICommand
    {
        /// <summary>
        /// 推迟格数（正数向后推，负数向前拉）
        /// </summary>
        public int ShiftValue { get; set; }

        public TimeShiftAllCommand(int shiftValue)
        {
            ShiftValue = shiftValue;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            // TimeShiftAllCommand 已暂时禁用
            return;
            
            /* 注释掉的原始代码
            var owner = state.GetUnitById(ownerId);
            if (owner == null || owner.IsDead)
            {
                Debug.LogWarning($"[TimeShiftAllCommand] 执行者不存在或已死亡: {ownerId}");
                return;
            }

            // 确定目标阵营（推迟对立阵营的所有单位）
            var targets = owner.IsPlayerUnit
                ? state.GetAliveEnemyUnits()
                : state.GetAlivePlayerUnits();

            if (targets.Count == 0)
            {
                Debug.Log($"[TimeShiftAllCommand] 没有可推迟的目标");
                return;
            }

            int affectedCount = 0;
            foreach (var target in targets)
            {
                if (target.Track == null)
                {
                    continue;
                }

                // 推迟目标的时间轴
                // 从当前时间索引+1开始推迟（避免影响当前正在执行的格子）
                int startIndex = state.CurrentTimeIndex + 1;
                if (startIndex >= TimelineTrack.TrackLength)
                {
                    Debug.Log($"[TimeShiftAllCommand] {target.UnitId} 时间轴已到末尾，无法推迟");
                    continue;
                }

                target.Track.ShiftBlocks(startIndex, ShiftValue);
                affectedCount++;
                Debug.Log($"[TimeShiftAllCommand] {target.UnitId} 的时间轴从索引 {startIndex} 开始推迟 {ShiftValue} 格");
            }

            Debug.Log($"[TimeShiftAllCommand] 全体推迟完成，影响 {affectedCount} 个单位");
            */
        }

        public int GetPriority()
        {
            return 60; // TimeShift优先级
        }

        public string GetCommandType()
        {
            return "TimeShiftAll";
        }

        public ICommand Clone()
        {
            return new TimeShiftAllCommand(ShiftValue);
        }
    }
}
