using Ashlight.Battle.Core.Data;
using Ashlight.Common.Events;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 换位指令，对应 MovePositionEffect。
    /// 位置模型：前排 = 同阵营列表里的第一个位置（索引 0，唯一），其余皆后排。
    /// 效果 = 施法者和【指定目标】交换在阵营列表中的顺序，前后排随之推导（唯一真相源为列表顺序）。
    /// UI 收到 PositionSwappedEvent 后交换二者的 sibling 顺序完成视觉重排。
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
            var owner = state.GetUnitById(ownerId);
            if (owner == null || owner.IsDead)
            {
                Debug.LogWarning($"[MovePositionCommand] invalid owner: {ownerId}");
                return;
            }

            // 永远和指定目标交换位置
            var target = state.GetUnitById(targetId);
            if (target == null || target == owner)
            {
                Debug.Log($"[MovePositionCommand] {ownerId} 换位目标无效或即为自身: {targetId}，跳过");
                return;
            }
            if (target.IsDead)
            {
                Debug.Log($"[MovePositionCommand] {ownerId} 换位目标已死亡: {targetId}，跳过");
                return;
            }
            if (target.IsPlayerUnit != owner.IsPlayerUnit)
            {
                Debug.LogWarning($"[MovePositionCommand] {ownerId} 换位目标不同阵营: {targetId}，跳过");
                return;
            }

            // 交换二者在阵营列表中的顺序（列表顺序 = 唯一真相源，前后排由此推导）
            var team = owner.IsPlayerUnit ? state.PlayerUnits : state.EnemyUnits;
            int ownerIndex = team.IndexOf(owner);
            int targetIndex = team.IndexOf(target);
            if (ownerIndex < 0 || targetIndex < 0)
            {
                Debug.LogWarning($"[MovePositionCommand] 单位不在阵营列表中: owner#{ownerIndex}, target#{targetIndex}，跳过");
                return;
            }
            team[ownerIndex] = target;
            team[targetIndex] = owner;
            Debug.Log($"[MovePositionCommand] {ownerId} <-> {target.UnitId} 交换位置 " +
                      $"(owner 现在={state.GetRowPosition(owner)}, mode={Mode})");

            // 发事件让 UI 交换二者 sibling 顺序；不发的话逻辑变了但画面不动，玩家会以为“没有效果”。
            GameEvent.Publish(new PositionSwappedEvent
            {
                UnitIdA = ownerId,
                UnitIdB = target.UnitId,
                IsPrediction = state.IsPrediction
            });
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
