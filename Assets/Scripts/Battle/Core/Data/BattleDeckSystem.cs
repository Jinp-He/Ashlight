using System.Collections.Generic;
using System.Linq;
using Ashlight.Config;
using Ashlight.State.Runtime;
using cfg;
using UnityEngine;

namespace Ashlight.Battle.Core.Data
{
    /// <summary>
    /// 单个参战角色的抽牌堆 + 弃牌堆（与全局手牌区配合使用）
    /// </summary>
    public sealed class CharacterDeckSplit
    {
        public List<CardRuntimeState> DrawPile { get; set; }
        public List<CardRuntimeState> DiscardPile { get; set; }

        public CharacterDeckSplit()
        {
            DrawPile = new List<CardRuntimeState>();
            DiscardPile = new List<CardRuntimeState>();
        }

        public CharacterDeckSplit Clone()
        {
            var clone = new CharacterDeckSplit();
            foreach (var c in DrawPile)
            {
                if (c != null)
                {
                    clone.DrawPile.Add(c.Clone());
                }
            }

            foreach (var c in DiscardPile)
            {
                if (c != null)
                {
                    clone.DiscardPile.Add(c.Clone());
                }
            }

            return clone;
        }
    }

    /// <summary>
    /// 战斗卡组系统：每名玩家角色独立抽/弃牌堆，开局按 BelongTo 分堆并各自洗牌。
    /// 手牌数据仍放在同一个列表中，以便 UI 同时展示全队的下一手牌；每张牌通过动态归属区分主人。
    /// </summary>
    public class BattleDeckSystem
    {
        /// <summary>
        /// 参战角色顺序（用于归属兜底、兼容旧 DrawCard）
        /// </summary>
        private readonly List<CharacterEnum> _partyCharacterOrder = new List<CharacterEnum>();

        /// <summary>
        /// 每名角色的抽牌堆与弃牌堆
        /// </summary>
        public Dictionary<CharacterEnum, CharacterDeckSplit> PerCharacterDecks { get; set; }

        /// <summary>
        /// 抽牌堆（过渡期保留，分堆后通常为空）
        /// </summary>
        public List<CardRuntimeState> DrawPile { get; set; }

        /// <summary>
        /// 弃牌堆（过渡期保留，分堆后通常为空）
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

        private System.Random _random;

        public BattleDeckSystem()
        {
            PerCharacterDecks = new Dictionary<CharacterEnum, CharacterDeckSplit>();
            DrawPile = new List<CardRuntimeState>();
            DiscardPile = new List<CardRuntimeState>();
            Hand = new List<CardRuntimeState>();
            RemovedPile = new List<CardRuntimeState>();
            InPlayPile = new List<CardRuntimeState>();
            _random = new System.Random();
        }

        /// <summary>
        /// 按参战角色拆分牌堆：每张牌根据 CardInfo.BelongTo 放入对应角色抽牌堆并各自洗牌。
        /// </summary>
        public void Initialize(List<CardRuntimeState> cards, IList<CharacterEnum> partyCharacters)
        {
            if (cards == null)
            {
                Debug.LogError("[BattleDeckSystem] 初始化卡组失败：卡牌列表为null");
                return;
            }

            DrawPile.Clear();
            DiscardPile.Clear();
            Hand.Clear();
            RemovedPile.Clear();
            InPlayPile.Clear();
            PerCharacterDecks.Clear();
            _partyCharacterOrder.Clear();

            if (partyCharacters == null || partyCharacters.Count == 0)
            {
                Debug.LogError("[BattleDeckSystem] 参战角色列表为空，无法按角色分堆");
                return;
            }

            var seen = new HashSet<CharacterEnum>();
            foreach (var p in partyCharacters)
            {
                if (seen.Add(p))
                {
                    _partyCharacterOrder.Add(p);
                    PerCharacterDecks[p] = new CharacterDeckSplit();
                }
            }

            foreach (var card in cards)
            {
                if (card == null)
                {
                    continue;
                }

                var c = card.Clone();
                var info = ConfigLoader.Tables?.TbCardInfo?.GetOrDefault(c.CardId);
                CharacterEnum owner = info != null ? info.BelongTo : _partyCharacterOrder[0];

                if (!PerCharacterDecks.ContainsKey(owner))
                {
                    Debug.LogWarning($"[BattleDeckSystem] 卡牌 {c.CardId} 归属 {owner} 不在本战队伍中，归入队伍首位 {_partyCharacterOrder[0]}");
                    owner = _partyCharacterOrder[0];
                }

                // 初始牌也写入运行时归属。这样全队手牌同时存在时，弃牌/预览不会混淆角色。
                c.OwnerCharacterId = owner;
                PerCharacterDecks[owner].DrawPile.Add(c);
            }

            foreach (var split in PerCharacterDecks.Values)
            {
                ShufflePile(split.DrawPile);
            }

            int total = 0;
            foreach (var kv in PerCharacterDecks)
            {
                total += kv.Value.DrawPile.Count;
                Debug.Log($"[BattleDeckSystem] 角色 {kv.Key} 抽牌堆: {kv.Value.DrawPile.Count} 张（已洗牌）");
            }

            Debug.Log($"[BattleDeckSystem] 按角色分堆完成，合计 {total} 张");
        }

        /// <summary>
        /// 兼容旧调用：将所有牌放入单一抽牌堆（不推荐）
        /// </summary>
        public void Initialize(List<CardRuntimeState> cards)
        {
            if (cards == null)
            {
                Debug.LogError("[BattleDeckSystem] 初始化卡组失败：卡牌列表为null");
                return;
            }

            DrawPile.Clear();
            DiscardPile.Clear();
            Hand.Clear();
            RemovedPile.Clear();
            InPlayPile.Clear();
            PerCharacterDecks.Clear();
            _partyCharacterOrder.Clear();

            foreach (var card in cards)
            {
                if (card != null)
                {
                    DrawPile.Add(card.Clone());
                }
            }

            ShuffleDeck();
            Debug.Log($"[BattleDeckSystem] 单堆模式初始化，抽牌堆: {DrawPile.Count} 张");
        }

        private void ShufflePile(List<CardRuntimeState> pile)
        {
            if (pile == null)
            {
                return;
            }

            int n = pile.Count;
            for (int i = n - 1; i > 0; i--)
            {
                int j = _random.Next(0, i + 1);
                var temp = pile[i];
                pile[i] = pile[j];
                pile[j] = temp;
            }
        }

        /// <summary>
        /// 洗牌：各角色抽牌堆分别洗牌；若仍存在旧版全局 DrawPile 亦洗牌。
        /// </summary>
        public void ShuffleDeck()
        {
            if (PerCharacterDecks != null)
            {
                foreach (var split in PerCharacterDecks.Values)
                {
                    ShufflePile(split.DrawPile);
                }
            }

            int n = DrawPile.Count;
            for (int i = n - 1; i > 0; i--)
            {
                int j = _random.Next(0, i + 1);
                var temp = DrawPile[i];
                DrawPile[i] = DrawPile[j];
                DrawPile[j] = temp;
            }

            Debug.Log("[BattleDeckSystem] ShuffleDeck 完成");
        }

        /// <summary>
        /// 从指定角色的牌堆顶抽牌；该角色抽牌堆空则将其弃牌堆洗回抽牌堆再继续。
        /// </summary>
        public int DrawCardForCharacter(CharacterEnum characterId, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            if (!PerCharacterDecks.TryGetValue(characterId, out var split) || split == null)
            {
                Debug.LogWarning($"[BattleDeckSystem] 未找到角色牌堆: {characterId}");
                return 0;
            }

            int drawnCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (split.DrawPile.Count == 0)
                {
                    if (split.DiscardPile.Count == 0)
                    {
                        Debug.LogWarning($"[BattleDeckSystem] 角色 {characterId} 抽牌堆与弃牌堆均为空");
                        break;
                    }

                    ReshuffleCharacterDiscardIntoDraw(split);
                }

                if (split.DrawPile.Count > 0)
                {
                    var card = split.DrawPile[0];
                    split.DrawPile.RemoveAt(0);
                    card.OwnerCharacterId = characterId;
                    Hand.Add(card);
                    drawnCount++;
                    Debug.Log($"[BattleDeckSystem] 抽牌({characterId}): {card.CardId}");
                }
            }

            Debug.Log($"[BattleDeckSystem] 角色 {characterId} 抽牌完成: {drawnCount}/{count}，当前手牌: {Hand.Count}");
            return drawnCount;
        }

        private void ReshuffleCharacterDiscardIntoDraw(CharacterDeckSplit split)
        {
            if (split == null || split.DiscardPile.Count == 0)
            {
                return;
            }

            split.DrawPile.AddRange(split.DiscardPile);
            split.DiscardPile.Clear();
            ShufflePile(split.DrawPile);
            Debug.Log("[BattleDeckSystem] 角色牌堆：弃牌洗回抽牌堆");
        }

        /// <summary>
        /// 兼容旧逻辑：对队伍首位角色抽牌；无分堆时走单堆抽顶。
        /// </summary>
        public int DrawCard(int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            if (_partyCharacterOrder.Count > 0 && PerCharacterDecks != null && PerCharacterDecks.Count > 0)
            {
                return DrawCardForCharacter(_partyCharacterOrder[0], count);
            }

            int drawnCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (DrawPile.Count == 0)
                {
                    if (DiscardPile.Count == 0)
                    {
                        Debug.LogWarning("[BattleDeckSystem] 抽牌堆和弃牌堆都为空，无法继续抽牌");
                        break;
                    }

                    DrawPile.AddRange(DiscardPile);
                    DiscardPile.Clear();
                    ShufflePile(DrawPile);
                }

                if (DrawPile.Count > 0)
                {
                    var card = DrawPile[0];
                    DrawPile.RemoveAt(0);
                    Hand.Add(card);
                    drawnCount++;
                }
            }

            return drawnCount;
        }

        private CharacterEnum ResolveCharacterForCard(CardRuntimeState card)
        {
            if (card == null)
            {
                return _partyCharacterOrder.Count > 0 ? _partyCharacterOrder[0] : default;
            }

            // 运行时归属优先：诅咒、衍生牌等可能与静态 CardInfo.BelongTo 不同。
            if (card.OwnerCharacterId.HasValue
                && PerCharacterDecks != null
                && PerCharacterDecks.ContainsKey(card.OwnerCharacterId.Value))
            {
                return card.OwnerCharacterId.Value;
            }

            var info = ConfigLoader.Tables?.TbCardInfo?.GetOrDefault(card.CardId);
            if (info != null && PerCharacterDecks != null && PerCharacterDecks.ContainsKey(info.BelongTo))
            {
                return info.BelongTo;
            }

            return _partyCharacterOrder.Count > 0 ? _partyCharacterOrder[0] : default;
        }

        private CharacterDeckSplit GetSplitForDiscard(CardRuntimeState card)
        {
            var ch = ResolveCharacterForCard(card);
            if (PerCharacterDecks != null && PerCharacterDecks.TryGetValue(ch, out var split) && split != null)
            {
                return split;
            }

            if (_partyCharacterOrder.Count > 0 && PerCharacterDecks != null &&
                PerCharacterDecks.TryGetValue(_partyCharacterOrder[0], out var first) && first != null)
            {
                return first;
            }

            return null;
        }

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
            var split = GetSplitForDiscard(card);
            if (split != null)
            {
                split.DiscardPile.Add(card);
            }
            else
            {
                DiscardPile.Add(card);
            }

            Debug.Log($"[BattleDeckSystem] 弃牌: {card.CardId}");
            return true;
        }

        /// <summary>
        /// [一次性]：卡牌配置带 IsExhaust 时，使用后自毁（进入 RemovedPile，不入弃牌堆）。
        /// </summary>
        private bool IsExhaustCard(CardRuntimeState card)
        {
            if (card == null) return false;
            var info = ConfigLoader.Tables?.TbCardInfo?.GetOrDefault(card.CardId);
            return info != null && info.IsExhaust;
        }

        /// <summary>
        /// [虚无]：卡牌配置带 IsEthereal 时，回合结束弃手牌时自毁（进入 RemovedPile，不入弃牌堆）。
        /// </summary>
        private bool IsEtherealCard(CardRuntimeState card)
        {
            if (card == null) return false;
            if (card.IsFlashback) return true;
            var info = ConfigLoader.Tables?.TbCardInfo?.GetOrDefault(card.CardId);
            return info != null && info.IsEthereal;
        }

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

            if (isExhaust || IsExhaustCard(card))
            {
                RemovedPile.Add(card);
            }
            else
            {
                var split = GetSplitForDiscard(card);
                if (split != null)
                {
                    split.DiscardPile.Add(card);
                }
                else
                {
                    DiscardPile.Add(card);
                }
            }

            return true;
        }

        public bool UseCardByInstanceId(string instanceId, bool isExhaust = false)
        {
            var card = Hand.FirstOrDefault(c => c.InstanceId == instanceId);
            if (card == null)
            {
                Debug.LogWarning($"[BattleDeckSystem] 使用卡牌失败：手牌中不存在 InstanceId={instanceId}");
                return false;
            }

            return UseCard(card, isExhaust);
        }

        public bool UseCardByCardId(string cardId, bool isExhaust = false)
        {
            var card = Hand.FirstOrDefault(c => c.CardId == cardId);
            if (card == null)
            {
                Debug.LogWarning($"[BattleDeckSystem] 使用卡牌失败：手牌中不存在 CardId={cardId}");
                return false;
            }

            return UseCard(card, isExhaust);
        }

        public int AddCardToHand(string cardId, int count)
        {
            return AddCardToHand(cardId, count, null);
        }

        /// <summary>
        /// 往手牌加卡，可指定动态归属 <paramref name="ownerCharacterId"/>（生成型 token 用生成者覆盖静态 BelongTo）。
        /// </summary>
        public int AddCardToHand(string cardId, int count, cfg.CharacterEnum? ownerCharacterId)
        {
            return AddCardToHand(cardId, count, ownerCharacterId, isFlashback: false, energyOverride: -1);
        }

        /// <summary>
        /// 往手牌加入运行时衍生牌。闪回牌不改动原始 CardInfo，而是以实例标记获得[虚无]与费用覆盖。
        /// </summary>
        public int AddCardToHand(
            string cardId,
            int count,
            cfg.CharacterEnum? ownerCharacterId,
            bool isFlashback,
            int energyOverride)
        {
            if (string.IsNullOrEmpty(cardId) || count <= 0)
            {
                return 0;
            }

            if (ConfigLoader.Tables?.TbCardInfo?.GetOrDefault(cardId) == null)
            {
                Debug.LogWarning($"[BattleDeckSystem] AddCardToHand failed, unknown CardId={cardId}");
                return 0;
            }

            for (int i = 0; i < count; i++)
            {
                var card = CardRuntimeState.CreateDefault(cardId);
                card.OwnerCharacterId = ownerCharacterId;
                card.IsFlashback = isFlashback;
                card.EnergyOverride = energyOverride;
                Hand.Add(card);
            }

            Debug.Log($"[BattleDeckSystem] AddCardToHand: {cardId} x{count} owner={ownerCharacterId}, flashback={isFlashback}, cost={energyOverride}, hand={Hand.Count}");
            return count;
        }

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

        public bool FinishPlayingCard(string cardId, bool isExhaust = false)
        {
            var card = InPlayPile.FirstOrDefault(c => c.CardId == cardId);
            if (card == null)
            {
                Debug.LogWarning($"[BattleDeckSystem] 卡牌执行完毕处理失败：InPlayPile中不存在卡牌 {cardId}");
                return false;
            }

            InPlayPile.Remove(card);

            if (isExhaust || IsExhaustCard(card))
            {
                RemovedPile.Add(card);
            }
            else
            {
                var split = GetSplitForDiscard(card);
                if (split != null)
                {
                    split.DiscardPile.Add(card);
                }
                else
                {
                    DiscardPile.Add(card);
                }
            }

            return true;
        }

        public bool FinishPlayingCardByInstanceId(string instanceId, bool isExhaust = false)
        {
            var card = InPlayPile.FirstOrDefault(c => c.InstanceId == instanceId);
            if (card == null)
            {
                Debug.LogWarning($"[BattleDeckSystem] 卡牌执行完毕处理失败：InPlayPile中不存在 InstanceId={instanceId}");
                return false;
            }

            InPlayPile.Remove(card);

            if (isExhaust || IsExhaustCard(card))
            {
                RemovedPile.Add(card);
            }
            else
            {
                var split = GetSplitForDiscard(card);
                if (split != null)
                {
                    split.DiscardPile.Add(card);
                }
                else
                {
                    DiscardPile.Add(card);
                }
            }

            return true;
        }

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

        public void DiscardAllHand()
        {
            int count = Hand.Count;
            int etherealCount = 0;
            foreach (var card in Hand.ToList())
            {
                // [虚无]：回合结束自毁，进入 RemovedPile，不入弃牌堆
                if (IsEtherealCard(card))
                {
                    RemovedPile.Add(card);
                    etherealCount++;
                    continue;
                }

                var split = GetSplitForDiscard(card);
                if (split != null)
                {
                    split.DiscardPile.Add(card);
                }
                else
                {
                    DiscardPile.Add(card);
                }
            }

            Hand.Clear();
            Debug.Log($"[BattleDeckSystem] 弃掉所有手牌: {count} 张（其中 [虚无] 自毁 {etherealCount} 张）");
        }

        /// <summary>
        /// 仅弃掉指定角色的手牌。用于全队手牌同时展示时，在该角色行动结束后立刻准备下一手。
        /// </summary>
        public int DiscardHandForCharacter(CharacterEnum characterId)
        {
            var cardsToDiscard = Hand
                .Where(card => card != null && ResolveCharacterForCard(card) == characterId)
                .ToList();
            int etherealCount = 0;

            foreach (var card in cardsToDiscard)
            {
                Hand.Remove(card);
                if (IsEtherealCard(card))
                {
                    RemovedPile.Add(card);
                    etherealCount++;
                    continue;
                }

                var split = GetSplitForDiscard(card);
                if (split != null)
                {
                    split.DiscardPile.Add(card);
                }
                else
                {
                    DiscardPile.Add(card);
                }
            }

            Debug.Log($"[BattleDeckSystem] 弃掉角色 {characterId} 的手牌: {cardsToDiscard.Count} 张（其中 [虚无] 自毁 {etherealCount} 张），全队剩余手牌: {Hand.Count}");
            return cardsToDiscard.Count;
        }

        public int GetHandCountForCharacter(CharacterEnum characterId)
        {
            return Hand.Count(card => card != null && ResolveCharacterForCard(card) == characterId);
        }

        /// <summary>
        /// 收集所有需要实例化 UI 的卡牌（各角色牌堆 + 旧堆 + 手牌等）
        /// </summary>
        public void CollectAllCardsForPool(List<CardRuntimeState> outList)
        {
            if (outList == null)
            {
                return;
            }

            outList.Clear();
            if (PerCharacterDecks != null)
            {
                foreach (var split in PerCharacterDecks.Values)
                {
                    outList.AddRange(split.DrawPile);
                    outList.AddRange(split.DiscardPile);
                }
            }

            outList.AddRange(DrawPile);
            outList.AddRange(DiscardPile);
            outList.AddRange(Hand);
            outList.AddRange(InPlayPile);
            outList.AddRange(RemovedPile);
        }

        public int GetTotalCardCount()
        {
            int total = Hand.Count + InPlayPile.Count + RemovedPile.Count + DrawPile.Count + DiscardPile.Count;
            if (PerCharacterDecks != null)
            {
                foreach (var split in PerCharacterDecks.Values)
                {
                    total += split.DrawPile.Count + split.DiscardPile.Count;
                }
            }

            return total;
        }

        public BattleDeckSystem Clone()
        {
            var clone = new BattleDeckSystem
            {
                DrawPile = new List<CardRuntimeState>(),
                DiscardPile = new List<CardRuntimeState>(),
                Hand = new List<CardRuntimeState>(),
                RemovedPile = new List<CardRuntimeState>(),
                InPlayPile = new List<CardRuntimeState>(),
                PerCharacterDecks = new Dictionary<CharacterEnum, CharacterDeckSplit>()
            };

            clone._partyCharacterOrder.AddRange(_partyCharacterOrder);

            foreach (var card in DrawPile)
            {
                clone.DrawPile.Add(card.Clone());
            }

            foreach (var card in DiscardPile)
            {
                clone.DiscardPile.Add(card.Clone());
            }

            foreach (var card in Hand)
            {
                clone.Hand.Add(card.Clone());
            }

            foreach (var card in RemovedPile)
            {
                clone.RemovedPile.Add(card.Clone());
            }

            foreach (var card in InPlayPile)
            {
                clone.InPlayPile.Add(card.Clone());
            }

            if (PerCharacterDecks != null)
            {
                foreach (var kv in PerCharacterDecks)
                {
                    clone.PerCharacterDecks[kv.Key] = kv.Value.Clone();
                }
            }

            return clone;
        }

        public string GetDebugInfo()
        {
            var parts = new List<string>();
            if (PerCharacterDecks != null)
            {
                foreach (var kv in PerCharacterDecks)
                {
                    parts.Add($"{kv.Key}:抽{kv.Value.DrawPile.Count}/弃{kv.Value.DiscardPile.Count}");
                }
            }

            string per = parts.Count > 0 ? string.Join(", ", parts) : "无分堆";
            return $"[{per}] 旧堆抽{DrawPile.Count}/弃{DiscardPile.Count}, 手牌: {Hand.Count}, 使用中: {InPlayPile.Count}, 移除: {RemovedPile.Count}";
        }
    }
}
