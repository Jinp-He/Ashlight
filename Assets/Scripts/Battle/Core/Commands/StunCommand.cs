using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 硬直指令
    /// 冻结目标敌人，停止其意图轴/执行轴推进和ATB推进 N tick
    /// 仅对处于意图轴或执行轴阶段的敌人有效
    /// </summary>
    public class StunCommand : ICommand
    {
        /// <summary>
        /// 硬直持续tick数
        /// </summary>
        public int StunTicks { get; set; }

        public StunCommand(int stunTicks)
        {
            StunTicks = Mathf.Max(1, stunTicks);
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var target = state.GetUnitById(targetId);
            if (target == null || target.IsDead)
            {
                Debug.LogWarning($"[StunCommand] 目标无效或已死亡: {targetId}");
                return;
            }

            // 仅对处于意图轴/执行轴的敌人生效
            if (target.CurrentPhase == EnemyPhase.None)
            {
                Debug.Log($"[StunCommand] 目标 {targetId} 不在意图轴/执行轴中，硬直无效");
                return;
            }

            // 叠加硬直：取较大值
            if (target.IsStunned)
            {
                target.StunRemainingTicks = Mathf.Max(target.StunRemainingTicks, StunTicks);
                Debug.Log($"[StunCommand] 目标 {targetId} 已处于硬直，刷新为 {target.StunRemainingTicks} tick");
            }
            else
            {
                target.IsStunned = true;
                target.StunRemainingTicks = StunTicks;
                Debug.Log($"[StunCommand] 目标 {targetId} 被硬直 {StunTicks} tick (阶段={target.CurrentPhase})");
            }
        }

        public int GetPriority()
        {
            return 65; // 在ActionBarShift(60)之上，Attack(80)之下
        }

        public string GetCommandType()
        {
            return "Stun";
        }

        public ICommand Clone()
        {
            return new StunCommand(StunTicks);
        }
    }
}
