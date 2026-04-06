using System.Collections.Generic;

namespace Ashlight.Battle.Core.Engine
{
    /// <summary>
    /// 预测结果
    /// 包含模拟战斗后的状态信息
    /// 支持 ATB 系统的扩展预测数据
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
        /// 单位ID -> 护甲变化量
        /// </summary>
        public Dictionary<string, int> BlockChangeMap { get; set; }

        /// <summary>
        /// 单位ID -> Buff变化列表（新增的BuffId）
        /// </summary>
        public Dictionary<string, List<string>> BuffChangeMap { get; set; }

        /// <summary>
        /// 死亡单位ID列表
        /// </summary>
        public List<string> DeadUnits { get; set; }

        /// <summary>
        /// 将被击杀的目标列表
        /// </summary>
        public List<string> WillKillTargets { get; set; }

        /// <summary>
        /// 预计总伤害（所有目标累计）
        /// </summary>
        public int ExpectedDamage { get; set; }

        /// <summary>
        /// 预计过载负债变化
        /// </summary>
        public int ExpectedOverloadDebt { get; set; }

        /// <summary>
        /// 单位ID -> 行动条段位偏移预测
        /// </summary>
        public Dictionary<string, int> ExpectedSegmentShift { get; set; }

        /// <summary>
        /// 单位ID -> 预计下次起跑段位
        /// </summary>
        public Dictionary<string, int> ExpectedRestartSegment { get; set; }

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
            BlockChangeMap = new Dictionary<string, int>();
            BuffChangeMap = new Dictionary<string, List<string>>();
            DeadUnits = new List<string>();
            WillKillTargets = new List<string>();
            ExpectedSegmentShift = new Dictionary<string, int>();
            ExpectedRestartSegment = new Dictionary<string, int>();
            IsBattleEnded = false;
            IsPlayerVictory = false;
            ExpectedDamage = 0;
            ExpectedOverloadDebt = 0;
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
                summary += $"{unitId}: HP {changeStr} (最终: {finalHp})";

                if (BlockChangeMap.ContainsKey(unitId) && BlockChangeMap[unitId] != 0)
                {
                    int blockChange = BlockChangeMap[unitId];
                    string blockStr = blockChange > 0 ? $"+{blockChange}" : blockChange.ToString();
                    summary += $", 护甲 {blockStr}";
                }

                if (ExpectedSegmentShift.ContainsKey(unitId) && ExpectedSegmentShift[unitId] != 0)
                {
                    int shift = ExpectedSegmentShift[unitId];
                    string shiftStr = shift > 0 ? $"前进{shift}段" : $"后退{-shift}段";
                    summary += $", 行动条{shiftStr}";
                }

                summary += "\n";
            }

            if (WillKillTargets.Count > 0)
            {
                summary += $"将被击杀: {string.Join(", ", WillKillTargets)}\n";
            }

            if (ExpectedDamage > 0)
            {
                summary += $"预计总伤害: {ExpectedDamage}\n";
            }

            if (ExpectedOverloadDebt > 0)
            {
                summary += $"过载负债变化: +{ExpectedOverloadDebt}\n";
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

