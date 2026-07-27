using System.Collections.Generic;
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
