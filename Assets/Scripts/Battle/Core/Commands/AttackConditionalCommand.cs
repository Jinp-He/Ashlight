using Ashlight.Battle.Core.Data;
using Ashlight.Common.Events;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 条件攻击指令
    /// 对应AttackConditionalEffect
    /// 根据特定条件（如目标正在攻击）触发额外伤害
    /// </summary>
    public class AttackConditionalCommand : ICommand
    {
        /// <summary>
        /// 额外伤害数值
        /// </summary>
        public int BonusDamage { get; set; }

        /// <summary>
        /// 触发条件类型（如"IsAttacking"）
        /// </summary>
        public string ConditionType { get; set; }

        public AttackConditionalCommand(int bonusDamage, string conditionType)
        {
            BonusDamage = bonusDamage;
            ConditionType = conditionType;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var owner = state.GetUnitById(ownerId);
            if (owner == null || owner.IsDead)
            {
                Debug.LogWarning($"[AttackConditionalCommand] 执行者不存在或已死亡: {ownerId}");
                return;
            }

            var target = state.GetUnitById(targetId);
            if (target == null)
            {
                Debug.LogWarning($"[AttackConditionalCommand] 目标不存在: {targetId}");
                return;
            }

            if (target.IsDead)
            {
                Debug.Log($"[AttackConditionalCommand] 目标已死亡，跳过: {targetId}");
                return;
            }

            // 检查是否满足条件
            bool conditionMet = CheckCondition(state, target);
            
            if (!conditionMet)
            {
                Debug.Log($"[AttackConditionalCommand] 目标 {targetId} 不满足条件 [{ConditionType}]，不造成额外伤害");
                return;
            }

            // 造成额外伤害
            int damageDealt = target.TakeDamage(BonusDamage);
            Debug.Log($"[AttackConditionalCommand] 目标 {targetId} 满足条件 [{ConditionType}]，受到额外伤害 {damageDealt} 点");

            // 发布攻击执行事件
            GameEvent.Publish(new AttackExecutedEvent
            {
                AttackerId = ownerId,
                TargetId = targetId,
                ActualDamage = damageDealt,
                IsAoe = false,
                IsPrediction = state.IsPrediction
            });

            // 检查战斗是否结束
            state.CheckBattleEnd();
        }

        /// <summary>
        /// 检查是否满足触发条件
        /// </summary>
        private bool CheckCondition(BattleStateSnapshot state, UnitState target)
        {
            if (string.IsNullOrEmpty(ConditionType))
            {
                return false;
            }

            switch (ConditionType)
            {
                case "IsAttacking":
                    // 检查目标时间轴上是否有Active阶段的攻击Block
                    return HasAttackingPhase(target);

                case "IsDefending":
                    // 检查目标是否有护甲
                    return target.Defense > 0;

                case "LowHp":
                    // 检查目标是否低血量（低于50%）
                    return target.CurrentHp < target.MaxHp * 0.5f;

                case "HighHp":
                    // 检查目标是否高血量（高于80%）
                    return target.CurrentHp > target.MaxHp * 0.8f;

                default:
                    Debug.LogWarning($"[AttackConditionalCommand] 未知的条件类型: {ConditionType}");
                    return false;
            }
        }

        /// <summary>
        /// 检查目标是否正在攻击（时间轴上有Active阶段）
        /// </summary>
        private bool HasAttackingPhase(UnitState target)
        {
            if (target.Track == null)
            {
                return false;
            }

            for (int i = 0; i < TimelineTrack.TrackLength; i++)
            {
                var block = target.Track.GetBlock(i);
                if (block != null && !block.IsEmpty() && block.Phase == PhaseEnum.Active)
                {
                    return true;
                }
            }

            return false;
        }

        public int GetPriority()
        {
            return 80; // Attack优先级
        }

        public string GetCommandType()
        {
            return "AttackConditional";
        }

        public ICommand Clone()
        {
            return new AttackConditionalCommand(BonusDamage, ConditionType);
        }
    }
}
