using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using cfg;
using cfg.Character;
using Ashlight.Common.Events;
using Ashlight.Common.Utils;
using Ashlight.Config;
using Ashlight.Battle;
using Ashlight.Battle.Core.Data;
using Ashlight.State.Runtime;
using Ashlight.Systems.Core;
using Sirenix.OdinInspector;
using DG.Tweening;

namespace Scripts.UI
{
    /// <summary>
    /// UI_BattleScene的业务逻辑部分（手动编写）
    /// 战斗场景UI控制器，管理战斗场景中的UI元素和交互
    /// </summary>
    public partial class UI_BattleScene : MonoBehaviour
    {
        #region 序列化字段

        [Header("卡牌设置")]
        [SerializeField]
        [Tooltip("CardViewController预制体，用于实例化手牌")]
        private GameObject cardViewControllerPrefab;

        [Header("手牌设置")]
        [SerializeField]
        [Tooltip("手牌最大数量")]
        private int maxHandSize = 10;

        [SerializeField]
        [Tooltip("手牌间距")]
        private float cardSpacing = 10f;

        [Header("单位预制体设置")]
        [SerializeField]
        [Tooltip("Character预制体")]
        private GameObject characterPrefab;

        [SerializeField]
        [Tooltip("Enemy预制体")]
        private GameObject enemyPrefab;

        [Header("时间轴设置")]
        [SerializeField]
        [Tooltip("TimelineTrackView预制体")]
        private GameObject timelineTrackPrefab;

        [Header("伤害数字设置")]
        [SerializeField]
        [Tooltip("伤害数字预制体（包含TextMeshProUGUI组件，如果为空则使用动态创建）")]
        private GameObject damageTextPrefab;

        [Header("测试设置")]
        [SerializeField]
        [Tooltip("测试时添加的卡牌数量")]
        [Range(1, 10)]
        private int testCardCount = 3;

        [SerializeField]
        [Tooltip("测试用遭遇战ID")]
        private string testEncounterId = "E001";

        #endregion

        #region 私有字段

        private List<CardViewController> _handCards = new List<CardViewController>();
        private int _currentMoney = 0;
        private BattleManager _battleManager;

        #region 卡牌对象池

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

        #endregion
        
        // 玩家角色UI列表
        private List<Character> _playerCharacters = new List<Character>();
        
        // 敌人UI列表
        private List<Enemy> _enemies = new List<Enemy>();
        
        // 时间轴UI列表
        private List<Timeline.TimelineTrackView> _playerTimelines = new List<Timeline.TimelineTrackView>();
        private Timeline.TimelineTrackView _enemyTimeline;

        private Dictionary<string, int> _pendingDamageByPair = new Dictionary<string, int>();

        #endregion

        #region Unity生命周期

        /// <summary>
        /// 初始化
        /// </summary>
        private void Awake()
        {
            // 调用自动生成的UI绑定初始化方法
            InitUIBindings();

            // 加载CardViewController预制体（如果未在Inspector中设置）
            LoadCardViewControllerPrefab();

            // 设置按钮监听
            SetupButtonListeners();

            // 订阅敌人意图事件
            GameEvent.Subscribe<EnemyIntentionSelectedEvent>(OnEnemyIntentionSelected);

            // 订阅攻击执行事件（保留用于伤害数字显示）
            GameEvent.Subscribe<AttackExecutedEvent>(OnAttackExecuted);

            // 订阅卡片执行事件（用于战斗演出动画）
            GameEvent.Subscribe<CardExecutedEvent>(OnCardExecuted);

            // 订阅血量预测事件
            GameEvent.Subscribe<HpPredictionEvent>(OnHpPredictionReceived);
            GameEvent.Subscribe<HpPredictionStopEvent>(OnHpPredictionStop);

            // 订阅时间轴前进事件
            GameEvent.Subscribe<BeforeTimelineAdvanceEvent>(OnBeforeTimelineAdvance);
            GameEvent.Subscribe<AfterTimelineAdvanceEvent>(OnAfterTimelineAdvance);
        }

        /// <summary>
        /// 启动时初始化
        /// </summary>
        private void Start()
        {
            // 初始化战斗场景
            InitializeBattleScene();
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        private void Update()
        {
            // 监听空格键触发时间轴前进
            if (Input.GetKeyDown(KeyCode.Space))
            {
                OnAdvanceStepButtonClick();
            }
        }

        /// <summary>
        /// 销毁时清理
        /// </summary>
        private void OnDestroy()
        {
            // 取消订阅事件
            GameEvent.Unsubscribe<EnemyIntentionSelectedEvent>(OnEnemyIntentionSelected);
            GameEvent.Unsubscribe<AttackExecutedEvent>(OnAttackExecuted);
            GameEvent.Unsubscribe<CardExecutedEvent>(OnCardExecuted);
            GameEvent.Unsubscribe<HpPredictionEvent>(OnHpPredictionReceived);
            GameEvent.Unsubscribe<HpPredictionStopEvent>(OnHpPredictionStop);
            GameEvent.Unsubscribe<BeforeTimelineAdvanceEvent>(OnBeforeTimelineAdvance);
            GameEvent.Unsubscribe<AfterTimelineAdvanceEvent>(OnAfterTimelineAdvance);

            // 移除按钮监听
            RemoveButtonListeners();

            // 清理手牌
            ClearHandCards();

            // 清理战斗单位
            ClearBattleUnits();

            // 清理时间轴
            ClearTimelines();
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 初始化战斗场景
        /// </summary>
        public void InitializeBattleScene()
        {
            // 初始化金钱显示
            UpdateMoneyDisplay();

            // 清空手牌
            ClearHandCards();

            // 获取或创建BattleManager
            _battleManager = BattleManager.Instance;
            if (_battleManager == null)
            {
                GameObject battleManagerObj = new GameObject("BattleManager");
                _battleManager = battleManagerObj.AddComponent<BattleManager>();
                Debug.Log("[UI_BattleScene] BattleManager已创建");
            }

            // 创建战斗信息并初始化战斗
            InitializeBattle();

            // 创建玩家和敌人的UI
            CreateBattleUnits();

            // 创建时间轴UI
            CreateTimelines();

            // 时间轴UI创建完成后，开始第一回合（触发敌人意图选择）
            if (_battleManager != null)
            {
                Debug.Log("[UI_BattleScene] 时间轴UI已创建，开始第一回合");
                _battleManager.StartPlayerTurn();
            }

            Debug.Log("[UI_BattleScene] 战斗场景初始化完成");
        }

        /// <summary>
        /// 初始化战斗（从GameManager获取队伍信息）
        /// </summary>
        private void InitializeBattle()
        {
            // 从GameManager获取当前激活的队伍
            var gameManager = GameManager.Instance;
            if (gameManager == null || gameManager.CurrentSave == null)
            {
                Debug.LogWarning("[UI_BattleScene] GameManager或CurrentSave不存在，使用测试数据初始化战斗");
                InitializeBattleWithTestData();
                return;
            }

            var activeTeam = gameManager.CurrentSave.ActiveTeam;
            if (activeTeam == null || activeTeam.Count == 0)
            {
                Debug.LogWarning("[UI_BattleScene] ActiveTeam为空，使用测试数据初始化战斗");
                InitializeBattleWithTestData();
                return;
            }

            // TODO: 从关卡配置或场景参数获取EncounterId
            // 目前使用测试用的EncounterId
            string encounterId = testEncounterId;

            // 创建战斗信息
            var battleInfo = BattleInfo.Create(activeTeam, encounterId, initialDrawCount: 5);

            // 初始化战斗
            _battleManager.InitializeBattle(battleInfo);

            // 初始化卡牌对象池（在 DisplayHandCards 之前）
            InitializeCardPool();

            // 显示初始手牌到UI
            DisplayHandCards();
        }

        /// <summary>
        /// 使用测试数据初始化战斗
        /// </summary>
        private void InitializeBattleWithTestData()
        {
            Debug.Log("[UI_BattleScene] 使用测试数据初始化战斗");

            // 创建测试用的角色列表（包含Rocket、Irene、Zhouzhou）
            var testCharacters = new List<CharacterEnum> 
            { 
                CharacterEnum.Rocket,
                CharacterEnum.Irene,
                CharacterEnum.Zhouzhou
            };
            string encounterId = testEncounterId;

            // 创建战斗信息
            var battleInfo = BattleInfo.Create(testCharacters, encounterId, initialDrawCount: 5);

            // 初始化战斗
            _battleManager.InitializeBattle(battleInfo);

            // 初始化卡牌对象池（在 DisplayHandCards 之前）
            InitializeCardPool();

            // 显示初始手牌到UI
            DisplayHandCards();
        }

        /// <summary>
        /// 显示手牌到UI（从对象池获取而非创建新的）
        /// </summary>
        private void DisplayHandCards()
        {
            if (_battleManager == null || _battleManager.CurrentState == null)
            {
                Debug.LogError("[UI_BattleScene] 无法显示手牌：BattleManager或CurrentState不存在");
                return;
            }

            // 1. 将当前 UI 层手牌移到弃牌堆（而非销毁）
            foreach (var card in _handCards.ToList())
            {
                MoveCardToDiscard(card);
            }

            // 2. 从数据层获取当前手牌
            var handCards = _battleManager.CurrentState.DeckSystem.Hand;
            if (handCards == null || handCards.Count == 0)
            {
                Debug.Log("[UI_BattleScene] 手牌为空");
                return;
            }

            // 3. 从池中获取对应的 CardViewController
            foreach (var cardState in handCards)
            {
                if (cardState == null)
                {
                    Debug.LogWarning("[UI_BattleScene] 手牌中存在null");
                    continue;
                }

                var cardView = GetCardFromPool(cardState.InstanceId);
                if (cardView != null)
                {
                    MoveCardToHand(cardView);
                }
                else
                {
                    Debug.LogWarning($"[UI_BattleScene] 池中未找到卡牌: {cardState.InstanceId} (CardId: {cardState.CardId})");
                }
            }

            Debug.Log($"[UI_BattleScene] 显示手牌完成: {_handCards.Count} 张");
        }

        /// <summary>
        /// 创建战斗单位UI（玩家和敌人）
        /// </summary>
        private void CreateBattleUnits()
        {
            if (_battleManager == null || _battleManager.CurrentState == null)
            {
                Debug.LogError("[UI_BattleScene] 无法创建战斗单位：BattleManager或CurrentState不存在");
                return;
            }

            // 清空现有单位
            ClearBattleUnits();

            // 创建玩家角色UI
            CreatePlayerCharacters();

            // 创建敌人UI
            CreateEnemies();
        }

        /// <summary>
        /// 创建玩家角色UI
        /// </summary>
        private void CreatePlayerCharacters()
        {
            if (PlayerPosition == null)
            {
                Debug.LogError("[UI_BattleScene] PlayerPosition未绑定");
                return;
            }

            if (characterPrefab == null)
            {
                Debug.LogError("[UI_BattleScene] Character预制体未设置");
                return;
            }

            var playerUnits = _battleManager.CurrentState.PlayerUnits;
            Debug.Log($"[UI_BattleScene] 创建 {playerUnits.Count} 个玩家角色UI");

            for (int i = 0; i < playerUnits.Count; i++)
            {
                var unitState = playerUnits[i];
                
                // 实例化Character预制体
                GameObject characterObj = Instantiate(characterPrefab, PlayerPosition);
                Character character = characterObj.GetComponent<Character>();

                if (character == null)
                {
                    Debug.LogError("[UI_BattleScene] Character组件未找到");
                    Destroy(characterObj);
                    continue;
                }

                // 使用UnitState初始化Character
                character.Initialize(unitState);

                // 设置位置（如果有多个角色，可以排列）
                RectTransform rectTransform = characterObj.GetComponent<RectTransform>();
                if (rectTransform != null && playerUnits.Count > 1)
                {
                    // 水平排列多个角色
                    float spacing = 200f; // 角色之间的间距
                    float totalWidth = (playerUnits.Count - 1) * spacing;
                    float startX = -totalWidth / 2f;
                    rectTransform.anchoredPosition = new Vector2(startX + i * spacing, 0f);
                }

                _playerCharacters.Add(character);
                Debug.Log($"[UI_BattleScene] 创建玩家角色: {unitState.UnitId} ({unitState.ConfigId})");
            }
        }

        /// <summary>
        /// 创建敌人UI
        /// </summary>
        private void CreateEnemies()
        {
            if (EnemyPostion == null)
            {
                Debug.LogError("[UI_BattleScene] EnemyPostion未绑定");
                return;
            }

            if (enemyPrefab == null)
            {
                Debug.LogError("[UI_BattleScene] Enemy预制体未设置");
                return;
            }

            var enemyUnits = _battleManager.CurrentState.EnemyUnits;
            Debug.Log($"[UI_BattleScene] 创建 {enemyUnits.Count} 个敌人UI");

            for (int i = 0; i < enemyUnits.Count; i++)
            {
                var unitState = enemyUnits[i];
                
                // 实例化Enemy预制体
                GameObject enemyObj = Instantiate(enemyPrefab, EnemyPostion);
                Enemy enemy = enemyObj.GetComponent<Enemy>();

                if (enemy == null)
                {
                    Debug.LogError("[UI_BattleScene] Enemy组件未找到");
                    Destroy(enemyObj);
                    continue;
                }

                // 使用UnitState初始化Enemy
                enemy.Initialize(unitState);

                // 设置位置（如果有多个敌人，可以排列）
                RectTransform rectTransform = enemyObj.GetComponent<RectTransform>();
                if (rectTransform != null && enemyUnits.Count > 1)
                {
                    // 水平排列多个敌人
                    float spacing = 200f; // 敌人之间的间距
                    float totalWidth = (enemyUnits.Count - 1) * spacing;
                    float startX = -totalWidth / 2f;
                    rectTransform.anchoredPosition = new Vector2(startX + i * spacing, 0f);
                }

                _enemies.Add(enemy);
                Debug.Log($"[UI_BattleScene] 创建敌人: {unitState.UnitId} ({unitState.ConfigId})");
            }
        }

        /// <summary>
        /// 清空战斗单位UI
        /// </summary>
        private void ClearBattleUnits()
        {
            // 清空玩家角色
            foreach (var character in _playerCharacters)
            {
                if (character != null)
                {
                    Destroy(character.gameObject);
                }
            }
            _playerCharacters.Clear();

            // 清空敌人
            foreach (var enemy in _enemies)
            {
                if (enemy != null)
                {
                    Destroy(enemy.gameObject);
                }
            }
            _enemies.Clear();

            Debug.Log("[UI_BattleScene] 已清空所有战斗单位UI");
        }

        /// <summary>
        /// 更新所有单位的显示状态
        /// </summary>
        public void UpdateAllUnitsDisplay()
        {
            // 更新玩家角色
            foreach (var character in _playerCharacters)
            {
                if (character != null)
                {
                    character.UpdateFromUnitState();
                }
            }

            // 更新敌人
            foreach (var enemy in _enemies)
            {
                if (enemy != null)
                {
                    enemy.UpdateFromUnitState();
                }
            }
        }

        /// <summary>
        /// 创建时间轴UI（所有时间轴都放在TimelineContainer中）
        /// </summary>
        private void CreateTimelines()
        {
            if (timelineTrackPrefab == null)
            {
                Debug.LogError("[UI_BattleScene] timelineTrackPrefab未设置");
                return;
            }

            if (TimeLineContainer == null)
            {
                Debug.LogError("[UI_BattleScene] timelineContainer未设置");
                return;
            }

            Debug.Log("[UI_BattleScene] 创建时间轴UI...");

            // 先创建敌人共享时间轴（确保显示在最前面）
            GameObject enemyTimelineObj = Instantiate(timelineTrackPrefab, TimeLineContainer);
            enemyTimelineObj.name = "EnemySharedTimeline";
            
            _enemyTimeline = enemyTimelineObj.GetComponent<Timeline.TimelineTrackView>();
            if (_enemyTimeline == null)
            {
                Debug.LogError($"[UI_BattleScene] timelineTrackPrefab缺少TimelineTrackView组件");
                Destroy(enemyTimelineObj);
                return;
            }

            _enemyTimeline.InitializeShared(_battleManager.CurrentState.SharedEnemyTrack);
            
            // 确保敌人时间轴显示在最前面（第一个位置）
            enemyTimelineObj.transform.SetAsFirstSibling();

            Debug.Log("[UI_BattleScene] 敌人共享时间轴已创建（显示在最前面）");

            // 然后为每个玩家角色创建独立时间轴
            for (int i = 0; i < _playerCharacters.Count; i++)
            {
                var character = _playerCharacters[i];
                var unitState = character.GetUnitState();

                if (unitState == null)
                {
                    Debug.LogError($"[UI_BattleScene] 角色{i}的UnitState为null");
                    continue;
                }

                GameObject timelineObj = Instantiate(timelineTrackPrefab, TimeLineContainer);
                timelineObj.name = $"PlayerTimeline_{unitState.UnitId}";
                
                Timeline.TimelineTrackView trackView = timelineObj.GetComponent<Timeline.TimelineTrackView>();
                if (trackView == null)
                {
                    Debug.LogError($"[UI_BattleScene] timelineTrackPrefab缺少TimelineTrackView组件");
                    Destroy(timelineObj);
                    continue;
                }

                trackView.Initialize(unitState);
                _playerTimelines.Add(trackView);

                Debug.Log($"[UI_BattleScene] 为角色 {unitState.UnitId} 创建时间轴");
            }

            Debug.Log($"[UI_BattleScene] 时间轴创建完成，敌人时间轴: 1（最前）, 玩家时间轴: {_playerTimelines.Count}");
        }

        /// <summary>
        /// 更新所有时间轴显示
        /// </summary>
        public void UpdateAllTimelines()
        {
            foreach (var timeline in _playerTimelines)
            {
                if (timeline != null)
                {
                    timeline.RefreshDisplay();
                }
            }

            if (_enemyTimeline != null)
            {
                _enemyTimeline.RefreshDisplay();
            }
        }

        /// <summary>
        /// 清空时间轴UI
        /// </summary>
        private void ClearTimelines()
        {
            foreach (var timeline in _playerTimelines)
            {
                if (timeline != null)
                {
                    Destroy(timeline.gameObject);
                }
            }
            _playerTimelines.Clear();

            if (_enemyTimeline != null)
            {
                Destroy(_enemyTimeline.gameObject);
                _enemyTimeline = null;
            }

            Debug.Log("[UI_BattleScene] 已清空所有时间轴UI");
        }

        /// <summary>
        /// 刷新时间轴UI显示
        /// </summary>
        private void RefreshTimelineDisplay()
        {
            // 刷新所有玩家时间轴
            foreach (var timeline in _playerTimelines)
            {
                if (timeline != null)
                {
                    timeline.RefreshDisplay();
                }
            }

            // 刷新敌人时间轴
            if (_enemyTimeline != null)
            {
                _enemyTimeline.RefreshDisplay();
            }
        }

        /// <summary>
        /// 添加卡牌到手牌
        /// </summary>
        /// <param name="cardInfo">卡牌信息</param>
        public void AddCardToHand(CardInfo cardInfo)
        {
            
            if (cardInfo == null)
            {
                Debug.LogError("[UI_BattleScene] 卡牌信息为空");
                return;
            }

            if (_handCards.Count >= maxHandSize)
            {
                Debug.LogWarning($"[UI_BattleScene] 手牌已满，无法添加卡牌: {cardInfo.Name}");
                return;
            }

            if (cardViewControllerPrefab == null)
            {
                Debug.LogError("[UI_BattleScene] CardViewController预制体未设置！");
                return;
            }

            if (CardContainer == null)
            {
                Debug.LogError("[UI_BattleScene] CardContainer未绑定！");
                return;
            }

            // 实例化卡牌
            GameObject cardObj = Instantiate(cardViewControllerPrefab, CardContainer.transform);
            CardViewController cardView = cardObj.GetComponent<CardViewController>();
            
            if (cardView == null)
            {
                Debug.LogError("[UI_BattleScene] CardViewController组件未找到！");
                Destroy(cardObj);
                return;
            }

            // 初始化卡牌（战斗模式）
            cardView.Initialize(cardInfo, DescriptionMode.Battle);
            
            // 添加到手牌列表
            _handCards.Add(cardView);

            // 更新手牌布局
            UpdateHandLayout();

            Debug.Log($"[UI_BattleScene] 添加卡牌到手牌: {cardInfo.Name}");
        }

        /// <summary>
        /// 从手牌移除卡牌
        /// </summary>
        /// <param name="cardView">卡牌视图控制器</param>
        public void RemoveCardFromHand(CardViewController cardView)
        {
            if (cardView == null) return;

            if (_handCards.Remove(cardView))
            {
                Destroy(cardView.gameObject);
                UpdateHandLayout();
                Debug.Log("[UI_BattleScene] 从手牌移除卡牌");
            }
        }

        /// <summary>
        /// 设置金钱
        /// </summary>
        /// <param name="money">金钱数量</param>
        public void SetMoney(int money)
        {
            _currentMoney = money;
            UpdateMoneyDisplay();
        }

        /// <summary>
        /// 获取当前金钱
        /// </summary>
        /// <returns>当前金钱数量</returns>
        public int GetMoney()
        {
            return _currentMoney;
        }

        /// <summary>
        /// 获取手牌列表
        /// </summary>
        /// <returns>手牌列表</returns>
        public List<CardViewController> GetHandCards()
        {
            return new List<CardViewController>(_handCards);
        }

        /// <summary>
        /// 获取所有玩家角色(用于目标选择系统)
        /// </summary>
        /// <returns>所有玩家角色列表</returns>
        public List<Character> GetAllPlayerCharacters()
        {
            return new List<Character>(_playerCharacters);
        }

        /// <summary>
        /// 获取所有敌人(用于目标选择系统)
        /// </summary>
        /// <returns>所有敌人列表</returns>
        public List<Enemy> GetAllEnemies()
        {
            return new List<Enemy>(_enemies);
        }

        /// <summary>
        /// 根据单位ID查找时间轴
        /// </summary>
        /// <param name="unitId">单位ID（可以是UnitState.UnitId如"player_0"，也可以是CharacterEnum字符串如"Irene"）</param>
        /// <returns>对应的时间轴,如果未找到则返回null</returns>
        public Timeline.TimelineTrackView FindTimelineByUnitId(string unitId)
        {
            if (string.IsNullOrEmpty(unitId))
            {
                return null;
            }

            // 搜索玩家时间轴
            foreach (var timeline in _playerTimelines)
            {
                if (timeline == null)
                {
                    continue;
                }

                // 优先通过UnitState.UnitId查找（用于解算系统）
                string timelineUnitId = timeline.GetUnitId();
                if (!string.IsNullOrEmpty(timelineUnitId) && timelineUnitId == unitId)
                {
                    return timeline;
                }

                // 兼容旧代码：通过CharacterEnum字符串查找
                var track = timeline.GetTrack();
                if (track != null && track.OwnerCharacterId.HasValue)
                {
                    string ownerIdStr = track.OwnerCharacterId.Value.ToString();
                    if (ownerIdStr == unitId)
                    {
                        return timeline;
                    }
                }
            }

            // 检查是否为敌人时间轴
            if (unitId.StartsWith("enemy_") && _enemyTimeline != null)
            {
                return _enemyTimeline;
            }

            return null;
        }

        #endregion

        #region 私有方法

        #region 卡牌对象池管理

        /// <summary>
        /// 初始化卡牌对象池（战斗开始时调用）
        /// 为牌组中的每张卡牌创建一个 CardViewController
        /// </summary>
        private void InitializeCardPool()
        {
            ClearCardPool();

            if (_battleManager?.CurrentState?.DeckSystem == null)
            {
                Debug.LogError("[UI_BattleScene] 初始化卡牌池失败：BattleManager 或 DeckSystem 为空");
                return;
            }

            var deckSystem = _battleManager.CurrentState.DeckSystem;

            // 收集所有卡牌（抽牌堆 + 弃牌堆 + 手牌 + 使用中）
            var allCards = new List<CardRuntimeState>();
            allCards.AddRange(deckSystem.DrawPile);
            allCards.AddRange(deckSystem.DiscardPile);
            allCards.AddRange(deckSystem.Hand);
            allCards.AddRange(deckSystem.InPlayPile);

            Debug.Log($"[UI_BattleScene] 开始初始化卡牌池，共 {allCards.Count} 张卡牌");

            foreach (var cardState in allCards)
            {
                var cardInfo = ConfigLoader.Tables.TbCardInfo.GetOrDefault(cardState.CardId);
                if (cardInfo == null)
                {
                    Debug.LogWarning($"[UI_BattleScene] 未找到卡牌配置: {cardState.CardId}");
                    continue;
                }

                // 实例化到 CardDeck 容器
                GameObject cardObj = Instantiate(cardViewControllerPrefab, CardDeck);
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

            Debug.Log($"[UI_BattleScene] 卡牌池初始化完成，共 {_cardPool.Count} 张卡牌 UI");
        }

        /// <summary>
        /// 根据 InstanceId 从池中获取 CardViewController
        /// </summary>
        private CardViewController GetCardFromPool(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                Debug.LogWarning("[UI_BattleScene] GetCardFromPool: instanceId 为空");
                return null;
            }
            return _cardPool.TryGetValue(instanceId, out var card) ? card : null;
        }

        /// <summary>
        /// 将卡牌移到手牌区
        /// </summary>
        private void MoveCardToHand(CardViewController card)
        {
            if (card == null) return;

            card.transform.SetParent(CardContainer.transform);
            card.ResetForReuse();
            card.Show();

            _deckCards.Remove(card);
            _discardCards.Remove(card);

            if (!_handCards.Contains(card))
                _handCards.Add(card);

            UpdateHandLayout();
        }

        /// <summary>
        /// 将卡牌移到弃牌堆
        /// </summary>
        private void MoveCardToDiscard(CardViewController card)
        {
            if (card == null) return;

            card.transform.SetParent(CardBin);
            card.Hide();

            _handCards.Remove(card);
            _deckCards.Remove(card);

            if (!_discardCards.Contains(card))
                _discardCards.Add(card);
        }

        /// <summary>
        /// 将卡牌移到抽牌堆
        /// </summary>
        private void MoveCardToDeck(CardViewController card)
        {
            if (card == null) return;

            card.transform.SetParent(CardDeck);
            card.Hide();

            _handCards.Remove(card);
            _discardCards.Remove(card);

            if (!_deckCards.Contains(card))
                _deckCards.Add(card);
        }

        /// <summary>
        /// 从 _handCards 中移除卡牌引用（不销毁，不隐藏）
        /// 用于卡牌放到时间轴时
        /// </summary>
        public void RemoveCardFromHandList(CardViewController card)
        {
            if (card == null) return;
            _handCards.Remove(card);
            UpdateHandLayout();
            Debug.Log($"[UI_BattleScene] 从手牌列表移除卡牌: {card.GetCurrentCard()?.Name}");
        }

        /// <summary>
        /// UI 层洗牌：弃牌堆 -> 抽牌堆
        /// </summary>
        private void ReshuffleDiscardToDeckUI()
        {
            foreach (var card in _discardCards.ToList())
            {
                MoveCardToDeck(card);
            }
            Debug.Log($"[UI_BattleScene] UI层洗牌完成，抽牌堆: {_deckCards.Count} 张");
        }

        /// <summary>
        /// 清空卡牌池（战斗结束时调用）
        /// </summary>
        private void ClearCardPool()
        {
            foreach (var card in _cardPool.Values)
            {
                if (card != null)
                    Destroy(card.gameObject);
            }
            _cardPool.Clear();
            _deckCards.Clear();
            _handCards.Clear();
            _discardCards.Clear();
            Debug.Log("[UI_BattleScene] 卡牌池已清空");
        }

        #endregion

        /// <summary>
        /// 加载CardViewController预制体
        /// </summary>
        private void LoadCardViewControllerPrefab()
        {
            // 如果已经在Inspector中设置了，则不需要加载
            if (cardViewControllerPrefab != null)
                return;

            // 从AssetPath获取路径并加载预制体
            string resourcePath = AssetPath.GetResourcesPath(AssetPath.CardViewControllerPath);
            GameObject prefab = Resources.Load<GameObject>(resourcePath);

            if (prefab != null)
            {
                cardViewControllerPrefab = prefab;
                Debug.Log($"[UI_BattleScene] 从Resources加载CardViewController预制体: {resourcePath}");
            }
            else
            {
                Debug.LogError($"[UI_BattleScene] 无法从Resources加载CardViewController预制体，路径: {resourcePath}");
            }
        }

        /// <summary>
        /// 设置按钮监听
        /// </summary>
        private void SetupButtonListeners()
        {
            if (Btn_EndRoundBase != null)
            {
                Btn_EndRoundBase.onClick.AddListener(OnEndRoundButtonClick);
            }

            if (Btn_EmptyBase != null)
            {
                Btn_EmptyBase.onClick.AddListener(OnEmptyButtonClick);
            }

            if (Btn_PaikuBase != null)
            {
                Btn_PaikuBase.onClick.AddListener(OnPaikuButtonClick);
            }
        }

        /// <summary>
        /// 移除按钮监听
        /// </summary>
        private void RemoveButtonListeners()
        {
            if (Btn_EndRoundBase != null)
            {
                Btn_EndRoundBase.onClick.RemoveListener(OnEndRoundButtonClick);
            }

            if (Btn_EmptyBase != null)
            {
                Btn_EmptyBase.onClick.RemoveListener(OnEmptyButtonClick);
            }

            if (Btn_PaikuBase != null)
            {
                Btn_PaikuBase.onClick.RemoveListener(OnPaikuButtonClick);
            }
        }

        /// <summary>
        /// 更新手牌布局
        /// </summary>
        private void UpdateHandLayout()
        {
            if (CardContainer == null || _handCards.Count == 0)
                return;

            // 计算总宽度
            float totalWidth = (_handCards.Count - 1) * cardSpacing;
            
            // 计算起始位置（居中）
            float startX = -totalWidth / 2f;

            // 更新每张卡牌的位置
            for (int i = 0; i < _handCards.Count; i++)
            {
                if (_handCards[i] == null) continue;

                RectTransform cardRect = _handCards[i].transform as RectTransform;
                if (cardRect != null)
                {
                    float xPos = startX + i * cardSpacing;
                    cardRect.anchoredPosition = new Vector2(xPos, 0f);
                }
            }
        }

        /// <summary>
        /// 更新金钱显示
        /// </summary>
        private void UpdateMoneyDisplay()
        {
            if (Txt_Money != null)
            {
                Txt_Money.text = _currentMoney.ToString();
            }
        }

        /// <summary>
        /// 清空手牌 UI（移到弃牌堆而非销毁）
        /// </summary>
        private void ClearHandCards()
        {
            foreach (var card in _handCards.ToList())
            {
                MoveCardToDiscard(card);
            }
            _handCards.Clear();
        }

        #endregion

        #region Odin Inspector 测试方法

        /// <summary>
        /// 测试：添加卡牌到手牌
        /// 在Inspector中点击此按钮可以快速测试卡牌添加功能
        /// </summary>

        [Button("测试添加卡牌", ButtonSizes.Medium)]
        [PropertyOrder(100)]
        [InfoBox("点击此按钮可以添加测试卡牌到手牌容器中", InfoMessageType.Info)]
        private void TestAddCards()
        {
            ConfigLoader.Load();
            // 检查配置表是否加载
            if (ConfigLoader.Tables == null || ConfigLoader.Tables.TbCardInfo == null)
            {
                Debug.LogError("[UI_BattleScene] 配置表未加载，无法添加测试卡牌");
                return;
            }

            // 获取所有卡牌列表
            var cardList = ConfigLoader.Tables.TbCardInfo.DataList;
            if (cardList == null || cardList.Count == 0)
            {
                Debug.LogWarning("[UI_BattleScene] 卡牌配置表为空，无法添加测试卡牌");
                return;
            }

            // 检查容器
            if (CardContainer == null)
            {
                Debug.LogError("[UI_BattleScene] CardContainer未绑定，无法添加测试卡牌");
                return;
            }

            // 清空现有手牌（可选，用于测试）
            // ClearHandCards();

            // 添加指定数量的卡牌
            int addedCount = 0;
            int maxCards = Mathf.Min(testCardCount, cardList.Count, maxHandSize - _handCards.Count);

            for (int i = 0; i < maxCards; i++)
            {
                // 循环使用卡牌列表中的卡牌
                var cardInfo = cardList[i % cardList.Count];
                if (cardInfo != null)
                {
                    AddCardToHand(cardInfo);
                    addedCount++;
                }
            }

            Debug.Log($"[UI_BattleScene] 测试完成：成功添加 {addedCount} 张卡牌到手牌");
        }

        #endregion

        #region 按钮事件处理

        /// <summary>
        /// 下个回合按钮点击（Btn_EndRoundBase用于结束回合）
        /// </summary>
        private void OnEndRoundButtonClick()
        {
            if (_battleManager == null)
            {
                Debug.LogError("[UI_BattleScene] BattleManager为null，无法结束回合");
                return;
            }

            StartCoroutine(EndRoundCoroutine());
        }

        /// <summary>
        /// 前进一步按钮点击（空格键或按钮触发）
        /// </summary>
        private void OnAdvanceStepButtonClick()
        {
            Debug.Log("[UI_BattleScene] 前进一步按钮点击");

            if (_battleManager == null)
            {
                Debug.LogError("[UI_BattleScene] BattleManager为null，无法前进");
                return;
            }

            // 使用协程版本，支持动画等待
            StartCoroutine(AdvanceStepCoroutine());
        }

        /// <summary>
        /// 前进一步协程（支持动画等待）
        /// </summary>
        private IEnumerator AdvanceStepCoroutine()
        {
            Debug.Log("[UI_BattleScene] 开始前进一步（动画模式）...");

            // 停止血量预测显示（避免与实际解算冲突）
            if (_battleManager.PredictionManager != null)
            {
                _battleManager.PredictionManager.StopPrediction();
            }

            // 如果当前回合没有任何可解算格子，直接结束回合并开始下一回合
            if (_battleManager.CurrentState != null)
            {
                bool hasAnyBlocks = false;

                foreach (var unit in _battleManager.CurrentState.PlayerUnits)
                {
                    if (unit != null && unit.Track != null && !unit.Track.IsEmpty())
                    {
                        hasAnyBlocks = true;
                        break;
                    }
                }

                if (!hasAnyBlocks && _battleManager.CurrentState.SharedEnemyTrack != null)
                {
                    if (!_battleManager.CurrentState.SharedEnemyTrack.IsEmpty())
                    {
                        hasAnyBlocks = true;
                    }
                }

                if (!hasAnyBlocks)
                {
                    Debug.Log("[UI_BattleScene] 当前回合无可解算格子，自动结束回合并开始下一回合");
                    yield return EndRoundCoroutine();
                    yield break;
                }
            }

            // 调用BattleManager的前进一步方法（会触发事件）
            yield return _battleManager.AdvanceOneStepCoroutine();

            // 更新所有单位的UI显示（血量、护甲等）
            UpdateAllUnitsDisplay();

            Debug.Log("[UI_BattleScene] 前进一步完成");
        }

        /// <summary>
        /// 结束回合按钮点击（Btn_EmptyBase改为真正的结束回合）
        /// </summary>
        private void OnEmptyButtonClick()
        {
            Debug.Log("[UI_BattleScene] 结束回合按钮点击");

            if (_battleManager == null)
            {
                Debug.LogError("[UI_BattleScene] BattleManager为null，无法结束回合");
                return;
            }

            // 使用协程版本，支持动画等待
            StartCoroutine(EndRoundCoroutine());
        }

        /// <summary>
        /// 结束回合协程（真正的回合结束清理）
        /// </summary>
        private IEnumerator EndRoundCoroutine()
        {
            Debug.Log("[UI_BattleScene] 开始结束回合...");

            // 停止血量预测显示
            if (_battleManager.PredictionManager != null)
            {
                _battleManager.PredictionManager.StopPrediction();
            }

            // 1. 先执行当前时间格的技能/卡牌（会自动位移数据层和触发UI位移事件）
            yield return _battleManager.AdvanceOneStepCoroutine();

            // 2. 更新所有单位的UI显示（血量、护甲等）
            UpdateAllUnitsDisplay();

            // 3. 弃掉所有手牌、抽取新牌（不清空时间轴，让已放置的卡牌继续保留）
            _battleManager.DiscardHandAndDraw();

            // 4. 更新手牌UI显示（显示新抽取的牌）
            DisplayHandCards();

            // 5. 开始下一回合（敌人选择新的意图）
            _battleManager.StartPlayerTurn();

            Debug.Log($"[UI_BattleScene] 回合结束完成，当前回合: {_battleManager.CurrentRound}");
            
            yield break;
        }

        /// <summary>
        /// 牌库按钮点击
        /// </summary>
        private void OnPaikuButtonClick()
        {
            Debug.Log("[UI_BattleScene] 牌库按钮点击");
            // TODO: 实现查看牌库逻辑
        }

        /// <summary>
        /// 处理敌人意图选择事件
        /// 当敌人选择技能和时间槽位置时，创建并放置 EnemyTimeSlot
        /// </summary>
        private void OnEnemyIntentionSelected(EnemyIntentionSelectedEvent evt)
        {
            Debug.Log($"[UI_BattleScene] 收到敌人意图事件: {evt.EnemyUnitId} 使用 {evt.SkillInfo.Name} 在位置 {evt.TimeSlotPosition}，目标: {evt.TargetUnitId}");

            if (_enemyTimeline == null)
            {
                Debug.LogError("[UI_BattleScene] 敌人时间轴不存在，无法放置 EnemyTimeSlot");
                return;
            }

            // 查找攻击者（敌人）
            Enemy attacker = FindEnemyByUnitId(evt.EnemyUnitId);
            if (attacker == null)
            {
                Debug.LogWarning($"[UI_BattleScene] 未找到敌人UI: {evt.EnemyUnitId}");
            }

            // 查找目标（角色）
            Character target = FindCharacterByUnitId(evt.TargetUnitId);
            if (target == null)
            {
                Debug.LogWarning($"[UI_BattleScene] 未找到目标角色UI: {evt.TargetUnitId}");
            }

            // 直接在敌人时间轴上放置 EnemyTimeSlot，传递攻击者和目标
            _enemyTimeline.PlaceEnemyTimeSlot(evt.SkillInfo, evt.TimeSlotPosition, attacker, target);
        }

        /// <summary>
        /// 根据UnitId查找敌人UI
        /// </summary>
        private Enemy FindEnemyByUnitId(string unitId)
        {
            if (string.IsNullOrEmpty(unitId))
                return null;

            foreach (var enemy in _enemies)
            {
                if (enemy != null && enemy.GetUnitState()?.UnitId == unitId)
                {
                    return enemy;
                }
            }

            return null;
        }

        /// <summary>
        /// 根据UnitId查找角色UI
        /// </summary>
        private Character FindCharacterByUnitId(string unitId)
        {
            if (string.IsNullOrEmpty(unitId))
                return null;

            foreach (var character in _playerCharacters)
            {
                if (character != null && character.GetUnitState()?.UnitId == unitId)
                {
                    return character;
                }
            }

            return null;
        }

        /// <summary>
        /// 根据UnitId查找对应的UI GameObject
        /// </summary>
        private GameObject FindUnitUIObject(string unitId)
        {
            if (string.IsNullOrEmpty(unitId))
                return null;

            // 先在玩家角色中查找
            var character = FindCharacterByUnitId(unitId);
            if (character != null)
                return character.gameObject;

            // 再在敌人中查找
            var enemy = FindEnemyByUnitId(unitId);
            if (enemy != null)
                return enemy.gameObject;

            return null;
        }

        /// <summary>
        /// 处理攻击执行事件 - 缓存伤害用于演出阶段显示
        /// 注意：战斗演出动画现在由CardExecutedEvent触发
        /// </summary>
        private void OnAttackExecuted(AttackExecutedEvent evt)
        {
            // 如果是预解算模式，不处理
            if (evt.IsPrediction)
            {
                Debug.Log($"[UI_BattleScene] 预解算模式：跳过伤害显示，{evt.AttackerId} -> {evt.TargetId}, 伤害: {evt.ActualDamage}");
                return;
            }

            string key = GetAttackPairKey(evt.AttackerId, evt.TargetId);
            if (_pendingDamageByPair.ContainsKey(key))
            {
                _pendingDamageByPair[key] += evt.ActualDamage;
            }
            else
            {
                _pendingDamageByPair[key] = evt.ActualDamage;
            }

            Debug.Log($"[UI_BattleScene] 缓存伤害: {evt.AttackerId} -> {evt.TargetId}, 伤害: {evt.ActualDamage}");
        }

        private string GetAttackPairKey(string attackerId, string targetId)
        {
            return $"{attackerId}->{targetId}";
        }

        private bool TryConsumePendingDamage(string attackerId, string targetId, out int damage)
        {
            string key = GetAttackPairKey(attackerId, targetId);
            if (_pendingDamageByPair.TryGetValue(key, out damage))
            {
                _pendingDamageByPair.Remove(key);
                return true;
            }

            damage = 0;
            return false;
        }

        /// <summary>
        /// 处理卡片执行事件 - 播放战斗演出动画
        /// </summary>
        private void OnCardExecuted(CardExecutedEvent evt)
        {
            // 如果是预解算模式，不播放动画
            if (evt.IsPrediction)
            {
                Debug.Log($"[UI_BattleScene] 预解算模式：跳过战斗演出，{evt.CasterId} -> {evt.TargetId}");
                return;
            }

            Debug.Log($"[UI_BattleScene] 卡片执行: {evt.CasterId} -> {evt.TargetId}, 攻击卡片={evt.IsAttackCard}");

            // 启动战斗演出动画协程
            StartCoroutine(PlayBattleAnimationWithBattleAnimation(evt));
        }

        /// <summary>
        /// 使用BattleAnimation组件播放战斗演出
        /// </summary>
        private IEnumerator PlayBattleAnimationWithBattleAnimation(CardExecutedEvent evt)
        {
            // 获取BattleAnimation组件
            if (BattleAnimation == null)
            {
                Debug.LogError("[UI_BattleScene] BattleAnimation RectTransform未绑定");
                SignalAnimationCompleteToResolver();
                yield break;
            }

            var battleAnimComponent = BattleAnimation.GetComponent<BattleAnimation>();
            if (battleAnimComponent == null)
            {
                Debug.LogError("[UI_BattleScene] BattleAnimation组件未找到");
                SignalAnimationCompleteToResolver();
                yield break;
            }

            // 获取施法者和目标的UnitState
            UnitState casterState = FindUnitStateById(evt.CasterId);
            UnitState targetState = FindUnitStateById(evt.TargetId);

            if (casterState == null || targetState == null)
            {
                Debug.LogError($"[UI_BattleScene] 无法找到UnitState: {evt.CasterId} 或 {evt.TargetId}");
                SignalAnimationCompleteToResolver();
                yield break;
            }

            // 获取对应的UI组件
            MonoBehaviour casterUI = FindUnitUIComponent(evt.CasterId);
            MonoBehaviour targetUI = FindUnitUIComponent(evt.TargetId);

            if (casterUI == null || targetUI == null)
            {
                Debug.LogWarning($"[UI_BattleScene] 无法找到UI组件: {evt.CasterId} 或 {evt.TargetId}");
                SignalAnimationCompleteToResolver();
                yield break;
            }

            // 获取缓存的伤害值
            int damage = 0;
            if (TryConsumePendingDamage(evt.CasterId, evt.TargetId, out damage))
            {
                Debug.Log($"[UI_BattleScene] 获取到缓存的伤害值: {damage}");
            }

            // 播放战斗演出动画
            yield return battleAnimComponent.PlayBattleAnimation(
                casterState,
                targetState,
                casterUI,
                targetUI,
                evt.IsAttackCard,
                damage,
                () => {
                    // 受击回调：更新目标血量显示
                    if (targetUI != null)
                    {
                        var character = targetUI.GetComponent<Character>();
                        var enemy = targetUI.GetComponent<Enemy>();

                        if (character != null)
                        {
                            var unitState = character.GetUnitState();
                            if (unitState != null)
                            {
                                character.UpdateHp(unitState.CurrentHp, unitState.MaxHp);
                                character.UpdateShield(unitState.Defense);
                            }
                        }
                        else if (enemy != null)
                        {
                            var unitState = enemy.GetUnitState();
                            if (unitState != null)
                            {
                                enemy.UpdateHp(unitState.CurrentHp, unitState.MaxHp);
                                enemy.UpdateShield(unitState.Defense);
                            }
                        }
                    }
                }
            );

            // 更新所有单位的UI显示
            UpdateAllUnitsDisplay();

            // 通知TimelineResolver动画完成
            SignalAnimationCompleteToResolver();
        }

        /// <summary>
        /// 根据UnitId查找UnitState
        /// </summary>
        private UnitState FindUnitStateById(string unitId)
        {
            if (_battleManager?.CurrentState == null) return null;

            // 在玩家单位中查找
            foreach (var unit in _battleManager.CurrentState.PlayerUnits)
            {
                if (unit.UnitId == unitId) return unit;
            }

            // 在敌人单位中查找
            foreach (var unit in _battleManager.CurrentState.EnemyUnits)
            {
                if (unit.UnitId == unitId) return unit;
            }

            return null;
        }

        /// <summary>
        /// 根据UnitId查找对应的UI组件（Character或Enemy）
        /// </summary>
        private MonoBehaviour FindUnitUIComponent(string unitId)
        {
            // 在玩家角色中查找
            foreach (var character in _playerCharacters)
            {
                if (character == null) continue;
                var unitState = character.GetUnitState();
                if (unitState != null && unitState.UnitId == unitId)
                {
                    return character;
                }
            }

            // 在敌人中查找
            foreach (var enemy in _enemies)
            {
                if (enemy == null) continue;
                var unitState = enemy.GetUnitState();
                if (unitState != null && unitState.UnitId == unitId)
                {
                    return enemy;
                }
            }

            return null;
        }

        /// <summary>
        /// 通知TimelineResolver动画完成
        /// </summary>
        private void SignalAnimationCompleteToResolver()
        {
            if (_battleManager != null)
            {
                _battleManager.SignalAnimationComplete();
                Debug.Log("[UI_BattleScene] 已发送动画完成信号");
            }
        }

        /// <summary>
        /// 处理血量预测事件 - 通知所有单位开始闪烁
        /// </summary>
        private void OnHpPredictionReceived(HpPredictionEvent evt)
        {
            if (evt.PredictedHpMap == null)
            {
                Debug.LogWarning("[UI_BattleScene] 预测结果为null");
                return;
            }

            Debug.Log($"[UI_BattleScene] 收到血量预测事件，单位数: {evt.PredictedHpMap.Count}");

            // 通知所有玩家角色开始闪烁
            foreach (var character in _playerCharacters)
            {
                if (character == null) continue;

                var unitState = character.GetUnitState();
                if (unitState == null) continue;

                if (evt.PredictedHpMap.TryGetValue(unitState.UnitId, out int predictedHp))
                {
                    character.StartHpPredictionBlink(predictedHp);
                    Debug.Log($"[UI_BattleScene] 角色 {unitState.UnitId} 开始闪烁: 当前={unitState.CurrentHp}, 预测={predictedHp}");
                }
            }

            // 通知所有敌人开始闪烁
            foreach (var enemy in _enemies)
            {
                if (enemy == null) continue;

                var unitState = enemy.GetUnitState();
                if (unitState == null) continue;

                if (evt.PredictedHpMap.TryGetValue(unitState.UnitId, out int predictedHp))
                {
                    enemy.StartHpPredictionBlink(predictedHp);
                    Debug.Log($"[UI_BattleScene] 敌人 {unitState.UnitId} 开始闪烁: 当前={unitState.CurrentHp}, 预测={predictedHp}");
                }
            }
        }

        /// <summary>
        /// 处理停止血量预测事件 - 通知所有单位停止闪烁
        /// </summary>
        private void OnHpPredictionStop(HpPredictionStopEvent evt)
        {
            Debug.Log("[UI_BattleScene] 收到停止血量预测事件");

            // 停止所有玩家角色的闪烁
            foreach (var character in _playerCharacters)
            {
                if (character != null)
                {
                    character.StopHpPredictionBlink();
                }
            }

            // 停止所有敌人的闪烁
            foreach (var enemy in _enemies)
            {
                if (enemy != null)
                {
                    enemy.StopHpPredictionBlink();
                }
            }
        }

        /// <summary>
        /// 处理时间轴前进前事件 - 标记将被执行的卡片
        /// </summary>
        private void OnBeforeTimelineAdvance(BeforeTimelineAdvanceEvent evt)
        {
            Debug.Log($"[UI_BattleScene] 收到时间轴前进前事件，将执行 {evt.ExecutedCards.Count} 个卡片/技能");

            // 遍历所有将被执行的卡片，找到对应的CardViewController并标记为锁定
            foreach (var executedCard in evt.ExecutedCards)
            {
                // 查找对应的时间轴
                var timeline = FindTimelineByUnitId(executedCard.OwnerId);
                if (timeline == null)
                {
                    Debug.LogWarning($"[UI_BattleScene] 未找到单位 {executedCard.OwnerId} 的时间轴");
                    continue;
                }

                // 查找对应的CardViewController
                var placedCards = timeline.GetAllPlacedCards();
                foreach (var card in placedCards)
                {
                    if (card == null) continue;

                    var cardInfo = card.GetCurrentCard();
                    if (cardInfo != null && cardInfo.Id == executedCard.SourceCardId)
                    {
                        // 找到了，标记为锁定
                        card.LockCard();
                        Debug.Log($"[UI_BattleScene] 锁定卡片: {cardInfo.Name} (OwnerId: {executedCard.OwnerId})");
                    }
                }
            }
        }

        /// <summary>
        /// 处理时间轴前进后事件 - 更新UI显示
        /// </summary>
        private void OnAfterTimelineAdvance(AfterTimelineAdvanceEvent evt)
        {
            Debug.Log("[UI_BattleScene] 收到时间轴前进后事件，开始更新UI");
            ApplyTimelineShiftEffect();
        }

        /// <summary>
        /// 应用时间轴整体位移效果（卡片与敌人槽前移一格）
        /// </summary>
        private void ApplyTimelineShiftEffect()
        {
            // 1. 移动所有卡片和敌人槽的位置
            foreach (var timeline in _playerTimelines)
            {
                if (timeline != null)
                {
                    timeline.ShiftAllCardsForward();
                }
            }

            if (_enemyTimeline != null)
            {
                _enemyTimeline.ShiftAllCardsForward();
            }

            // 2. 统一移除已完成的卡片和敌人时间槽
            RemoveCompletedTimeSlots();

            // 3. 注意：这里不调用 UpdateAllTimelines() / RefreshDisplay()
            // 因为 RefreshDisplay() 会清除并重建所有敌人时间槽
            // 而我们已经通过 ShiftAllCardsForward 和 RemoveCompletedTimeSlots 手动处理了
            // UpdateAllTimelines(); // ❌ 不要调用！会清除所有敌人槽！
            
            Debug.Log("<color=green>[UI_BattleScene] 时间轴UI更新完成（已跳过RefreshDisplay以保留移动后的敌人槽）</color>");
        }

        /// <summary>
        /// 统一移除已完成的卡片和敌人时间槽（已移出时间轴）
        /// 将 CardTimeSlot 和 EnemyTimeSlot 的移除逻辑统一到一个函数中
        /// </summary>
        private void RemoveCompletedTimeSlots()
        {
            Debug.Log("<color=magenta>========== 【统一移除已完成的TimeSlot】开始 ==========</color>");

            // 1. 移除玩家卡片的 CardTimeSlot（通过 CardViewController）
            foreach (var timeline in _playerTimelines)
            {
                if (timeline == null) continue;

                var cardsToRemove = timeline.GetCardsToRemove();
                string unitId = timeline.GetUnitId() ?? "未知Owner";
                Debug.Log($"<color=magenta>时间轴 {unitId} 待移除卡片数: {cardsToRemove.Count}</color>");
                
                if (cardsToRemove.Count == 0)
                {
                    Debug.Log($"<color=green>✓ 时间轴 {unitId} 没有需要移除的卡片</color>");
                    continue; // 如果没有待移除的卡片，直接跳过
                }
                
                Debug.LogWarning($"<color=red>⚠️ 警告：时间轴 {unitId} 有 {cardsToRemove.Count} 张卡片待移除，开始处理...</color>");
                
                foreach (var card in cardsToRemove)
                {
                    if (card == null)
                    {
                        Debug.LogWarning($"[UI_BattleScene] 待移除列表中有null卡片，跳过");
                        continue;
                    }

                    var cardInfo = card.GetCurrentCard();
                    if (cardInfo != null)
                    {
                        var cardTimeSlot = card.CardTimeSlot;
                        int currentIndex = cardTimeSlot != null ? cardTimeSlot.GetSlotIndex() : -1;
                        
                        Debug.Log($"<color=red>========== 【准备销毁UI - CardTimeSlot】 ==========</color>");
                        Debug.Log($"<color=red>卡片: {cardInfo.Name} (CardId: {cardInfo.Id}, Owner: {unitId})</color>");
                        Debug.Log($"<color=red>当前索引: {currentIndex}</color>");
                        Debug.Log($"<color=red>GameObject名称: {card.gameObject.name}</color>");
                        Debug.Log($"<color=red>GameObject是否已销毁: {card == null || card.gameObject == null}</color>");
                        
                        // 双重检查：确认卡片真的应该被移除
                        if (currentIndex >= 0 && currentIndex != 0)
                        {
                            Debug.LogError($"<color=red>❌ 错误！卡片 {cardInfo.Name} 的索引是 {currentIndex}，不是0，不应该被移除！</color>");
                            Debug.LogError($"<color=red>   跳过此卡片的销毁，可能是误标记！</color>");
                            continue; // 跳过，不销毁
                        }
                        
                        Debug.Log($"<color=yellow>【移除UI - CardTimeSlot】卡片: {cardInfo.Name} (Owner: {unitId}), 当前索引: {currentIndex}</color>");

                        // 从时间轴的已放置列表中移除（使用专门的方法）
                        timeline.RemoveFromPlacedCards(card);

                        // 数据层弃牌：从 InPlayPile 移到 DiscardPile
                        if (_battleManager != null && _battleManager.CurrentState != null)
                        {
                            // 使用 InstanceId 从 InPlayPile 正确移除
                            string instanceId = card.InstanceId;
                            if (!string.IsNullOrEmpty(instanceId))
                            {
                                _battleManager.CurrentState.DeckSystem.FinishPlayingCardByInstanceId(instanceId, false);
                                Debug.Log($"<color=yellow>数据层弃牌: {cardInfo.Name} (InstanceId: {instanceId})</color>");
                            }
                        }

                        // UI层：移到弃牌堆（而非销毁）
                        MoveCardToDiscard(card);
                        Debug.Log($"<color=yellow>UI层移入弃牌堆: {cardInfo.Name}</color>");
                    }
                    else
                    {
                        Debug.LogWarning($"[UI_BattleScene] 待移除的卡片无法获取CardInfo，跳过");
                    }
                }

                // 清空待移除列表
                timeline.ClearCardsToRemove();
            }

            // 2. 移除敌人时间槽的 EnemyTimeSlot
            if (_enemyTimeline != null)
            {
                _enemyTimeline.RemoveCompletedEnemySlots();
            }

            Debug.Log("<color=magenta>========== 【统一移除已完成的TimeSlot】完成 ==========</color>");
        }

        /// <summary>
        /// 播放攻击动画序列（攻击和受击动画同时播放）
        /// </summary>
        private IEnumerator PlayAttackAnimationSequence(AttackExecutedEvent evt)
        {
            // 1. 找到攻击者和目标UI对象
            GameObject attackerObj = FindUnitUIObject(evt.AttackerId);
            GameObject targetObj = FindUnitUIObject(evt.TargetId);

            float maxDuration = 1.0f;

            // 2. 应用攻击演出效果：无关角色变暗，有关角色放大
            ApplyAttackPerformanceEffect(attackerObj, targetObj);

            // 3. 同时播放攻击者attack动画和目标shouji动画
            if (attackerObj != null)
            {
                var character = attackerObj.GetComponent<Character>();
                var enemy = attackerObj.GetComponent<Enemy>();

                if (character != null)
                {
                    character.PlayAttackAnimation();
                    Debug.Log("[UI_BattleScene] 播放角色攻击动画");
                }
                else if (enemy != null)
                {
                    enemy.PlayAttackAnimation();
                    Debug.Log("[UI_BattleScene] 播放敌人攻击动画");
                }
            }
            else
            {
                Debug.LogWarning($"[UI_BattleScene] 未找到攻击者UI对象: {evt.AttackerId}");
            }

            // 4. 同时播放目标shouji动画 + 伤害数字 + 实时更新血量显示
            if (targetObj != null)
            {
                var character = targetObj.GetComponent<Character>();
                var enemy = targetObj.GetComponent<Enemy>();

                if (character != null)
                {
                    character.PlayShoujiAnimation();
                    ShowDamageNumber(targetObj.transform.position, evt.ActualDamage);
                    
                    // 实时更新血量显示（从UnitState获取最新值）
                    var unitState = character.GetUnitState();
                    if (unitState != null)
                    {
                        character.UpdateHp(unitState.CurrentHp, unitState.MaxHp);
                        character.UpdateShield(unitState.Defense);
                        Debug.Log($"[UI_BattleScene] 实时更新角色血量: {unitState.CurrentHp}/{unitState.MaxHp}");
                    }
                    
                    Debug.Log("[UI_BattleScene] 播放角色受击动画");
                }
                else if (enemy != null)
                {
                    enemy.PlayShoujiAnimation();
                    ShowDamageNumber(targetObj.transform.position, evt.ActualDamage);
                    
                    // 实时更新血量显示（从UnitState获取最新值）
                    var unitState = enemy.GetUnitState();
                    if (unitState != null)
                    {
                        enemy.UpdateHp(unitState.CurrentHp, unitState.MaxHp);
                        enemy.UpdateShield(unitState.Defense);
                        Debug.Log($"[UI_BattleScene] 实时更新敌人血量: {unitState.CurrentHp}/{unitState.MaxHp}");
                    }
                    
                    Debug.Log("[UI_BattleScene] 播放敌人受击动画");
                }
            }
            else
            {
                Debug.LogWarning($"[UI_BattleScene] 未找到目标UI对象: {evt.TargetId}");
            }

            // 5. 等待所有动画完成（取较大值：0.5秒动画 + 1.0秒伤害数字）
            yield return new WaitForSeconds(Mathf.Max(maxDuration, 1.0f));

            // 6. 恢复所有角色的视觉效果
            RestoreAllUnitsVisualEffect();

            // 7. 通知TimelineResolver动画完成
            if (_battleManager != null)
            {
                _battleManager.SignalAnimationComplete();
                Debug.Log("[UI_BattleScene] 攻击动画序列完成，已发送完成信号");
            }
        }

        /// <summary>
        /// 应用攻击演出效果（无关角色变黑，有关角色放大）
        /// </summary>
        /// <param name="attackerObj">攻击者对象</param>
        /// <param name="targetObj">目标对象</param>
        private void ApplyAttackPerformanceEffect(GameObject attackerObj, GameObject targetObj)
        {
            // 遍历所有玩家角色
            foreach (var character in _playerCharacters)
            {
                if (character == null || character.gameObject == null) continue;

                // 判断是否为有关角色（攻击者或受击者）
                bool isRelated = (attackerObj != null && character.gameObject == attackerObj) ||
                                 (targetObj != null && character.gameObject == targetObj);

                if (isRelated)
                {
                    // 有关角色：放大，保持白色
                    character.SetScale(1.2f);
                    character.SetColor(Color.white);
                }
                else
                {
                    // 无关角色：变黑
                    character.SetColor(Color.black);
                    character.SetScale(1.0f);
                }
            }

            // 遍历所有敌人
            foreach (var enemy in _enemies)
            {
                if (enemy == null || enemy.gameObject == null) continue;

                // 判断是否为有关角色（攻击者或受击者）
                bool isRelated = (attackerObj != null && enemy.gameObject == attackerObj) ||
                                 (targetObj != null && enemy.gameObject == targetObj);

                if (isRelated)
                {
                    // 有关角色：放大，保持白色
                    enemy.SetScale(1.2f);
                    enemy.SetColor(Color.white);
                }
                else
                {
                    // 无关角色：变黑
                    enemy.SetColor(Color.black);
                    enemy.SetScale(1.0f);
                }
            }

            Debug.Log("[UI_BattleScene] 应用攻击演出效果：无关角色变黑，有关角色scale=1.2");
        }

        /// <summary>
        /// 恢复所有单位的视觉效果（颜色和缩放）
        /// </summary>
        private void RestoreAllUnitsVisualEffect()
        {
            // 恢复所有玩家角色
            foreach (var character in _playerCharacters)
            {
                if (character != null)
                {
                    character.SetColor(Color.white);
                    character.SetScale(1.0f);
                }
            }

            // 恢复所有敌人
            foreach (var enemy in _enemies)
            {
                if (enemy != null)
                {
                    enemy.SetColor(Color.white);
                    enemy.SetScale(1.0f);
                }
            }

            Debug.Log("[UI_BattleScene] 恢复所有单位视觉效果：颜色=白色，scale=1.0");
        }

        /// <summary>
        /// 显示伤害数字（使用DOTween动画）
        /// </summary>
        private void ShowDamageNumber(Vector3 targetPosition, int damage)
        {
            if (damage <= 0)
            {
                Debug.Log("[UI_BattleScene] 伤害为0，不显示伤害数字");
                return;
            }

            GameObject damageTextObj = null;
            TMPro.TextMeshProUGUI textMesh = null;
            RectTransform rectTransform = null;

            // 优先使用prefab，如果prefab为空则使用动态创建（向后兼容）
            if (damageTextPrefab != null)
            {
                // 使用prefab实例化
                damageTextObj = Instantiate(damageTextPrefab, transform);
                damageTextObj.transform.position = targetPosition;
                damageTextObj.name = "DamageText";

                // 获取TextMeshPro组件
                textMesh = damageTextObj.GetComponent<TMPro.TextMeshProUGUI>();
                if (textMesh == null)
                {
                    Debug.LogWarning("[UI_BattleScene] 伤害数字prefab缺少TextMeshProUGUI组件，尝试添加");
                    textMesh = damageTextObj.AddComponent<TMPro.TextMeshProUGUI>();
                }

                // 获取RectTransform
                rectTransform = damageTextObj.GetComponent<RectTransform>();
                if (rectTransform == null)
                {
                    rectTransform = damageTextObj.AddComponent<RectTransform>();
                }
            }
            else
            {
                // 回退到动态创建（向后兼容）
                damageTextObj = new GameObject("DamageText");
                damageTextObj.transform.SetParent(transform);
                damageTextObj.transform.position = targetPosition;

                // 添加TextMeshPro组件
                textMesh = damageTextObj.AddComponent<TMPro.TextMeshProUGUI>();
                textMesh.fontSize = 48;
                textMesh.color = Color.red;
                textMesh.alignment = TMPro.TextAlignmentOptions.Center;

                // 设置RectTransform
                rectTransform = damageTextObj.GetComponent<RectTransform>();
                rectTransform.sizeDelta = new Vector2(200, 100);
            }

            // 设置伤害数值
            if (textMesh != null)
            {
                textMesh.text = damage.ToString();
            }

            // DOTween动画：向上飘动 + 淡出
            Sequence damageSequence = DOTween.Sequence();
            damageSequence.Append(
                rectTransform.DOAnchorPosY(rectTransform.anchoredPosition.y + 100f, 1.0f)
                    .SetEase(Ease.OutQuad)
            );
            if (textMesh != null)
            {
                damageSequence.Join(
                    textMesh.DOFade(0f, 1.0f).SetEase(Ease.InQuad)
                );
            }
            damageSequence.OnComplete(() => Destroy(damageTextObj));

            Debug.Log($"[UI_BattleScene] 显示伤害数字: {damage} (使用{(damageTextPrefab != null ? "Prefab" : "动态创建")})");
        }

        #endregion


    
    }
}

