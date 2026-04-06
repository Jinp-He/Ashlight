using System.Collections.Generic;
using System.Linq;
using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Engine
{
    /// <summary>
    /// 行动条推进解算器
    /// 负责按速度推进所有单位的行动条，确定下一个获得行动权的单位
    /// 无状态类，只做 Input State -> Output State 转换
    /// </summary>
    public class ActionBarResolver
    {
        /// <summary>
        /// 推进所有存活单位的行动条，直到有单位到达终点
        /// </summary>
        /// <param name="state">战场快照（会被修改）</param>
        /// <returns>获得行动权的单位ID，如果战斗已结束返回null</returns>
        public string AdvanceUntilAction(BattleStateSnapshot state)
        {
            if (state == null || state.IsBattleEnded)
            {
                return null;
            }

            var aliveUnits = GetAliveUnitsWithActionBar(state);
            if (aliveUnits.Count == 0)
            {
                return null;
            }

            // 检查是否已有单位到达终点（上一次推进的残余）
            var readyUnit = GetHighestPriorityReady(aliveUnits);
            if (readyUnit != null)
            {
                return readyUnit.UnitId;
            }

            // 循环推进，每次推进 1 段，直到有单位到达终点
            int safetyCounter = 0;
            const int maxIterations = 10000;

            while (safetyCounter < maxIterations)
            {
                safetyCounter++;

                foreach (var unit in aliveUnits)
                {
                    unit.ActionBar.Advance(unit.Speed);
                }

                readyUnit = GetHighestPriorityReady(aliveUnits);
                if (readyUnit != null)
                {
                    Debug.Log($"[ActionBarResolver] {readyUnit.UnitId} 到达行动条终点 (段位: {readyUnit.ActionBar.CurrentSegment}/{readyUnit.ActionBar.MaxSegment}, 推进轮次: {safetyCounter})");
                    return readyUnit.UnitId;
                }
            }

            Debug.LogError("[ActionBarResolver] 推进超过安全上限，强制中止");
            return null;
        }

        /// <summary>
        /// 获取当前所有已到达终点的单位中优先级最高的
        /// 优先级规则：Speed 更高的优先；Speed 相同时玩家单位优先
        /// </summary>
        private UnitState GetHighestPriorityReady(List<UnitState> aliveUnits)
        {
            UnitState bestUnit = null;

            foreach (var unit in aliveUnits)
            {
                if (!unit.ActionBar.HasReachedEnd)
                {
                    continue;
                }

                if (bestUnit == null)
                {
                    bestUnit = unit;
                    continue;
                }

                if (unit.Speed > bestUnit.Speed)
                {
                    bestUnit = unit;
                }
                else if (unit.Speed == bestUnit.Speed && unit.IsPlayerUnit && !bestUnit.IsPlayerUnit)
                {
                    bestUnit = unit;
                }
            }

            return bestUnit;
        }

        /// <summary>
        /// 回合结束后重置单位行动条
        /// 处理过载负债对起跑段位的影响
        /// </summary>
        public void RestartUnitActionBar(UnitState unit)
        {
            if (unit == null || unit.ActionBar == null)
            {
                return;
            }

            int debtConsumed = 0;
            if (unit.Overload != null)
            {
                debtConsumed = unit.Overload.ConsumeDebt();
            }

            if (debtConsumed > 0)
            {
                unit.ActionBar.ApplyOverloadPenalty(debtConsumed);
                Debug.Log($"[ActionBarResolver] {unit.UnitId} 过载负债消耗 {debtConsumed} 段, 实际起跑段位: {unit.ActionBar.RestartSegment}");
            }
            else
            {
                unit.ActionBar.RestartSegment = unit.ActionBar.BaseRestartSegment;
            }

            unit.ActionBar.Restart();
            Debug.Log($"[ActionBarResolver] {unit.UnitId} 行动条重置到段位 {unit.ActionBar.CurrentSegment}");
        }

        /// <summary>
        /// 预测下一个行动单位（不修改状态）
        /// </summary>
        /// <param name="state">当前战场快照（不会被修改）</param>
        /// <returns>预测的下一个行动单位ID列表（按预测行动顺序）</returns>
        public List<string> PredictActionOrder(BattleStateSnapshot state, int lookAhead = 5)
        {
            var result = new List<string>();
            if (state == null || state.IsBattleEnded)
            {
                return result;
            }

            var clonedState = state.Clone();
            for (int i = 0; i < lookAhead; i++)
            {
                string nextUnitId = AdvanceUntilAction(clonedState);
                if (nextUnitId == null)
                {
                    break;
                }

                result.Add(nextUnitId);

                var unit = clonedState.GetUnitById(nextUnitId);
                if (unit != null)
                {
                    RestartUnitActionBar(unit);
                }
            }

            return result;
        }

        /// <summary>
        /// 获取所有存活且拥有行动条的单位
        /// </summary>
        private List<UnitState> GetAliveUnitsWithActionBar(BattleStateSnapshot state)
        {
            return state.GetAllUnits()
                .Where(u => !u.IsDead && u.ActionBar != null)
                .ToList();
        }

        /// <summary>
        /// 获取单位当前所处的行动条区段
        /// </summary>
        public ActionBarZone GetUnitZone(UnitState unit)
        {
            if (unit?.ActionBar == null)
            {
                return ActionBarZone.Start;
            }

            return ActionBarZoneHelper.GetZone(
                unit.ActionBar.CurrentSegment,
                unit.ActionBar.MaxSegment
            );
        }
    }
}
