using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 立即获得能量，对应 EnergyEffect（区别于 Energized buff 的「下回合 +能量」）。
    /// 卡牌费用在 PlayCard 之前已扣除，所以本命令的加量可用于本回合继续出牌。
    /// </summary>
    public class EnergyCommand : ICommand
    {
        public int Value { get; set; }

        public EnergyCommand(int value)
        {
            Value = value;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var owner = state.GetUnitById(ownerId);
            if (owner == null || owner.IsDead || Value <= 0)
            {
                return;
            }

            owner.CurrentEnergy += Value;
            Debug.Log($"[EnergyCommand] {ownerId} 立即获得 {Value} 点能量 (当前 {owner.CurrentEnergy})");
        }

        public int GetPriority()
        {
            return 40;
        }

        public string GetCommandType()
        {
            return "Energy";
        }

        public ICommand Clone()
        {
            return new EnergyCommand(Value);
        }
    }
}
