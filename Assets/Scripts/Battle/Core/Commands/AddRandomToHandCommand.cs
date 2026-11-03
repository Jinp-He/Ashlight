using System;
using System.Collections.Generic;
using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>从候选池随机生成手牌；一次生成内不重复。</summary>
    public class AddRandomToHandCommand : ICommand
    {
        public string CardIds { get; set; }
        public int Count { get; set; }
        public string ConditionType { get; set; }
        public int ConditionMultiplier { get; set; }

        public AddRandomToHandCommand(string cardIds, int count, string conditionType, int conditionMultiplier)
        {
            CardIds = cardIds;
            Count = count;
            ConditionType = conditionType;
            ConditionMultiplier = conditionMultiplier;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var owner = state?.GetUnitById(ownerId);
            if (owner == null || owner.IsDead || state.DeckSystem == null)
            {
                return;
            }

            int count = Mathf.Max(0, Count);
            if (ConditionType == "SelfInBackRow" && owner.RowPosition == BattleRowPosition.BackRow)
            {
                count *= Mathf.Max(1, ConditionMultiplier);
            }

            var pool = new List<string>();
            foreach (string raw in (CardIds ?? string.Empty).Split('|'))
            {
                string id = raw.Trim();
                if (!string.IsNullOrEmpty(id) && !pool.Contains(id))
                {
                    pool.Add(id);
                }
            }

            count = Mathf.Min(count, pool.Count);
            var random = new System.Random(BuildStableSeed(state, ownerId));
            var ownerCharacterId = owner.GetCharacterId();
            for (int i = 0; i < count; i++)
            {
                int index = random.Next(pool.Count);
                string cardId = pool[index];
                pool.RemoveAt(index);
                state.DeckSystem.AddCardToHand(cardId, 1, ownerCharacterId);
            }
        }

        private static int BuildStableSeed(BattleStateSnapshot state, string ownerId)
        {
            unchecked
            {
                int hash = 17;
                foreach (char c in ownerId ?? string.Empty)
                {
                    hash = hash * 31 + c;
                }
                hash = hash * 31 + state.CurrentRound;
                hash = hash * 31 + state.TurnCount;
                hash = hash * 31 + state.DeckSystem.Hand.Count;
                return hash;
            }
        }

        public int GetPriority() => 40;
        public string GetCommandType() => "AddRandomToHand";
        public ICommand Clone() => new AddRandomToHandCommand(CardIds, Count, ConditionType, ConditionMultiplier);
    }
}
