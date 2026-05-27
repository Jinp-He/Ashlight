using System.Collections.Generic;
using Ashlight.Battle.Core.Commands;
using Ashlight.Battle.Core.Data;
using Ashlight.Common.Events;
using UnityEngine;

namespace Ashlight.Battle.Core.Engine
{
    /// <summary>
    /// 回合解算器
    /// ATB 系统核心执行闭环：管理单位获得行动权后的完整回合流程
    /// 无状态类，只做 Input State -> Output State 转换
    /// </summary>
    public class TurnResolver
    {
        private readonly CardPlayResolver _cardPlayResolver;
        private readonly ActionBarResolver _actionBarResolver;

        public TurnResolver()
        {
            _cardPlayResolver = new CardPlayResolver();
            _actionBarResolver = new ActionBarResolver();
        }

        public TurnResolver(CardPlayResolver cardPlayResolver, ActionBarResolver actionBarResolver)
        {
            _cardPlayResolver = cardPlayResolver ?? new CardPlayResolver();
            _actionBarResolver = actionBarResolver ?? new ActionBarResolver();
        }

        /// <summary>
        /// 执行一个完整的单位回合
        /// </summary>
        /// <param name="state">战场快照（会被修改）</param>
        /// <param name="unitId">行动单位ID</param>
        /// <param name="cardActions">本回合要打出的卡牌列表（cardInfo, targetId 对）</param>
        /// <returns>回合是否正常完成</returns>
        public bool ExecuteTurn(BattleStateSnapshot state, string unitId, List<CardAction> cardActions)
        {
            if (state == null || state.IsBattleEnded)
            {
                return false;
            }

            var unit = state.GetUnitById(unitId);
            if (unit == null || unit.IsDead)
            {
                Debug.LogWarning($"[TurnResolver] 行动单位无效或已死亡: {unitId}");
                return false;
            }

            state.CurrentTurnUnitId = unitId;
            state.TurnCount++;

            Debug.Log($"[TurnResolver] ========== {unitId} 回合开始 (第 {state.TurnCount} 回合) ==========");

            // 1. 回合开始效果
            OnTurnStart(state, unit);
            if (state.IsBattleEnded) return true;

            // 2. 刷新能量
            RefreshEnergy(unit);

            // 3. 抽牌（仅玩家单位）
            if (unit.IsPlayerUnit)
            {
                DrawCards(state, unit);
            }

            // 4. 执行卡牌
            if (cardActions != null)
            {
                foreach (var action in cardActions)
                {
                    if (state.IsBattleEnded) break;

                    int energyCost = GetCardEnergyCost(action.CardInfo);

                    // CardCost（减费）：每次消耗一层 CardCost，下张牌费用 -V
                    var cardCostBuff = unit.GetBuff("CardCost");
                    if (cardCostBuff != null)
                    {
                        int reduction = Mathf.Max(0, Mathf.RoundToInt(cardCostBuff.Value));
                        if (reduction > 0)
                        {
                            int reduced = Mathf.Min(reduction, energyCost);
                            energyCost -= reduced;
                            Debug.Log($"[TurnResolver] {unitId} [减费] -{reduced} 能量 (原 {energyCost + reduced})");
                        }
                        unit.RemoveBuff("CardCost");
                    }

                    if (unit.CurrentEnergy < energyCost)
                    {
                        Debug.Log($"[TurnResolver] {unitId} 能量不足 ({unit.CurrentEnergy}/{energyCost})，需要过载或跳过");
                        break;
                    }

                    unit.CurrentEnergy -= energyCost;
                    _cardPlayResolver.PlayCard(state, action.CardInfo, unitId, action.TargetId);

                    Debug.Log($"[TurnResolver] {unitId} 剩余能量: {unit.CurrentEnergy}");
                }
            }

            // 5. 回合结束处理
            OnTurnEnd(state, unit);

            // 6. 行动条重启
            _actionBarResolver.RestartUnitActionBar(unit);

            state.CurrentTurnUnitId = null;

            Debug.Log($"[TurnResolver] ========== {unitId} 回合结束 ==========");

            return true;
        }

        /// <summary>
        /// 回合开始效果处理
        /// </summary>
        private void OnTurnStart(BattleStateSnapshot state, UnitState unit)
        {
            Debug.Log($"[TurnResolver] {unit.UnitId} 回合开始效果处理");

            // 触发回合开始 Buff 效果（如中毒、再生等）
            var buffsToProcess = new List<BuffState>(unit.Buffs);
            foreach (var buff in buffsToProcess)
            {
                ProcessTurnStartBuff(state, unit, buff);
                if (state.IsBattleEnded) return;
            }
        }

        /// <summary>
        /// 处理回合开始 Buff（Burn/Poison 真伤，Regen 回血）
        /// 真伤无视护甲，直接扣 CurrentHp
        /// </summary>
        private void ProcessTurnStartBuff(BattleStateSnapshot state, UnitState unit, BuffState buff)
        {
            switch (buff.BuffId)
            {
                case "Burn":
                {
                    int dmg = Mathf.Max(0, Mathf.RoundToInt(buff.Value));
                    if (dmg <= 0) break;
                    unit.CurrentHp -= dmg;
                    if (unit.CurrentHp <= 0) { unit.CurrentHp = 0; unit.IsDead = true; }
                    Debug.Log($"[TurnResolver] {unit.UnitId} 燃烧造成 {dmg} 伤害 (剩余 HP: {unit.CurrentHp})");
                    break;
                }
                case "Poison":
                {
                    int dmg = Mathf.Max(0, Mathf.RoundToInt(buff.Value));
                    if (dmg > 0)
                    {
                        unit.CurrentHp -= dmg;
                        if (unit.CurrentHp <= 0) { unit.CurrentHp = 0; unit.IsDead = true; }
                        Debug.Log($"[TurnResolver] {unit.UnitId} 中毒造成 {dmg} 伤害 (剩余 HP: {unit.CurrentHp})");
                    }
                    // 中毒 V 每回合衰减 1
                    buff.Value = Mathf.Max(0f, buff.Value - 1f);
                    if (buff.Value <= 0f)
                    {
                        unit.RemoveBuff("Poison");
                    }
                    break;
                }
                case "Regen":
                {
                    int heal = Mathf.Max(0, Mathf.RoundToInt(buff.Value));
                    if (heal <= 0) break;
                    int actual = unit.Heal(heal);
                    Debug.Log($"[TurnResolver] {unit.UnitId} 再生恢复 {actual} 生命 (当前 HP: {unit.CurrentHp})");
                    break;
                }
            }
        }

        /// <summary>
        /// 刷新能量（基础能量 + 充能 buff）
        /// </summary>
        private void RefreshEnergy(UnitState unit)
        {
            unit.CurrentEnergy = unit.BaseEnergy;

            var energized = unit.GetBuff("Energized");
            if (energized != null)
            {
                int bonus = Mathf.Max(0, Mathf.RoundToInt(energized.Value));
                if (bonus > 0)
                {
                    unit.CurrentEnergy += bonus;
                    Debug.Log($"[TurnResolver] {unit.UnitId} 充能 +{bonus} 能量");
                }
                unit.RemoveBuff("Energized");
            }

            Debug.Log($"[TurnResolver] {unit.UnitId} 能量刷新: {unit.CurrentEnergy}");
        }

        /// <summary>
        /// 抽牌
        /// </summary>
        private void DrawCards(BattleStateSnapshot state, UnitState unit)
        {
            if (state.DeckSystem == null)
            {
                return;
            }

            int drawCount = unit.BaseDrawCount;
            var cid = unit.GetCharacterId();
            if (cid.HasValue)
            {
                state.DeckSystem.DrawCardForCharacter(cid.Value, drawCount);
            }
            else
            {
                state.DeckSystem.DrawCard(drawCount);
            }

            Debug.Log($"[TurnResolver] {unit.UnitId} 抽取 {drawCount} 张牌");
        }

        /// <summary>
        /// 执行过载（在回合中途调用，允许单位继续出牌）
        /// </summary>
        /// <param name="state">战场快照</param>
        /// <param name="unit">行动单位</param>
        /// <param name="bonusEnergy">过载获得的额外能量</param>
        /// <returns>是否成功过载</returns>
        public bool ProcessOverload(BattleStateSnapshot state, UnitState unit, int bonusEnergy)
        {
            if (unit == null || unit.IsDead)
            {
                return false;
            }

            if (unit.Overload == null)
            {
                unit.Overload = new OverloadState();
            }

            if (unit.Overload.OverloadLevel >= 3)
            {
                Debug.LogWarning($"[TurnResolver] {unit.UnitId} 已达到最大过载等级，无法继续过载");
                return false;
            }

            var overloadCmd = new OverloadCommand(bonusEnergy);
            overloadCmd.Execute(state, unit.UnitId, unit.UnitId);

            return true;
        }

        /// <summary>
        /// 回合结束处理
        /// </summary>
        private void OnTurnEnd(BattleStateSnapshot state, UnitState unit)
        {
            Debug.Log($"[TurnResolver] {unit.UnitId} 回合结束处理");

            // 清空护甲
            if (unit.Defense > 0)
            {
                Debug.Log($"[TurnResolver] {unit.UnitId} 护甲清零: {unit.Defense} -> 0");
                unit.Defense = 0;
            }

            // 更新 Buff（衰减持续时间，移除过期 Buff）
            int buffCountBefore = unit.Buffs.Count;
            unit.UpdateBuffs();
            if (unit.Buffs.Count < buffCountBefore)
            {
                Debug.Log($"[TurnResolver] {unit.UnitId} Buff 更新: {buffCountBefore} -> {unit.Buffs.Count}");
            }

            // 过载状态回合末处理
            if (unit.Overload != null)
            {
                unit.Overload.OnTurnEnd();
            }

            // 清空当前回合能量
            unit.CurrentEnergy = 0;

            // 检查战斗结束
            state.CheckBattleEnd();
        }

        /// <summary>
        /// 获取卡牌能量消耗
        /// 当前临时实现：统一消耗 1 能量
        /// 待 CardInfo 配置表新增 EnergyCost 字段后替换为读取配置
        /// </summary>
        private int GetCardEnergyCost(cfg.Character.CardInfo card)
        {
            return card?.Energy ?? 1;
        }
    }

    /// <summary>
    /// 卡牌行动描述（一次出牌的信息）
    /// </summary>
    public class CardAction
    {
        public cfg.Character.CardInfo CardInfo { get; set; }
        public string OwnerId { get; set; }
        public string TargetId { get; set; }
        public string InstanceId { get; set; }

        public CardAction(cfg.Character.CardInfo cardInfo, string ownerId, string targetId, string instanceId = null)
        {
            CardInfo = cardInfo;
            OwnerId = ownerId;
            TargetId = targetId;
            InstanceId = instanceId;
        }
    }
}
