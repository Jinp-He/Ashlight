using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 快速攻击指令
    /// 对应SwiftEffect
    /// 提供更高的执行优先级，用于快速反击等机制
    /// </summary>
    public class SwiftCommand : ICommand
    {
        public SwiftCommand()
        {
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            // Swift效果主要体现在优先级上，不需要特殊的执行逻辑
            // 实际的攻击伤害等效果会由其他Command（如DamageCommand）处理
            Debug.Log($"[SwiftCommand] {ownerId} 执行快速攻击（高优先级）");
        }

        public int GetPriority()
        {
            return 90; // Swift优先级：仅次于Defense/Intercept
        }

        public string GetCommandType()
        {
            return "Swift";
        }

        public ICommand Clone()
        {
            return new SwiftCommand();
        }
    }
}
