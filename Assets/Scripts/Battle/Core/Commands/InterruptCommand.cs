using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 打断指令
    /// 取消目标敌人当前意图，重置阶段为None
    /// 仅在目标处于【意图轴】阶段时生效；执行轴阶段无法打断
    /// </summary>
    public class InterruptCommand : ICommand
    {
        public InterruptCommand()
        {
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var target = state.GetUnitById(targetId);
            if (target == null || target.IsDead)
            {
                Debug.LogWarning($"[InterruptCommand] 目标无效或已死亡: {targetId}");
                return;
            }

            if (target.CurrentPhase != EnemyPhase.IntentAxis)
            {
                string reason = target.CurrentPhase == EnemyPhase.ExecuteAxis
                    ? "已进入执行轴，无法打断"
                    : "不在意图轴/执行轴中";
                Debug.Log($"[InterruptCommand] 打断失败: 目标 {targetId} {reason}");
                return;
            }

            // 打断成功：清除意图，重置阶段
            Debug.Log($"[InterruptCommand] 成功打断 {targetId}！技能={target.PendingSkillId}");

            target.CurrentPhase = EnemyPhase.None;
            target.IntentAxisLength = 0;
            target.IntentAxisProgress = 0;
            target.ExecuteAxisLength = 1;
            target.ExecuteAxisProgress = 0;
            target.PendingSkillId = null;
            target.PendingTargetId = null;
            target.IsStunned = false;
            target.StunRemainingTicks = 0;

            // 重启ATB（被打断的敌人从头开始推进）
            if (target.ActionBar != null)
            {
                target.ActionBar.Restart();
            }
        }

        public int GetPriority()
        {
            return 95; // 高优先级，在防御(100)之下，其他之上
        }

        public string GetCommandType()
        {
            return "Interrupt";
        }

        public ICommand Clone()
        {
            return new InterruptCommand();
        }
    }
}
