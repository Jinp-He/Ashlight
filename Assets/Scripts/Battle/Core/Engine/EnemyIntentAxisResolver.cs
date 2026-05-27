using System.Collections.Generic;
using System.Linq;
using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Engine
{
    /// <summary>
    /// 敌人意图轴/执行轴推进解算器
    /// 负责管理敌人从意图公示 → 意图轴倒计时 → 执行轴 → 效果生效的完整流程
    /// 无状态类，所有数据存储在 UnitState 中
    /// </summary>
    public class EnemyIntentAxisResolver
    {
        /// <summary>
        /// 敌人进入意图轴：设置阶段、填充轴长度、存储待执行技能
        /// </summary>
        /// <param name="enemy">敌人单位状态</param>
        /// <param name="skillId">选定技能ID</param>
        /// <param name="targetId">技能目标ID</param>
        /// <param name="intentAxisLength">意图轴长度（格数，来自 EnemySkillInfo.ExecutingCost）</param>
        /// <param name="executeAxisLength">执行轴长度（格数，默认1）</param>
        public void StartIntentAxis(UnitState enemy, string skillId, string targetId, int intentAxisLength, int executeAxisLength = 1)
        {
            if (enemy == null || enemy.IsDead || enemy.IsPlayerUnit)
            {
                return;
            }

            enemy.CurrentPhase = EnemyPhase.IntentAxis;
            enemy.IntentAxisLength = Mathf.Max(1, intentAxisLength);
            enemy.IntentAxisProgress = 0;
            enemy.ExecuteAxisLength = Mathf.Max(0, executeAxisLength);
            enemy.ExecuteAxisProgress = 0;
            enemy.PendingSkillId = skillId;
            enemy.PendingTargetId = targetId;

            Debug.Log($"[EnemyIntentAxisResolver] {enemy.UnitId} 进入意图轴: 技能={skillId}, 目标={targetId}, 意图轴长度={enemy.IntentAxisLength}, 执行轴长度={enemy.ExecuteAxisLength}");
        }

        /// <summary>
        /// 每 tick 推进所有敌人的意图轴/执行轴
        /// 全场暂停或硬直状态下跳过推进
        /// </summary>
        /// <param name="state">战场快照</param>
        /// <returns>本 tick 内完成执行轴、需要触发技能效果的敌人列表</returns>
        public List<UnitState> AdvanceEnemyAxes(BattleStateSnapshot state)
        {
            var completedEnemies = new List<UnitState>();

            if (state == null || state.IsBattleEnded || state.IsGlobalPaused)
            {
                return completedEnemies;
            }

            foreach (var enemy in state.EnemyUnits)
            {
                if (enemy.IsDead || enemy.CurrentPhase == EnemyPhase.None)
                {
                    continue;
                }

                // 硬直状态：递减计时器，不推进轴
                if (enemy.IsStunned)
                {
                    enemy.StunRemainingTicks--;
                    if (enemy.StunRemainingTicks <= 0)
                    {
                        enemy.IsStunned = false;
                        enemy.StunRemainingTicks = 0;
                        Debug.Log($"[EnemyIntentAxisResolver] {enemy.UnitId} 硬直解除");
                    }
                    continue;
                }

                // 推进当前阶段
                if (enemy.CurrentPhase == EnemyPhase.IntentAxis)
                {
                    enemy.IntentAxisProgress++;
                    if (enemy.IntentAxisProgress >= enemy.IntentAxisLength)
                    {
                        // 意图轴结束 → 进入执行轴
                        TransitionToExecuteAxis(enemy);
                    }
                }
                else if (enemy.CurrentPhase == EnemyPhase.ExecuteAxis)
                {
                    enemy.ExecuteAxisProgress++;
                    if (enemy.ExecuteAxisProgress >= enemy.ExecuteAxisLength)
                    {
                        // 执行轴结束 → 准备触发效果
                        completedEnemies.Add(enemy);
                    }
                }
            }

            return completedEnemies;
        }

        /// <summary>
        /// 意图轴结束，转换到执行轴
        /// </summary>
        private void TransitionToExecuteAxis(UnitState enemy)
        {
            enemy.CurrentPhase = EnemyPhase.ExecuteAxis;
            enemy.ExecuteAxisProgress = 0;
            Debug.Log($"[EnemyIntentAxisResolver] {enemy.UnitId} 意图轴结束，进入执行轴 (长度={enemy.ExecuteAxisLength})");
        }

        /// <summary>
        /// 重置敌人阶段到 None，清除待执行数据
        /// 在技能效果执行完毕后调用
        /// </summary>
        public void ResetPhase(UnitState enemy)
        {
            if (enemy == null)
            {
                return;
            }

            enemy.CurrentPhase = EnemyPhase.None;
            enemy.IntentAxisLength = 0;
            enemy.IntentAxisProgress = 0;
            enemy.ExecuteAxisLength = 1;
            enemy.ExecuteAxisProgress = 0;
            enemy.PendingSkillId = null;
            enemy.PendingTargetId = null;
            enemy.IsStunned = false;
            enemy.StunRemainingTicks = 0;

            Debug.Log($"[EnemyIntentAxisResolver] {enemy.UnitId} 阶段重置为 None");
        }

        /// <summary>
        /// 打断敌人意图（仅在意图轴阶段有效）
        /// 清除意图，重置阶段
        /// </summary>
        /// <returns>是否成功打断</returns>
        public bool TryInterrupt(UnitState enemy)
        {
            if (enemy == null || enemy.IsDead || enemy.CurrentPhase != EnemyPhase.IntentAxis)
            {
                return false;
            }

            Debug.Log($"[EnemyIntentAxisResolver] {enemy.UnitId} 被打断！技能={enemy.PendingSkillId}");
            ResetPhase(enemy);
            return true;
        }

        /// <summary>
        /// 对敌人施加硬直（意图轴/执行轴均可）
        /// </summary>
        public bool TryApplyStun(UnitState enemy, int stunTicks)
        {
            if (enemy == null || enemy.IsDead || enemy.CurrentPhase == EnemyPhase.None)
            {
                return false;
            }

            enemy.IsStunned = true;
            enemy.StunRemainingTicks = Mathf.Max(1, stunTicks);
            Debug.Log($"[EnemyIntentAxisResolver] {enemy.UnitId} 被硬直 {stunTicks} tick");
            return true;
        }

        /// <summary>
        /// 延后敌人意图轴进度（意图轴阶段有效，执行轴阶段作用于执行轴进度）
        /// </summary>
        /// <param name="enemy">目标敌人</param>
        /// <param name="delayAmount">延后格数（正值）</param>
        /// <returns>是否成功延后</returns>
        public bool TryDelay(UnitState enemy, int delayAmount)
        {
            if (enemy == null || enemy.IsDead || enemy.CurrentPhase == EnemyPhase.None || delayAmount <= 0)
            {
                return false;
            }

            if (enemy.CurrentPhase == EnemyPhase.IntentAxis)
            {
                enemy.IntentAxisProgress = Mathf.Max(0, enemy.IntentAxisProgress - delayAmount);
                Debug.Log($"[EnemyIntentAxisResolver] {enemy.UnitId} 意图轴延后 {delayAmount} 格, 当前进度={enemy.IntentAxisProgress}/{enemy.IntentAxisLength}");
            }
            else if (enemy.CurrentPhase == EnemyPhase.ExecuteAxis)
            {
                enemy.ExecuteAxisProgress = Mathf.Max(0, enemy.ExecuteAxisProgress - delayAmount);
                Debug.Log($"[EnemyIntentAxisResolver] {enemy.UnitId} 执行轴延后 {delayAmount} 格, 当前进度={enemy.ExecuteAxisProgress}/{enemy.ExecuteAxisLength}");
            }

            return true;
        }
    }
}
