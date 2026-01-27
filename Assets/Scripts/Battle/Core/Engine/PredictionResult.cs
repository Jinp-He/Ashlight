using System.Collections.Generic;

namespace Ashlight.Battle.Core.Engine
{
    /// <summary>
    /// 预测结果
    /// 包含模拟战斗后的状态信息（基础版本）
    /// </summary>
    public class PredictionResult
    {
        /// <summary>
        /// 单位ID -> 最终HP
        /// </summary>
        public Dictionary<string, int> FinalHpMap { get; set; }

        /// <summary>
        /// 单位ID -> HP变化量（正数表示治疗，负数表示受伤）
        /// </summary>
        public Dictionary<string, int> HpChangeMap { get; set; }

        /// <summary>
        /// 死亡单位ID列表
        /// </summary>
        public List<string> DeadUnits { get; set; }

        /// <summary>
        /// 战斗是否结束
        /// </summary>
        public bool IsBattleEnded { get; set; }

        /// <summary>
        /// 玩家是否胜利
        /// </summary>
        public bool IsPlayerVictory { get; set; }

        public PredictionResult()
        {
            FinalHpMap = new Dictionary<string, int>();
            HpChangeMap = new Dictionary<string, int>();
            DeadUnits = new List<string>();
            IsBattleEnded = false;
            IsPlayerVictory = false;
        }

        /// <summary>
        /// 获取格式化的预测结果摘要
        /// </summary>
        public string GetSummary()
        {
            var summary = "=== 预测结果 ===\n";

            foreach (var kvp in HpChangeMap)
            {
                string unitId = kvp.Key;
                int hpChange = kvp.Value;
                int finalHp = FinalHpMap.ContainsKey(unitId) ? FinalHpMap[unitId] : 0;

                string changeStr = hpChange > 0 ? $"+{hpChange}" : hpChange.ToString();
                summary += $"{unitId}: HP {changeStr} (最终: {finalHp})\n";
            }

            if (DeadUnits.Count > 0)
            {
                summary += $"死亡单位: {string.Join(", ", DeadUnits)}\n";
            }

            if (IsBattleEnded)
            {
                summary += $"战斗结束 - {(IsPlayerVictory ? "玩家胜利" : "玩家失败")}\n";
            }

            return summary;
        }
    }
}

