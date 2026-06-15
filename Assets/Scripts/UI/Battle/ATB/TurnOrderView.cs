using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Ashlight.Battle.Core.Data;

namespace Scripts.UI
{
    /// <summary>
    /// 行动顺序视图
    /// 管理一排 UI_ActionOrder 卡片，按 ATB 位置从左到右排列。
    /// 最左侧 = 最先行动；金框 = 当前行动单位；攻击图标 = 进入执行轨。
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
        [Tooltip("Prefab_LevelIndicator（竖线分隔条），用于在不同 Slot 格之间分隔；留空则从 Resources 自动加载")]
        private GameObject levelIndicatorPrefab;

        [Header("子容器")]
        [Tooltip("存放所有卡片的 RectTransform，需挂 HorizontalLayoutGroup。留空自动创建。")]
        [SerializeField] private RectTransform cardsContainer;

        [Header("布局")]
        [SerializeField] private float cardSpacing = 6f;

        [Header("ATB 引用")]
        [SerializeField] private ATB atb;

        #endregion

        #region 内部数据

        private class CardEntry
        {
            public string        UnitId;
            public UI_行动顺序   Card;
            public bool          IsExecuting; // 是否处于执行轨
        }

        private readonly List<CardEntry> _cards = new List<CardEntry>();
        private string _activeUnitId;

        /// <summary>回合分隔条对象池（穿插在卡片之间，按需创建、复用、隐藏）</summary>
        private readonly List<LevelIndicator> _separators = new List<LevelIndicator>();

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            EnsureContainer();
            EnsurePrefab();
            EnsureLevelIndicatorPrefab();

            // 在 Awake 就设好 HLG，防止 Initialize() 早于 Start() 被调用时布局缺失
            ApplyLayoutGroup();
        }

        #endregion

        #region 公共 API

        /// <summary>
        /// 初始化：根据战斗单位创建卡片，在 ATB.InitializeByUnits 之后调用
        /// </summary>
        public void Initialize(IReadOnlyList<UnitState> playerUnits, IReadOnlyList<UnitState> enemyUnits)
        {
            ClearCards();

            if (playerUnits != null)
                foreach (var u in playerUnits)
                {
                    if (u == null || u.IsDead) continue;
                    SpawnCard(u.UnitId, u.ConfigId);
                }

            if (enemyUnits != null)
                foreach (var u in enemyUnits)
                {
                    if (u == null || u.IsDead) continue;
                    SpawnCard(u.UnitId, u.ConfigId);
                }

            RefreshOrder();
        }

        /// <summary>
        /// 设置高亮（金框）：当前行动单位；传 null 清除所有高亮
        /// </summary>
        public void SetActiveUnit(string unitId)
        {
            _activeUnitId = unitId;
            ApplyHighlight();
        }

        /// <summary>
        /// 标记单位进入/退出执行轨（显示攻击图标）
        /// </summary>
        public void SetExecuting(string unitId, bool executing)
        {
            var entry = FindEntry(unitId);
            if (entry == null) return;
            entry.IsExecuting = executing;
            entry.Card.SetAttacking(executing);
        }

        /// <summary>
        /// 每帧刷新卡片排列顺序（根据 ATB 行动顺序重排 sibling index），
        /// 并在相邻卡片所属 Slot 格不同处插入一条 LevelIndicator 分隔条。
        /// 同格（相同 DistanceTo0）的单位挨在一起不分隔；执行轨单位（-1）算同一组。
        /// 分隔条会显示其右侧那一组的格数（即“后面这些单位在 N 个回合之后行动”）。
        /// </summary>
        public void RefreshOrder()
        {
            if (atb == null || cardsContainer == null) return;

            var order = atb.GetSortedTurnOrder();
            if (order == null || order.Count == 0)
            {
                HideSeparatorsFrom(0);
                return;
            }

            int  sibling = 0;     // 当前要分配的 sibling index
            int  sepUsed = 0;     // 本次已使用的分隔条数量
            bool hasPrev = false;
            int  prevKey = 0;     // 上一张卡片的分组键（GroupKey：执行轨 -1，规划轨 Slots）

            for (int i = 0; i < order.Count; i++)
            {
                var entry = FindEntry(order[i].UnitId);
                if (entry?.Card == null) continue;

                var t = entry.Card.transform;
                if (t.parent != cardsContainer) continue;

                int key = order[i].GroupKey;

                // 跨组：进入新的一组前插入一条分隔条，并标注新组的格数
                // （执行轨整组 GroupKey=-1，彼此不分隔，共享同一条执行轨）
                if (hasPrev && key != prevKey)
                {
                    var sep = GetSeparator(sepUsed++);
                    if (sep != null)
                    {
                        sep.transform.SetSiblingIndex(sibling++);
                        sep.SetLevel(Mathf.Max(0, key));
                    }
                }

                t.SetSiblingIndex(sibling++);
                prevKey = key;
                hasPrev = true;
            }

            // 隐藏本次未用到的多余分隔条
            HideSeparatorsFrom(sepUsed);
        }

        /// <summary>
        /// 移除指定单位的卡片（单位死亡时调用）
        /// </summary>
        public void RemoveUnit(string unitId)
        {
            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                if (_cards[i].UnitId != unitId) continue;
                if (_cards[i].Card != null) Destroy(_cards[i].Card.gameObject);
                _cards.RemoveAt(i);
                break;
            }
        }

        #endregion

        #region 卡片创建

        private void SpawnCard(string unitId, string configId)
        {
            if (actionOrderPrefab == null)
            {
                Debug.LogError("[TurnOrderView] actionOrderPrefab 未设置");
                return;
            }

            var go   = Instantiate(actionOrderPrefab, cardsContainer);
            var card = go.GetComponent<UI_行动顺序>();
            if (card == null)
            {
                // Prefab 上还没手动挂脚本时，运行时自动添加（Awake 会立即执行并完成 UIBind 绑定）
                card = go.AddComponent<UI_行动顺序>();
            }

            card.Setup(configId);

            _cards.Add(new CardEntry
            {
                UnitId      = unitId,
                Card        = card,
                IsExecuting = false
            });
        }

        #endregion

        #region 高亮逻辑

        private void ApplyHighlight()
        {
            bool anyActive = !string.IsNullOrEmpty(_activeUnitId);
            foreach (var entry in _cards)
            {
                bool isActive = entry.UnitId == _activeUnitId;
                entry.Card.SetHighlight(isActive);
            }
        }

        #endregion

        #region 工具方法

        private CardEntry FindEntry(string unitId)
        {
            foreach (var e in _cards)
                if (e.UnitId == unitId) return e;
            return null;
        }

        private void ClearCards()
        {
            foreach (var e in _cards)
                if (e.Card != null) Destroy(e.Card.gameObject);
            _cards.Clear();
            _activeUnitId = null;

            // 重新初始化时隐藏所有分隔条（对象池保留，等下次 RefreshOrder 复用）
            HideSeparatorsFrom(0);
        }

        private void EnsureContainer()
        {
            if (cardsContainer != null) return;

            // 在自身下自动创建 CardRow 子物体
            var go = new GameObject("CardRow", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            cardsContainer = go.GetComponent<RectTransform>();
        }

        private void EnsurePrefab()
        {
            if (actionOrderPrefab != null) return;
            // 尝试从 Resources 加载
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

        /// <summary>
        /// 获取第 index 条分隔条（不够则实例化新的），并确保其处于激活态。
        /// 返回 null 表示 prefab 缺失。
        /// </summary>
        private LevelIndicator GetSeparator(int index)
        {
            if (levelIndicatorPrefab == null)
            {
                Debug.LogWarning("[TurnOrderView] levelIndicatorPrefab 未设置，无法显示回合分隔条");
                return null;
            }

            while (_separators.Count <= index)
            {
                var go  = Instantiate(levelIndicatorPrefab, cardsContainer);
                go.name = $"LevelIndicator_{_separators.Count}";
                var li  = go.GetComponent<LevelIndicator>();
                if (li == null)
                {
                    // prefab 上还没手动挂脚本时，运行时自动添加
                    li = go.AddComponent<LevelIndicator>();
                }
                _separators.Add(li);
            }

            var sep = _separators[index];
            if (sep != null) sep.SetVisible(true);
            return sep;
        }

        /// <summary>
        /// 隐藏下标 &gt;= startIndex 的所有分隔条（本帧未用到的）。
        /// </summary>
        private void HideSeparatorsFrom(int startIndex)
        {
            for (int i = startIndex; i < _separators.Count; i++)
            {
                if (_separators[i] != null)
                    _separators[i].SetVisible(false);
            }
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
        }

        #endregion
    }
}
