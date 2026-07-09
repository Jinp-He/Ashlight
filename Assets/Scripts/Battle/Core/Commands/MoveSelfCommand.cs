using Ashlight.Battle.Core.Data;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 施法者自身移动，对应 MoveSelfEffect。
    /// 与 <see cref="MovePositionCommand"/> 的区别：后者移动的是**卡牌目标**（敌方目标卡会把敌人移走），
    /// 本命令永远移动施法者自己——用于「打敌人并自己撤到后排」类卡（掩护射击）。
    /// 复用 MovePositionCommand 的移动/事件/触发逻辑（把 targetId 强制为 ownerId）。
    /// </summary>
    public class MoveSelfCommand : ICommand
    {
        public string Mode { get; set; }

        public MoveSelfCommand(string mode)
        {
            Mode = mode;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            new MovePositionCommand(Mode).Execute(state, ownerId, ownerId);
        }

        public int GetPriority()
        {
            return 90; // 同 MovePosition（ATB 路径按声明顺序执行，优先级仅元数据）
        }

        public string GetCommandType()
        {
            return "MoveSelf";
        }

        public ICommand Clone()
        {
            return new MoveSelfCommand(Mode);
        }
    }
}
