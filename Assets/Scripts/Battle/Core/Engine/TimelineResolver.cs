using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashlight.Battle.Core.Data;
using Ashlight.Battle.Core.Commands;
using Ashlight.Battle.Core.Utils;
using Ashlight.Common.Events;
using UnityEngine;

namespace Ashlight.Battle.Core.Engine
{
    /// <summary>
    /// 时间轴解算器
    /// 无状态类，只负责 Input State -> Output State 的转换
    /// </summary>
    public class TimelineResolver
    {
        private static readonly PriorityComparer _priorityComparer = new PriorityComparer();

        /// <summary>
        /// 动画等待标志
        /// </summary>
        private bool _waitingForAnimation = false;

        /// <summary>
        /// 解算单个时间格
        /// </summary>
        /// <param name="state">战场状态快照（会被修改）</param>
        /// <param name="timeIndex">时间索引（0-14）</param>
        public void ResolveStep(BattleStateSnapshot state, int timeIndex)
        {
            if (state == null)
            {
                Debug.LogError("[TimelineResolver] 状态快照为null");
                return;
            }

            if (timeIndex < 0 || timeIndex >= TimelineTrack.TrackLength)
            {
                Debug.LogWarning($"[TimelineResolver] 时间索引超出范围: {timeIndex}");
                return;
            }

            if (state.IsBattleEnded)
            {
                Debug.Log("[TimelineResolver] 战斗已结束，停止解算");
                return;
            }

            Debug.Log($"[TimelineResolver] ======== 解算时间格 {timeIndex} ========");

            // 1. 收集所有单位在timeIndex的Blocks
            List<TimelineBlock> blocksToExecute = CollectBlocks(state, timeIndex);

            if (blocksToExecute.Count == 0)
            {
                Debug.Log($"[TimelineResolver] 时间格 {timeIndex} 无任何Block");
            }
            else
            {
                // 2. 按优先级排序
                blocksToExecute.Sort(_priorityComparer);

                // 3. 依次执行每个Block的Commands
                ExecuteBlocks(state, blocksToExecute);
            }

            // 4. 处理回合末逻辑
            ProcessEndOfTurn(state);

            // 5. 更新时间指针
            state.CurrentTimeIndex = timeIndex + 1;

            Debug.Log($"[TimelineResolver] ======== 时间格 {timeIndex} 解算完成 ========\n");
        }

        /// <summary>
        /// 收集所有单位在指定时间格的Blocks
        /// </summary>
        private List<TimelineBlock> CollectBlocks(BattleStateSnapshot state, int timeIndex)
        {
            var blocks = new List<TimelineBlock>();

            Debug.Log($"[TimelineResolver] ========== 【收集Blocks】时间索引: {timeIndex} ==========");
            Debug.Log($"[TimelineResolver] ⚠️ 注意：只应该收集index={timeIndex}的blocks，其他索引的blocks不应该被执行！");

            // 1. 收集玩家单位的 Blocks（每个玩家有独立时间轴）
            foreach (var unit in state.GetAllUnits())
            {
                if (unit.IsDead)
                {
                    continue; // 跳过已死亡单位
                }

                // 玩家单位有独立时间轴，敌人单位的 Track 为 null
                if (unit.Track != null)
                {
                    var block = unit.Track.GetBlock(timeIndex);
                    if (block != null && !block.IsEmpty())
                    {
                        // 检查block的实际位置（通过SourceCardId查找）
                        Debug.Log($"[TimelineResolver] ✅ 收集玩家Block: {unit.UnitId} - CardId: {block.SourceCardId}, Phase: {block.Phase}, Priority: {block.Priority}, TimeIndex: {timeIndex}");
                        
                        // 验证：只有Phase为Active的block才应该执行Commands
                        if (block.Phase != PhaseEnum.Active)
                        {
                            Debug.LogWarning($"[TimelineResolver] ⚠️ Block的Phase不是Active ({block.Phase})，不会执行Commands，但会被收集");
                        }
                        
                        blocks.Add(block);
                    }
                    else
                    {
                        // 检查其他索引是否有block（不应该被执行）
                        for (int i = 0; i < TimelineTrack.TrackLength; i++)
                        {
                            if (i == timeIndex) continue;
                            var otherBlock = unit.Track.GetBlock(i);
                            if (otherBlock != null && !otherBlock.IsEmpty())
                            {
                                Debug.Log($"[TimelineResolver] ℹ️ 发现其他索引的Block: {unit.UnitId} - CardId: {otherBlock.SourceCardId}, Index: {i} (不会被收集，正确)");
                            }
                        }
                    }
                }
            }

            // 2. 收集敌人共享时间轴的 Block
            if (state.SharedEnemyTrack != null)
            {
                var enemyBlock = state.SharedEnemyTrack.GetBlock(timeIndex);
                if (enemyBlock != null && !enemyBlock.IsEmpty())
                {
                    Debug.Log($"[TimelineResolver] ✅ 收集敌人共享Block: CardId: {enemyBlock.SourceCardId}, Phase: {enemyBlock.Phase}, Priority: {enemyBlock.Priority}, TimeIndex: {timeIndex}");
                    
                    if (enemyBlock.Phase != PhaseEnum.Active)
                    {
                        Debug.LogWarning($"[TimelineResolver] ⚠️ 敌人Block的Phase不是Active ({enemyBlock.Phase})，不会执行Commands，但会被收集");
                    }
                    
                    blocks.Add(enemyBlock);
                }
            }

            Debug.Log($"[TimelineResolver] ========== 【收集完成】共收集 {blocks.Count} 个Blocks ==========");
            return blocks;
        }

        /// <summary>
        /// 按优先级执行所有Blocks
        /// </summary>
        private void ExecuteBlocks(BattleStateSnapshot state, List<TimelineBlock> blocks)
        {
            foreach (var block in blocks)
            {
                // 检查执行者是否还存活
                var owner = state.GetUnitById(block.OwnerId);
                if (owner == null || owner.IsDead)
                {
                    Debug.Log($"[TimelineResolver] 跳过死亡单位的Block: {block.OwnerId}");
                    continue;
                }

                // 只在Active阶段执行Commands
                if (block.Phase == PhaseEnum.Active)
                {
                    Debug.Log($"[TimelineResolver] 执行Block: {block.OwnerId} -> {block.TargetId} ({block.Commands.Count} commands)");
                    
                    foreach (var command in block.Commands)
                    {
                        if (command == null) continue;

                        Debug.Log($"[TimelineResolver]   执行Command: {command.GetCommandType()}");
                        command.Execute(state, block.OwnerId, block.TargetId);

                        // 如果战斗已结束，停止执行
                        if (state.IsBattleEnded)
                        {
                            Debug.Log("[TimelineResolver] 战斗结束，停止执行后续Commands");
                            return;
                        }
                    }
                }
                else
                {
                    Debug.Log($"[TimelineResolver] Block处于 {block.Phase} 阶段，不执行Commands");
                }
            }
        }

        /// <summary>
        /// 回合末处理
        /// </summary>
        private void ProcessEndOfTurn(BattleStateSnapshot state)
        {
            Debug.Log("[TimelineResolver] 处理回合末逻辑");

            foreach (var unit in state.GetAllUnits())
            {
                if (unit.IsDead)
                {
                    continue;
                }

                // 清空护甲（每回合结束后护甲重置）
                if (unit.Defense > 0)
                {
                    Debug.Log($"[TimelineResolver] {unit.UnitId} 护甲清零: {unit.Defense} -> 0");
                    unit.Defense = 0;
                }

                // 更新Buff状态（减少持续时间，移除过期Buff）
                int buffCountBefore = unit.Buffs.Count;
                unit.UpdateBuffs();
                if (unit.Buffs.Count < buffCountBefore)
                {
                    Debug.Log($"[TimelineResolver] {unit.UnitId} 移除过期Buff: {buffCountBefore} -> {unit.Buffs.Count}");
                }
            }

            // 再次检查战斗是否结束
            state.CheckBattleEnd();
        }

        /// <summary>
        /// 解算完整时间轴（0-14所有格子）
        /// </summary>
        public void ResolveFullTimeline(BattleStateSnapshot state)
        {
            Debug.Log("[TimelineResolver] 开始解算完整时间轴");

            for (int i = 0; i < TimelineTrack.TrackLength; i++)
            {
                ResolveStep(state, i);

                if (state.IsBattleEnded)
                {
                    Debug.Log($"[TimelineResolver] 战斗在时间格 {i} 结束");
                    break;
                }
            }

            Debug.Log("[TimelineResolver] 完整时间轴解算完成");
        }

        /// <summary>
        /// 解算完整时间轴（协程版本，支持动画等待）
        /// </summary>
        public IEnumerator ResolveFullTimelineCoroutine(BattleStateSnapshot state)
        {
            Debug.Log("[TimelineResolver] 开始解算完整时间轴（协程模式）");

            for (int i = 0; i < TimelineTrack.TrackLength; i++)
            {
                yield return ResolveStepCoroutine(state, i);

                if (state.IsBattleEnded)
                {
                    Debug.Log($"[TimelineResolver] 战斗在时间格 {i} 结束");
                    break;
                }
            }

            Debug.Log("[TimelineResolver] 完整时间轴解算完成（协程模式）");
        }

        /// <summary>
        /// 解算单个时间格（协程版本）
        /// </summary>
        public IEnumerator ResolveStepCoroutine(BattleStateSnapshot state, int timeIndex)
        {
            if (state == null)
            {
                Debug.LogError("[TimelineResolver] 状态快照为null");
                yield break;
            }

            if (timeIndex < 0 || timeIndex >= TimelineTrack.TrackLength)
            {
                Debug.LogWarning($"[TimelineResolver] 时间索引超出范围: {timeIndex}");
                yield break;
            }

            if (state.IsBattleEnded)
            {
                Debug.Log("[TimelineResolver] 战斗已结束，停止解算");
                yield break;
            }

            Debug.Log($"[TimelineResolver] ======== 解算时间格 {timeIndex} ========");

            // 1. 收集所有单位在timeIndex的Blocks
            List<TimelineBlock> blocksToExecute = CollectBlocks(state, timeIndex);

            if (blocksToExecute.Count == 0)
            {
                Debug.Log($"[TimelineResolver] 时间格 {timeIndex} 无任何Block");
            }
            else
            {
                // 2. 按优先级排序
                blocksToExecute.Sort(_priorityComparer);

                // 3. 依次执行每个Block的Commands（协程版本）
                yield return ExecuteBlocksCoroutine(state, blocksToExecute);
            }

            // 4. 处理回合末逻辑
            ProcessEndOfTurn(state);

            // 5. 更新时间指针
            state.CurrentTimeIndex = timeIndex + 1;

            Debug.Log($"[TimelineResolver] ======== 时间格 {timeIndex} 解算完成 ========\n");
        }

        /// <summary>
        /// 按优先级执行所有Blocks（协程版本，等待动画）
        /// </summary>
        private IEnumerator ExecuteBlocksCoroutine(BattleStateSnapshot state, List<TimelineBlock> blocks)
        {
            foreach (var block in blocks)
            {
                // 检查执行者是否还存活
                var owner = state.GetUnitById(block.OwnerId);
                if (owner == null || owner.IsDead)
                {
                    Debug.Log($"[TimelineResolver] 跳过死亡单位的Block: {block.OwnerId}");
                    continue;
                }

                // 只在Active阶段执行Commands
                Debug.Log($"[TimelineResolver] 检查Block: {block.OwnerId} -> {block.TargetId}, CardId: {block.SourceCardId}, Phase: {block.Phase}, Commands数量: {block.Commands.Count}");
                
                if (block.Phase != PhaseEnum.Active)
                {
                    Debug.Log($"[TimelineResolver] ⏭️ 跳过Block（Phase不是Active）: {block.OwnerId} -> {block.TargetId}, Phase: {block.Phase}");
                    continue;
                }
                
                if (block.Commands.Count == 0)
                {
                    Debug.Log($"[TimelineResolver] ⏭️ 跳过Block（没有Commands）: {block.OwnerId} -> {block.TargetId}");
                    continue;
                }
                
                Debug.Log($"[TimelineResolver] ✅ 执行Block: {block.OwnerId} -> {block.TargetId}, CardId: {block.SourceCardId} ({block.Commands.Count} commands)");

                // 判断是否包含攻击效果（DamageCommand）
                bool isAttackCard = block.Commands.Any(c => c is DamageCommand);

                // 发布卡片执行事件（触发战斗演出动画）
                if (!state.IsPrediction)
                {
                    GameEvent.Publish(new CardExecutedEvent
                    {
                        CasterId = block.OwnerId,
                        TargetId = block.TargetId,
                        CardId = block.SourceCardId,
                        IsAttackCard = isAttackCard,
                        IsPrediction = state.IsPrediction
                    });

                    // 标记等待动画（放到执行指令之后等待）
                    _waitingForAnimation = true;
                }

                // 执行所有Commands
                foreach (var command in block.Commands)
                {
                    if (command == null) continue;

                    Debug.Log($"[TimelineResolver]   执行Command: {command.GetCommandType()}");

                    // 执行指令
                    command.Execute(state, block.OwnerId, block.TargetId);

                    // 如果战斗已结束，停止执行
                    if (state.IsBattleEnded)
                    {
                        Debug.Log("[TimelineResolver] 战斗结束，停止执行后续Commands");
                        yield break;
                    }
                }

                // 等待战斗演出动画完成
                if (!state.IsPrediction)
                {
                    float timeout = 0f;
                    const float MAX_WAIT = 3f; // 3秒超时保护

                    Debug.Log("[TimelineResolver] 等待战斗演出动画完成...");
                    while (_waitingForAnimation && timeout < MAX_WAIT)
                    {
                        yield return null;
                        timeout += Time.deltaTime;
                    }

                    if (timeout >= MAX_WAIT)
                    {
                        Debug.LogWarning("[TimelineResolver] 战斗演出动画等待超时");
                    }
                    else
                    {
                        Debug.Log("[TimelineResolver] 战斗演出动画完成");
                    }
                }
            }
        }

        /// <summary>
        /// 标记动画完成（由UI层调用）
        /// </summary>
        public void SignalAnimationComplete()
        {
            _waitingForAnimation = false;
            Debug.Log("[TimelineResolver] 收到动画完成信号");
        }
    }
}

