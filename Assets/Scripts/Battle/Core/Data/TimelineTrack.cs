using System;
using cfg;
using UnityEngine;

namespace Ashlight.Battle.Core.Data
{
    /// <summary>
    /// 单个战斗单位的时间轴（固定长度15格）
    /// 支持位移操作（用于实现"推迟/打断"机制）
    /// </summary>
    public class TimelineTrack
    {
        /// <summary>
        /// 时间轴长度（固定格）
        /// </summary>
        public const int TrackLength = 10;

        /// <summary>
        /// 时间块数组
        /// </summary>
        public TimelineBlock[] Blocks { get; private set; }

        /// <summary>
        /// 时间轴所有者：角色ID（如果是玩家角色时间轴）
        /// 如果为 null，表示这是敌人共享时间轴
        /// </summary>
        public CharacterEnum? OwnerCharacterId { get; set; }

        /// <summary>
        /// 是否为敌人时间轴
        /// </summary>
        public bool IsEnemyTrack => OwnerCharacterId == null;

        /// <summary>
        /// 是否为玩家角色时间轴
        /// </summary>
        public bool IsPlayerTrack => OwnerCharacterId != null;

        public TimelineTrack()
        {
            Blocks = new TimelineBlock[TrackLength];
            OwnerCharacterId = null; // 默认为敌人时间轴
        }

        /// <summary>
        /// 创建玩家角色时间轴
        /// </summary>
        /// <param name="characterId">角色ID</param>
        public TimelineTrack(CharacterEnum characterId)
        {
            Blocks = new TimelineBlock[TrackLength];
            OwnerCharacterId = characterId;
        }

        /// <summary>
        /// 获取指定位置的Block
        /// </summary>
        public TimelineBlock GetBlock(int index)
        {
            if (index < 0 || index >= TrackLength)
            {
                throw new ArgumentOutOfRangeException(nameof(index), $"时间轴索引超出范围: {index}");
            }

            return Blocks[index];
        }

        /// <summary>
        /// 设置指定位置的Block
        /// </summary>
        public void SetBlock(int index, TimelineBlock block)
        {
            if (index < 0 || index >= TrackLength)
            {
                throw new ArgumentOutOfRangeException(nameof(index), $"时间轴索引超出范围: {index}");
            }

            Blocks[index] = block;
        }

        /// <summary>
        /// 插入Block到指定位置（会推移后续Block）
        /// </summary>
        public void InsertBlock(int index, TimelineBlock block)
        {
            if (index < 0 || index >= TrackLength)
            {
                throw new ArgumentOutOfRangeException(nameof(index), $"时间轴索引超出范围: {index}");
            }

            // 如果目标位置为空，直接插入
            if (Blocks[index] == null)
            {
                Blocks[index] = block;
                return;
            }

            // 否则，向后推移所有Block（最后一个会被丢弃）
            ShiftBlocks(index, 1);
            Blocks[index] = block;
        }

        /// <summary>
        /// 位移操作：将startIndex及之后的Blocks向后移动shiftAmount格
        /// </summary>
        /// <param name="startIndex">起始索引</param>
        /// <param name="shiftAmount">位移格数（正数向后，负数向前）</param>
        public void ShiftBlocks(int startIndex, int shiftAmount)
        {
            if (startIndex < 0 || startIndex >= TrackLength)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            }

            if (shiftAmount == 0) return;

            if (shiftAmount > 0)
            {
                // 向后位移（从后往前复制，避免覆盖）
                for (int i = TrackLength - 1; i >= startIndex + shiftAmount; i--)
                {
                    Blocks[i] = Blocks[i - shiftAmount];
                }

                // 清空被移出的位置
                for (int i = startIndex; i < startIndex + shiftAmount && i < TrackLength; i++)
                {
                    Blocks[i] = null;
                }
            }
            else
            {
                // 向前位移（从前往后复制）
                int absShift = Math.Abs(shiftAmount);
                for (int i = startIndex; i < TrackLength - absShift; i++)
                {
                    Blocks[i] = Blocks[i + absShift];
                }

                // 清空末尾位置
                for (int i = TrackLength - absShift; i < TrackLength; i++)
                {
                    Blocks[i] = null;
                }
            }
        }

        /// <summary>
        /// 清空指定位置的Block
        /// </summary>
        public void ClearBlock(int index)
        {
            if (index < 0 || index >= TrackLength)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            Blocks[index] = null;
        }

        /// <summary>
        /// 清空整个时间轴
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < TrackLength; i++)
            {
                Blocks[i] = null;
            }
        }

        /// <summary>
        /// 深拷贝
        /// </summary>
        public TimelineTrack Clone()
        {
            TimelineTrack clone;
            if (OwnerCharacterId.HasValue)
            {
                clone = new TimelineTrack(OwnerCharacterId.Value);
            }
            else
            {
                clone = new TimelineTrack();
            }

            for (int i = 0; i < TrackLength; i++)
            {
                if (Blocks[i] != null)
                {
                    clone.Blocks[i] = Blocks[i].Clone();
                }
            }

            return clone;
        }

        /// <summary>
        /// 检查时间轴是否为空
        /// </summary>
        public bool IsEmpty()
        {
            for (int i = 0; i < TrackLength; i++)
            {
                if (Blocks[i] != null && !Blocks[i].IsEmpty())
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 检查指定范围是否可以放置卡牌
        /// </summary>
        /// <param name="startIndex">起始索引</param>
        /// <param name="slotCount">需要占用的格子数</param>
        /// <returns>是否可以放置</returns>
        public bool CanPlaceCard(int startIndex, int slotCount)
        {
            // 检查索引范围
            if (startIndex < 0 || startIndex + slotCount > TrackLength)
            {
                return false;
            }

            // 检查范围内是否有已占用的格子
            for (int i = startIndex; i < startIndex + slotCount; i++)
            {
                if (Blocks[i] != null && !Blocks[i].IsEmpty())
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 放置卡牌到时间轴（批量设置Blocks）
        /// </summary>
        /// <param name="startIndex">起始索引</param>
        /// <param name="blocks">要放置的TimelineBlock列表</param>
        /// <returns>是否放置成功</returns>
        public bool PlaceCard(int startIndex, System.Collections.Generic.List<TimelineBlock> blocks)
        {
            if (blocks == null || blocks.Count == 0)
            {
                return false;
            }

            // 再次检查是否可以放置
            if (!CanPlaceCard(startIndex, blocks.Count))
            {
                return false;
            }

            // 放置所有Block
            for (int i = 0; i < blocks.Count; i++)
            {
                Blocks[startIndex + i] = blocks[i];
            }

            // Debug: 记录放置的卡牌/技能信息
            string trackType = IsEnemyTrack ? "敌人共享时间轴" : $"玩家时间轴({OwnerCharacterId})";
            string sourceId = blocks.Count > 0 && blocks[0] != null ? blocks[0].SourceCardId : "未知";
            string ownerId = blocks.Count > 0 && blocks[0] != null ? blocks[0].OwnerId : "未知";
            
            Debug.Log($"<color=orange>【TimelineTrack.PlaceCard】{trackType}</color>");
            Debug.Log($"<color=orange>  SourceCardId: {sourceId}, OwnerId: {ownerId}</color>");
            Debug.Log($"<color=orange>  起始位置: {startIndex}, Block数量: {blocks.Count}</color>");
            
            // 详细记录每个Block
            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (block != null)
                {
                    Debug.Log($"<color=orange>  Block[{startIndex + i}]: Phase={block.Phase}, Commands={block.Commands?.Count ?? 0}, IsLastBlock={block.IsLastBlock}</color>");
                }
            }
            
            // 打印当前时间轴完整状态
            Debug.Log($"<color=yellow>【时间轴当前状态】{trackType}</color>");
            for (int i = 0; i < TrackLength; i++)
            {
                var block = Blocks[i];
                if (block != null)
                {
                    Debug.Log($"<color=yellow>  [{i}]: {block.SourceCardId} (Phase={block.Phase}, Commands={block.Commands?.Count ?? 0})</color>");
                }
            }

            return true;
        }
        
        /// <summary>
        /// 从时间轴移除卡牌（清空指定范围的Blocks）
        /// </summary>
        /// <param name="startIndex">起始索引</param>
        /// <param name="slotCount">要清空的格子数</param>
        public void RemoveCard(int startIndex, int slotCount)
        {
            if (startIndex < 0 || startIndex >= TrackLength)
            {
                return;
            }
            
            for (int i = startIndex; i < startIndex + slotCount && i < TrackLength; i++)
            {
                Blocks[i] = null;
            }
        }
    }
}

