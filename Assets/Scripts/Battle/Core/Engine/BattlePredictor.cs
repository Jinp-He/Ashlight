using Ashlight.Battle.Core.Data;
using cfg.Character;
using UnityEngine;

namespace Ashlight.Battle.Core.Engine
{
    /// <summary>
    /// 战斗预测器
    /// 用于模拟卡牌效果，预测战斗结果
    /// </summary>
    public class BattlePredictor
    {
        private readonly TimelineResolver _resolver;
        private readonly CardToTimelineConverter _converter;

        public BattlePredictor()
        {
            _resolver = new TimelineResolver();
            _converter = new CardToTimelineConverter();
        }

        /// <summary>
        /// 模拟卡牌使用，预测战斗结果
        /// </summary>
        /// <param name="currentState">当前战场状态（不会被修改）</param>
        /// <param name="card">要使用的卡牌</param>
        /// <param name="ownerId">使用卡牌的单位ID</param>
        /// <param name="targetId">目标单位ID</param>
        /// <param name="insertSlot">插入到时间轴的位置（0-14）</param>
        /// <returns>预测结果</returns>
        public PredictionResult Simulate(BattleStateSnapshot currentState, CardInfo card, string ownerId, string targetId, int insertSlot)
        {
            if (currentState == null)
            {
                Debug.LogError("[BattlePredictor] 状态快照为null");
                return new PredictionResult();
            }

            if (card == null)
            {
                Debug.LogError("[BattlePredictor] 卡牌配置为null");
                return new PredictionResult();
            }

            Debug.Log($"[BattlePredictor] ======== 开始预测模拟 ========");
            Debug.Log($"[BattlePredictor] 卡牌: {card.Name}, 使用者: {ownerId}, 目标: {targetId}, 插入位置: {insertSlot}");

            // 1. Clone: 创建当前状态的深拷贝
            BattleStateSnapshot virtualState = currentState.Clone();
            Debug.Log($"[BattlePredictor] 已创建虚拟状态副本");

            // 记录初始HP（用于计算变化量）
            var initialHpMap = RecordInitialHp(virtualState);

            // 2. Convert: 将卡牌转换为TimelineBlocks
            var blocks = _converter.ConvertCard(card, ownerId, targetId);
            Debug.Log($"[BattlePredictor] 卡牌转换为 {blocks.Count} 个TimelineBlocks");

            // 3. Inject: 插入Blocks到虚拟状态的时间轴
            InjectBlocks(virtualState, ownerId, blocks, insertSlot);
            Debug.Log($"[BattlePredictor] 已插入Blocks到时间轴");

            // 4. Fast-Forward: 解算完整时间轴
            _resolver.ResolveFullTimeline(virtualState);
            Debug.Log($"[BattlePredictor] 完整时间轴解算完成");

            // 5. Return: 创建预测结果
            PredictionResult result = CreatePredictionResult(virtualState, initialHpMap);

            Debug.Log($"[BattlePredictor] ======== 预测模拟完成 ========\n");
            Debug.Log(result.GetSummary());

            return result;
        }

        /// <summary>
        /// 记录初始HP（用于计算变化量）
        /// </summary>
        private System.Collections.Generic.Dictionary<string, int> RecordInitialHp(BattleStateSnapshot state)
        {
            var hpMap = new System.Collections.Generic.Dictionary<string, int>();

            foreach (var unit in state.GetAllUnits())
            {
                hpMap[unit.UnitId] = unit.CurrentHp;
            }

            return hpMap;
        }

        /// <summary>
        /// 插入Blocks到指定单位的时间轴
        /// </summary>
        private void InjectBlocks(BattleStateSnapshot state, string ownerId, System.Collections.Generic.List<TimelineBlock> blocks, int startSlot)
        {
            var owner = state.GetUnitById(ownerId);
            if (owner == null)
            {
                Debug.LogError($"[BattlePredictor] 找不到单位: {ownerId}");
                return;
            }

            if (startSlot < 0 || startSlot >= TimelineTrack.TrackLength)
            {
                Debug.LogError($"[BattlePredictor] 插入位置超出范围: {startSlot}");
                return;
            }

            // 逐个插入Blocks
            for (int i = 0; i < blocks.Count; i++)
            {
                int targetSlot = startSlot + i;

                if (targetSlot >= TimelineTrack.TrackLength)
                {
                    Debug.LogWarning($"[BattlePredictor] Block索引 {targetSlot} 超出时间轴范围，跳过");
                    break;
                }

                owner.Track.SetBlock(targetSlot, blocks[i]);
            }

            Debug.Log($"[BattlePredictor] 已插入 {blocks.Count} 个Blocks到 {ownerId} 的时间轴（起始位置: {startSlot}）");
        }

        /// <summary>
        /// 创建预测结果
        /// </summary>
        private PredictionResult CreatePredictionResult(BattleStateSnapshot virtualState, System.Collections.Generic.Dictionary<string, int> initialHpMap)
        {
            var result = new PredictionResult
            {
                IsBattleEnded = virtualState.IsBattleEnded,
                IsPlayerVictory = virtualState.IsPlayerVictory
            };

            foreach (var unit in virtualState.GetAllUnits())
            {
                result.FinalHpMap[unit.UnitId] = unit.CurrentHp;

                // 计算HP变化量
                if (initialHpMap.ContainsKey(unit.UnitId))
                {
                    int hpChange = unit.CurrentHp - initialHpMap[unit.UnitId];
                    result.HpChangeMap[unit.UnitId] = hpChange;
                }

                // 记录死亡单位
                if (unit.IsDead)
                {
                    result.DeadUnits.Add(unit.UnitId);
                }
            }

            return result;
        }
    }
}

