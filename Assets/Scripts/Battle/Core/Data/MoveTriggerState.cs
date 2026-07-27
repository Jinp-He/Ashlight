namespace Ashlight.Battle.Core.Data
{
    /// <summary>
    /// 回合内移动触发器（「这回合每次有角色移动就 XX」类效果的登记项）。
    /// 由 RegisterMoveTriggerCommand 写入 <see cref="BattleStateSnapshot.MoveTriggers"/>，
    /// 每次单位移动时由 MoveTriggerProcessor 逐个触发，回合结束时整表清空。
    /// </summary>
    public class MoveTriggerState
    {
        public const string TypeDamage = "Damage";   // 铁蒺藜：每次移动随机对一名敌人造成 Amount 伤害
        public const string TypeAddCard = "AddCard"; // 隧穿效应：每次移动把 CardId 加入手牌 Amount 张
        public const string TypeDraw = "Draw";       // 飒沓流星：满足移动条件时抽牌

        public const string ScopeAny = "Any";
        public const string ScopeFriendly = "Friendly";
        public const string ScopeOwner = "Owner";

        /// <summary>登记者（效果来源单位）Id。</summary>
        public string OwnerId { get; set; }

        /// <summary>触发类型：<see cref="TypeDamage"/> / <see cref="TypeAddCard"/>。</summary>
        public string TriggerType { get; set; }

        /// <summary>数值（Damage=每次伤害；AddCard=每次加牌张数）。</summary>
        public int Amount { get; set; }

        /// <summary>AddCard 专用：加入手牌的卡牌 Id。</summary>
        public string CardId { get; set; }

        /// <summary>已触发次数（也用作确定性伪随机的序号，保证预测与实战一致）。</summary>
        public int FireCount { get; set; }

        /// <summary>可触发的最大次数；0 表示不设上限。</summary>
        public int MaxFireCount { get; set; }

        /// <summary>移动者筛选：Any / Friendly（与登记者同阵营）/ Owner（仅登记者自身）。</summary>
        public string MoverScope { get; set; } = ScopeAny;

        public MoveTriggerState Clone()
        {
            return new MoveTriggerState
            {
                OwnerId = OwnerId,
                TriggerType = TriggerType,
                Amount = Amount,
                CardId = CardId,
                FireCount = FireCount,
                MaxFireCount = MaxFireCount,
                MoverScope = MoverScope
            };
        }
    }
}
