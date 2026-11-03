using System;
using Ashlight.Battle.Core.Data;
using Ashlight.Config;
using cfg.Enemy;
using UnityEngine;

namespace Scripts.UI
{
    /// <summary>
    /// CardTestController 使用的战斗场景桥接 API。
    /// 独立于正式战斗主逻辑，测试场景不需要直接操作私有卡池和单位 UI 管理器。
    /// </summary>
    public partial class UI_BattleScene
    {
        /// <summary>供卡牌测试面板生成只读敌人模型预览。</summary>
        public GameObject EnemyPrefabForCardTest => enemyPrefab;

        public bool TryAddCardForCardTest(string cardId, out string message)
        {
            message = string.Empty;
            if (_battleManager?.CurrentState?.DeckSystem == null)
            {
                message = "战斗尚未初始化。";
                return false;
            }

            var cardInfo = ConfigLoader.Tables?.TbCardInfo?.GetOrDefault(cardId);
            if (cardInfo == null)
            {
                message = $"找不到卡牌配置：{cardId}";
                return false;
            }

            var deck = _battleManager.CurrentState.DeckSystem;
            if (deck.Hand.Count >= maxHandSize)
            {
                message = $"手牌已满（{deck.Hand.Count}/{maxHandSize}）。";
                return false;
            }

            int added = deck.AddCardToHand(cardId, 1);
            if (added <= 0)
            {
                message = $"卡牌加入数据层失败：{cardId}";
                return false;
            }

            // 从真实 DeckSystem.Hand 重建/补齐卡牌 UI，保证 InstanceId、弃牌与出牌流程一致。
            DisplayHandCards();
            RefreshHandEnergyAffordability();
            message = $"已将 {cardInfo.Name} [{cardInfo.Id}] 临时加入手牌。";
            return true;
        }

        public bool TryAddEnemyForCardTest(string enemyConfigId, out string unitId, out string message)
        {
            unitId = string.Empty;
            message = string.Empty;

            var state = _battleManager?.CurrentState;
            if (state == null)
            {
                message = "战斗尚未初始化。";
                return false;
            }

            if (state.IsBattleEnded)
            {
                message = "战斗已经结束，请先重置战斗再添加敌人。";
                return false;
            }

            EnemyInfo enemyInfo = ConfigLoader.Tables?.TbEnemyInfo?.GetOrDefault(enemyConfigId);
            if (enemyInfo == null)
            {
                message = $"找不到敌人配置：{enemyConfigId}";
                return false;
            }

            if (enemyPrefab == null)
            {
                message = "UI_BattleScene 未绑定 Enemy prefab。";
                return false;
            }

            unitId = CreateUniqueTestEnemyId(state);
            var unitState = new UnitState
            {
                UnitId = unitId,
                ConfigId = enemyInfo.Id,
                MaxHp = enemyInfo.Hp,
                CurrentHp = enemyInfo.Hp,
                Defense = 0,
                IsPlayerUnit = false,
                IsElite = enemyInfo.IsElite,
                IsDead = false,
                Track = null,
                Speed = Mathf.Max(1, enemyInfo.Speed),
                BaseEnergy = 2,
                BaseDrawCount = 0,
                ActionBar = new ActionBarState(),
                Overload = new OverloadState(),
                RowPosition = enemyInfo.StartRow == cfg.TargetZoneEnum.Back
                    ? BattleRowPosition.BackRow
                    : BattleRowPosition.FrontRow
            };

            RectTransform parent = ResolveEnemyRowParent(unitState);
            if (parent == null)
            {
                message = "敌方前后排容器与 EnemyPostion 均未绑定。";
                return false;
            }

            state.EnemyUnits.Add(unitState);

            GameObject enemyObject = null;
            Enemy enemyView = null;
            try
            {
                enemyObject = Instantiate(enemyPrefab, parent);
                enemyView = enemyObject.GetComponent<Enemy>();
                if (enemyView == null)
                {
                    throw new InvalidOperationException("Enemy prefab 上没有 Enemy 组件。");
                }

                enemyView.Initialize(unitState);
                _unitUIManager.RegisterEnemy(enemyView);
                RebuildEnemyRowLayout();

                if (ATB != null)
                {
                    ATB.AddEnemyIcon(unitState.ConfigId, unitState.UnitId, unitState.Speed);
                    // AddEnemyIcon 的默认种子用于开局；运行中追加时必须从当前公共回合向后排。
                    ATB.SetNextRound(unitState.UnitId, ATB.CurrentRound + unitState.Speed);
                    ATB.SyncScheduleToState(state);
                }

                RefreshTurnOrderRegistrationForCardTest();
                PrepareAddedEnemyIntentWithoutStealingTurn(unitState.UnitId);
                UpdateAllUnitsDisplay();

                message = $"已添加敌人 {enemyInfo.Name} [{enemyInfo.Id}]。";
                return true;
            }
            catch (Exception exception)
            {
                state.EnemyUnits.Remove(unitState);
                if (enemyView != null) _unitUIManager.UnregisterEnemy(enemyView);
                if (enemyObject != null) Destroy(enemyObject);
                ATB?.RemoveUnitIcon(unitState.UnitId);
                TurnOrderView?.RemoveUnit(unitState.UnitId);
                unitId = string.Empty;
                message = $"添加敌人失败：{exception.Message}";
                Debug.LogException(exception);
                return false;
            }
        }

        private void RefreshTurnOrderRegistrationForCardTest()
        {
            if (TurnOrderView == null || _battleManager?.CurrentState == null) return;

            var state = _battleManager.CurrentState;
            TurnOrderView.Initialize(state.PlayerUnits, state.EnemyUnits);
            TurnOrderView.SetWeather(_battleManager.CurrentWeather);

            // Initialize 会清空引线登记，立即按 BattleManager 的真实在轨状态补回。
            foreach (var cast in _battleManager.GetPendingCasts())
            {
                if (cast?.Card != null) TurnOrderView.SetCast(cast.CastId, cast.Card);
            }

            TurnOrderView.SetActiveUnit(state.CurrentTurnUnitId);
            TurnOrderView.RefreshOrder();
        }

        private void PrepareAddedEnemyIntentWithoutStealingTurn(string enemyUnitId)
        {
            if (_battleManager?.CurrentState == null) return;

            var state = _battleManager.CurrentState;
            string savedTurnUnitId = state.CurrentTurnUnitId;
            bool savedGlobalPaused = state.IsGlobalPaused;

            DeclareEnemyIntent(enemyUnitId);

            // DeclareEnemyIntent 内部会调用 StartEnemyTurn；测试期追加敌人不能抢走玩家当前回合。
            state.CurrentTurnUnitId = savedTurnUnitId;
            state.IsGlobalPaused = savedGlobalPaused;
            TurnOrderView?.SetActiveUnit(savedTurnUnitId);
            if (!string.IsNullOrEmpty(savedTurnUnitId))
            {
                UpdateEnergyBarByUnitId(savedTurnUnitId);
            }
            RefreshHandEnergyAffordability();
        }

        private static string CreateUniqueTestEnemyId(BattleStateSnapshot state)
        {
            int suffix = 1;
            string candidate;
            do
            {
                candidate = $"enemy_test_{suffix++}";
            } while (state.GetUnitById(candidate) != null);
            return candidate;
        }
    }
}
