using System.Collections.Generic;
using Ashlight.Battle.Core.Commands;

namespace Ashlight.Battle.Core.Data
{
    /// <summary>
    /// 时间轴上的最小单元
    /// 纯数据结构，支持深拷贝
    /// </summary>
    public class TimelineBlock
    {
        /// <summary>
        /// 当前阶段（Startup/Active/Recoil）
        /// </summary>
        public PhaseEnum Phase { get; set; }

        /// <summary>
        /// 执行者ID（角色或敌人的唯一标识）
        /// </summary>
        public string OwnerId { get; set; }

        /// <summary>
        /// 目标ID（可为null，表示无目标或自己）
        /// </summary>
        public string TargetId { get; set; }

        /// <summary>
        /// 具体指令列表（在Active阶段执行）
        /// </summary>
        public List<ICommand> Commands { get; set; }

        /// <summary>
        /// 优先级（用于排序，数值越高越先执行）
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// 源卡牌ID（用于调试和追踪）
        /// </summary>
        public string SourceCardId { get; set; }

        /// <summary>
        /// 是否为卡牌的最后一个Block（用于判断卡牌执行完毕）
        /// </summary>
        public bool IsLastBlock { get; set; }

        public TimelineBlock()
        {
            Commands = new List<ICommand>();
        }

        /// <summary>
        /// 深拷贝
        /// </summary>
        public TimelineBlock Clone()
        {
            var clone = new TimelineBlock
            {
                Phase = this.Phase,
                OwnerId = this.OwnerId,
                TargetId = this.TargetId,
                Priority = this.Priority,
                SourceCardId = this.SourceCardId,
                IsLastBlock = this.IsLastBlock,
                Commands = new List<ICommand>()
            };

            // 深拷贝Commands列表
            if (this.Commands != null)
            {
                foreach (var command in this.Commands)
                {
                    clone.Commands.Add(command.Clone());
                }
            }

            return clone;
        }

        /// <summary>
        /// 判断是否为空Block（无指令）
        /// </summary>
        public bool IsEmpty()
        {
            return Commands == null || Commands.Count == 0;
        }
    }
}

