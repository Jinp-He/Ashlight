using System.Collections.Generic;
using Ashlight.Battle.Core.Data;
using cfg.Character;
using UnityEngine;

namespace Ashlight.Battle.Core.Engine
{
    /// <summary>
    /// 战斗预测器（ATB 版本）
    /// 使用与真实战斗完全相同的 ATB 解算规则进行预测
    /// 核心要求：预测结果必须与真实执行一致
    /// </summary>
    public class BattlePredictor
    {
        private readonly CardPlayResolver _cardPlayResolver;

        public BattlePredictor()
        {
            _cardPlayResolver = new CardPlayResolver();
        }

        /// <summary>
        /// 模拟单张卡牌效果（ATB 版本）
        /// 卡牌直接结算命令，不再插入时间轴
        /// </summary>
        /// <param name="currentState">当前战场状态（不会被修改）</param>
        /// <param name="card">要使用的卡牌</param>
        /// <param name="ownerId">施法者ID</param>
        /// <param name="targetId">目标ID</param>
        /// <returns>预测结果</returns>
        public PredictionResult SimulateCard(BattleStateSnapshot currentState, CardInfo card, string ownerId, string targetId)
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
            Debug.Log($"[BattlePredictor] 卡牌: {card.Name}, 使用者: {ownerId}, 目标: {targetId}");

            BattleStateSnapshot virtualState = currentState.Clone();
            virtualState.IsPrediction = true;

            var initialSnapshot = RecordSnapshot(virtualState);

            _cardPlayResolver.PlayCard(virtualState, card, ownerId, targetId);

            PredictionResult result = BuildResult(virtualState, initialSnapshot);

            Debug.Log($"[BattlePredictor] ======== 预测模拟完成 ========");
            Debug.Log(result.GetSummary());

            return result;
        }

        /// <summary>
        /// 模拟多张卡牌的组合效果
        /// </summary>
        public PredictionResult SimulateCards(BattleStateSnapshot currentState, List<CardAction> actions)
        {
            if (currentState == null || actions == null || actions.Count == 0)
            {
                return new PredictionResult();
            }

            BattleStateSnapshot virtualState = currentState.Clone();
            virtualState.IsPrediction = true;

            var initialSnapshot = RecordSnapshot(virtualState);

            foreach (var action in actions)
            {
                if (virtualState.IsBattleEnded) break;

                string ownerId = action.OwnerId ?? virtualState.CurrentTurnUnitId;
                if (string.IsNullOrEmpty(ownerId))
                {
                    UnityEngine.Debug.LogWarning("[BattlePredictor] SimulateCards: 无法确定施法者ID，跳过");
                    continue;
                }

                _cardPlayResolver.PlayCard(virtualState, action.CardInfo, ownerId, action.TargetId);
            }

            return BuildResult(virtualState, initialSnapshot);
        }

        /// <summary>
        /// 模拟完整回合效果（包括能量刷新、出牌、回合结束）
        /// </summary>
        public PredictionResult SimulateTurn(BattleStateSnapshot currentState, string unitId, List<CardAction> actions)
        {
            if (currentState == null)
            {
                return new PredictionResult();
            }

            BattleStateSnapshot virtualState = currentState.Clone();
            virtualState.IsPrediction = true;

            var initialSnapshot = RecordSnapshot(virtualState);

            var turnResolver = new TurnResolver();
            turnResolver.ExecuteTurn(virtualState, unitId, actions);

            return BuildResult(virtualState, initialSnapshot);
        }

        /// <summary>
        /// 兼容旧接口：模拟卡牌使用（保留旧签名以减少外部断裂）
        /// insertSlot 参数在 ATB 系统中不再使用
        /// </summary>
        public PredictionResult Simulate(BattleStateSnapshot currentState, CardInfo card, string ownerId, string targetId, int insertSlot)
        {
            return SimulateCard(currentState, card, ownerId, targetId);
        }

        /// <summary>
        /// 记录状态快照（用于计算变化量）
        /// </summary>
        private UnitSnapshot RecordSnapshot(BattleStateSnapshot state)
        {
            var snapshot = new UnitSnapshot();

            foreach (var unit in state.GetAllUnits())
            {
                snapshot.HpMap[unit.UnitId] = unit.CurrentHp;
                snapshot.DefenseMap[unit.UnitId] = unit.Defense;
                snapshot.BuffCountMap[unit.UnitId] = unit.Buffs.Count;
                snapshot.WasAlive[unit.UnitId] = !unit.IsDead;

                if (unit.ActionBar != null)
                {
                    snapshot.SegmentMap[unit.UnitId] = unit.ActionBar.CurrentSegment;
                }

                if (unit.Overload != null)
                {
                    snapshot.OverloadDebtMap[unit.UnitId] = unit.Overload.OverloadDebt;
                }
            }

            return snapshot;
        }

        /// <summary>
        /// 构建预测结果
        /// </summary>
        private PredictionResult BuildResult(BattleStateSnapshot virtualState, UnitSnapshot initial)
        {
            var result = new PredictionResult
            {
                IsBattleEnded = virtualState.IsBattleEnded,
                IsPlayerVictory = virtualState.IsPlayerVictory
            };

            int totalDamage = 0;

            foreach (var unit in virtualState.GetAllUnits())
            {
                string id = unit.UnitId;

                result.FinalHpMap[id] = unit.CurrentHp;

                // HP 变化
                int initialHp = initial.HpMap.ContainsKey(id) ? initial.HpMap[id] : 0;
                int hpChange = unit.CurrentHp - initialHp;
                result.HpChangeMap[id] = hpChange;

                if (hpChange < 0)
                {
                    totalDamage += -hpChange;
                }

                // 护甲变化
                int initialDef = initial.DefenseMap.ContainsKey(id) ? initial.DefenseMap[id] : 0;
                result.BlockChangeMap[id] = unit.Defense - initialDef;

                // 行动条段位变化
                if (unit.ActionBar != null && initial.SegmentMap.ContainsKey(id))
                {
                    int segmentShift = unit.ActionBar.CurrentSegment - initial.SegmentMap[id];
                    if (segmentShift != 0)
                    {
                        result.ExpectedSegmentShift[id] = segmentShift;
                    }

                    result.ExpectedRestartSegment[id] = unit.ActionBar.RestartSegment;
                }

                // 死亡判定
                if (unit.IsDead)
                {
                    result.DeadUnits.Add(id);

                    bool wasAlive = initial.WasAlive.ContainsKey(id) && initial.WasAlive[id];
                    if (wasAlive)
                    {
                        result.WillKillTargets.Add(id);
                    }
                }

                // 过载负债变化
                if (unit.Overload != null && initial.OverloadDebtMap.ContainsKey(id))
                {
                    int debtChange = unit.Overload.OverloadDebt - initial.OverloadDebtMap[id];
                    if (debtChange > 0)
                    {
                        result.ExpectedOverloadDebt += debtChange;
                    }
                }

                // Buff 变化
                if (unit.Buffs.Count > 0)
                {
                    var newBuffs = new List<string>();
                    foreach (var buff in unit.Buffs)
                    {
                        newBuffs.Add(buff.BuffId);
                    }
                    if (newBuffs.Count > 0)
                    {
                        result.BuffChangeMap[id] = newBuffs;
                    }
                }
            }

            result.ExpectedDamage = totalDamage;
            return result;
        }

        /// <summary>
        /// 内部状态快照记录
        /// </summary>
        private class UnitSnapshot
        {
            public Dictionary<string, int> HpMap = new Dictionary<string, int>();
            public Dictionary<string, int> DefenseMap = new Dictionary<string, int>();
            public Dictionary<string, int> BuffCountMap = new Dictionary<string, int>();
            public Dictionary<string, bool> WasAlive = new Dictionary<string, bool>();
            public Dictionary<string, int> SegmentMap = new Dictionary<string, int>();
            public Dictionary<string, int> OverloadDebtMap = new Dictionary<string, int>();
        }
    }
}
