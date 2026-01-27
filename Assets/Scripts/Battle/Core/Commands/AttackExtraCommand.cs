using Ashlight.Battle.Core.Data;
using Ashlight.Common.Events;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 额外伤害指令
    /// 对应AttackExtraEffect
    /// 根据目标状态（如Recoil、Channel）触发额外伤害
    /// </summary>
    public class AttackExtraCommand : ICommand
    {
        /// <summary>
        /// 基础伤害值
        /// </summary>
        public int Damage { get; set; }

        /// <summary>
        /// 触发条件（如"Recoil|Channel"，用|分隔多个条件）
        /// </summary>
        public string Conditions { get; set; }

        /// <summary>
        /// 满足条件时的伤害倍率
        /// </summary>
        public float Multiplier { get; set; }

        public AttackExtraCommand(int damage, string conditions, float multiplier)
        {
            Damage = damage;
            Conditions = conditions;
            Multiplier = multiplier;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var owner = state.GetUnitById(ownerId);
            if (owner == null || owner.IsDead)
            {
                Debug.LogWarning($"[AttackExtraCommand] 执行者不存在或已死亡: {ownerId}");
                return;
            }

            var target = state.GetUnitById(targetId);
            if (target == null)
            {
                Debug.LogWarning($"[AttackExtraCommand] 目标不存在: {targetId}");
                return;
            }

            if (target.IsDead)
            {
                Debug.Log($"[AttackExtraCommand] 目标已死亡，跳过: {targetId}");
                return;
            }

            // 检查目标是否满足条件
            bool conditionMet = CheckConditions(target);
            
            // 计算实际伤害
            int actualDamage = conditionMet ? Mathf.RoundToInt(Damage * Multiplier) : Damage;
            
            int damageDealt = target.TakeDamage(actualDamage);
            
            if (conditionMet)
            {
                Debug.Log($"[AttackExtraCommand] {targetId} 满足条件 [{Conditions}]，受到额外伤害 {damageDealt} 点 (倍率: {Multiplier}x)");
            }
            else
            {
                Debug.Log($"[AttackExtraCommand] {targetId} 不满足条件，受到基础伤害 {damageDealt} 点");
            }

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
        /// 检查目标是否满足触发条件
        /// </summary>
        private bool CheckConditions(UnitState target)
        {
            if (string.IsNullOrEmpty(Conditions))
            {
                return false;
            }

            // 解析条件（用|分隔）
            string[] conditionList = Conditions.Split('|');
            
            foreach (var condition in conditionList)
            {
                string trimmedCondition = condition.Trim();
                
                // 检查目标是否处于指定状态
                if (trimmedCondition == "Recoil")
                {
                    // 检查目标时间轴上是否有Recoil阶段的Block
                    if (HasPhaseInTimeline(target, PhaseEnum.Recoil))
                    {
                        return true;
                    }
                }
                else if (trimmedCondition == "Channel")
                {
                    // 检查目标时间轴上是否有Startup阶段的Block
                    if (HasPhaseInTimeline(target, PhaseEnum.Startup))
                    {
                        return true;
                    }
                }
                // 可以扩展更多条件类型
            }

            return false;
        }

        /// <summary>
        /// 检查目标时间轴上是否有指定阶段的Block
        /// </summary>
        private bool HasPhaseInTimeline(UnitState target, PhaseEnum phase)
        {
            if (target.Track == null)
            {
                return false;
            }

            for (int i = 0; i < TimelineTrack.TrackLength; i++)
            {
                var block = target.Track.GetBlock(i);
                if (block != null && !block.IsEmpty() && block.Phase == phase)
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
            return "AttackExtra";
        }

        public ICommand Clone()
        {
            return new AttackExtraCommand(Damage, Conditions, Multiplier);
        }
    }
}
