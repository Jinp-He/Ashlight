using System.Collections.Generic;
using Ashlight.Battle.Core.Data;
using Ashlight.Config;
using Ashlight.Systems.Character;
using cfg;
using cfg.Character;
using UnityEngine;

namespace Ashlight.Battle.Core.Engine
{
    /// <summary>
    /// 升级效果应用器。
    /// 战斗开始（单位与卡组都已建好）后调用，把每名参战角色「已获得的升级」
    /// (CharacterRuntimeState.AcquiredUpgrades) 重新派生为本场战斗的实际效果：
    ///   - GrantBuff       -> 给单位贴整场永久 buff（力量/敏捷/再生 等）
    ///   - ModifyUnitStat  -> 改单位战斗属性字段（最大生命/护甲/能量）
    ///   - ModifyCardStat  -> 写入卡牌修正表（伤害/护甲增减）
    ///   - ModifyCardFlag  -> 写入卡牌修正表（如 单体->AOE）
    /// 不改任何静态配置，故无存档污染；每场战斗重新派生。
    /// </summary>
    public static class UpgradeEffectApplier
    {
        /// <summary>升级 buff 的固定时长：整场永久。</summary>
        private const int PermanentDuration = -1;

        public static void Apply(BattleStateSnapshot state, IList<CharacterEnum> party)
        {
            if (state == null || party == null)
            {
                return;
            }

            var tables = ConfigLoader.Tables;
            if (tables == null || tables.TbUpgradeOptions == null)
            {
                Debug.LogWarning("[UpgradeEffectApplier] 配置表未就绪，跳过升级应用");
                return;
            }

            if (state.CardModifiers == null)
            {
                state.CardModifiers = new CardModifierRegistry();
            }

            foreach (var characterId in party)
            {
                var charState = CharacterSystem.GetCharacterState(characterId);
                var upgrades = charState?.AcquiredUpgrades;
                if (upgrades == null || upgrades.Count == 0)
                {
                    continue;
                }

                foreach (var upgradeId in upgrades)
                {
                    var option = tables.TbUpgradeOptions.GetOrDefault(upgradeId);
                    if (option == null)
                    {
                        Debug.LogWarning($"[UpgradeEffectApplier] 未找到升级配置: {upgradeId}（角色 {characterId}）");
                        continue;
                    }

                    if (option.Effects == null)
                    {
                        continue;
                    }

                    foreach (var effect in option.Effects)
                    {
                        ApplyEffect(state, characterId, option, effect);
                    }
                }
            }
        }

        private static void ApplyEffect(BattleStateSnapshot state, CharacterEnum owner, UpgradeOptions option, UpgradeEffect effect)
        {
            switch (effect)
            {
                case GrantBuff grantBuff:
                    ApplyGrantBuff(state, owner, grantBuff);
                    break;
                case ModifyUnitStat modifyUnitStat:
                    ApplyModifyUnitStat(state, owner, modifyUnitStat);
                    break;
                case ModifyCardStat modifyCardStat:
                    ApplyModifyCardStat(state, owner, modifyCardStat);
                    break;
                case ModifyCardFlag modifyCardFlag:
                    ApplyModifyCardFlag(state, owner, modifyCardFlag);
                    break;
                default:
                    Debug.LogWarning($"[UpgradeEffectApplier] 未处理的升级效果类型: {effect?.GetType().Name}（升级 {option?.Id}）");
                    break;
            }
        }

        // ---------------------------------------------------------------- GrantBuff

        private static void ApplyGrantBuff(BattleStateSnapshot state, CharacterEnum owner, GrantBuff effect)
        {
            foreach (var unit in ResolveUnitTargets(state, owner, effect.Target))
            {
                // 同名永久 buff 合并数值，避免 GetBuff 只取首个导致叠加失效
                var existing = unit.GetBuff(effect.BuffId);
                if (existing != null)
                {
                    existing.Value += effect.Stack;
                    existing.RemainingDuration = PermanentDuration;
                }
                else
                {
                    unit.AddBuff(new BuffState
                    {
                        BuffId = effect.BuffId,
                        Value = effect.Stack,
                        RemainingDuration = PermanentDuration
                    });
                }

                Debug.Log($"[UpgradeEffectApplier] {unit.UnitId} 获得升级buff {effect.BuffId} +{effect.Stack}（永久）");
            }
        }

        // ---------------------------------------------------------------- ModifyUnitStat

        private static void ApplyModifyUnitStat(BattleStateSnapshot state, CharacterEnum owner, ModifyUnitStat effect)
        {
            foreach (var unit in ResolveUnitTargets(state, owner, effect.Target))
            {
                switch (Normalize(effect.Stat))
                {
                    case "maxhp":
                        unit.MaxHp += effect.Delta;
                        if (unit.MaxHp < 1) unit.MaxHp = 1;
                        unit.CurrentHp = Mathf.Clamp(unit.CurrentHp + effect.Delta, 1, unit.MaxHp);
                        break;
                    case "armor":
                    case "defense":
                        unit.AddDefense(effect.Delta);
                        break;
                    case "energy":
                        unit.BaseEnergy = Mathf.Max(0, unit.BaseEnergy + effect.Delta);
                        break;
                    default:
                        Debug.LogWarning($"[UpgradeEffectApplier] ModifyUnitStat 未支持的属性: {effect.Stat}");
                        continue;
                }

                Debug.Log($"[UpgradeEffectApplier] {unit.UnitId} 属性 {effect.Stat} {(effect.Delta >= 0 ? "+" : "")}{effect.Delta}");
            }
        }

        // ---------------------------------------------------------------- ModifyCardStat / Flag

        private static void ApplyModifyCardStat(BattleStateSnapshot state, CharacterEnum owner, ModifyCardStat effect)
        {
            foreach (var cardId in ResolveCardSelector(effect.Selector, owner))
            {
                var mod = state.CardModifiers.GetOrCreate(cardId);
                if (mod == null) continue;

                switch (Normalize(effect.Stat))
                {
                    case "damage":
                        mod.DamageDelta += effect.Delta;
                        break;
                    case "defense":
                        mod.DefenseDelta += effect.Delta;
                        break;
                    default:
                        Debug.LogWarning($"[UpgradeEffectApplier] ModifyCardStat 未支持的属性: {effect.Stat}");
                        continue;
                }

                Debug.Log($"[UpgradeEffectApplier] 卡牌 {cardId} {effect.Stat} {(effect.Delta >= 0 ? "+" : "")}{effect.Delta}");
            }
        }

        private static void ApplyModifyCardFlag(BattleStateSnapshot state, CharacterEnum owner, ModifyCardFlag effect)
        {
            foreach (var cardId in ResolveCardSelector(effect.Selector, owner))
            {
                var mod = state.CardModifiers.GetOrCreate(cardId);
                if (mod == null) continue;

                switch (Normalize(effect.Flag))
                {
                    case "isaoe":
                        mod.ForceAoe = effect.Value;
                        break;
                    default:
                        Debug.LogWarning($"[UpgradeEffectApplier] ModifyCardFlag 未支持的标记: {effect.Flag}");
                        continue;
                }

                Debug.Log($"[UpgradeEffectApplier] 卡牌 {cardId} 标记 {effect.Flag}={effect.Value}");
            }
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>把 TargetTypeEnum 解析为具体玩家单位（Self=升级所属角色 / AllAlly=全体我方）。</summary>
        private static List<UnitState> ResolveUnitTargets(BattleStateSnapshot state, CharacterEnum owner, TargetTypeEnum target)
        {
            var result = new List<UnitState>();
            if (state.PlayerUnits == null) return result;

            if (target == TargetTypeEnum.AllAlly)
            {
                foreach (var u in state.PlayerUnits)
                {
                    if (u != null) result.Add(u);
                }
                return result;
            }

            // 默认 Self：玩家单位 ConfigId == 角色枚举名
            string ownerKey = owner.ToString();
            var self = state.PlayerUnits.Find(u => u != null && u.ConfigId == ownerKey);
            if (self != null) result.Add(self);
            return result;
        }

        /// <summary>
        /// 把选卡器解析成一组 CardId。
        /// 支持：纯卡Id / "BelongTo:角色" / "CardType:类型" / "All"（后两者默认限定升级所属角色）。
        /// </summary>
        private static List<string> ResolveCardSelector(string selector, CharacterEnum owner)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(selector)) return result;

            var tables = ConfigLoader.Tables;
            var allCards = tables?.TbCardInfo?.DataList;

            string sel = selector.Trim();

            // All：升级所属角色的全部卡
            if (string.Equals(sel, "All", System.StringComparison.OrdinalIgnoreCase))
            {
                if (allCards != null)
                {
                    foreach (var c in allCards)
                    {
                        if (c != null && c.BelongTo == owner) result.Add(c.Id);
                    }
                }
                return result;
            }

            int colon = sel.IndexOf(':');
            if (colon > 0)
            {
                string key = sel.Substring(0, colon).Trim();
                string val = sel.Substring(colon + 1).Trim();

                if (string.Equals(key, "BelongTo", System.StringComparison.OrdinalIgnoreCase)
                    && System.Enum.TryParse<CharacterEnum>(val, true, out var belongTo))
                {
                    if (allCards != null)
                    {
                        foreach (var c in allCards)
                        {
                            if (c != null && c.BelongTo == belongTo) result.Add(c.Id);
                        }
                    }
                    return result;
                }

                if (string.Equals(key, "CardType", System.StringComparison.OrdinalIgnoreCase)
                    && System.Enum.TryParse<CardTypeEnum>(val, true, out var cardType))
                {
                    if (allCards != null)
                    {
                        foreach (var c in allCards)
                        {
                            // CardType 选择器默认限定升级所属角色，避免误伤其他角色同类卡
                            if (c != null && c.CardType == cardType && c.BelongTo == owner) result.Add(c.Id);
                        }
                    }
                    return result;
                }

                Debug.LogWarning($"[UpgradeEffectApplier] 无法识别的选卡器: {selector}");
                return result;
            }

            // 纯卡Id
            result.Add(sel);
            return result;
        }

        private static string Normalize(string s)
        {
            return string.IsNullOrEmpty(s) ? string.Empty : s.Trim().ToLowerInvariant();
        }
    }
}
