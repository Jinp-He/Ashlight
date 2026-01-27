using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 拦截指令
    /// 对应InterceptEffect
    /// 拦截攻击并获得护甲值
    /// </summary>
    public class InterceptCommand : ICommand
    {
        /// <summary>
        /// 拦截后获得的护甲值
        /// </summary>
        public int ShieldValue { get; set; }

        public InterceptCommand(int shieldValue)
        {
            ShieldValue = shieldValue;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            // 如果没有指定目标，则对自己生效
            string actualTargetId = string.IsNullOrEmpty(targetId) ? ownerId : targetId;

            var target = state.GetUnitById(actualTargetId);
            if (target == null)
            {
                Debug.LogWarning($"[InterceptCommand] 目标不存在: {actualTargetId}");
                return;
            }

            if (target.IsDead)
            {
                Debug.Log($"[InterceptCommand] 目标已死亡，跳过: {actualTargetId}");
                return;
            }

            // 拦截效果：获得护甲
            target.AddDefense(ShieldValue);
            Debug.Log($"[InterceptCommand] {actualTargetId} 拦截攻击，获得 {ShieldValue} 点护甲 (当前护甲: {target.Defense})");
        }

        public int GetPriority()
        {
            return 100; // Intercept优先级最高，与Defense相同
        }

        public string GetCommandType()
        {
            return "Intercept";
        }

        public ICommand Clone()
        {
            return new InterceptCommand(ShieldValue);
        }
    }
}
