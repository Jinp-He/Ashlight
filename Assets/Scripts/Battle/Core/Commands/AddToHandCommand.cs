using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    public class AddToHandCommand : ICommand
    {
        public string CardId { get; set; }
        public int Count { get; set; }

        public AddToHandCommand(string cardId, int count)
        {
            CardId = cardId;
            Count = count;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            if (state?.DeckSystem == null)
            {
                Debug.LogWarning("[AddToHandCommand] DeckSystem is null");
                return;
            }

            // 动态归属：token（如飞刀）按「生成者」写入 owner，使其可被生成者打出（而非卡牌静态 BelongTo）
            var owner = state.GetUnitById(ownerId);
            var ownerCharacterId = owner?.GetCharacterId();
            int added = state.DeckSystem.AddCardToHand(CardId, Count, ownerCharacterId);
            Debug.Log($"[AddToHandCommand] added {added}/{Count} card(s): {CardId}, owner={ownerCharacterId}");
        }

        public int GetPriority()
        {
            return 40;
        }

        public string GetCommandType()
        {
            return "AddToHand";
        }

        public ICommand Clone()
        {
            return new AddToHandCommand(CardId, Count);
        }
    }
}
