using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Ashlight.Battle;
using Ashlight.Config;
using Ashlight.State.Runtime;

namespace Scripts.UI
{
    /// <summary>
    /// 战斗卡牌对象池管理器
    /// 负责卡牌UI对象的创建、复用、移动和销毁
    /// </summary>
    public class BattleCardPoolManager
    {
        #region 私有字段

        /// <summary>
        /// 卡牌对象池：InstanceId -> CardViewController 的映射
        /// </summary>
        private Dictionary<string, CardViewController> _cardPool = new Dictionary<string, CardViewController>();

        /// <summary>
        /// 抽牌堆中的卡牌 UI（SetActive false）
        /// </summary>
        private List<CardViewController> _deckCards = new List<CardViewController>();

        /// <summary>
        /// 弃牌堆中的卡牌 UI（SetActive false）
        /// </summary>
        private List<CardViewController> _discardCards = new List<CardViewController>();

        /// <summary>
        /// 手牌列表（SetActive true）
        /// </summary>
        private List<CardViewController> _handCards = new List<CardViewController>();

        // 外部依赖
        private BattleManager _battleManager;
        private GameObject _cardViewControllerPrefab;
        private Transform _deckContainer;
        private Transform _discardContainer;
        private Transform _handContainer;

        #endregion

        #region 公共属性

        /// <summary>
        /// 获取手牌列表（只读）
        /// </summary>
        public IReadOnlyList<CardViewController> HandCards => _handCards;

        /// <summary>
        /// 获取抽牌堆列表（只读）
        /// </summary>
        public IReadOnlyList<CardViewController> DeckCards => _deckCards;

        /// <summary>
        /// 获取弃牌堆列表（只读）
        /// </summary>
        public IReadOnlyList<CardViewController> DiscardCards => _discardCards;

        /// <summary>
        /// 获取卡牌池数量
        /// </summary>
        public int PoolCount => _cardPool.Count;

        #endregion

        #region 公共方法

        /// <summary>
        /// 初始化管理器
        /// </summary>
        /// <param name="battleManager">战斗管理器</param>
        /// <param name="prefab">CardViewController预制体</param>
        /// <param name="deckContainer">抽牌堆容器</param>
        /// <param name="discardContainer">弃牌堆容器</param>
        /// <param name="handContainer">手牌容器</param>
        public void Initialize(
            BattleManager battleManager,
            GameObject prefab,
            Transform deckContainer,
            Transform discardContainer,
            Transform handContainer)
        {
            _battleManager = battleManager;
            _cardViewControllerPrefab = prefab;
            _deckContainer = deckContainer;
            _discardContainer = discardContainer;
            _handContainer = handContainer;
        }

        /// <summary>
        /// 初始化卡牌对象池（战斗开始时调用）
        /// 为牌组中的每张卡牌创建一个 CardViewController
        /// </summary>
        public void InitializePool()
        {
            Clear();

            if (_battleManager?.CurrentState?.DeckSystem == null)
            {
                Debug.LogError("[BattleCardPoolManager] 初始化卡牌池失败：BattleManager 或 DeckSystem 为空");
                return;
            }

            var deckSystem = _battleManager.CurrentState.DeckSystem;

            // 收集所有卡牌（抽牌堆 + 弃牌堆 + 手牌 + 使用中）
            var allCards = new List<CardRuntimeState>();
            allCards.AddRange(deckSystem.DrawPile);
            allCards.AddRange(deckSystem.DiscardPile);
            allCards.AddRange(deckSystem.Hand);
            allCards.AddRange(deckSystem.InPlayPile);

            Debug.Log($"[BattleCardPoolManager] 开始初始化卡牌池，共 {allCards.Count} 张卡牌");

            foreach (var cardState in allCards)
            {
                var cardInfo = ConfigLoader.Tables.TbCardInfo.GetOrDefault(cardState.CardId);
                if (cardInfo == null)
                {
                    Debug.LogWarning($"[BattleCardPoolManager] 未找到卡牌配置: {cardState.CardId}");
                    continue;
                }

                // 实例化到抽牌堆容器
                GameObject cardObj = Object.Instantiate(_cardViewControllerPrefab, _deckContainer);
                CardViewController cardView = cardObj.GetComponent<CardViewController>();

                if (cardView != null)
                {
                    // 使用 Reinitialize 初始化
                    cardView.Reinitialize(cardInfo, cardState.InstanceId, DescriptionMode.Battle);
                    cardView.Hide(); // 初始隐藏

                    _cardPool[cardState.InstanceId] = cardView;
                    _deckCards.Add(cardView);
                }
            }

            Debug.Log($"[BattleCardPoolManager] 卡牌池初始化完成，共 {_cardPool.Count} 张卡牌 UI");
        }

        /// <summary>
        /// 根据 InstanceId 从池中获取 CardViewController
        /// </summary>
        /// <param name="instanceId">卡牌实例ID</param>
        /// <returns>CardViewController，如果不存在则返回null</returns>
        public CardViewController GetCard(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                Debug.LogWarning("[BattleCardPoolManager] GetCard: instanceId 为空");
                return null;
            }
            return _cardPool.TryGetValue(instanceId, out var card) ? card : null;
        }

        /// <summary>
        /// 将卡牌移到手牌区
        /// </summary>
        /// <param name="card">要移动的卡牌</param>
        public void MoveToHand(CardViewController card)
        {
            if (card == null) return;

            card.transform.SetParent(_handContainer);
            card.ResetForReuse();
            card.Show();

            _deckCards.Remove(card);
            _discardCards.Remove(card);

            if (!_handCards.Contains(card))
                _handCards.Add(card);
        }

        /// <summary>
        /// 将卡牌移到弃牌堆
        /// </summary>
        /// <param name="card">要移动的卡牌</param>
        public void MoveToDiscard(CardViewController card)
        {
            if (card == null) return;

            card.transform.SetParent(_discardContainer);
            card.Hide();

            _handCards.Remove(card);
            _deckCards.Remove(card);

            if (!_discardCards.Contains(card))
                _discardCards.Add(card);
        }

        /// <summary>
        /// 将卡牌移到抽牌堆
        /// </summary>
        /// <param name="card">要移动的卡牌</param>
        public void MoveToDeck(CardViewController card)
        {
            if (card == null) return;

            card.transform.SetParent(_deckContainer);
            card.Hide();

            _handCards.Remove(card);
            _discardCards.Remove(card);

            if (!_deckCards.Contains(card))
                _deckCards.Add(card);
        }

        /// <summary>
        /// 从手牌列表中移除卡牌引用（不销毁，不隐藏）
        /// 用于卡牌放到时间轴时
        /// </summary>
        /// <param name="card">要移除的卡牌</param>
        public void RemoveFromHandList(CardViewController card)
        {
            if (card == null) return;
            _handCards.Remove(card);
            Debug.Log($"[BattleCardPoolManager] 从手牌列表移除卡牌: {card.GetCurrentCard()?.Name}");
        }

        /// <summary>
        /// UI 层洗牌：弃牌堆 -> 抽牌堆
        /// </summary>
        public void ReshuffleDiscardToDeck()
        {
            foreach (var card in _discardCards.ToList())
            {
                MoveToDeck(card);
            }
            Debug.Log($"[BattleCardPoolManager] UI层洗牌完成，抽牌堆: {_deckCards.Count} 张");
        }

        /// <summary>
        /// 清空手牌 UI（移到弃牌堆而非销毁）
        /// </summary>
        public void ClearHandCards()
        {
            foreach (var card in _handCards.ToList())
            {
                MoveToDiscard(card);
            }
            _handCards.Clear();
        }

        /// <summary>
        /// 清空卡牌池（战斗结束时调用）
        /// </summary>
        public void Clear()
        {
            foreach (var card in _cardPool.Values)
            {
                if (card != null)
                    Object.Destroy(card.gameObject);
            }
            _cardPool.Clear();
            _deckCards.Clear();
            _handCards.Clear();
            _discardCards.Clear();
            Debug.Log("[BattleCardPoolManager] 卡牌池已清空");
        }

        #endregion
    }
}
