using System.Collections.Generic;

namespace Ashlight.Battle.Core.Data
{
    /// <summary>
    /// 单张卡牌（按 CardId）的累积修正。
    /// 由升级系统（UpgradeEffectApplier）在战斗开始时填充，
    /// CardPlayResolver 在生成 Command 时叠加到卡牌的基础数值上。
    /// </summary>
    public class CardModifier
    {
        /// <summary>攻击伤害增减（AttackEffect.Damage += DamageDelta）</summary>
        public int DamageDelta;

        /// <summary>护甲增减（DefenseEffect.Value += DefenseDelta）</summary>
        public int DefenseDelta;

        /// <summary>强制 AOE 覆盖（null=不覆盖，沿用卡牌原值）</summary>
        public bool? ForceAoe;

        public CardModifier Clone()
        {
            return new CardModifier
            {
                DamageDelta = DamageDelta,
                DefenseDelta = DefenseDelta,
                ForceAoe = ForceAoe
            };
        }
    }

    /// <summary>
    /// 战斗内卡牌修正表：CardId -> CardModifier。
    /// 挂在 BattleStateSnapshot 上，随快照一起深拷贝（预测系统复用）。
    /// </summary>
    public class CardModifierRegistry
    {
        private readonly Dictionary<string, CardModifier> _byCardId = new Dictionary<string, CardModifier>();

        /// <summary>查询某卡的修正，无则返回 null。</summary>
        public CardModifier Get(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return null;
            return _byCardId.TryGetValue(cardId, out var m) ? m : null;
        }

        /// <summary>取出或新建某卡的修正条目（用于累加）。</summary>
        public CardModifier GetOrCreate(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return null;
            if (!_byCardId.TryGetValue(cardId, out var m))
            {
                m = new CardModifier();
                _byCardId[cardId] = m;
            }
            return m;
        }

        public bool HasAny => _byCardId.Count > 0;

        public CardModifierRegistry Clone()
        {
            var clone = new CardModifierRegistry();
            foreach (var kv in _byCardId)
            {
                clone._byCardId[kv.Key] = kv.Value.Clone();
            }
            return clone;
        }
    }
}
