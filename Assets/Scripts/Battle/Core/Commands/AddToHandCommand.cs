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

            int added = state.DeckSystem.AddCardToHand(CardId, Count);
            Debug.Log($"[AddToHandCommand] added {added}/{Count} card(s): {CardId}");
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
