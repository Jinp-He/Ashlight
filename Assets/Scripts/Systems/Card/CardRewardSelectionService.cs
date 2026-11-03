using System.Collections.Generic;
using Ashlight.Config;
using cfg.Character;

namespace Ashlight.Systems.Card
{
    /// <summary>
    /// 胜利后「升级三选一卡牌」的候选筛选服务。
    /// 从 TbCardInfo 中按「从属角色 / 未锁定 / IsInUpgrade」过滤，随机抽取若干张供玩家加入卡组。
    /// 与 <see cref="Ashlight.Systems.Upgrade.UpgradeSelectionService"/> 同构。
    /// </summary>
    public static class CardRewardSelectionService
    {
        private static readonly System.Random _rng = new System.Random();

        /// <summary>
        /// 为指定角色抽取若干张可选卡牌。
        /// </summary>
        /// <param name="character">角色枚举（卡牌 BelongTo 需匹配）</param>
        /// <param name="count">期望抽取数量（候选不足时返回实际数量）</param>
        public static List<CardInfo> GetChoices(cfg.CharacterEnum character, int count)
        {
            var result = new List<CardInfo>();

            var all = ConfigLoader.Tables?.TbCardInfo?.DataList;
            if (all == null || count <= 0)
            {
                return result;
            }

            // 1. 过滤合法候选
            var pool = new List<CardInfo>();
            foreach (var card in all)
            {
                if (card == null) continue;
                if (card.BelongTo != character) continue;
                if (card.Rarity == cfg.RarityEnum.Temporary ||
                    card.Rarity == cfg.RarityEnum.Basic) continue;
                if (card.IsLocked) continue;
                if (!card.IsInUpgrade) continue;   // 仅纳入被标记为可升级三选一的卡牌
                pool.Add(card);
            }

            // 2. 按稀有度加权、无放回抽取。普通/稀有/史诗权重为 60/30/10，
            // 避免所有稀有度实际等概率，也不会在同一次三选一里重复同一张牌。
            while (pool.Count > 0 && result.Count < count)
            {
                int totalWeight = 0;
                foreach (var card in pool)
                {
                    totalWeight += GetRarityWeight(card.Rarity);
                }

                int roll = _rng.Next(0, totalWeight);
                int accumulated = 0;
                int pickedIndex = 0;
                for (int i = 0; i < pool.Count; i++)
                {
                    accumulated += GetRarityWeight(pool[i].Rarity);
                    if (roll < accumulated)
                    {
                        pickedIndex = i;
                        break;
                    }
                }

                result.Add(pool[pickedIndex]);
                pool.RemoveAt(pickedIndex);
            }

            return result;
        }

        private static int GetRarityWeight(cfg.RarityEnum rarity)
        {
            switch (rarity)
            {
                case cfg.RarityEnum.Temporary:
                case cfg.RarityEnum.Basic:
                    return 0;
                case cfg.RarityEnum.Epic:
                    return 10;
                case cfg.RarityEnum.Rare:
                    return 30;
                default:
                    return 60;
            }
        }
    }
}
