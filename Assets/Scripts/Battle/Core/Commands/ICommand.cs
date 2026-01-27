using Ashlight.Battle.Core.Data;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 战斗指令接口
    /// 所有具体指令必须实现此接口
    /// </summary>
    public interface ICommand
    {
        /// <summary>
        /// 执行指令
        /// </summary>
        /// <param name="state">战场状态快照</param>
        /// <param name="ownerId">执行者ID</param>
        /// <param name="targetId">目标ID（可为null）</param>
        void Execute(BattleStateSnapshot state, string ownerId, string targetId);

        /// <summary>
        /// 获取指令优先级
        /// 优先级越高越先执行
        /// Defense/Intercept=100, Swift=90, Attack=80, Heal=70, TimeShift=60, Buff=50, Others=0
        /// </summary>
        int GetPriority();

        /// <summary>
        /// 获取指令类型名称（用于调试）
        /// </summary>
        string GetCommandType();

        /// <summary>
        /// 深拷贝指令
        /// </summary>
        ICommand Clone();
    }
}

