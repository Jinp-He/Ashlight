using Ashlight.Battle.Core.Data;
using Ashlight.Battle.Core.Engine;
using Ashlight.Common.Events;
using cfg;
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

        public bool IsAoe { get; set; }

        public TargetZoneEnum TargetZone { get; set; } = TargetZoneEnum.Any;

        public AttackExtraCommand(
            int damage,
            string conditions,
            float multiplier,
            bool isAoe = false,
            TargetZoneEnum targetZone = TargetZoneEnum.Any)
        {
            Damage = damage;
            Conditions = conditions;
            Multiplier = multiplier;
            IsAoe = isAoe;
            TargetZone = targetZone;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var owner = state.GetUnitById(ownerId);
            if (owner == null || owner.IsDead)
            {
                Debug.LogWarning($"[AttackExtraCommand] 执行者不存在或已死亡: {ownerId}");
                return;
            }

            state.LastDamageHitCount = 0;
            if (IsAoe)
            {
                ExecuteAoe(state, owner);
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

            state.LastDamageHitCount = 1;
            int damageDealt = DealDamage(state, owner, target, out bool conditionMet);
            
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

        private void ExecuteAoe(BattleStateSnapshot state, UnitState owner)
        {
            var targets = owner.IsPlayerUnit
                ? state.GetAliveEnemyUnits()
                : state.GetAlivePlayerUnits();
            targets = ZoneTargeting.FilterByZone(state, targets, TargetZone, strict: true);
            state.LastDamageHitCount = targets.Count;

            foreach (var target in targets)
            {
                int damageDealt = DealDamage(state, owner, target, out bool conditionMet);
                Debug.Log($"[AttackExtraCommand] AOE {target.UnitId}: 条件={(conditionMet ? "满足" : "不满足")}, 伤害={damageDealt}");
                GameEvent.Publish(new AttackExecutedEvent
                {
                    AttackerId = owner.UnitId,
                    TargetId = target.UnitId,
                    ActualDamage = damageDealt,
                    IsAoe = true,
                    IsPrediction = state.IsPrediction
                });
            }

            state.CheckBattleEnd();
        }

        private int DealDamage(BattleStateSnapshot state, UnitState owner, UnitState target, out bool conditionMet)
        {
            conditionMet = CheckConditions(state, target);
            int baseDamage = conditionMet ? Mathf.RoundToInt(Damage * Multiplier) : Damage;
            int adjustedDamage = DamageCommand.ApplyAttackerModifiers(owner, baseDamage);
            int dealt = target.TakeDamage(adjustedDamage);
            ArmorBreakMoveProcessor.ResolvePending(state, target);
            return dealt;
        }

        /// <summary>
        /// 检查目标是否满足触发条件。
        /// 【公共回合口径】Channeling/Channel/Recoil 都视为「处于执行中」= 目标本回合将行动；
        /// 同名 buff 真实存在时也算满足（兼容将来真的贴 引导/僵直 buff 的设计）。
        /// 旧时间轴阶段检查保留为兜底。
        /// </summary>
        private bool CheckConditions(BattleStateSnapshot state, UnitState target)
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

                if (target.HasBuff(trimmedCondition))
                {
                    return true;
                }

                if (trimmedCondition == "Channeling" || trimmedCondition == "Channel" || trimmedCondition == "Recoil")
                {
                    if (AttackConditionalCommand.IsActingThisRound(state, target))
                    {
                        return true;
                    }
                    var legacyPhase = trimmedCondition == "Recoil" ? PhaseEnum.Recoil : PhaseEnum.Startup;
                    if (HasPhaseInTimeline(target, legacyPhase))
                    {
                        return true;
                    }
                }
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
            return new AttackExtraCommand(Damage, Conditions, Multiplier, IsAoe, TargetZone);
        }
    }
}
