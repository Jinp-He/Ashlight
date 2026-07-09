using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 过载指令
    /// 允许单位在能量耗尽后继续出牌，以未来行动节奏为代价
    /// </summary>
    public class OverloadCommand : ICommand
    {
        /// <summary>
        /// 过载时获得的额外能量
        /// </summary>
        public int BonusEnergy { get; set; }

        /// <summary>
        /// 过载格数：下次行动额外推迟的公共回合数（卡牌 [过载]{V} 效果传 V，默认 1）
        /// </summary>
        public int Delay { get; set; }

        public OverloadCommand(int bonusEnergy = 0, int delay = 1)
        {
            BonusEnergy = bonusEnergy;
            Delay = delay;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var owner = state.GetUnitById(ownerId);
            if (owner == null || owner.IsDead)
            {
                Debug.LogWarning($"[OverloadCommand] 施法者无效或已死亡: {ownerId}");
                return;
            }

            if (owner.Overload == null)
            {
                owner.Overload = new OverloadState();
            }

            int debtAdded = owner.Overload.ApplyOverload(Delay);

            if (BonusEnergy > 0)
            {
                owner.CurrentEnergy += BonusEnergy;
            }

            Debug.Log($"[OverloadCommand] {ownerId} 进入过载 Lv{owner.Overload.OverloadLevel}, 下次行动 +{debtAdded} 回合 (本回合累计过载 {owner.Overload.OverloadCountThisTurn} 格), 额外能量+{BonusEnergy}");
        }

        public int GetPriority()
        {
            return 45;
        }

        public string GetCommandType()
        {
            return "Overload";
        }

        public ICommand Clone()
        {
            return new OverloadCommand(BonusEnergy, Delay);
        }
    }
}
