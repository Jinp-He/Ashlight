using Ashlight.Battle.Core.Data;
using Ashlight.Common.Events;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 移动指令，对应 MovePositionEffect。
    /// 效果 = 把【施法者自己】移动到目标区（前排/后排），可独立进出、两区多人共存，**不再与他人换位**。
    /// 目标区由 <see cref="Mode"/> 决定："FrontRow" 去前排 / "BackRow" 去后排 / "Toggle"(或空) 前后翻转。
    /// 直接改施法者的 <see cref="UnitState.RowPosition"/>（唯一真相源），UI 收到 PositionSwappedEvent 后把该角色重挂到对应区容器。
    /// </summary>
    public class MovePositionCommand : ICommand
    {
        public string Mode { get; set; }

        public MovePositionCommand(string mode)
        {
            Mode = mode;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            // 移动的是「目标」：自我移动卡（疾步/翻滚，TargetType=Self）目标即施法者；
            // 移动友军卡（法师闪现，TargetType=SingleAlly）目标是被指定的友军。targetId 为空时回退到施法者。
            string moverId = string.IsNullOrEmpty(targetId) ? ownerId : targetId;
            var mover = state.GetUnitById(moverId);
            if (mover == null || mover.IsDead)
            {
                Debug.LogWarning($"[MovePositionCommand] invalid mover: {moverId} (owner={ownerId})");
                return;
            }

            var before = mover.RowPosition;
            var dest = ResolveDestination(Mode, before);
            if (dest == before)
            {
                Debug.Log($"[MovePositionCommand] {moverId} 已在 {dest}，无需移动 (mode={Mode})");
                return;
            }

            mover.RowPosition = dest;
            mover.HasMovedThisTurn = true; // 供条件效果读取（影袭「本回合移动过则叠毒」）——记在真正移动的单位上
            Debug.Log($"[MovePositionCommand] {moverId} 移动 {before} -> {dest} (mode={Mode}, 由 {ownerId} 发动)");

            // 独立移动（无换位对象）：UnitIdB 留空，UI 只把被移动者重挂到新区容器。
            GameEvent.Publish(new PositionSwappedEvent
            {
                UnitIdA = moverId,
                UnitIdB = null,
                IsPrediction = state.IsPrediction
            });

            // 回合内移动触发器（铁蒺藜/隧穿效应）：任何单位完成移动都触发一次
            Engine.MoveTriggerProcessor.OnUnitMoved(state, mover);
        }

        /// <summary>把 Mode 解析成目标区：FrontRow/BackRow 指定去向；Toggle 或空/未知则相对当前翻转。</summary>
        private static BattleRowPosition ResolveDestination(string mode, BattleRowPosition current)
        {
            string m = mode?.Trim();
            if (m == "FrontRow" || m == "Front")
            {
                return BattleRowPosition.FrontRow;
            }
            if (m == "BackRow" || m == "Back")
            {
                return BattleRowPosition.BackRow;
            }
            // Toggle / 空 / 未知：前后翻转
            return current == BattleRowPosition.FrontRow ? BattleRowPosition.BackRow : BattleRowPosition.FrontRow;
        }

        public int GetPriority()
        {
            return 90;
        }

        public string GetCommandType()
        {
            return "MovePosition";
        }

        public ICommand Clone()
        {
            return new MovePositionCommand(Mode);
        }
    }
}
