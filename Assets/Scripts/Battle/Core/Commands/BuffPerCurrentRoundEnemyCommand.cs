using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 按「当前公共回合将行动」的敌对单位数量给施法者叠 buff，对应 BuffPerCurrentRoundEnemyEffect（全知全闪）。
    /// 层数 = 当前回合敌人数 × ValuePer；0 个敌人则不获得。
    /// </summary>
    public class BuffPerCurrentRoundEnemyCommand : ICommand
    {
        public string BuffId { get; set; }
        public float ValuePer { get; set; }

        public BuffPerCurrentRoundEnemyCommand(string buffId, float valuePer)
        {
            BuffId = buffId;
            ValuePer = valuePer;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var owner = state.GetUnitById(ownerId);
            if (owner == null || owner.IsDead)
            {
                return;
            }

            int count = state.GetCurrentRoundOpponents(owner).Count;
            if (count <= 0)
            {
                Debug.Log($"[BuffPerCurrentRoundEnemy] 回合 {state.CurrentRound} 无敌人行动，{ownerId} 不获得 {BuffId}");
                return;
            }

            float total = ValuePer * count;
            // 复用 BuffCommand：走 BuffInfo 默认持续/叠层/圣物规则，目标=施法者自己
            new BuffCommand(BuffId, total).Execute(state, ownerId, ownerId);
            Debug.Log($"[BuffPerCurrentRoundEnemy] {ownerId} 按 {count} 个当前回合敌人获得 {BuffId} x{total}");
        }

        public int GetPriority()
        {
            return 50;
        }

        public string GetCommandType()
        {
            return "BuffPerCurrentRoundEnemy";
        }

        public ICommand Clone()
        {
            return new BuffPerCurrentRoundEnemyCommand(BuffId, ValuePer);
        }
    }
}
