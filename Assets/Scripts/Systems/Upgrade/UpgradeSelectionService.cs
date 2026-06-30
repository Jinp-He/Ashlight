using System.Collections.Generic;
using Ashlight.Config;
using Ashlight.Systems.Character;
using cfg;
using cfg.Character;

namespace Ashlight.Systems.Upgrade
{
    /// <summary>
    /// 升级三选一的候选筛选服务。
    /// 从 TbUpgradeOptions 中按「从属角色 / 未拥有 / 前置已满足」过滤，随机抽取若干个供玩家选择。
    /// </summary>
    public static class UpgradeSelectionService
    {
        private static readonly System.Random _rng = new System.Random();

        /// <summary>
        /// 为指定角色抽取若干个可选升级。
        /// </summary>
        /// <param name="character">角色枚举（升级 BelongTo 需匹配）</param>
        /// <param name="count">期望抽取数量（候选不足时返回实际数量）</param>
        public static List<UpgradeOptions> GetChoices(CharacterEnum character, int count)
        {
            var result = new List<UpgradeOptions>();

            var tables = ConfigLoader.Tables;
            var all = tables?.TbUpgradeOptions?.DataList;
            if (all == null || count <= 0)
            {
                return result;
            }

            var charState = CharacterSystem.GetCharacterState(character);
            var acquired = charState?.AcquiredUpgrades;

            // 1. 过滤出合法候选
            var pool = new List<UpgradeOptions>();
            foreach (var opt in all)
            {
                if (opt == null) continue;
                if (opt.BelongTo != character) continue;

                // 已拥有则跳过
                if (acquired != null && acquired.Contains(opt.Id)) continue;

                // 前置条件：无前置，或前置已拥有
                if (!string.IsNullOrEmpty(opt.Prerequisite))
                {
                    if (acquired == null || !acquired.Contains(opt.Prerequisite)) continue;
                }

                pool.Add(opt);
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
