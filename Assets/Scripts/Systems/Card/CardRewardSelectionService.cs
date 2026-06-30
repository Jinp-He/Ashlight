using System.Collections.Generic;
using Ashlight.Config;
using cfg.Character;

namespace Ashlight.Systems.Card
{
    /// <summary>
    /// 胜利后「升级三选一卡牌」的候选筛选服务。
    /// 从 TbCardInfo 中按「从属角色 / 未锁定」过滤，随机抽取若干张供玩家加入卡组。
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
                if (card.IsLocked) continue;
                pool.Add(card);
            }

            // 2. 洗牌后取前 count 个（Fisher–Yates）
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(0, i + 1);
                var tmp = pool[i];
                pool[i] = pool[j];
                pool[j] = tmp;
            }

            for (int i = 0; i < pool.Count && result.Count < count; i++)
            {
                result.Add(pool[i]);
            }

            return result;
        }
    }
}
