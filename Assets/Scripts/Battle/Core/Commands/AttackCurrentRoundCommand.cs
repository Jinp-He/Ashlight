using Ashlight.Battle.Core.Data;
using Ashlight.Battle.Core.Engine;
using Ashlight.Common.Events;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 对「当前公共回合将行动」的所有敌对单位造成伤害，对应 AttackCurrentRoundEffect。
    /// 目标判定 = NextActionRound == CurrentRound（公共回合镜像，见 BattleStateSnapshot.GetCurrentRoundOpponents）。
    /// 本回合无敌人行动时打空（miss，不回退全体）。
    /// </summary>
    public class AttackCurrentRoundCommand : ICommand
    {
        public int Damage { get; set; }

        public AttackCurrentRoundCommand(int damage)
        {
            Damage = damage;
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
                Debug.Log($"[AttackCurrentRound] 回合 {state.CurrentRound} 无敌人行动，{ownerId} 打空");
                return;
            }

            int adjustedDamage = ApplyAttackerModifiers(owner, Damage);
            foreach (var target in targets)
            {
                int dealt = target.TakeDamage(adjustedDamage);
                ArmorBreakMoveProcessor.ResolvePending(state, target);
                Debug.Log($"[AttackCurrentRound] {target.UnitId} (本回合行动者) 受到 {dealt} 点伤害");

                GameEvent.Publish(new AttackExecutedEvent
                {
                    AttackerId = ownerId,
                    TargetId = target.UnitId,
                    ActualDamage = dealt,
                    ArmorDamage = target.LastArmorDamage,
                    IsAoe = true,
                    IsPrediction = state.IsPrediction
                });
            }

            state.CheckBattleEnd();
        }

        /// <summary>攻方 buff 修正（与 DamageCommand 同口径：力量加值、虚弱衰减）。</summary>
        private static int ApplyAttackerModifiers(UnitState attacker, int baseDamage)
        {
            if (attacker == null || baseDamage <= 0) return baseDamage;

            float modified = baseDamage;
            var strength = attacker.GetBuff("Strength");
            if (strength != null)
            {
                modified += strength.Value;
            }
            var weak = attacker.GetBuff("Weak");
            if (weak != null)
            {
                modified *= Mathf.Max(0f, 1f - weak.Value / 100f);
            }
            return Mathf.Max(0, Mathf.RoundToInt(modified));
        }

        public int GetPriority()
        {
            return 80;
        }

        public string GetCommandType()
        {
            return "AttackCurrentRound";
        }

        public ICommand Clone()
        {
            return new AttackCurrentRoundCommand(Damage);
        }
    }
}
