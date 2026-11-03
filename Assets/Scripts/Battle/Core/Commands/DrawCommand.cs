using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 抽牌指令：立即为施法者抽 <see cref="Count"/> 张牌。
    /// 对应「抽 N 张牌」类效果（由 BuffEffect(DrawCard) 特判而来——旧 DrawCard buff 从未被消费，改为即时抽牌）。
    /// </summary>
    public class DrawCommand : ICommand
    {
        public int Count { get; set; }

        public DrawCommand(int count)
        {
            Count = count;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            if (state?.DeckSystem == null || Count <= 0)
            {
                return;
            }

            var owner = state.GetUnitById(ownerId);
            var cid = owner?.GetCharacterId();
            int drawn = cid.HasValue
                ? state.DeckSystem.DrawCardForCharacter(cid.Value, Count)
                : state.DeckSystem.DrawCard(Count);

            Debug.Log($"[DrawCommand] {ownerId} 立即抽 {drawn}/{Count} 张牌");
        }

        public int GetPriority()
        {
            return 40;
        }

        public string GetCommandType()
        {
            return "Draw";
        }

        public ICommand Clone()
        {
            return new DrawCommand(Count);
        }
    }
}
