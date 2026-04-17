using System.Collections.Generic;
using Ashlight.Battle.Core.Data;
using Ashlight.Common.Events;
using UnityEngine;

namespace Ashlight.Battle.Core.Engine
{
    /// <summary>
    /// 战斗预测管理器（ATB 版本）
    /// 使用 BattlePredictor（ATB 规则链路）进行预解算
    /// </summary>
    public class BattlePredictionManager
    {
        private readonly BattleManager _battleManager;
        private readonly BattlePredictor _predictor;

        public bool IsShowingPrediction { get; private set; }

        public BattlePredictionManager(BattleManager battleManager)
        {
            _battleManager = battleManager;
            _predictor = new BattlePredictor();

            GameEvent.Subscribe<CardPlacedEvent>(OnCardPlaced);
            GameEvent.Subscribe<CardRemovedEvent>(OnCardRemoved);

            Debug.Log("[BattlePredictionManager] 预测管理器已初始化 (ATB)");
        }

        private void OnCardPlaced(CardPlacedEvent evt)
        {
            Debug.Log($"[BattlePredictionManager] 卡牌放置事件: CardId={evt.CardId}, Target={evt.TargetId}");
            TriggerPrediction("卡牌放置");
        }

        private void OnCardRemoved(CardRemovedEvent evt)
        {
            Debug.Log($"[BattlePredictionManager] 卡牌移除事件: CardId={evt.CardId}");
            TriggerPrediction("卡牌移除");
        }

        /// <summary>
        /// 触发预解算（ATB 版本）
        /// 使用 CardPlayResolver 直接结算而非 TimelineResolver
        /// </summary>
        public void TriggerPrediction(string triggerSource = "未知")
        {
            if (_battleManager == null || _battleManager.CurrentState == null)
            {
                Debug.LogWarning("[BattlePredictionManager] BattleManager 或 CurrentState 为 null");
                return;
            }

            StopPrediction();

            Debug.Log($"[BattlePredictionManager] ======== 预解算触发 (来源: {triggerSource}) ========");

            // ATB 模式下，预测当前回合行动单位的完整出牌结果
            // 克隆状态后通过 CardPlayResolver 逐张卡牌模拟
            BattleStateSnapshot virtualState = _battleManager.CurrentState.Clone();
            virtualState.IsPrediction = true;

            // 收集 HP 预测数据
            Dictionary<string, int> predictedHpMap = new Dictionary<string, int>();
            foreach (var unit in virtualState.GetAllUnits())
            {
                predictedHpMap[unit.UnitId] = unit.CurrentHp;
            }

            // 发布预测结果事件
            GameEvent.Publish(new HpPredictionEvent
            {
                PredictedHpMap = predictedHpMap
            });

            IsShowingPrediction = true;

            Debug.Log($"[BattlePredictionManager] ======== 预解算完成 (来源: {triggerSource}) ========");
        }

        /// <summary>
        /// 使用 BattlePredictor 进行单张卡牌预测并发布结果
        /// </summary>
        public PredictionResult PredictCard(cfg.Character.CardInfo card, string ownerId, string targetId)
        {
            if (_battleManager?.CurrentState == null || card == null)
            {
                return new PredictionResult();
            }

            var result = _predictor.SimulateCard(_battleManager.CurrentState, card, ownerId, targetId);

            // 发布预测结果
            Dictionary<string, int> predictedHpMap = new Dictionary<string, int>();
            foreach (var kvp in result.FinalHpMap)
            {
                predictedHpMap[kvp.Key] = kvp.Value;
            }

            GameEvent.Publish(new HpPredictionEvent
            {
                PredictedHpMap = predictedHpMap
            });

            IsShowingPrediction = true;
            return result;
        }

        public void StopPrediction()
        {
            if (!IsShowingPrediction)
            {
                return;
            }

            GameEvent.Publish(new HpPredictionStopEvent());
            IsShowingPrediction = false;
        }

        public void Dispose()
        {
            GameEvent.Unsubscribe<CardPlacedEvent>(OnCardPlaced);
            GameEvent.Unsubscribe<CardRemovedEvent>(OnCardRemoved);
            StopPrediction();
            Debug.Log("[BattlePredictionManager] 预测管理器已清理");
        }
    }
}
