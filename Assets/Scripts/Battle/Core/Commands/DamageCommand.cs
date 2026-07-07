using Ashlight.Battle.Core.Data;
using Ashlight.Common.Events;
using cfg;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 伤害指令
    /// 对应AttackEffect
    /// </summary>
    public class DamageCommand : ICommand
    {
        /// <summary>
        /// 基础伤害值
        /// </summary>
        public int Damage { get; set; }

        /// <summary>
        /// 是否为群体伤害（AOE）
        /// </summary>
        public bool IsAoe { get; set; }

        /// <summary>
        /// AOE 的目标分区限制（仅在 <see cref="IsAoe"/> 时生效）。
        /// 默认 Any = 打目标阵营全体（玩家卡牌沿用旧行为）；敌人技能会按 skill 的 TargetZone 传入 Front/Back 以只扫某区。
        /// </summary>
        public TargetZoneEnum TargetZone { get; set; } = TargetZoneEnum.Any;

        public DamageCommand(int damage, bool isAoe = false)
        {
            Damage = damage;
            IsAoe = isAoe;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var owner = state.GetUnitById(ownerId);
            if (owner == null || owner.IsDead)
            {
                Debug.LogWarning($"[DamageCommand] 执行者不存在或已死亡: {ownerId}");
                return;
            }

            if (IsAoe)
            {
                // 群体攻击：对目标阵营所有存活单位造成伤害
                ExecuteAoeDamage(state, owner);
            }
            else
            {
                // 单体攻击
                ExecuteSingleDamage(state, ownerId, targetId);
            }
        }

        private void ExecuteSingleDamage(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var target = state.GetUnitById(targetId);
            if (target == null)
            {
                Debug.LogWarning($"[DamageCommand] 目标不存在: {targetId}");
                return;
            }

            if (target.IsDead)
            {
                Debug.Log($"[DamageCommand] 目标已死亡，跳过: {targetId}");
                return;
            }

            var attacker = state.GetUnitById(ownerId);
            int adjustedDamage = ApplyAttackerModifiers(attacker, Damage);
            int actualDamage = target.TakeDamage(adjustedDamage);
            Debug.Log($"[DamageCommand] {targetId} 受到 {actualDamage} 点伤害 (基础: {Damage}, 攻方修正后: {adjustedDamage})");

            // 发布攻击执行事件，用于触发动画
            GameEvent.Publish(new AttackExecutedEvent
            {
                AttackerId = ownerId,
                TargetId = targetId,
                ActualDamage = actualDamage,
                IsAoe = false,
                IsPrediction = state.IsPrediction
            });

            // 检查战斗是否结束
            state.CheckBattleEnd();
        }

        private void ExecuteAoeDamage(BattleStateSnapshot state, UnitState owner)
        {
            // 确定目标阵营（攻击对立阵营）
            var targets = owner.IsPlayerUnit
                ? state.GetAliveEnemyUnits()
                : state.GetAlivePlayerUnits();

            // 分区过滤：AllEnemy+Front 只扫前区，+Back 只扫后区；Any 不过滤。
            // strict：目标区空排时只打区内剩余单位、不回退全体（与「空排 miss」口径一致）。
            targets = ZoneTargeting.FilterByZone(state, targets, TargetZone, strict: true);

            int adjustedDamage = ApplyAttackerModifiers(owner, Damage);

            foreach (var target in targets)
            {
                int actualDamage = target.TakeDamage(adjustedDamage);
                Debug.Log($"[DamageCommand] AOE: {target.UnitId} 受到 {actualDamage} 点伤害");

                // 发布攻击执行事件（每个目标单独发布）
                GameEvent.Publish(new AttackExecutedEvent
                {
                    AttackerId = owner.UnitId,
                    TargetId = target.UnitId,
                    ActualDamage = actualDamage,
                    IsAoe = true,
                    IsPrediction = state.IsPrediction
                });
            }

            // 检查战斗是否结束
            state.CheckBattleEnd();
        }

        /// <summary>
        /// 攻方 buff 修正：Strength +V 加值，Weak -V% 衰减
        /// </summary>
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
            return 80; // Attack优先级
        }

        public string GetCommandType()
        {
            return IsAoe ? "DamageAOE" : "Damage";
        }

        public ICommand Clone()
        {
            return new DamageCommand(Damage, IsAoe) { TargetZone = TargetZone };
        }
    }
}

