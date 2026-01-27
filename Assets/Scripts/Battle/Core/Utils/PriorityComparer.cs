using System.Collections.Generic;
using Ashlight.Battle.Core.Data;

namespace Ashlight.Battle.Core.Utils
{
    /// <summary>
    /// TimelineBlock优先级比较器
    /// 优先级规则（从高到低）：
    /// Defense/Intercept(100) > Swift(90) > Attack(80) > Heal(70) > TimeShift(60) > Buff(50) > Others(0)
    /// </summary>
    public class PriorityComparer : IComparer<TimelineBlock>
    {
        public int Compare(TimelineBlock x, TimelineBlock y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return 1;  // null排在后面
            if (y == null) return -1;

            // 按优先级降序排序（数值越大越靠前）
            int priorityComparison = y.Priority.CompareTo(x.Priority);
            
            if (priorityComparison != 0)
            {
                return priorityComparison;
            }

            // 如果优先级相同，保持原有顺序（稳定排序）
            return 0;
        }
    }
}

