using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 登记一个回合内移动触发器，对应 OnMoveDamageEffect（铁蒺藜）/ OnMoveAddCardEffect（隧穿效应）。
    /// 登记后，本回合内每有单位移动一次就触发一次（结算在 MoveTriggerProcessor）；回合结束整表清空。
    /// </summary>
    public class RegisterMoveTriggerCommand : ICommand
    {
        public string TriggerType { get; set; }
        public int Amount { get; set; }
        public string CardId { get; set; }

        public RegisterMoveTriggerCommand(string triggerType, int amount, string cardId = null)
        {
            TriggerType = triggerType;
            Amount = amount;
            CardId = cardId;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            if (state.MoveTriggers == null)
            {
                state.MoveTriggers = new System.Collections.Generic.List<MoveTriggerState>();
            }

            state.MoveTriggers.Add(new MoveTriggerState
            {
                OwnerId = ownerId,
                TriggerType = TriggerType,
                Amount = Amount,
                CardId = CardId
            });

            Debug.Log($"[RegisterMoveTrigger] {ownerId} 登记移动触发器 {TriggerType} (amount={Amount}, card={CardId ?? "-"})，本回合生效");
        }

        public int GetPriority()
        {
            return 50;
        }

        public string GetCommandType()
        {
            return "RegisterMoveTrigger";
        }

        public ICommand Clone()
        {
            return new RegisterMoveTriggerCommand(TriggerType, Amount, CardId);
        }
    }
}
