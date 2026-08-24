using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using Ashlight.Battle.Core.Data;
using cfg.Enemy;

namespace Scripts.UI
{
    /// <summary>
    /// 行动顺序视图（公共回合制）。
    ///
    /// 平铺展示「接下来 N 个行动」的时间线（最左 = 最先行动）：
    ///   · 从 <see cref="ATB.GetTurnOrderWithFuture"/> 取平铺顺序，快单位会重复出现多次（正确）。
    ///   · 按绝对公共回合分组，组与组之间插一条分隔条并标注该组的回合号。
    ///   · 无空回合空格、无幽灵半透明——每次出现都是一张普通卡。
    ///   · 金框 = 当前行动单位；敌人卡显示意图图标 + 悬停技能说明。
    ///
    /// 卡片是「按位置」复用的对象池（非每单位一张），因此同一单位可在时间线上出现多次。
    /// </summary>
    public class TurnOrderView : MonoBehaviour
    {
        #region 序列化字段

        [Header("卡片 Prefab")]
        [SerializeField]
        [Tooltip("UI_ActionOrder prefab 路径，留空则从 Resources 自动加载")]
        private GameObject actionOrderPrefab;

        [Header("回合分隔条 Prefab")]
        [SerializeField]
        [Tooltip("Prefab_LevelIndicator（竖线分隔条），插在不同回合组之间并标注回合号；留空则从 Resources 自动加载")]
        private GameObject levelIndicatorPrefab;

        [Header("子容器")]
        [Tooltip("存放所有卡片的 RectTransform，需挂 HorizontalLayoutGroup。留空自动创建。")]
        [SerializeField] private RectTransform cardsContainer;

        [Header("布局")]
        [SerializeField] private float cardSpacing = 6f;

        [Header("显示窗口")]
        [Tooltip("时间线展示接下来多少个行动")]
        [SerializeField] private int windowSize = 10;

        [Header("拖拽行动预览")]
        [Tooltip("非 0 费行动牌拖拽时，行动卡插入预览的动画时长（秒）。")]
        [SerializeField] private float actionPreviewDuration = 1f;

        [Tooltip("预览行动卡从目标位置下方多远处升起（像素）。")]
        [SerializeField] private float actionPreviewRiseDistance = 76f;

        [Header("ATB 引用")]
        [SerializeField] private ATB atb;

        [Header("技能 Tooltip")]
        [SerializeField]
        [Tooltip("DescriptionViewController 预制体（与 IntentionView 同款）。敌人卡 hover 显示技能说明；留空则从 Resources 加载")]
        private GameObject descriptionViewControllerPrefab;

        #endregion

        #region 内部数据

        /// <summary>一个池化的卡槽（按时间线位置复用，可承载不同单位）。</summary>
        private class CardSlot
        {
            public UI_行动顺序 Card;
            public string      UnitId;   // 当前承载的单位（用于避免每帧重复 Setup 加载图标）
        }

        /// <summary>单位静态信息（configId + 是否玩家），供卡片 Setup 使用。</summary>
        private struct UnitInfo
        {
            public string ConfigId;
            public bool   IsPlayer;
        }

        private readonly List<CardSlot> _slots = new List<CardSlot>();
        private readonly List<LevelIndicator> _separators = new List<LevelIndicator>();
        private readonly Dictionary<string, UnitInfo> _unitInfo = new Dictionary<string, UnitInfo>();

        /// <summary>各敌人当前的意图（供其最近一张卡显示攻击图标 + 悬停说明）。</summary>
        private readonly Dictionary<string, EnemySkillInfo> _intents = new Dictionary<string, EnemySkillInfo>();

        /// <summary>本场天气（null = 无天气）。天气条目由 ATB 的虚拟单位提供，这里只负责渲染天气卡 + hover 说明。</summary>
        private cfg.WeatherInfo _weather;

        /// <summary>在轨引线（castId → 卡牌配置）。引线条目由 ATB 的虚拟单位提供，这里只负责渲染卡牌图。</summary>
        private readonly Dictionary<string, cfg.Character.CardInfo> _casts = new Dictionary<string, cfg.Character.CardInfo>();
        private readonly HashSet<string> _selectableCastIds = new HashSet<string>();
        private System.Action<string> _onCastSelected;

        private string _activeUnitId;

        private HorizontalLayoutGroup _layoutGroup;
        private bool _actionPreviewActive;
        private bool _actionPreviewCommitted;
        private string _actionPreviewUnitId;
        private int _actionPreviewDelay;
        private GameObject _actionPreviewPlaceholder;
        private GameObject _actionPreviewGhost;
        private Sequence _actionPreviewTween;
        private Sequence _actionPreviewGhostTween;
        private Vector2 _actionPreviewGhostTarget;
        private float _actionPreviewPlaceholderWidth;

        /// <summary>冻结期间渲染的顺序快照（null = 未冻结，每帧从 ATB 实时读取）。</summary>
        private List<ATB.TurnOrderEntry> _frozenOrder;

        /// <summary>hover 行动卡时通知 (unitId, hovering)。由 UI_BattleScene 注入，用于联动战场上的选中标记。</summary>
        public System.Action<string, bool> OnUnitHover;

        /// <summary>共享的技能说明 tooltip（所有卡片共用一个，hover 时显示）。</summary>
        private DescriptionViewController _skillTooltip;

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            EnsureContainer();
            EnsurePrefab();
            EnsureLevelIndicatorPrefab();
            ApplyLayoutGroup();
            EnsureSkillTooltip();
        }

        private void EnsureSkillTooltip()
        {
            if (_skillTooltip != null) return;

            if (descriptionViewControllerPrefab == null)
                descriptionViewControllerPrefab = Resources.Load<GameObject>("UI/常用UI/PageViewer/DescriptionViewController");
            if (descriptionViewControllerPrefab == null) return;

            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            var obj = Instantiate(descriptionViewControllerPrefab, canvas.transform);
            _skillTooltip = obj.GetComponent<DescriptionViewController>();
            _skillTooltip?.Hide();
        }

        #endregion

        #region 公共 API

        /// <summary>
        /// 初始化：登记所有单位的 configId / 阵营（供卡片显示用）。在 ATB.InitializeByUnits 之后调用。
        /// </summary>
        public void Initialize(IReadOnlyList<UnitState> playerUnits, IReadOnlyList<UnitState> enemyUnits)
        {
            ClearActionPreviewImmediate(false);
            _unitInfo.Clear();
            _intents.Clear();
            _casts.Clear();
            EndCastSelection();
            _activeUnitId = null;
            _frozenOrder = null;
            _weather = null; // 新战斗默认无天气，由 SetWeather 重新注入

            RegisterUnits(playerUnits, true);
            RegisterUnits(enemyUnits, false);

            RefreshOrder();
        }

        private void RegisterUnits(IReadOnlyList<UnitState> units, bool isPlayer)
        {
            if (units == null) return;
            foreach (var u in units)
            {
                if (u == null || u.IsDead || string.IsNullOrEmpty(u.UnitId)) continue;
                _unitInfo[u.UnitId] = new UnitInfo { ConfigId = u.ConfigId, IsPlayer = isPlayer };
            }
        }

        /// <summary>设置高亮（金框）：当前行动单位；传 null 清除。</summary>
        public void SetActiveUnit(string unitId)
        {
            _activeUnitId = unitId;
        }

        /// <summary>
        /// 冻结顺序条：立刻快照当前 ATB 顺序，此后 RefreshOrder 一律渲染这份快照。
        /// 用于原子回合结算：逻辑层同步 Reschedule 会让该单位的条目瞬间跳去未来回合、
        /// 条头翻页成后续单位——但演出还没播。冻结让条子在演出期间保持
        /// 「行动者仍在条头 + 金框」的画面；下一个单位回合开始时解冻。
        /// </summary>
        public void FreezeOrder()
        {
            if (atb == null) return;
            _frozenOrder = atb.GetTurnOrderWithFuture(Mathf.Max(1, windowSize));
        }

        /// <summary>解冻顺序条：恢复每帧从 ATB 读取实时顺序。</summary>
        public void UnfreezeOrder()
        {
            _frozenOrder = null;
        }

        /// <summary>注入本场天气（在 ATB.AddWeatherIcon 之后调用）。传 null 清除。</summary>
        public void SetWeather(cfg.WeatherInfo weather)
        {
            _weather = weather;
        }

        /// <summary>登记一条在轨引线（在 ATB.AddCastIcon 之后调用），其行动格用卡牌图渲染。</summary>
        public void SetCast(string castId, cfg.Character.CardInfo card)
        {
            if (string.IsNullOrEmpty(castId) || card == null) return;
            _casts[castId] = card;
        }

        /// <summary>引线结算/作废：移除登记（其卡片在下一次 RefreshOrder 自动消失）。</summary>
        public void RemoveCast(string castId)
        {
            if (string.IsNullOrEmpty(castId)) return;
            _casts.Remove(castId);
        }

        /// <summary>进入执行牌目标选择；只有 allowedCastIds 中的引线会显示金框并响应点击。</summary>
        public bool BeginCastSelection(IEnumerable<string> allowedCastIds, System.Action<string> onSelected)
        {
            _selectableCastIds.Clear();
            if (allowedCastIds != null)
            {
                foreach (string id in allowedCastIds)
                    if (!string.IsNullOrEmpty(id) && _casts.ContainsKey(id)) _selectableCastIds.Add(id);
            }
            _onCastSelected = onSelected;
            RefreshOrder();
            return _selectableCastIds.Count > 0;
        }

        public void EndCastSelection()
        {
            _selectableCastIds.Clear();
            _onCastSelected = null;
            if (isActiveAndEnabled) RefreshOrder();
        }

        private void SelectCast(string castId)
        {
            if (string.IsNullOrEmpty(castId) || !_selectableCastIds.Contains(castId)) return;
            _onCastSelected?.Invoke(castId);
        }

        /// <summary>
        /// 标记单位（敌人）的意图：executing=true 且传入技能时，其最近一张卡显示攻击图标并可 hover 出说明；
        /// executing=false 则清除该单位的意图显示。
        /// </summary>
        public void SetExecuting(string unitId, bool executing, EnemySkillInfo skill = null)
        {
            if (string.IsNullOrEmpty(unitId)) return;
            if (executing && skill != null)
                _intents[unitId] = skill;
            else
                _intents.Remove(unitId);
        }

        /// <summary>单位死亡：从登记表与意图表移除（其卡片会在下一次 RefreshOrder 自动消失）。</summary>
        public void RemoveUnit(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return;
            _unitInfo.Remove(unitId);
            _intents.Remove(unitId);
            if (_activeUnitId == unitId) _activeUnitId = null;
        }

        /// <summary>
        /// 每帧刷新：读取 ATB 的「接下来 N 个行动」，按绝对回合分组平铺；组间插分隔条并标回合号。
        /// </summary>
        public void RefreshOrder()
        {
            if (atb == null || cardsContainer == null) return;
            // 拖拽预览期间冻结基础顺序；临时占位槽负责让位，避免每帧对象池重排破坏动画。
            if (_actionPreviewActive) return;

            // 冻结期间渲染快照（演出与条子同步）；未冻结时实时读 ATB。
            var order = _frozenOrder ?? atb.GetTurnOrderWithFuture(Mathf.Max(1, windowSize));

            int sibling = 0;   // 当前分配的 sibling index
            int sepUsed = 0;   // 已使用的分隔条数量
            int cardUsed = 0;  // 已使用的卡槽数量
            bool activeShown = false;                 // 金框只给当前单位的第一张卡
            var intentShownFor = new HashSet<string>(); // 每个敌人意图只标在其最近一张卡上
            int prevRound = int.MinValue;

            for (int i = 0; i < order.Count; i++)
            {
                var entry = order[i];

                // 天气/引线条目（虚拟单位）不查单位登记表，各自用专用渲染；普通条目查不到=已死亡/未登记。
                bool isWeatherEntry = entry.IsWeather;
                bool isCastEntry = entry.IsCast;
                cfg.Character.CardInfo castCard = null;
                UnitInfo info = default;
                if (isWeatherEntry)
                {
                    if (_weather == null) continue;
                }
                else if (isCastEntry)
                {
                    if (!_casts.TryGetValue(entry.UnitId, out castCard) || castCard == null) continue;
                }
                else if (!_unitInfo.TryGetValue(entry.UnitId, out info))
                {
                    continue;
                }

                // 跨回合：进入新回合组前插一条分隔条并标该组回合号。
                if (entry.Round != prevRound)
                {
                    var sep = GetSeparator(sepUsed++);
                    if (sep != null)
                    {
                        sep.transform.SetSiblingIndex(sibling++);
                        sep.SetLevel(entry.Round);
                    }
                    prevRound = entry.Round;
                }

                var slot = GetSlot(cardUsed++);
                if (slot?.Card == null) continue;

                // 仅当该卡槽承载的单位变化时才 Setup（避免每帧 Resources.Load）。
                if (slot.UnitId != entry.UnitId)
                {
                    if (isWeatherEntry)
                        slot.Card.SetupWeather(_weather);
                    else if (isCastEntry)
                        slot.Card.SetupCast(castCard);
                    else
                        slot.Card.Setup(info.ConfigId, info.IsPlayer);
                    slot.UnitId = entry.UnitId;
                }

                var go = slot.Card.gameObject;
                if (!go.activeSelf) go.SetActive(true);
                slot.Card.transform.SetSiblingIndex(sibling++);

                // 金框高亮：当前行动单位的第一张卡（天气/引线卡不高亮）。
                bool highlight = !isWeatherEntry && !isCastEntry && !activeShown && entry.UnitId == _activeUnitId;
                bool selectableCast = isCastEntry && _selectableCastIds.Contains(entry.UnitId);
                slot.Card.SetHighlight(highlight || selectableCast);
                if (highlight) activeShown = true;
                slot.Card.SetCastSelection(entry.UnitId, selectableCast, SelectCast);

                // 天气卡 hover 显示天气说明；普通卡清除天气引用（卡槽是复用的）。
                slot.Card.SetWeatherInfo(isWeatherEntry ? _weather : null, _skillTooltip);

                // hover 联动战场选中标记（天气/引线虚拟单位不联动）。
                slot.Card.SetHoverUnit(isWeatherEntry || isCastEntry ? null : entry.UnitId, OnUnitHover);

                // 敌人意图：标在该敌人最近一张卡上（其余卡不显示）。
                bool showIntent = !isWeatherEntry
                                  && !isCastEntry
                                  && !info.IsPlayer
                                  && !intentShownFor.Contains(entry.UnitId)
                                  && _intents.TryGetValue(entry.UnitId, out var skill)
                                  && skill != null;
                if (showIntent)
                {
                    slot.Card.SetAttacking(true);
                    slot.Card.SetExecutingSkill(_intents[entry.UnitId], _skillTooltip);
                    intentShownFor.Add(entry.UnitId);
                }
                else
                {
                    slot.Card.SetAttacking(false);
                    slot.Card.SetExecutingSkill(null, _skillTooltip);
                }
            }

            HideSlotsFrom(cardUsed);
            HideSeparatorsFrom(sepUsed);
        }

        /// <summary>
        /// 开启非 0 费主行动的拖拽预览。真实 ATB 不变：仅在目标位置插入一个布局占位槽，
        /// 并把当前角色行动卡的视觉分身从占位槽正下方升入。
        /// </summary>
        public void BeginActionPreview(string unitId, int actionDelay)
        {
            if (atb == null || cardsContainer == null || string.IsNullOrEmpty(unitId) || actionDelay <= 0)
            {
                return;
            }

            // 某些拖拽路径可能重复派发 BeginDrag；同一次拖拽只允许创建一次相同预览。
            if (_actionPreviewActive
                && _actionPreviewUnitId == unitId
                && _actionPreviewDelay == actionDelay)
            {
                return;
            }

            ClearActionPreviewImmediate(true);
            RefreshOrder();
            ForceLayout();

            var source = FindVisibleCard(unitId);
            if (source == null) return;

            int previewRound = atb.CurrentRound + actionDelay;
            var baseOrder = atb.GetTurnOrderWithFuture(Mathf.Max(1, windowSize));
            var previewOrder = atb.GetTurnOrderPreview(unitId, previewRound, actionDelay, Mathf.Max(1, windowSize));
            int previewCardIndex = -1;
            for (int i = 0; i < previewOrder.Count; i++)
            {
                if (previewOrder[i].UnitId == unitId)
                {
                    previewCardIndex = i;
                    break;
                }
            }
            if (previewCardIndex < 0) return;

            _actionPreviewActive = true;
            _actionPreviewCommitted = false;
            _actionPreviewUnitId = unitId;
            _actionPreviewDelay = actionDelay;

            _actionPreviewPlaceholder = CreatePreviewPlaceholder(source.GetComponent<RectTransform>());
            if (_actionPreviewPlaceholder == null)
            {
                ClearActionPreviewImmediate(true);
                return;
            }

            // 用预览角色的后继条目作为锚点。简单使用 previewIndex + 1 会被同单位的未来重复项带偏。
            int targetCardIndex = ResolvePreviewInsertionCardIndex(
                baseOrder,
                previewOrder,
                previewCardIndex,
                previewRound);
            int targetSibling = GetInsertionSiblingIndex(targetCardIndex);
            _actionPreviewPlaceholder.transform.SetSiblingIndex(targetSibling);

            // 先让 Layout 用完整卡宽算出唯一的最终插入位置。
            ForceLayout();
            var targetRect = _actionPreviewPlaceholder.GetComponent<RectTransform>();
            Vector2 targetPosition = targetRect != null ? targetRect.anchoredPosition : Vector2.zero;
            _actionPreviewPlaceholderWidth = source.GetComponent<RectTransform>().rect.width;

            // Layout 始终保持启用；通过占位槽从 0 展开到完整卡宽，让其他卡自然、稳定地被推出。
            SetPreviewPlaceholderWidth(0f);
            ForceLayout();

            _actionPreviewGhost = CreatePreviewGhost(source, targetRect, targetPosition);
            if (_actionPreviewGhost == null)
            {
                ClearActionPreviewImmediate(true);
                return;
            }

            var ghostGroup = _actionPreviewGhost.GetComponent<CanvasGroup>();
            var ghostRect = _actionPreviewGhost.GetComponent<RectTransform>();
            _actionPreviewTween = DOTween.Sequence().SetUpdate(true);
            _actionPreviewTween.Append(
                DOTween.To(
                        () => 0f,
                        width =>
                        {
                            SetPreviewPlaceholderWidth(width);
                            ForceLayout();
                        },
                        _actionPreviewPlaceholderWidth,
                        actionPreviewDuration)
                    .SetEase(Ease.OutCubic));
            _actionPreviewTween.OnComplete(() =>
            {
                SetPreviewPlaceholderWidth(_actionPreviewPlaceholderWidth);
                ForceLayout();
            });

            StartPreviewGhostLoop(ghostRect, ghostGroup, targetPosition);
        }

        /// <summary>标记拖拽已成功出牌；预览保持到真实 ATB 重排完成。</summary>
        public void MarkActionPreviewCommitted(string unitId)
        {
            if (_actionPreviewActive && _actionPreviewUnitId == unitId)
            {
                _actionPreviewCommitted = true;
                _actionPreviewGhostTween?.Kill();
                _actionPreviewGhostTween = null;
                var ghostRect = _actionPreviewGhost != null
                    ? _actionPreviewGhost.GetComponent<RectTransform>()
                    : null;
                if (ghostRect != null) ghostRect.anchoredPosition = _actionPreviewGhostTarget;
                var group = _actionPreviewGhost != null
                    ? _actionPreviewGhost.GetComponent<CanvasGroup>()
                    : null;
                if (group != null) group.alpha = 1f;
            }
        }

        /// <summary>拖拽结束但未成功出牌时，预览卡向下退出，其他行动卡顺滑复位。</summary>
        public void CancelUncommittedActionPreview()
        {
            if (!_actionPreviewActive || _actionPreviewCommitted) return;

            _actionPreviewTween?.Kill();
            _actionPreviewGhostTween?.Kill();
            _actionPreviewGhostTween = null;

            var ghost = _actionPreviewGhost;
            var ghostGroup = ghost != null ? ghost.GetComponent<CanvasGroup>() : null;
            var ghostRect = ghost != null ? ghost.GetComponent<RectTransform>() : null;
            Vector2 exitPosition = ghostRect != null
                ? ghostRect.anchoredPosition + Vector2.down * actionPreviewRiseDistance
                : Vector2.zero;

            _actionPreviewTween = DOTween.Sequence().SetUpdate(true);
            float currentWidth = GetPreviewPlaceholderWidth();
            _actionPreviewTween.Append(
                DOTween.To(
                        () => currentWidth,
                        width =>
                        {
                            SetPreviewPlaceholderWidth(width);
                            ForceLayout();
                        },
                        0f,
                        actionPreviewDuration)
                    .SetEase(Ease.OutCubic));
            if (ghostRect != null)
            {
                _actionPreviewTween.Join(ghostRect.DOAnchorPos(exitPosition, actionPreviewDuration).SetEase(Ease.InCubic));
                if (ghostGroup != null) _actionPreviewTween.Join(ghostGroup.DOFade(0f, actionPreviewDuration * 0.8f));
            }
            _actionPreviewTween.OnComplete(() => ClearActionPreviewImmediate(true));
        }

        /// <summary>真实 ATB 已按本次主行动重排，移除拖拽预览并恢复实时顺序。</summary>
        public void CompleteCommittedActionPreview(string unitId)
        {
            if (!_actionPreviewActive || _actionPreviewUnitId != unitId) return;
            ClearActionPreviewImmediate(true);
        }

        #endregion

        #region 卡槽 / 分隔条池

        private CardSlot GetSlot(int index)
        {
            while (_slots.Count <= index)
            {
                var slot = new CardSlot { Card = SpawnCard(), UnitId = null };
                _slots.Add(slot);
            }
            return _slots[index];
        }

        private UI_行动顺序 FindVisibleCard(string unitId)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot?.Card != null && slot.UnitId == unitId && slot.Card.gameObject.activeInHierarchy)
                {
                    return slot.Card;
                }
            }
            return null;
        }

        private GameObject CreatePreviewPlaceholder(RectTransform sourceRect)
        {
            if (sourceRect == null) return null;
            var go = new GameObject("ActionPreviewPlaceholder", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(cardsContainer, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = sourceRect.anchorMin;
            rect.anchorMax = sourceRect.anchorMax;
            rect.pivot = sourceRect.pivot;
            rect.sizeDelta = sourceRect.rect.size;
            var element = go.GetComponent<LayoutElement>();
            element.preferredWidth = sourceRect.rect.width;
            element.preferredHeight = sourceRect.rect.height;
            element.minWidth = sourceRect.rect.width;
            element.minHeight = sourceRect.rect.height;
            return go;
        }

        private GameObject CreatePreviewGhost(
            UI_行动顺序 source,
            RectTransform targetRect,
            Vector2 targetPosition)
        {
            if (source == null || targetRect == null) return null;
            var ghost = Instantiate(source.gameObject, cardsContainer);
            ghost.name = "ActionPreviewGhost";
            var element = ghost.GetComponent<LayoutElement>();
            if (element == null) element = ghost.AddComponent<LayoutElement>();
            element.ignoreLayout = true;
            // 先确定最终层级，再写入本地坐标；避免首帧 sibling 变化触发一次延迟布局修正。
            ghost.transform.SetAsLastSibling();
            var ghostRect = ghost.GetComponent<RectTransform>();
            if (ghostRect != null)
            {
                // 预览卡与目标占位槽共享锚点/轴心，只改变本地 Y，保证严格从正下方垂直升入。
                ghostRect.anchorMin = targetRect.anchorMin;
                ghostRect.anchorMax = targetRect.anchorMax;
                ghostRect.pivot = targetRect.pivot;
                ghostRect.anchoredPosition = targetPosition + Vector2.down * actionPreviewRiseDistance;
            }

            // 分身只承担视觉职责，不能响应悬停或点击。
            var group = ghost.GetComponent<CanvasGroup>();
            if (group == null) group = ghost.AddComponent<CanvasGroup>();
            group.alpha = 0.45f;
            group.blocksRaycasts = false;
            group.interactable = false;
            return ghost;
        }

        private void StartPreviewGhostLoop(
            RectTransform ghostRect,
            CanvasGroup ghostGroup,
            Vector2 targetPosition)
        {
            if (ghostRect == null) return;

            _actionPreviewGhostTween?.Kill();
            _actionPreviewGhostTarget = targetPosition;
            Vector2 startPosition = targetPosition + Vector2.down * actionPreviewRiseDistance;
            ghostRect.anchoredPosition = startPosition;
            if (ghostGroup != null) ghostGroup.alpha = 0.15f;

            // 把坐标重置放进循环本身。这样首轮会在 Tween 真正开始的那一帧重新读取稳定后的
            // RectTransform 状态，不会使用实例化/布局尚未完成时留下的首帧坐标。
            _actionPreviewGhostTween = DOTween.Sequence().SetUpdate(true);
            _actionPreviewGhostTween.AppendCallback(() =>
            {
                if (ghostRect == null) return;
                ghostRect.anchoredPosition = startPosition;
                if (ghostGroup != null) ghostGroup.alpha = 0.15f;
            });
            // 不使用 DOAnchorPos：它会在 Sequence 首次启动时缓存 RectTransform 的当时位置，
            // 而预览对象首帧仍可能被 Unity UI 做一次延迟布局。改为显式 0→1 插值后，
            // 每一帧的位置都只由固定的 start/target 决定，首轮与后续循环完全一致。
            _actionPreviewGhostTween.Append(
                DOTween.To(
                        () => 0f,
                        progress =>
                        {
                            if (ghostRect != null)
                            {
                                ghostRect.anchoredPosition = Vector2.LerpUnclamped(
                                    startPosition,
                                    targetPosition,
                                    progress);
                            }
                        },
                        1f,
                        actionPreviewDuration)
                    .SetEase(Ease.OutCubic));
            if (ghostGroup != null)
            {
                _actionPreviewGhostTween.Join(
                    ghostGroup.DOFade(1f, actionPreviewDuration * 0.65f).SetEase(Ease.OutQuad));
            }
            _actionPreviewGhostTween.AppendInterval(0.18f);
            if (ghostGroup != null)
            {
                _actionPreviewGhostTween.Append(ghostGroup.DOFade(0.15f, 0.16f));
            }
            _actionPreviewGhostTween.SetLoops(-1, LoopType.Restart);
        }

        private static int ResolvePreviewInsertionCardIndex(
            IReadOnlyList<ATB.TurnOrderEntry> baseOrder,
            IReadOnlyList<ATB.TurnOrderEntry> previewOrder,
            int previewCardIndex,
            int previewRound)
        {
            // 找预览角色之后的第一张卡，并在当前显示顺序中定位同一条目，在它之前插入。
            if (previewCardIndex + 1 < previewOrder.Count)
            {
                var successor = previewOrder[previewCardIndex + 1];
                for (int i = 1; i < baseOrder.Count; i++)
                {
                    var candidate = baseOrder[i];
                    if (candidate.UnitId == successor.UnitId
                        && candidate.Round == successor.Round
                        && candidate.IsWeather == successor.IsWeather
                        && candidate.IsCast == successor.IsCast)
                    {
                        return i;
                    }
                }
            }

            // 后继超出显示窗口时，退化为按绝对回合寻找第一个更晚的可见条目。
            for (int i = 1; i < baseOrder.Count; i++)
            {
                if (baseOrder[i].Round > previewRound) return i;
            }
            return baseOrder.Count;
        }

        private int GetInsertionSiblingIndex(int targetCardIndex)
        {
            int visibleCard = 0;
            int lastSibling = 0;
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot?.Card == null || !slot.Card.gameObject.activeSelf) continue;
                int sibling = slot.Card.transform.GetSiblingIndex();
                lastSibling = Mathf.Max(lastSibling, sibling + 1);
                if (visibleCard == targetCardIndex) return sibling;
                visibleCard++;
            }
            return Mathf.Clamp(lastSibling, 0, cardsContainer.childCount);
        }

        private Dictionary<RectTransform, Vector3> CaptureLayoutPositions()
        {
            var result = new Dictionary<RectTransform, Vector3>();
            if (cardsContainer == null) return result;
            for (int i = 0; i < cardsContainer.childCount; i++)
            {
                var child = cardsContainer.GetChild(i) as RectTransform;
                if (child == null || !child.gameObject.activeSelf) continue;
                if (_actionPreviewGhost != null && child.gameObject == _actionPreviewGhost) continue;
                if (_actionPreviewPlaceholder != null && child.gameObject == _actionPreviewPlaceholder) continue;
                result[child] = child.position;
            }
            return result;
        }

        private static void RestoreLayoutPositions(Dictionary<RectTransform, Vector3> positions)
        {
            foreach (var pair in positions)
            {
                if (pair.Key != null) pair.Key.position = pair.Value;
            }
        }

        private void SetPreviewPlaceholderWidth(float width)
        {
            if (_actionPreviewPlaceholder == null) return;
            var element = _actionPreviewPlaceholder.GetComponent<LayoutElement>();
            if (element == null) return;
            float resolvedWidth = Mathf.Max(0f, width);
            element.minWidth = resolvedWidth;
            element.preferredWidth = resolvedWidth;
        }

        private float GetPreviewPlaceholderWidth()
        {
            if (_actionPreviewPlaceholder == null) return 0f;
            var element = _actionPreviewPlaceholder.GetComponent<LayoutElement>();
            return element != null ? Mathf.Max(0f, element.preferredWidth) : 0f;
        }

        private void SetLayoutEnabled(bool enabled)
        {
            if (_layoutGroup != null) _layoutGroup.enabled = enabled;
        }

        private void ForceLayout()
        {
            if (cardsContainer == null) return;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(cardsContainer);
            Canvas.ForceUpdateCanvases();
        }

        private void ClearActionPreviewImmediate(bool refresh)
        {
            _actionPreviewTween?.Kill();
            _actionPreviewTween = null;
            _actionPreviewGhostTween?.Kill();
            _actionPreviewGhostTween = null;
            if (_actionPreviewPlaceholder != null)
            {
                _actionPreviewPlaceholder.SetActive(false);
                Destroy(_actionPreviewPlaceholder);
            }
            if (_actionPreviewGhost != null)
            {
                _actionPreviewGhost.SetActive(false);
                Destroy(_actionPreviewGhost);
            }
            _actionPreviewPlaceholder = null;
            _actionPreviewGhost = null;
            _actionPreviewUnitId = null;
            _actionPreviewDelay = 0;
            _actionPreviewGhostTarget = Vector2.zero;
            _actionPreviewPlaceholderWidth = 0f;
            _actionPreviewActive = false;
            _actionPreviewCommitted = false;
            SetLayoutEnabled(true);
            ForceLayout();
            if (refresh) RefreshOrder();
        }

        private UI_行动顺序 SpawnCard()
        {
            if (actionOrderPrefab == null)
            {
                Debug.LogError("[TurnOrderView] actionOrderPrefab 未设置");
                return null;
            }

            var go = Instantiate(actionOrderPrefab, cardsContainer);
            var card = go.GetComponent<UI_行动顺序>();
            if (card == null) card = go.AddComponent<UI_行动顺序>();
            return card;
        }

        private void HideSlotsFrom(int startIndex)
        {
            for (int i = startIndex; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot?.Card == null) continue;
                if (slot.Card.gameObject.activeSelf) slot.Card.gameObject.SetActive(false);
                slot.Card.SetCastSelection(null, false, null);
                slot.UnitId = null; // 复用前清掉承载单位，下次强制 Setup
            }
        }

        private LevelIndicator GetSeparator(int index)
        {
            if (levelIndicatorPrefab == null)
            {
                Debug.LogWarning("[TurnOrderView] levelIndicatorPrefab 未设置，无法显示回合分隔条");
                return null;
            }

            while (_separators.Count <= index)
            {
                var go = Instantiate(levelIndicatorPrefab, cardsContainer);
                go.name = $"LevelIndicator_{_separators.Count}";
                var li = go.GetComponent<LevelIndicator>();
                if (li == null) li = go.AddComponent<LevelIndicator>();
                _separators.Add(li);
            }

            var sep = _separators[index];
            if (sep != null) sep.SetVisible(true);
            return sep;
        }

        private void HideSeparatorsFrom(int startIndex)
        {
            for (int i = startIndex; i < _separators.Count; i++)
            {
                if (_separators[i] != null)
                    _separators[i].SetVisible(false);
            }
        }

        #endregion

        #region 容器 / 布局

        private void EnsureContainer()
        {
            if (cardsContainer != null) return;

            var go = new GameObject("CardRow", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            cardsContainer = go.GetComponent<RectTransform>();
        }

        private void EnsurePrefab()
        {
            if (actionOrderPrefab != null) return;
            var loaded = Resources.Load<GameObject>("UI/BattleScene/UI_ActionOrder");
            if (loaded != null)
                actionOrderPrefab = loaded;
            else
                Debug.LogWarning("[TurnOrderView] 未在 Inspector 赋值 actionOrderPrefab，也未能从 Resources 找到");
        }

        private void EnsureLevelIndicatorPrefab()
        {
            if (levelIndicatorPrefab != null) return;
            var loaded = Resources.Load<GameObject>("UI/BattleScene/Prefab_LevelIndicator");
            if (loaded != null)
                levelIndicatorPrefab = loaded;
            else
                Debug.LogWarning("[TurnOrderView] 未在 Inspector 赋值 levelIndicatorPrefab，也未能从 Resources 找到");
        }

        private void ApplyLayoutGroup()
        {
            if (cardsContainer == null) return;
            var hlg = cardsContainer.GetComponent<HorizontalLayoutGroup>();
            if (hlg == null) hlg = cardsContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
            _layoutGroup = hlg;
            hlg.spacing               = cardSpacing;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth      = false;
            hlg.childControlHeight     = false;
            hlg.childAlignment         = TextAnchor.MiddleLeft;

            var fitter = cardsContainer.GetComponent<ContentSizeFitter>();
            if (fitter == null) fitter = cardsContainer.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        }

        #endregion
    }
}
