using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 清除后摇指令
    /// 对应ClearRecoilEffect
    /// 移除时间轴上的Recoil阶段，实现"取消后摇"效果
    /// </summary>
    public class ClearRecoilCommand : ICommand
    {
        public ClearRecoilCommand()
        {
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            // 如果没有指定目标，则对自己生效
            string actualTargetId = string.IsNullOrEmpty(targetId) ? ownerId : targetId;

            var target = state.GetUnitById(actualTargetId);
            if (target == null)
            {
                Debug.LogWarning($"[ClearRecoilCommand] 目标不存在: {actualTargetId}");
                return;
            }

            if (target.IsDead)
            {
                Debug.Log($"[ClearRecoilCommand] 目标已死亡，跳过: {actualTargetId}");
                return;
            }

            if (target.Track == null)
            {
                Debug.LogWarning($"[ClearRecoilCommand] 目标没有时间轴: {actualTargetId}");
                return;
            }

            // 清除时间轴上所有Recoil阶段的Block
            int clearedCount = 0;
            for (int i = 0; i < TimelineTrack.TrackLength; i++)
            {
                var block = target.Track.GetBlock(i);
                if (block != null && !block.IsEmpty() && block.Phase == PhaseEnum.Recoil)
                {
                    target.Track.ClearBlock(i);
                    clearedCount++;
                }
            }

            if (clearedCount > 0)
            {
                Debug.Log($"[ClearRecoilCommand] {actualTargetId} 清除了 {clearedCount} 个后摇（Recoil）时间块");
            }
            else
            {
                Debug.Log($"[ClearRecoilCommand] {actualTargetId} 没有后摇时间块需要清除");
            }
        }

        public int GetPriority()
        {
            return 50; // 与Buff相同的优先级
        }

        public string GetCommandType()
        {
            return "ClearRecoil";
        }

        public ICommand Clone()
        {
            return new ClearRecoilCommand();
        }
    }
}
