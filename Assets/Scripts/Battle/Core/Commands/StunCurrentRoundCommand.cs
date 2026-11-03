using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 晕眩「当前公共回合将行动」的敌对单位，对应 StunCurrentRoundEffect（示现/眩晕飞镖）。
    /// 施加 Stun buff（Value=剩余晕眩回合数）；消费在 UI 层敌人原子回合：带 Stun 的敌人跳过本次行动并扣 1 层。
    /// RandomOne=true 时只随机晕一名（确定性伪随机，预测与实战一致）。
    /// </summary>
    public class StunCurrentRoundCommand : ICommand
    {
        public int Duration { get; set; }
        public bool RandomOne { get; set; }

        public StunCurrentRoundCommand(int duration, bool randomOne)
        {
            Duration = Mathf.Max(1, duration);
            RandomOne = randomOne;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var owner = state.GetUnitById(ownerId);
            if (owner == null || owner.IsDead)
            {
                return;
            }

            var targets = state.GetCurrentRoundOpponents(owner);
            if (targets.Count == 0)
            {
                Debug.Log($"[StunCurrentRound] 回合 {state.CurrentRound} 无敌人行动，{ownerId} 晕眩落空");
                return;
            }

            if (RandomOne)
            {
                var picked = targets[(state.TurnCount * 31 + state.CurrentRound) % targets.Count];
                targets = new System.Collections.Generic.List<UnitState> { picked };
            }

            foreach (var target in targets)
            {
                target.AddBuff(new BuffState
                {
                    BuffId = "Stun",
                    Value = Duration,
                    RemainingDuration = -1 // 不走回合末自然衰减，由「被跳过行动」时扣层（UI 敌人原子回合）
                });
                Debug.Log($"[StunCurrentRound] {target.UnitId} 被晕眩 {Duration} 回合 (本回合行动将被跳过)");
            }

            state.CheckBattleEnd();
        }

        public int GetPriority()
        {
            return 65;
        }

        public string GetCommandType()
        {
            return "StunCurrentRound";
        }

        public ICommand Clone()
        {
            return new StunCurrentRoundCommand(Duration, RandomOne);
        }
    }
}
