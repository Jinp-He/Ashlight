using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    public class AddToHandCommand : ICommand
    {
        public string CardId { get; set; }
        public int Count { get; set; }
        public bool UseTargetOwner { get; set; }

        public AddToHandCommand(string cardId, int count, bool useTargetOwner = false)
        {
            CardId = cardId;
            Count = count;
            UseTargetOwner = useTargetOwner;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            if (state?.DeckSystem == null)
            {
                Debug.LogWarning("[AddToHandCommand] DeckSystem is null");
                return;
            }

            // 玩家生成的 token 归生成者；敌人塞入的牌归技能目标。
            // 动态 owner 会覆盖 CardInfo 的静态 BelongTo，使同一张诅咒牌可塞给任意角色。
            var cardOwner = state.GetUnitById(UseTargetOwner ? targetId : ownerId);
            var ownerCharacterId = cardOwner?.GetCharacterId();
            int added = state.DeckSystem.AddCardToHand(CardId, Count, ownerCharacterId);
            Debug.Log($"[AddToHandCommand] added {added}/{Count} card(s): {CardId}, owner={ownerCharacterId}, fromTarget={UseTargetOwner}");
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
            return new AddToHandCommand(CardId, Count, UseTargetOwner);
        }
    }
}
