using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Ashlight.Config;
using Ashlight.Systems.Character;
using Ashlight.Systems.Card;
using Ashlight.Systems.Upgrade;

namespace Scripts.UI
{
    /// <summary>
    /// UpgradePanel 逻辑分部。UI 绑定（ExpArea / CardParent / ChooseCardPanel / BaixiangPanel / Btn_SkipChoose）
    /// 由生成的 <see cref="UpgradePanel"/> 分部提供。
    ///
    /// 编排：Show() 按表生成 N 张经验卡到 ExpArea → 玩家拖到角色身上 +1 经验 →
    /// 本场首次获得经验则开「选卡」三选一，升级则开「百相」三选一（百相为占位，第③步实现）→
    /// 经验全部用完后回调 onComplete（进结算）。
    /// </summary>
    public partial class UpgradePanel : MonoBehaviour
    {
        [Header("选卡")]
        [SerializeField]
        [Tooltip("卡牌预制体（CardViewController，普通手牌大卡）；留空则从 Resources/Cards/Prefabs/CardViewController 加载")]
        private GameObject cardViewPrefab;

        [Header("经验卡")]
        [SerializeField]
        [Tooltip("经验卡预制体（Prefab_ExpCard）；留空则从 Resources/UI/UpgradePanel/Prefab_ExpCard 加载")]
        private GameObject expCardPrefab;

        [SerializeField]
        [Tooltip("查不到经验奖励配置时的兜底点数")]
        private int fallbackExpReward = 2;

        private bool _bound;
        private readonly List<GameObject> _spawnedCards = new List<GameObject>();

        // —— 编排状态 ——
        private Action _onComplete;
        private List<Character> _candidates;
        private int _remainingExp;
        private bool _selectionBusy;
        private readonly HashSet<cfg.CharacterEnum> _gainedExpThisBattle = new HashSet<cfg.CharacterEnum>();
        private readonly List<ExpCardDragHandler> _spawnedExpCards = new List<ExpCardDragHandler>();

        /// <summary>该实例是否为「真正带 ExpArea 的升级面板」（场景里可能存在重复/空壳 UpgradePanel，用此区分）。</summary>
        public bool HasExpArea
        {
            get { EnsureBound(); return ExpArea != null; }
        }

        private void EnsureBound()
        {
            if (_bound && ExpArea != null) return;
            InitUIBindings();
            // CardParent 的 UIBind 在场景里 BindName 为空 → 生成绑定拿不到，运行时按名字兜底
            if (CardParent == null) CardParent = FindChildRect("CardParent");
            _bound = true;
        }

        private RectTransform FindChildRect(string childName)
        {
            var all = GetComponentsInChildren<RectTransform>(true);
            foreach (var rt in all)
                if (rt != null && rt.name == childName) return rt;
            return null;
        }

        /// <summary>
        /// 胜利入口：显示面板，按表生成 N 张经验卡到 ExpArea。
        /// 玩家把经验卡拖到 candidates 中的角色身上发放经验；全部用完后回调 onComplete（通常用于进结算）。
        /// 敌人隐藏 / 经验条显隐由调用方（UI_BattleScene）负责。
        /// </summary>
        public void Show(List<Character> candidates, Action onComplete)
        {
            EnsureBound();

            _candidates = candidates;
            _onComplete = onComplete;
            _selectionBusy = false;
            _gainedExpThisBattle.Clear();

            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            // 子面板初始隐藏 + 清空卡牌区域里预制体自带的占位卡（否则会和经验卡叠在一起）
            if (ChooseCardPanel != null) ChooseCardPanel.gameObject.SetActive(false);
            if (BaixiangPanel != null) BaixiangPanel.gameObject.SetActive(false);
            if (Btn_SkipChoose != null) Btn_SkipChoose.gameObject.SetActive(false);
            ClearCardArea();

            SpawnExpCards(GetExpReward());
        }

        /// <summary>
        /// 本场胜利奖励的经验点数。
        /// 暂取 TbCharacterExp[Lv1].Exp（即 1→2 级所需经验，当前=2）；查不到则用 fallbackExpReward。
        /// 注：若以后有专门的"战斗奖励经验"字段/表，改这里即可。
        /// </summary>
        private int GetExpReward()
        {
            var lv1 = ConfigLoader.Tables?.TbCharacterExp?.GetOrDefault(1);
            int n = lv1 != null ? lv1.Exp : fallbackExpReward;
            return Mathf.Max(0, n);
        }

        private void SpawnExpCards(int count)
        {
            ClearExpCards();
            _remainingExp = 0;

            var prefab = ResolveExpCardPrefab();
            if (prefab == null || ExpArea == null)
            {
                Debug.LogError("[UpgradePanel] 经验卡预制体或 ExpArea 未就绪，直接结算");
                Finish();
                return;
            }

            for (int i = 0; i < count; i++)
            {
                var go = Instantiate(prefab, ExpArea);
                var drag = go.GetComponent<ExpCardDragHandler>();
                if (drag == null) drag = go.AddComponent<ExpCardDragHandler>();
                drag.Init(this);
                _spawnedExpCards.Add(drag);
                _remainingExp++;
            }

            if (_remainingExp == 0) Finish();
        }

        /// <summary>
        /// 打开「选卡」三选一：为 charId 抽 3 张候选卡，实例化到 CardParent。
        /// 选中 → AddCardToDeck 并关闭；跳过 → 直接关闭。两种情况都回调 onDone。
        /// </summary>
        public void OpenChooseCardPanel(cfg.CharacterEnum charId, Action onDone)
        {
            EnsureBound();

            var cards = CardRewardSelectionService.GetChoices(charId, 3);
            if (cards == null || cards.Count == 0)
            {
                Debug.Log($"[UpgradePanel] 角色 {charId} 无可选卡牌，跳过选卡");
                onDone?.Invoke();
                return;
            }

            var prefab = ResolveCardPrefab();
            if (prefab == null || CardParent == null)
            {
                Debug.LogError("[UpgradePanel] 卡牌预制体或 CardParent 未就绪，跳过选卡");
                onDone?.Invoke();
                return;
            }

            if (ChooseCardPanel != null) ChooseCardPanel.gameObject.SetActive(true);

            ClearCardArea();
            for (int i = 0; i < cards.Count; i++)
            {
                var cardInfo = cards[i];
                if (cardInfo == null) continue;

                var go = Instantiate(prefab, CardParent);
                _spawnedCards.Add(go);

                var view = go.GetComponent<CardViewController>();
                if (view != null)
                {
                    view.Initialize(cardInfo, DescriptionMode.View);
                    view.SetHoverScale(1.1f); // 选卡面板里 hover 放大别太大
                }

                // 在卡根上挂一个 Button 接收左键选择（卡自身的右键删除逻辑互不影响）
                var btn = go.GetComponent<Button>();
                if (btn == null) btn = go.AddComponent<Button>();
                btn.onClick.RemoveAllListeners();
                var picked = cardInfo;
                btn.onClick.AddListener(() =>
                {
                    if (CharacterSystem.AddCardToDeck(charId, picked.Id))
                        Debug.Log($"[UpgradePanel] 角色 {charId} 获得卡牌 {picked.Id} ({picked.Name})");
                    CloseChooseCardPanel();
                    onDone?.Invoke();
                });
            }

            // 跳过按钮：仅跳过选卡（不加卡），不影响后续升级流程
            if (Btn_SkipChoose != null)
            {
                Btn_SkipChoose.gameObject.SetActive(true);
                Btn_SkipChoose.onClick.RemoveAllListeners();
                Btn_SkipChoose.onClick.AddListener(() =>
                {
                    CloseChooseCardPanel();
                    onDone?.Invoke();
                });
            }
        }

        /// <summary>
        /// 经验卡拖到角色身上时由 <see cref="ExpCardDragHandler"/> 调用。
        /// 返回 true=接受并消耗本卡，false=拒绝(卡回弹)。
        /// </summary>
        public bool TryDropExpOnCharacter(ExpCardDragHandler card, Character target)
        {
            // 选卡/百相面板打开时不接受新的发放
            if (_selectionBusy) return false;
            if (target == null || _candidates == null || !_candidates.Contains(target)) return false;

            var unit = target.GetUnitState();
            if (unit == null || !Enum.TryParse<cfg.CharacterEnum>(unit.ConfigId, out var charId))
            {
                Debug.LogWarning($"[UpgradePanel] 无法解析角色 ConfigId={unit?.ConfigId}");
                return false;
            }

            // 存档里没有该角色状态时拒绝（卡回弹），避免空发经验/卡牌
            if (CharacterSystem.GetCharacterState(charId) == null)
            {
                Debug.LogWarning($"[UpgradePanel] 角色 {charId} 在存档中无状态，拒绝发放（请确保已走 GameManager 初始化/对账）");
                return false;
            }

            bool firstExp = !_gainedExpThisBattle.Contains(charId);
            _gainedExpThisBattle.Add(charId);

            int levelBefore = CharacterSystem.GetCharacterState(charId)?.Level ?? 1;
            CharacterSystem.AddExperience(charId, 1);
            int levelAfter = CharacterSystem.GetCharacterState(charId)?.Level ?? levelBefore;
            bool leveledUp = levelAfter > levelBefore;

            target.UpdateExpDisplay();

            // 消耗这张经验卡
            _spawnedExpCards.Remove(card);
            if (card != null) Destroy(card.gameObject);
            _remainingExp--;

            // 先选卡（本场首次获得经验），再升级百相
            RunSelectionSequence(charId, firstExp, leveledUp);
            return true;
        }

        /// <summary>按「先选卡 → 后百相」顺序串联两个可选弹窗，结束后检查是否收尾。</summary>
        private void RunSelectionSequence(cfg.CharacterEnum charId, bool doCard, bool doBaixiang)
        {
            Action afterAll = () =>
            {
                _selectionBusy = false;
                CheckFinish();
            };

            Action afterCard = () =>
            {
                if (doBaixiang) OpenBaixiangPanel(charId, afterAll);
                else afterAll();
            };

            if (doCard)
            {
                _selectionBusy = true;
                OpenChooseCardPanel(charId, afterCard);
            }
            else if (doBaixiang)
            {
                _selectionBusy = true;
                OpenBaixiangPanel(charId, afterAll);
            }
            else
            {
                CheckFinish();
            }
        }

        /// <summary>
        /// 打开「百相（天赋）」三选一：为 charId 抽 3 个可选升级（#UpgradeOptions）填入 BaixiangPanel 的 3 个槽位。
        /// 选中 → AddUpgrade 写入角色；候选为空或面板缺失则直接放行。完成后回调 onDone。
        /// 注意：百相没有"跳过"，升级解算必须做出选择。
        /// </summary>
        private void OpenBaixiangPanel(cfg.CharacterEnum charId, Action onDone)
        {
            if (BaixiangPanel == null)
            {
                Debug.LogWarning("[UpgradePanel] BaixiangPanel 未绑定，跳过百相");
                onDone?.Invoke();
                return;
            }

            var talents = UpgradeSelectionService.GetChoices(charId, 3);
            if (talents == null || talents.Count == 0)
            {
                Debug.Log($"[UpgradePanel] 角色 {charId} 无可选百相，跳过");
                onDone?.Invoke();
                return;
            }

            var options = new List<BaixiangPanel.Option>(talents.Count);
            foreach (var t in talents)
                options.Add(new BaixiangPanel.Option { Name = t.Name, EffectText = t.Description });

            BaixiangPanel.Show(options, idx =>
            {
                var picked = talents[idx];
                var state = CharacterSystem.GetCharacterState(charId);
                if (state != null && picked != null && state.AddUpgrade(picked.Id))
                    Debug.Log($"[UpgradePanel] 角色 {charId} 获得百相 {picked.Id} ({picked.Name})");
                else
                    Debug.LogWarning($"[UpgradePanel] 百相写入失败或重复: {picked?.Id}");
                onDone?.Invoke();
            }, LoadCharacterHead(charId));
        }

        /// <summary>加载角色头像（Resources/Characters/{id}/Icon/Icon_{id}）。找不到返回 null（保留面板原图）。</summary>
        private Sprite LoadCharacterHead(cfg.CharacterEnum charId)
        {
            var path = Ashlight.Common.Utils.AssetPath.GetCharacterIconAssetPath(charId.ToString());
            if (string.IsNullOrEmpty(path)) return null;
            return Resources.Load<Sprite>(path.Replace('\\', '/'));
        }

        private void CheckFinish()
        {
            if (_selectionBusy) return;
            if (_remainingExp > 0) return;
            Finish();
        }

        private void Finish()
        {
            ClearExpCards();
            ClearCardArea();
            gameObject.SetActive(false);

            // 测试期：不落盘（经验/卡/百相不在测试之间持久化）。
            // 想恢复持久化时，在此调用 Ashlight.Systems.Core.GameManager.Instance?.SaveGame();
            var cb = _onComplete;
            _onComplete = null;
            cb?.Invoke();
        }

        private void ClearExpCards()
        {
            foreach (var c in _spawnedExpCards)
                if (c != null) Destroy(c.gameObject);
            _spawnedExpCards.Clear();
        }

        private GameObject ResolveExpCardPrefab()
        {
            if (expCardPrefab != null) return expCardPrefab;
            expCardPrefab = Resources.Load<GameObject>("UI/UpgradePanel/Prefab_ExpCard");
            return expCardPrefab;
        }

        private void CloseChooseCardPanel()
        {
            if (Btn_SkipChoose != null)
            {
                Btn_SkipChoose.onClick.RemoveAllListeners();
                Btn_SkipChoose.gameObject.SetActive(false);
            }
            ClearCardArea();
            if (ChooseCardPanel != null) ChooseCardPanel.gameObject.SetActive(false);
        }

        private void ClearSpawnedCards()
        {
            foreach (var go in _spawnedCards)
                if (go != null) Destroy(go);
            _spawnedCards.Clear();
        }

        /// <summary>清空卡牌区域：本流程实例化的卡 + CardParent 下预制体自带的占位卡，全部销毁。</summary>
        private void ClearCardArea()
        {
            ClearSpawnedCards();
            if (CardParent != null)
            {
                for (int i = CardParent.childCount - 1; i >= 0; i--)
                    Destroy(CardParent.GetChild(i).gameObject);
            }
        }

        private GameObject ResolveCardPrefab()
        {
            if (cardViewPrefab != null) return cardViewPrefab;
            cardViewPrefab = Resources.Load<GameObject>("Cards/Prefabs/CardViewController");
            return cardViewPrefab;
        }
    }
}
