using System;
using System.Collections.Generic;
using System.Linq;
using Ashlight.State.Runtime;
using UnityEngine;

namespace Ashlight.Battle.Core.Data
{
    /// <summary>
    /// 战斗卡组系统
    /// 管理抽牌堆、弃牌堆、手牌区（类似杀戮尖塔）
    /// POCO类，支持深拷贝用于预测系统
    /// </summary>
    public class BattleDeckSystem
    {
        /// <summary>
        /// 抽牌堆
        /// </summary>
        public List<CardRuntimeState> DrawPile { get; set; }

        /// <summary>
        /// 弃牌堆
        /// </summary>
        public List<CardRuntimeState> DiscardPile { get; set; }

        /// <summary>
        /// 手牌区
        /// </summary>
        public List<CardRuntimeState> Hand { get; set; }

        /// <summary>
        /// 已移除的卡牌（消耗型卡牌使用后放入此处）
        /// </summary>
        public List<CardRuntimeState> RemovedPile { get; set; }

        /// <summary>
        /// 正在时间轴上的卡牌（已打出但未执行完毕）
        /// </summary>
        public List<CardRuntimeState> InPlayPile { get; set; }

        /// <summary>
        /// 随机数生成器（用于洗牌）
        /// </summary>
        private System.Random _random;

        public BattleDeckSystem()
        {
            DrawPile = new List<CardRuntimeState>();
            DiscardPile = new List<CardRuntimeState>();
            Hand = new List<CardRuntimeState>();
            RemovedPile = new List<CardRuntimeState>();
            InPlayPile = new List<CardRuntimeState>();
            _random = new System.Random();
        }

        /// <summary>
        /// 初始化卡组
        /// </summary>
        /// <param name="cards">所有卡牌列表</param>
        public void Initialize(List<CardRuntimeState> cards)
        {
            if (cards == null)
            {
                Debug.LogError("[BattleDeckSystem] 初始化卡组失败：卡牌列表为null");
                return;
            }

            // 清空所有区域
            DrawPile.Clear();
            DiscardPile.Clear();
            Hand.Clear();
            RemovedPile.Clear();
            InPlayPile.Clear();

            // 将所有卡牌放入抽牌堆
            foreach (var card in cards)
            {
                if (card != null)
                {
                    DrawPile.Add(card.Clone());
                }
            }

            Debug.Log($"[BattleDeckSystem] 卡组初始化完成，抽牌堆: {DrawPile.Count} 张");
        }

        /// <summary>
        /// 洗牌（使用Fisher-Yates洗牌算法）
        /// </summary>
        public void ShuffleDeck()
        {
            int n = DrawPile.Count;
            for (int i = n - 1; i > 0; i--)
            {
                int j = _random.Next(0, i + 1);
                // 交换
                var temp = DrawPile[i];
                DrawPile[i] = DrawPile[j];
                DrawPile[j] = temp;
            }

            Debug.Log($"[BattleDeckSystem] 抽牌堆已洗牌: {DrawPile.Count} 张");
        }

        /// <summary>
        /// 抽牌
        /// </summary>
        /// <param name="count">抽牌数量</param>
        /// <returns>实际抽到的卡牌数量</returns>
        public int DrawCard(int count)
        {
            int drawnCount = 0;

            for (int i = 0; i < count; i++)
            {
                // 如果抽牌堆为空，将弃牌堆洗入抽牌堆
                if (DrawPile.Count == 0)
                {
                    if (DiscardPile.Count == 0)
                    {
                        Debug.LogWarning("[BattleDeckSystem] 抽牌堆和弃牌堆都为空，无法继续抽牌");
                        break;
                    }

                    Debug.Log($"[BattleDeckSystem] 抽牌堆为空，将弃牌堆 {DiscardPile.Count} 张卡牌洗入抽牌堆");
                    ReshuffleDiscardIntoDraw();
                }

                if (DrawPile.Count > 0)
                {
                    var card = DrawPile[0];
                    DrawPile.RemoveAt(0);
                    Hand.Add(card);
                    drawnCount++;
                    Debug.Log($"[BattleDeckSystem] 抽牌: {card.CardId}");
                }
            }

            Debug.Log($"[BattleDeckSystem] 抽牌完成: {drawnCount}/{count}，当前手牌: {Hand.Count} 张");
            return drawnCount;
        }

        /// <summary>
        /// 弃牌
        /// </summary>
        /// <param name="card">要弃掉的卡牌</param>
        /// <returns>是否成功弃牌</returns>
        public bool DiscardCard(CardRuntimeState card)
        {
            if (card == null)
            {
                Debug.LogError("[BattleDeckSystem] 弃牌失败：卡牌为null");
                return false;
            }

            if (!Hand.Contains(card))
            {
                Debug.LogWarning($"[BattleDeckSystem] 弃牌失败：手牌中不存在卡牌 {card.CardId}");
                return false;
            }

            Hand.Remove(card);
            DiscardPile.Add(card);
            Debug.Log($"[BattleDeckSystem] 弃牌: {card.CardId}");
            return true;
        }

        /// <summary>
        /// 使用卡牌（从手牌移除，放入弃牌堆）
        /// </summary>
        /// <param name="card">要使用的卡牌</param>
        /// <param name="isExhaust">是否为消耗型卡牌（消耗型卡牌不进入弃牌堆，而是移除）</param>
        /// <returns>是否成功使用</returns>
        public bool UseCard(CardRuntimeState card, bool isExhaust = false)
        {
            if (card == null)
            {
                Debug.LogError("[BattleDeckSystem] 使用卡牌失败：卡牌为null");
                return false;
            }

            if (!Hand.Contains(card))
            {
                Debug.LogWarning($"[BattleDeckSystem] 使用卡牌失败：手牌中不存在卡牌 {card.CardId}");
                return false;
            }

            Hand.Remove(card);

            if (isExhaust)
            {
                RemovedPile.Add(card);
                Debug.Log($"[BattleDeckSystem] 使用并消耗卡牌: {card.CardId}");
            }
            else
            {
                DiscardPile.Add(card);
                Debug.Log($"[BattleDeckSystem] 使用卡牌: {card.CardId}");
            }

            return true;
        }

        /// <summary>
        /// 将卡牌放置到时间轴（从手牌移到InPlayPile）
        /// </summary>
        /// <param name="card">要放置的卡牌</param>
        /// <returns>是否成功</returns>
        public bool PlayCardToTimeline(CardRuntimeState card)
        {
            if (card == null)
            {
                Debug.LogError("[BattleDeckSystem] 放置卡牌到时间轴失败：卡牌为null");
                return false;
            }

            if (!Hand.Contains(card))
            {
                Debug.LogWarning($"[BattleDeckSystem] 放置卡牌到时间轴失败：手牌中不存在卡牌 {card.CardId}");
                return false;
            }

            Hand.Remove(card);
            InPlayPile.Add(card);
            Debug.Log($"[BattleDeckSystem] 卡牌放置到时间轴: {card.CardId}，当前InPlayPile: {InPlayPile.Count} 张");
            return true;
        }

        /// <summary>
        /// 通过卡牌ID将卡牌放置到时间轴
        /// </summary>
        /// <param name="cardId">卡牌ID</param>
        /// <returns>是否成功</returns>
        public bool PlayCardToTimelineById(string cardId)
        {
            var card = Hand.FirstOrDefault(c => c.CardId == cardId);
            if (card == null)
            {
                Debug.LogWarning($"[BattleDeckSystem] 放置卡牌到时间轴失败：手牌中不存在卡牌ID {cardId}");
                return false;
            }
            return PlayCardToTimeline(card);
        }

        /// <summary>
        /// 通过 InstanceId 将卡牌放置到时间轴
        /// </summary>
        /// <param name="instanceId">卡牌实例ID</param>
        /// <returns>是否成功</returns>
        public bool PlayCardToTimelineByInstanceId(string instanceId)
        {
            var card = Hand.FirstOrDefault(c => c.InstanceId == instanceId);
            if (card == null)
            {
                Debug.LogWarning($"[BattleDeckSystem] 放置卡牌到时间轴失败：手牌中不存在 InstanceId={instanceId}");
                return false;
            }
            return PlayCardToTimeline(card);
        }

        /// <summary>
        /// 卡牌执行完毕，从InPlayPile移到弃牌堆
        /// </summary>
        /// <param name="cardId">卡牌ID</param>
        /// <param name="isExhaust">是否为消耗型卡牌</param>
        /// <returns>是否成功</returns>
        public bool FinishPlayingCard(string cardId, bool isExhaust = false)
        {
            var card = InPlayPile.FirstOrDefault(c => c.CardId == cardId);
            if (card == null)
            {
                Debug.LogWarning($"[BattleDeckSystem] 卡牌执行完毕处理失败：InPlayPile中不存在卡牌 {cardId}");
                return false;
            }

            InPlayPile.Remove(card);

            if (isExhaust)
            {
                RemovedPile.Add(card);
                Debug.Log($"[BattleDeckSystem] 卡牌执行完毕并消耗: {cardId}");
            }
            else
            {
                DiscardPile.Add(card);
                Debug.Log($"[BattleDeckSystem] 卡牌执行完毕，移入弃牌堆: {cardId}");
            }

            return true;
        }

        /// <summary>
        /// 卡牌执行完毕，从InPlayPile移到弃牌堆（通过 InstanceId 查找）
        /// </summary>
        /// <param name="instanceId">卡牌实例ID</param>
        /// <param name="isExhaust">是否为消耗型卡牌</param>
        /// <returns>是否成功</returns>
        public bool FinishPlayingCardByInstanceId(string instanceId, bool isExhaust = false)
        {
            var card = InPlayPile.FirstOrDefault(c => c.InstanceId == instanceId);
            if (card == null)
            {
                Debug.LogWarning($"[BattleDeckSystem] 卡牌执行完毕处理失败：InPlayPile中不存在 InstanceId={instanceId}");
                return false;
            }

            InPlayPile.Remove(card);

            if (isExhaust)
            {
                RemovedPile.Add(card);
                Debug.Log($"[BattleDeckSystem] 卡牌执行完毕并消耗: {card.CardId} (InstanceId: {instanceId})");
            }
            else
            {
                DiscardPile.Add(card);
                Debug.Log($"[BattleDeckSystem] 卡牌执行完毕，移入弃牌堆: {card.CardId} (InstanceId: {instanceId})");
            }

            return true;
        }

        /// <summary>
        /// 将卡牌从时间轴返回手牌（用于取消放置）
        /// </summary>
        /// <param name="cardId">卡牌ID</param>
        /// <returns>是否成功</returns>
        public bool ReturnCardToHand(string cardId)
        {
            var card = InPlayPile.FirstOrDefault(c => c.CardId == cardId);
            if (card == null)
            {
                Debug.LogWarning($"[BattleDeckSystem] 返回卡牌到手牌失败：InPlayPile中不存在卡牌 {cardId}");
                return false;
            }

            InPlayPile.Remove(card);
            Hand.Add(card);
            Debug.Log($"[BattleDeckSystem] 卡牌返回手牌: {cardId}");
            return true;
        }

        /// <summary>
        /// 弃掉所有手牌
        /// </summary>
        public void DiscardAllHand()
        {
            int count = Hand.Count;
            DiscardPile.AddRange(Hand);
            Hand.Clear();
            Debug.Log($"[BattleDeckSystem] 弃掉所有手牌: {count} 张");
        }

        /// <summary>
        /// 将弃牌堆洗入抽牌堆
        /// </summary>
        private void ReshuffleDiscardIntoDraw()
        {
            DrawPile.AddRange(DiscardPile);
            DiscardPile.Clear();
            ShuffleDeck();
        }

        /// <summary>
        /// 获取卡组总数（抽牌堆 + 弃牌堆 + 手牌）
        /// </summary>
        public int GetTotalCardCount()
        {
            return DrawPile.Count + DiscardPile.Count + Hand.Count + InPlayPile.Count;
        }

        /// <summary>
        /// 深拷贝（用于预测系统）
        /// </summary>
        public BattleDeckSystem Clone()
        {
            var clone = new BattleDeckSystem
            {
                DrawPile = new List<CardRuntimeState>(),
                DiscardPile = new List<CardRuntimeState>(),
                Hand = new List<CardRuntimeState>(),
                RemovedPile = new List<CardRuntimeState>(),
                InPlayPile = new List<CardRuntimeState>()
            };

            // 深拷贝抽牌堆
            foreach (var card in DrawPile)
            {
                clone.DrawPile.Add(card.Clone());
            }

            // 深拷贝弃牌堆
            foreach (var card in DiscardPile)
            {
                clone.DiscardPile.Add(card.Clone());
            }

            // 深拷贝手牌区
            foreach (var card in Hand)
            {
                clone.Hand.Add(card.Clone());
            }

            // 深拷贝移除区
            foreach (var card in RemovedPile)
            {
                clone.RemovedPile.Add(card.Clone());
            }

            // 深拷贝正在使用区
            foreach (var card in InPlayPile)
            {
                clone.InPlayPile.Add(card.Clone());
            }

            return clone;
        }

        /// <summary>
        /// 获取调试信息
        /// </summary>
        public string GetDebugInfo()
        {
            return $"抽牌堆: {DrawPile.Count}, 弃牌堆: {DiscardPile.Count}, 手牌: {Hand.Count}, 使用中: {InPlayPile.Count}, 移除: {RemovedPile.Count}";
        }
    }
}

