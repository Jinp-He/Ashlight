using System.Collections;
using System.Collections.Generic;
using Ashlight.Battle.Core.Data;
using Ashlight.Common.Events;
using UnityEngine;

namespace Ashlight.Battle.Core.Engine
{
    /// <summary>
    /// 战斗预测管理器
    /// 负责在放置/移除卡牌时触发预解算，并管理血量预测的显示
    /// </summary>
    public class BattlePredictionManager
    {
        private readonly BattleManager _battleManager;
        private readonly BattlePredictor _predictor;
        
        /// <summary>
        /// 是否正在显示预测结果
        /// </summary>
        public bool IsShowingPrediction { get; private set; }

        public BattlePredictionManager(BattleManager battleManager)
        {
            _battleManager = battleManager;
            _predictor = new BattlePredictor();
            
            // 订阅卡牌放置/移除事件
            GameEvent.Subscribe<CardPlacedEvent>(OnCardPlaced);
            GameEvent.Subscribe<CardRemovedEvent>(OnCardRemoved);
            
            Debug.Log("[BattlePredictionManager] 预测管理器已初始化");
        }

        /// <summary>
        /// 处理卡牌放置事件
        /// </summary>
        private void OnCardPlaced(CardPlacedEvent evt)
        {
            Debug.Log($"[BattlePredictionManager] ========== 【触发点：卡牌放置/移动】 ==========");
            Debug.Log($"[BattlePredictionManager] 收到卡牌放置事件:");
            Debug.Log($"[BattlePredictionManager]   - CardId: {evt.CardId}");
            Debug.Log($"[BattlePredictionManager]   - OwnerId: {evt.OwnerId}");
            Debug.Log($"[BattlePredictionManager]   - TargetId: {evt.TargetId}");
            Debug.Log($"[BattlePredictionManager] 注意：此事件可能由卡牌放置或卡牌移动触发");
            TriggerPrediction("卡牌放置/移动");
        }

        /// <summary>
        /// 处理卡牌移除事件
        /// </summary>
        private void OnCardRemoved(CardRemovedEvent evt)
        {
            Debug.Log($"[BattlePredictionManager] ========== 【触发点：卡牌移除】 ==========");
            Debug.Log($"[BattlePredictionManager] 收到卡牌移除事件: CardId={evt.CardId}, OwnerId={evt.OwnerId}");
            TriggerPrediction("卡牌移除");
        }

        /// <summary>
        /// 触发预解算
        /// </summary>
        /// <param name="triggerSource">触发来源（用于调试）</param>
        public void TriggerPrediction(string triggerSource = "未知")
        {
            Debug.Log($"[BattlePredictionManager] ========== 【预解算触发】来源: {triggerSource} ==========");
            
            if (_battleManager == null || _battleManager.CurrentState == null)
            {
                Debug.LogWarning("[BattlePredictionManager] BattleManager或CurrentState为null，跳过预解算");
                return;
            }

            // 输出当前状态信息
            Debug.Log($"[BattlePredictionManager] 当前战斗状态:");
            Debug.Log($"[BattlePredictionManager]   - 当前回合: {_battleManager.CurrentRound}");
            Debug.Log($"[BattlePredictionManager]   - 当前时间格: {_battleManager.CurrentState.CurrentTimeIndex}");
            Debug.Log($"[BattlePredictionManager]   - 玩家单位数: {_battleManager.CurrentState.PlayerUnits.Count}");
            Debug.Log($"[BattlePredictionManager]   - 敌人单位数: {_battleManager.CurrentState.EnemyUnits.Count}");

            // 输出所有单位当前血量
            foreach (var unit in _battleManager.CurrentState.GetAllUnits())
            {
                Debug.Log($"[BattlePredictionManager]   当前血量: {unit.UnitId} = {unit.CurrentHp}/{unit.MaxHp}");
            }

            // 停止之前的预测显示
            StopPrediction();

            Debug.Log("[BattlePredictionManager] ======== 开始预解算 ========");

            // 克隆当前战斗状态
            BattleStateSnapshot virtualState = _battleManager.CurrentState.Clone();
            
            // 标记为预解算状态，避免触发动画事件
            virtualState.IsPrediction = true;

            // 只解算索引0（与实际执行一致），预测下一个时间格执行后的血量
            TimelineResolver resolver = new TimelineResolver();
            int currentTimeIndex = 0;
            
            Debug.Log($"[BattlePredictionManager] 预测索引 {currentTimeIndex} 执行后的血量");
            
            // 解算索引0
            resolver.ResolveStep(virtualState, currentTimeIndex);
            
            // 如果战斗结束，记录日志
            if (virtualState.IsBattleEnded)
            {
                Debug.Log($"[BattlePredictionManager] 预测：战斗将在时间格 {currentTimeIndex} 结束");
            }

            // 创建预测结果字典
            Dictionary<string, int> predictedHpMap = new Dictionary<string, int>();
            Debug.Log("[BattlePredictionManager] ======== 预测结果对比 ========");
            foreach (var unit in virtualState.GetAllUnits())
            {
                var originalUnit = _battleManager.CurrentState.GetUnitById(unit.UnitId);
                int originalHp = originalUnit != null ? originalUnit.CurrentHp : 0;
                int predictedHp = unit.CurrentHp;
                int hpChange = predictedHp - originalHp;
                
                predictedHpMap[unit.UnitId] = predictedHp;
                
                string changeText = hpChange > 0 ? $"+{hpChange}" : hpChange.ToString();
                Debug.Log($"[BattlePredictionManager]   {unit.UnitId}: {originalHp} -> {predictedHp} ({changeText})");
            }

            // 发布预测结果事件
            GameEvent.Publish(new HpPredictionEvent
            {
                PredictedHpMap = predictedHpMap
            });

            // 标记为正在显示预测（不再自动停止，持续显示直到回合开始）
            IsShowingPrediction = true;

            Debug.Log($"[BattlePredictionManager] ======== 预解算完成（触发来源: {triggerSource}），预测将持续显示直到回合开始 ========");
        }

        /// <summary>
        /// 停止预测显示
        /// </summary>
        public void StopPrediction()
        {
            if (!IsShowingPrediction)
            {
                return;
            }

            // 发布停止预测事件
            GameEvent.Publish(new HpPredictionStopEvent());
            IsShowingPrediction = false;

            Debug.Log("[BattlePredictionManager] 停止预测显示");
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        public void Dispose()
        {
            // 取消订阅事件
            GameEvent.Unsubscribe<CardPlacedEvent>(OnCardPlaced);
            GameEvent.Unsubscribe<CardRemovedEvent>(OnCardRemoved);
            
            StopPrediction();
            
            Debug.Log("[BattlePredictionManager] 预测管理器已清理");
        }
    }
}
