using Ashlight.Battle.Core.Data;
using Ashlight.Common.Events;
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

            int actualDamage = target.TakeDamage(Damage);
            Debug.Log($"[DamageCommand] {targetId} 受到 {actualDamage} 点伤害 (护甲吸收: {Damage - actualDamage})");

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

            foreach (var target in targets)
            {
                int actualDamage = target.TakeDamage(Damage);
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
            return new DamageCommand(Damage, IsAoe);
        }
    }
}

