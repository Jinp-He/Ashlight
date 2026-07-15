using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using cfg;
using cfg.Character;
using cfg.Enemy;
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
        [Tooltip("测试用遭遇战ID（若 BattleManager.PendingEncounterId 有值则优先使用那个；testEncounterSequence 非空时也会被它覆盖）")]
        private string testEncounterId = "E001";

        [SerializeField]
        [Tooltip("测试关卡推进序列。胜利后按当前关 Id 在数组中的下标 +1 取下一关。留空则不启用序列推进，回退到 testEncounterId")]
        private string[] testEncounterSequence = new[] { "E001", "E002", "E003", "E004" };

        [SerializeField]
        [Tooltip("胜利后自动跳下一关（不弹胜利弹窗）。关闭则走 VictoryPanel 流程")]
        private bool testAutoAdvance = true;

        [SerializeField]
        [Tooltip("自动跳关延迟（秒），让玩家短暂看到结算/最后一击")]
        [Range(0f, 5f)]
        private float testAutoAdvanceDelay = 1.5f;

        [SerializeField]
        [Tooltip("最后一关后是否循环回第一关。关闭则停在最后一关结束态")]
        private bool testLoopAfterLast = true;

        [SerializeField]
        [Tooltip("失败后是否自动重开当前关（仅测试模式）")]
        private bool testAutoRetryOnDefeat = true;

        [Header("战斗结算UI")]
        [SerializeField]
        [Tooltip("战斗胜利弹窗（由用户在场景内手搭，挂 VictoryPanel 脚本后拖进来）")]
        private VictoryPanel victoryPanel;

        [SerializeField]
        [Tooltip("胜利面板 WinPanel（场景内 WinPanel 实例拖进来）。若已绑定则优先于 victoryPanel")]
        private WinPanel winPanel;

        #endregion

        #region 私有字段

        private int _currentMoney = 0;
        private BattleManager _battleManager;

        /// <summary>本场战斗的 EncounterId（决定胜利后跳到哪一关、失败后重置回哪一关）</summary>
        private string _currentEncounterId;

        /// <summary>胜负后自动跳关的协程引用，避免重复触发</summary>
        private Coroutine _autoAdvanceCoroutine;

        /// <summary>
        /// 卡牌对象池管理器
        /// </summary>
        private BattleCardPoolManager _cardPoolManager = new BattleCardPoolManager();
        
        /// <summary>
        /// 手牌列表的快捷访问（委托给 _cardPoolManager.HandCards）
        /// </summary>
        private IReadOnlyList<CardViewController> _handCards => _cardPoolManager.HandCards;

        /// <summary>
        /// 单位UI管理器
        /// </summary>
        private BattleUnitUIManager _unitUIManager = new BattleUnitUIManager();
        
        /// <summary>
        /// 玩家角色UI列表的快捷访问（委托给 _unitUIManager.PlayerCharacters）
        /// </summary>
        private IReadOnlyList<Character> _playerCharacters => _unitUIManager.PlayerCharacters;
        
        /// <summary>
        /// 敌人UI列表的快捷访问（委托给 _unitUIManager.Enemies）
        /// </summary>
        private IReadOnlyList<Enemy> _enemies => _unitUIManager.Enemies;
        
        // 时间轴UI列表
        private List<Timeline.TimelineTrackView> _playerTimelines = new List<Timeline.TimelineTrackView>();
        private Timeline.TimelineTrackView _enemyTimeline;

        /// <summary>
        /// 动画处理器
        /// </summary>
        private BattleAnimationHandler _animationHandler;
        private bool _isProcessingAtbTurn;

        /// <summary>
        /// 当前回合内是否已打出过执行牌（用于手牌压制：一回合限一张执行牌）。
        /// </summary>
        private bool _playerPlayedExecutionCardThisAtbTurn;

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

            // 获取或创建动画处理器
            _animationHandler = GetComponent<BattleAnimationHandler>();
            if (_animationHandler == null)
            {
                _animationHandler = gameObject.AddComponent<BattleAnimationHandler>();
            }

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

            // 订阅战斗结束事件
            GameEvent.Subscribe<BattleEndedEvent>(OnBattleEnded);

            // 订阅换位事件（MovePosition 效果驱动角色交换 sibling 顺序）
            GameEvent.Subscribe<PositionSwappedEvent>(OnPositionSwapped);

            if (ATB != null)
            {
                ATB.OnUnitTurn += HandleAtbUnitTurn;
                ATB.OnObjectiveRoundAdvanced += HandleObjectiveRoundAdvanced;
                // 演出闸门：上一个单位的战斗演出没播完，ATB 不开下一个单位的回合
                ATB.AnimationBusyPredicate = () => _animationHandler != null && _animationHandler.IsAnimating;
                // ATB 节点在场景里是隐藏的（旧图标条已弃用），协程借本组件跑——
                // 否则 TriggerNextUnit 退化为同步一帧跑完，节奏/闸门全部失效。
                ATB.CoroutineHost = this;
            }

            if (TurnOrderView != null)
            {
                // hover 行动顺序卡 → 战场上对应敌人亮选中圈
                TurnOrderView.OnUnitHover = HandleTurnOrderUnitHover;
            }
        }

        /// <summary>
        /// hover 行动顺序卡的联动：点亮/熄灭战场上对应敌人的 Indicator。
        /// 玩家单位暂不标记（需求只覆盖敌人）；已死亡/找不到的单位静默忽略。
        /// </summary>
        private void HandleTurnOrderUnitHover(string unitId, bool hovering)
        {
            if (string.IsNullOrEmpty(unitId)) return;

            var enemy = FindEnemyByUnitId(unitId);
            if (enemy == null) return;

            if (hovering) enemy.ShowIndicator();
            else enemy.HideIndicator();
        }

        /// <summary>
        /// 客观回合推进（整排 slot 前进一格）：驱动游戏侧的逐客观回合效果——目前是全体闪避各掉 1 层。
        /// </summary>
        private void HandleObjectiveRoundAdvanced()
        {
            _battleManager?.DecayAllDodgeOneObjectiveRound();
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
            if (ATB != null)
            {
                // 【回合制说明】规划轨和执行轨都改为离散 Slots 队列，由 TriggerNextUnit() 统一驱动。
                // ATB 始终 IsPaused=true，因此此 Tick 实际是空操作（保留仅为兼容旧实时模式）。
                ATB.Tick(Time.deltaTime);
            }

            // 行动顺序视图每帧同步排序
            TurnOrderView?.RefreshOrder();

            string currentTurnUnitId = _battleManager?.CurrentState?.CurrentTurnUnitId;
            if (!string.IsNullOrEmpty(currentTurnUnitId))
            {
                UpdateEnergyBarByUnitId(currentTurnUnitId);
            }

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
            GameEvent.Unsubscribe<BattleEndedEvent>(OnBattleEnded);
            GameEvent.Unsubscribe<PositionSwappedEvent>(OnPositionSwapped);

            if (ATB != null)
            {
                ATB.OnUnitTurn -= HandleAtbUnitTurn;
                ATB.OnObjectiveRoundAdvanced -= HandleObjectiveRoundAdvanced;
                ATB.OnIconRemoved -= OnAtbIconRemoved;
            }

            // 移除按钮监听
            RemoveButtonListeners();

            // 清理卡牌池（包括所有手牌、抽牌堆、弃牌堆的卡牌）
            _cardPoolManager.Clear();

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

            // 获取或创建BattleManager
            _battleManager = BattleManager.Instance;
            if (_battleManager == null)
            {
                GameObject battleManagerObj = new GameObject("BattleManager");
                _battleManager = battleManagerObj.AddComponent<BattleManager>();
                Debug.Log("[UI_BattleScene] BattleManager已创建");
            }

            // 设置单位UI管理器的BattleManager引用
            _unitUIManager.SetBattleManager(_battleManager);

            // 初始化动画处理器
            _animationHandler.Initialize(
                _battleManager,
                _unitUIManager,
                BattleAnimation,
                UpdateAllUnitsDisplay);
            _animationHandler.SetDamageTextPrefab(damageTextPrefab);

            // 创建战斗信息并初始化战斗
            InitializeBattle();

            // 创建玩家和敌人的UI
            CreateBattleUnits();

            // 悬停敌人意图时，抛物线需要把「锁定目标 UnitId」解析为角色 UI 位置：注入解析器。
            IntentionView.TargetTransformResolver = id =>
            {
                var ch = FindCharacterByUnitId(id);
                return ch != null ? ch.transform : null;
            };

            // 新战斗开始：复位升级流程相关 UI（隐藏经验条、恢复手牌区、收起升级面板）
            ResetUpgradeUIForNewBattle();

            // 初始化ATB图标（按单位速度决定初始位置）
            if (ATB != null && _battleManager != null && _battleManager.CurrentState != null)
            {
                ATB.InitializeByUnits(_battleManager.CurrentState.PlayerUnits, _battleManager.CurrentState.EnemyUnits);

                // 【死亡保护】注入死亡判定，使 ATB 推进队列时能跳过/移除死亡单位的图标，
                // 避免单位死亡后图标残留导致 TriggerNextUnit 反复触发死亡单位回合卡死。
                ATB.IsUnitDeadPredicate = id =>
                {
                    var u = _battleManager?.CurrentState?.GetUnitById(id);
                    return u == null || u.IsDead;
                };

                // ATB 图标被移除时，同步移除行动顺序视图（卡牌区域）中对应的卡片，
                // 保证死亡单位的卡片不会残留在卡牌区域。
                ATB.OnIconRemoved -= OnAtbIconRemoved;
                ATB.OnIconRemoved += OnAtbIconRemoved;

                // 初始化行动顺序视图（需在 TriggerNextUnit 之前，确保卡片已创建好）
                if (TurnOrderView != null)
                {
                    TurnOrderView.Initialize(
                        _battleManager.CurrentState.PlayerUnits,
                        _battleManager.CurrentState.EnemyUnits);
                }

                // 【公共回合镜像】开局把调度同步进快照（CurrentRound / 各单位 NextActionRound），
                // 供 Core 命令查询「当前回合的敌人」。之后每个原子回合开始时都会再同步。
                ATB.SyncScheduleToState(_battleManager.CurrentState);

                // 【天气】把本场天气挂进公共回合时钟（虚拟单位·第三方阵营，首次结算=第 Period 回合），
                // 并做开场预告（TurnOrderView 雷暴格 / 常驻角标 / 开场横幅）。
                var weather = _battleManager.CurrentWeather;
                if (weather != null)
                {
                    ATB.AddWeatherIcon(weather.IconPath, weather.Period);
                    TurnOrderView?.SetWeather(weather);
                    ShowWeatherAnnouncement(weather);
                }

                // 【回合制】暂停 ATB 后立即触发第一个单位的回合，无需等待实时推进
                ATB.Pause();

                // 【公共回合制】敌人开局种子回合 = Speed（由 InitializeByUnits 设定），我方 = 第 1 回合。
                // 这里只为每个敌人预告首次意图（telegraph，供行动顺序视图显示），不做任何排队；
                // 轮到敌人回合（第 Speed 个公共回合）时才结算。
                var bootstrapEnemies = _battleManager.CurrentState.EnemyUnits;
                if (bootstrapEnemies != null)
                {
                    foreach (var enemy in bootstrapEnemies)
                    {
                        if (enemy == null || enemy.IsDead) continue;
                        DeclareEnemyIntent(enemy.UnitId);
                    }
                }

                ATB.TriggerNextUnit();
            }

            // 创建时间轴UI
            CreateTimelines();

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

            // 解析本场 EncounterId（含 Pending / 测试序列 / Inspector 回退）
            string encounterId = ResolveStartingEncounterId();
            _currentEncounterId = encounterId;

            // 创建战斗信息
            var battleInfo = BattleInfo.Create(activeTeam, encounterId, initialDrawCount: 0);

            // 初始化战斗
            _battleManager.InitializeBattle(battleInfo);

            // 初始化卡牌对象池管理器并创建卡牌池
            _cardPoolManager.Initialize(
                _battleManager,
                cardViewControllerPrefab,
                CardDeck,
                CardBin,
                CardContainer.transform);
            _cardPoolManager.InitializePool();

            // 显示初始手牌到UI
            DisplayHandCards();
        }

        /// <summary>
        /// 使用测试数据初始化战斗
        /// </summary>
        private void InitializeBattleWithTestData()
        {
            Debug.Log("[UI_BattleScene] 使用测试数据初始化战斗");

            // 测试期：仅在「本次 PlayMode 首次进入战斗」时把角色重建为 Lv1 + BaseDeck，
            // 同一次运行内的后续战斗沿用（经验/卡组累积），下次 PlayMode 再重置。
            // 判定：GameManager 是 DontDestroyOnLoad，跨场景重载存活，本次为空=本次运行首次。
            bool firstBattleThisSession = GameManager.Instance == null;
            GameManager.EnsureInstance();
            if (firstBattleThisSession)
                Ashlight.Systems.Character.CharacterSystem.InitializeCharacters(unlockFirst: true);

            // 创建测试用的角色列表（包含Rocket、Irene、Zhouzhou）
            var testCharacters = new List<CharacterEnum>
            {
                CharacterEnum.Rocket,
                CharacterEnum.Irene,
                CharacterEnum.Zhouzhou
            };
            // 解析本场 EncounterId（含 Pending / 测试序列 / Inspector 回退）
            string encounterId = ResolveStartingEncounterId();
            _currentEncounterId = encounterId;

            // 创建战斗信息
            var battleInfo = BattleInfo.Create(testCharacters, encounterId, initialDrawCount: 5);

            // 初始化战斗
            _battleManager.InitializeBattle(battleInfo);

            // 初始化卡牌对象池管理器并创建卡牌池
            _cardPoolManager.Initialize(
                _battleManager,
                cardViewControllerPrefab,
                CardDeck,
                CardBin,
                CardContainer.transform);
            _cardPoolManager.InitializePool();

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
            _cardPoolManager.ClearHandCards();

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

                // 运行时产出的卡（如 AddToHand 生成的飞刀）不在初始池中，GetOrCreateCard 会按需补建 UI
                var cardView = _cardPoolManager.GetOrCreateCard(cardState);
                if (cardView != null)
                {
                    _cardPoolManager.MoveToHand(cardView);
                }
                else
                {
                    Debug.LogWarning($"[UI_BattleScene] 无法获取/创建卡牌 UI: {cardState.InstanceId} (CardId: {cardState.CardId})");
                }
            }

            // 更新手牌布局
            UpdateHandLayout();

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
            if (characterPrefab == null)
            {
                Debug.LogError("[UI_BattleScene] Character预制体未设置");
                return;
            }

            // 两区容器：确保各自有 HorizontalLayoutGroup + ContentSizeFitter（缺失才补，不覆盖场景设置）
            EnsureRowLayout(PlayerFrontRow);
            EnsureRowLayout(PlayerBackRow);

            var playerUnits = _battleManager.CurrentState.PlayerUnits;
            Debug.Log($"[UI_BattleScene] 创建 {playerUnits.Count} 个玩家角色UI");

            for (int i = 0; i < playerUnits.Count; i++)
            {
                var unitState = playerUnits[i];

                // 按单位显式前后排(RowPosition)放入对应容器；由容器的 HLG 负责排布，不再手动摆位
                RectTransform parent = ResolveRowParent(unitState);
                if (parent == null)
                {
                    Debug.LogError("[UI_BattleScene] 前后排容器与 PlayerPosition 均未绑定");
                    return;
                }

                GameObject characterObj = Instantiate(characterPrefab, parent);
                Character character = characterObj.GetComponent<Character>();

                if (character == null)
                {
                    Debug.LogError("[UI_BattleScene] Character组件未找到");
                    Destroy(characterObj);
                    continue;
                }

                character.Initialize(unitState);
                _unitUIManager.RegisterCharacter(character);
                Debug.Log($"[UI_BattleScene] 创建玩家角色: {unitState.UnitId} ({unitState.ConfigId}) -> {parent.name}");
            }

            // Instantiate 到 HLG 后不会自动重排，强制立即重建两区（及父级 PlayerPosition）布局
            RebuildRowLayout();
        }

        /// <summary>强制立即重建前后排容器（及父级 PlayerPosition）的布局——HLG 加入子物体后需手动触发。</summary>
        private void RebuildRowLayout()
        {
            if (PlayerFrontRow != null) LayoutRebuilder.ForceRebuildLayoutImmediate(PlayerFrontRow);
            if (PlayerBackRow != null) LayoutRebuilder.ForceRebuildLayoutImmediate(PlayerBackRow);
            if (PlayerPosition != null) LayoutRebuilder.ForceRebuildLayoutImmediate(PlayerPosition);
        }

        /// <summary>按单位当前前后排返回对应容器：前排→PlayerFrontRow，后排→PlayerBackRow；容器缺失时回退 PlayerPosition。</summary>
        private RectTransform ResolveRowParent(UnitState unit)
        {
            bool front = unit != null && _battleManager.CurrentState.IsFrontRow(unit);
            RectTransform target = front ? PlayerFrontRow : PlayerBackRow;
            return target != null ? target : PlayerPosition;
        }

        /// <summary>确保前后排容器有水平布局 + ContentSizeFitter（仅在缺失时添加，尊重场景已有设置）。</summary>
        private void EnsureRowLayout(RectTransform row)
        {
            if (row == null) return;

            if (row.GetComponent<HorizontalLayoutGroup>() == null)
            {
                var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = false;
                hlg.childControlWidth = false;
                hlg.childControlHeight = false;
            }

            if (row.GetComponent<ContentSizeFitter>() == null)
            {
                var csf = row.gameObject.AddComponent<ContentSizeFitter>();
                csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
        }

        /// <summary>
        /// 换位事件处理：交换两名角色在 HorizontalLayoutGroup 里的 sibling 顺序，由 layout 完成实际重排。
        /// 逻辑层已交换好二者的 RowPosition（唯一真相源），这里只做视觉呈现。
        /// 注意：HorizontalLayoutGroup 默认瞬间重排；如需缓动要另做（临时脱离 layout 再 tween）。
        /// </summary>
        private void OnPositionSwapped(PositionSwappedEvent e)
        {
            if (e.IsPrediction) return; // 预解算不移动

            // 独立移动：UnitIdA 必移动；UnitIdB 可空（旧的双人换位才有两个）
            var a = _unitUIManager?.FindCharacter(e.UnitIdA);
            if (a != null) ReparentToRow(a);

            var b = string.IsNullOrEmpty(e.UnitIdB) ? null : _unitUIManager?.FindCharacter(e.UnitIdB);
            if (b != null) ReparentToRow(b);

            if (a == null && b == null) return;
            RebuildRowLayout(); // 移动/换位后强制重排两区

            // 站位变化会改变条件牌（SelfInFrontRow/SelfInBackRow 等）的判定 → 刷新手牌条件描边
            RefreshHandEnergyAffordability();

            Debug.Log($"[UI_BattleScene] 移动重排 {e.UnitIdA}{(string.IsNullOrEmpty(e.UnitIdB) ? "" : " <-> " + e.UnitIdB)}");
        }

        /// <summary>把角色 UI 重新挂到其当前前后排对应的容器下（HLG 负责实际排布）。</summary>
        private void ReparentToRow(Character character)
        {
            var unit = character?.GetUnitState();
            if (unit == null) return;

            RectTransform parent = ResolveRowParent(unit);
            if (parent != null && character.transform.parent != parent)
            {
                character.transform.SetParent(parent, false);
            }
        }

        /// <summary>
        /// 创建敌人UI
        /// </summary>
        private void CreateEnemies()
        {
            // 与玩家侧同构：有前后排容器则按 RowPosition 分区挂载；
            // 容器未绑定（如 TestScene）时回退到旧的 EnemyPostion 单容器手动排布。
            bool hasRowContainers = EnemyFrontRow != null && EnemyBackRow != null;
            if (!hasRowContainers && EnemyPostion == null)
            {
                Debug.LogError("[UI_BattleScene] EnemyFrontRow/EnemyBackRow 与 EnemyPostion 均未绑定");
                return;
            }

            if (enemyPrefab == null)
            {
                Debug.LogError("[UI_BattleScene] Enemy预制体未设置");
                return;
            }

            if (hasRowContainers)
            {
                EnsureRowLayout(EnemyFrontRow);
                EnsureRowLayout(EnemyBackRow);
            }

            var enemyUnits = _battleManager.CurrentState.EnemyUnits;
            Debug.Log($"[UI_BattleScene] 创建 {enemyUnits.Count} 个敌人UI（分区容器={hasRowContainers}）");

            for (int i = 0; i < enemyUnits.Count; i++)
            {
                var unitState = enemyUnits[i];

                RectTransform parent = hasRowContainers ? ResolveEnemyRowParent(unitState) : EnemyPostion;
                GameObject enemyObj = Instantiate(enemyPrefab, parent);
                Enemy enemy = enemyObj.GetComponent<Enemy>();

                if (enemy == null)
                {
                    Debug.LogError("[UI_BattleScene] Enemy组件未找到");
                    Destroy(enemyObj);
                    continue;
                }

                // 使用UnitState初始化Enemy
                enemy.Initialize(unitState);

                // 回退路径：单容器时手动水平排列多个敌人
                if (!hasRowContainers)
                {
                    RectTransform rectTransform = enemyObj.GetComponent<RectTransform>();
                    if (rectTransform != null && enemyUnits.Count > 1)
                    {
                        float spacing = 200f; // 敌人之间的间距
                        float totalWidth = (enemyUnits.Count - 1) * spacing;
                        float startX = -totalWidth / 2f;
                        rectTransform.anchoredPosition = new Vector2(startX + i * spacing, 0f);
                    }
                }

                _unitUIManager.RegisterEnemy(enemy);
                Debug.Log($"[UI_BattleScene] 创建敌人: {unitState.UnitId} ({unitState.ConfigId}) -> {parent.name}");
            }

            if (hasRowContainers)
            {
                RebuildEnemyRowLayout();
            }
        }

        /// <summary>按敌人当前前后排返回对应容器：前排→EnemyFrontRow，后排→EnemyBackRow；容器缺失时回退 EnemyPostion。</summary>
        private RectTransform ResolveEnemyRowParent(UnitState unit)
        {
            bool front = unit != null && _battleManager.CurrentState.IsFrontRow(unit);
            RectTransform target = front ? EnemyFrontRow : EnemyBackRow;
            return target != null ? target : EnemyPostion;
        }

        /// <summary>强制立即重建敌方前后排容器（及父级 EnemyPostion）的布局——HLG 加入子物体后需手动触发。</summary>
        private void RebuildEnemyRowLayout()
        {
            if (EnemyFrontRow != null) LayoutRebuilder.ForceRebuildLayoutImmediate(EnemyFrontRow);
            if (EnemyBackRow != null) LayoutRebuilder.ForceRebuildLayoutImmediate(EnemyBackRow);
            if (EnemyPostion != null) LayoutRebuilder.ForceRebuildLayoutImmediate(EnemyPostion);
        }

        /// <summary>
        /// 清空战斗单位UI
        /// </summary>
        private void ClearBattleUnits()
        {
            _unitUIManager.ClearAll();
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

            // 单位死亡后，立即把它从卡牌区域（行动顺序视图）与 ATB 队列移除，避免残留。
            RemoveDeadUnitsFromTurnViews();

            // 【动态索敌·预告期】玩家移动/站位变化后，重解敌人意图锁定目标并刷新意图指示/抛物线。
            RefreshEnemyIntentTargets();
        }

        /// <summary>
        /// 重解所有待执行敌人意图的锁定目标（原目标离开目标区 → 区内改选 / 空区清空），
        /// 并把发生变化的敌人意图指示（坐标高亮 + 悬停抛物线目标 / 思考态）刷新为新目标。
        /// </summary>
        private void RefreshEnemyIntentTargets()
        {
            if (_battleManager?.CurrentState == null) return;

            var changed = _battleManager.RefreshPendingEnemyIntentTargets();
            if (changed == null || changed.Count == 0) return;

            foreach (var kv in changed)
            {
                string enemyId = kv.Key;
                if (!_battleManager.TryGetPendingEnemyIntent(enemyId, out var skill, out var targetUnitId)) continue;
                ApplyEnemyIntentDisplay(enemyId, skill, targetUnitId);
            }
        }

        /// <summary>
        /// 统一设置某敌人的意图显示（意图指示物 + 行动顺序卡攻击图标）。
        /// 无合法目标（目标区空排、未锁到人）→ 显示思考态 intention_think（并撤下攻击图标），命中时会落空 miss；
        /// 否则 → 显示攻击意图（图标/数值/坐标高亮 + 悬停抛物线目标）。
        /// </summary>
        private void ApplyEnemyIntentDisplay(string unitId, EnemySkillInfo skill, string targetUnitId)
        {
            var enemyUi = FindEnemyByUnitId(unitId);

            if (string.IsNullOrEmpty(targetUnitId))
            {
                // 找不到合法目标 → 思考态；行动顺序卡撤下攻击图标（仍在执行轨、到点会 miss）
                enemyUi?.SetIntentionThinking();
                TurnOrderView?.SetExecuting(unitId, false);
                return;
            }

            int[] dotStates = BuildIntentDotStates(skill, targetUnitId);
            enemyUi?.SetIntentionExecuting(skill, dotStates, targetUnitId);
            TurnOrderView?.SetExecuting(unitId, true, skill);
        }

        /// <summary>
        /// 为敌人意图 Coord 计算「每个玩家点」的状态：0=未被攻击(灰)、1=被攻击且前排(红)、2=被攻击且后排(蓝)。
        /// 点顺序 = PlayerUnits 名单序（3 个玩家 → 3 个点）。
        /// · 单体 → 只有锁定目标 targetUnitId 那个点被标记；
        /// · AOE → 技能目标区内的存活玩家都被标记（各按自身前/后排上色）；
        /// · 技能不指向玩家(Self/AllAlly) → 返回 null（隐藏 Coord）。
        /// </summary>
        private int[] BuildIntentDotStates(EnemySkillInfo skill, string targetUnitId)
        {
            var state = _battleManager?.CurrentState;
            var players = state?.PlayerUnits;
            if (state == null || players == null || skill == null) return null;

            bool targetsPlayers = skill.TargetType == cfg.TargetTypeEnum.SingleEnemy
                || skill.TargetType == cfg.TargetTypeEnum.AllEnemy;
            if (!targetsPlayers) return null;

            bool isAoe = SkillIsAoe(skill);
            var zone = skill.TargetZone;

            int n = players.Count;
            var states = new int[n];
            for (int i = 0; i < n; i++)
            {
                var p = players[i];
                if (p == null) { states[i] = 0; continue; }

                bool front = state.IsFrontRow(p);
                bool hit;
                if (isAoe)
                {
                    if (p.IsDead) hit = false;
                    else if (zone == cfg.TargetZoneEnum.Front) hit = front;
                    else if (zone == cfg.TargetZoneEnum.Back) hit = !front;
                    else hit = true; // Any / Conditional → 全体
                }
                else
                {
                    hit = (p.UnitId == targetUnitId);
                }

                states[i] = hit ? (front ? 1 : 2) : 0;
            }
            return states;
        }

        /// <summary>
        /// 扫描当前战场，移除所有死亡单位在 ATB 队列与行动顺序视图（卡牌区域）中的残留。
        /// 由 UpdateAllUnitsDisplay 在每次状态变化后调用，保证死亡单位的卡片即时消失。
        /// </summary>
        private void RemoveDeadUnitsFromTurnViews()
        {
            var state = _battleManager?.CurrentState;
            if (state == null) return;

            RemoveDeadUnitsFrom(state.PlayerUnits);
            RemoveDeadUnitsFrom(state.EnemyUnits);
        }

        private void RemoveDeadUnitsFrom(IReadOnlyList<UnitState> units)
        {
            if (units == null) return;

            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == null || !u.IsDead) continue;

                // RemoveUnitIcon 会触发 OnIconRemoved → OnAtbIconRemoved，同步移除卡片；
                // 两个方法都对不存在的 unitId 静默无操作，可安全重复调用。
                ATB?.RemoveUnitIcon(u.UnitId);
                TurnOrderView?.RemoveUnit(u.UnitId);

                // 施法者倒下 → 其在轨引线全部作废（规则书：结算前被击倒则取消），图标一并移除。
                if (u.IsPlayerUnit && _battleManager != null)
                {
                    foreach (var castId in _battleManager.CancelPendingCasts(u.UnitId))
                    {
                        ATB?.RemoveUnitIcon(castId);
                        TurnOrderView?.RemoveCast(castId);
                    }
                }

                // 移除该单位残留在敌人共享时间轴上的时间槽卡牌（仅敌人会有）。
                _enemyTimeline?.RemoveEnemyTimeSlotsByOwner(u.UnitId);
            }
        }

        /// <summary>
        /// ATB 图标被移除时的回调：同步移除行动顺序视图中的对应卡片。
        /// </summary>
        private void OnAtbIconRemoved(string unitId)
        {
            TurnOrderView?.RemoveUnit(unitId);
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
        /// 添加卡牌到手牌（注意：此方法创建新的卡牌UI，不使用对象池）
        /// 推荐使用 DisplayHandCards() 从对象池获取卡牌
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
            
            // 注意：这里创建的卡牌不在对象池中，需要手动管理
            // 将卡牌移到手牌（通过管理器管理状态）
            _cardPoolManager.MoveToHand(cardView);

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

            // 从管理器的手牌列表中移除
            _cardPoolManager.RemoveFromHandList(cardView);
            
            // 销毁卡牌对象
            Destroy(cardView.gameObject);
            UpdateHandLayout();
            Debug.Log("[UI_BattleScene] 从手牌移除卡牌");
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

        #region 卡牌池委托方法（委托给 BattleCardPoolManager）

        /// <summary>
        /// 将卡牌移到弃牌堆（委托给管理器）
        /// </summary>
        private void MoveCardToDiscard(CardViewController card)
        {
            _cardPoolManager.MoveToDiscard(card);
        }

        /// <summary>
        /// 从手牌列表中移除卡牌引用（不销毁，不隐藏）
        /// 用于卡牌放到时间轴时
        /// </summary>
        public void RemoveCardFromHandList(CardViewController card)
        {
            if (card == null) return;
            _cardPoolManager.RemoveFromHandList(card);
            UpdateHandLayout();
            Debug.Log($"[UI_BattleScene] 从手牌列表移除卡牌: {card.GetCurrentCard()?.Name}");
        }

        /// <summary>
        /// 立即出牌后消费手牌UI（移入弃牌堆）
        /// </summary>
        public void ConsumeHandCard(CardViewController card)
        {
            if (card == null)
            {
                return;
            }

            _cardPoolManager.MoveToDiscard(card);
            UpdateHandLayout();
            // 出牌后能量减少，剩余手牌可能转为能量不足 —— 立即刷新变色
            RefreshHandEnergyAffordability();
            Debug.Log($"[UI_BattleScene] 立即出牌后移入弃牌堆: {card.GetCurrentCard()?.Name}");
        }

        /// <summary>
        /// 从数据层补齐手牌 UI：为 DeckSystem.Hand 中尚未显示的卡牌（如打牌时 AddToHand 产出的 token）创建/取出 UI 并移入手牌。
        /// 增量式，不销毁现有手牌 view；出牌后产出卡牌时调用。
        /// </summary>
        public void RefreshHandFromData()
        {
            if (_battleManager?.CurrentState?.DeckSystem == null)
            {
                return;
            }

            var handData = _battleManager.CurrentState.DeckSystem.Hand;
            if (handData == null)
            {
                return;
            }

            var shownInstanceIds = new HashSet<string>();
            foreach (var view in _cardPoolManager.HandCards)
            {
                if (view != null)
                {
                    shownInstanceIds.Add(view.InstanceId);
                }
            }

            bool added = false;
            foreach (var cardState in handData)
            {
                if (cardState == null || shownInstanceIds.Contains(cardState.InstanceId))
                {
                    continue;
                }

                var cardView = _cardPoolManager.GetOrCreateCard(cardState);
                if (cardView != null)
                {
                    _cardPoolManager.MoveToHand(cardView);
                    added = true;
                    Debug.Log($"[UI_BattleScene] 补齐产出卡牌到手牌: {cardState.CardId} ({cardState.InstanceId})");
                }
            }

            if (added)
            {
                UpdateHandLayout();
                RefreshHandEnergyAffordability();
            }
        }

        /// <summary>
        /// 玩家打出执行牌：挂起动作 + 压暗其余执行牌（一回合限一张执行牌）。
        /// 【真延迟】把引线（BattleManager.LastQueuedCast）作为虚拟单位挂进 ATB 时钟，
        /// 到 ResolveRound 回合由 ResolveCastAtomicTurn 结算；行动顺序轴上可见卡牌图标。
        /// </summary>
        public void OnPlayerPlayedExecutionCard(CardViewController playedCard, string ownerUnitId)
        {
            if (string.IsNullOrEmpty(ownerUnitId) || playedCard == null)
            {
                return;
            }

            _playerPlayedExecutionCardThisAtbTurn = true;
            ApplyHandExecutionSuppressionExcept(playedCard);

            var cast = _battleManager?.LastQueuedCast;
            if (cast != null && ATB != null)
            {
                string iconPath = Ashlight.Common.Utils.AssetPath.GetCardMiniSpriteAssetPath(cast.Card.Id);
                ATB.AddCastIcon(cast.CastId, iconPath, cast.ResolveRound);
                TurnOrderView?.SetCast(cast.CastId, cast.Card);
                TurnOrderView?.RefreshOrder();
            }
        }

        private void ApplyHandExecutionSuppressionExcept(CardViewController playedCard)
        {
            foreach (var c in _handCards)
            {
                if (c == null || c == playedCard)
                {
                    continue;
                }

                var info = c.GetCurrentCard();
                if (info != null && info.CardType == CardTypeEnum.Execution)
                {
                    c.SetExecutionSuppressed(true);
                }
            }
        }

        private void ClearHandExecutionSuppression()
        {
            foreach (var c in _handCards)
            {
                c?.SetExecutionSuppressed(false);
            }
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
        /// 排序：Swift(蓝/即时)在左，Execution(橙/延迟)在右；同类型保持加入顺序（OrderBy 稳定）。
        /// </summary>
        private void UpdateHandLayout()
        {
            if (CardContainer == null || _handCards.Count == 0)
                return;

            var sortedCards = _handCards.OrderBy(GetHandSortKey).ToList();

            float totalWidth = (sortedCards.Count - 1) * cardSpacing;
            float startX = -totalWidth / 2f;

            for (int i = 0; i < sortedCards.Count; i++)
            {
                if (sortedCards[i] == null) continue;

                RectTransform cardRect = sortedCards[i].transform as RectTransform;
                if (cardRect != null)
                {
                    float xPos = startX + i * cardSpacing;
                    cardRect.anchoredPosition = new Vector2(xPos, 0f);
                    cardRect.SetSiblingIndex(i);
                }
            }
        }

        private static int GetHandSortKey(CardViewController cardView)
        {
            var info = cardView?.GetCurrentCard();
            if (info == null) return 0;
            if (info.CardType == CardTypeEnum.Swift) return 0;
            return Mathf.Max(1, info.ExecutingCost);
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
            _cardPoolManager.ClearHandCards();
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

            // 非玩家回合时按钮无效
            if (!IsPlayerTurnActive())
            {
                Debug.Log("[UI_BattleScene] 非玩家回合，下个回合按钮无效");
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

            // 非玩家回合（敌人意图/执行轴、或无回合状态）按钮无效，
            // 避免误点导致悄悄 AdvanceOneStep + 重抽手牌。
            if (!IsPlayerTurnActive())
            {
                Debug.Log("[UI_BattleScene] 非玩家回合，结束回合按钮无效");
                return;
            }

            // 使用协程版本，支持动画等待
            StartCoroutine(EndRoundCoroutine());
        }

        /// <summary>
        /// 当前是否处于"玩家可结束的回合"：
        /// 1) BattleManager / CurrentState 必须存在
        /// 2) CurrentTurnUnitId 必须指向一个活着的玩家单位
        /// 3) 该玩家还没把执行卡推入执行轨（已推入说明回合实际上已经在收尾，不能重复结束）
        /// </summary>
        private bool IsPlayerTurnActive()
        {
            if (_battleManager == null || _battleManager.CurrentState == null)
                return false;

            string currentUnitId = _battleManager.CurrentState.CurrentTurnUnitId;
            if (string.IsNullOrEmpty(currentUnitId))
                return false;

            var unit = _battleManager.CurrentState.GetUnitById(currentUnitId);
            if (unit == null || unit.IsDead || !unit.IsPlayerUnit)
                return false;

            return true;
        }

        /// <summary>
        /// 【公共回合镜像】把命令结算累计的推迟（PendingRoundDelay）落到 ATB 真调度并刷新行动顺序视图。
        /// 每次卡牌/敌技结算完成后调用（CardViewController 出牌成功、执行牌结算、敌人原子回合）。
        /// </summary>
        public void ApplyPendingScheduleChanges()
        {
            if (ATB == null || _battleManager?.CurrentState == null)
            {
                return;
            }

            if (ATB.ApplyPendingDelays(_battleManager.CurrentState))
            {
                ATB.SyncScheduleToState(_battleManager.CurrentState);
                TurnOrderView?.RefreshOrder();
            }
        }

        /// <summary>
        /// 结束回合协程（真正的回合结束清理）
        /// </summary>
        private IEnumerator EndRoundCoroutine()
        {
            Debug.Log("[UI_BattleScene] 开始结束回合...");

            if (ATB != null && _battleManager?.CurrentState != null)
            {
                string currentTurnUnitId = _battleManager.CurrentState.CurrentTurnUnitId;
                if (!string.IsNullOrEmpty(currentTurnUnitId))
                {
                    var currentTurnUnit = _battleManager.CurrentState.GetUnitById(currentTurnUnitId);
                    if (currentTurnUnit != null && currentTurnUnit.IsPlayerUnit)
                    {
                        currentTurnUnit.CurrentEnergy = 0;
                        _battleManager.DiscardCurrentHand();
                        DisplayHandCards();
                        ClearHandExecutionSuppression();

                        // 过载次数要在 EndCurrentTurn 之前读取（= 下次行动的额外回合延迟）。
                        int overloadDelay = currentTurnUnit.Overload != null ? currentTurnUnit.Overload.OverloadCountThisTurn : 0;

                        // 【真延迟】执行牌不在回合末结算——引线已挂在 ATB 时钟上，到点走 ResolveCastAtomicTurn。

                        // 【推迟落账】本回合所有卡牌积累的行动推迟统一落到 ATB 调度。
                        ApplyPendingScheduleChanges();

                        _playerPlayedExecutionCardThisAtbTurn = false;
                        _battleManager.EndCurrentTurn();

                        // 结算执行牌可能打死敌人 → 战斗结束：停止推进，等结算面板。
                        if (_battleManager.CurrentState.IsBattleEnded)
                        {
                            ATB.AutoAdvanceSuspended = true;
                            yield break;
                        }

                        // 【公共回合制】重排：下次行动 = 当前公共回合 + Speed + 过载次数。
                        ATB.Reschedule(currentTurnUnitId, currentTurnUnit.Speed, overloadDelay);
                        // 记录已落账的过载负债，供肾上腺素（ClearOverload）拉回
                        currentTurnUnit.AppliedOverloadRoundDelay = overloadDelay;
                        ATB.TriggerNextUnit();
                        Debug.Log($"[UI_BattleScene] 玩家回合结束，重排 {currentTurnUnitId} (speed={currentTurnUnit.Speed}, overload={overloadDelay})");
                        yield break;
                    }
                }
            }

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

            // 5. 非ATB模式下保持原有自动开回合逻辑
            if (ATB == null)
            {
                _battleManager.StartPlayerTurn();
            }

            Debug.Log($"[UI_BattleScene] 回合结束完成，当前回合: {_battleManager.CurrentRound}");
            
            yield break;
        }

        /// <summary>
        /// 【公共回合制·原子回合】轮到某单位行动（由 ATB.OnUnitTurn 触发）：
        ///   · 玩家 → 进入出牌阶段，停下等玩家结束回合（EndRoundCoroutine 再重排+推进）。
        ///   · 敌人 → 立即结算其预告意图 → 结束回合 → 重排(Speed+过载) → 预告下一次意图；
        ///           全程在 TriggerNextUnit 的循环内同步完成，循环自动继续到下一单位。
        /// </summary>
        private void HandleAtbUnitTurn(string unitId, bool isPlayerUnit)
        {
            if (_battleManager == null || _battleManager.CurrentState == null || _isProcessingAtbTurn)
            {
                return;
            }

            // 新单位的回合到来 = 上一个单位的演出已被 ATB 闸门等完 → 解冻顺序条，恢复实时刷新。
            TurnOrderView?.UnfreezeOrder();

            // 【死亡/结束保护】战斗已结束：不再开启回合。
            if (_battleManager.CurrentState.IsBattleEnded)
            {
                Debug.Log($"[UI_BattleScene] 轮到 {unitId} 时战斗已结束，停止推进");
                if (ATB != null) ATB.AutoAdvanceSuspended = true;
                return;
            }

            // 【天气虚拟单位】轮到天气结算（第三方阵营，无 UnitState）——
            // 必须在下面的 GetUnitById 死亡保护之前拦截，否则会被当"已不存在"移除图标。
            if (unitId == ATB.WeatherUnitId)
            {
                _isProcessingAtbTurn = true;
                try
                {
                    ResolveWeatherAtomicTurn();
                }
                finally
                {
                    _isProcessingAtbTurn = false;
                }
                return;
            }

            // 【引线虚拟单位】轮到玩家在轨执行牌结算（同天气：无 UnitState，必须在死亡保护之前拦截）。
            if (unitId.StartsWith(Ashlight.Battle.BattleManager.CastIdPrefix, System.StringComparison.Ordinal))
            {
                _isProcessingAtbTurn = true;
                try
                {
                    ResolveCastAtomicTurn(unitId);
                }
                finally
                {
                    _isProcessingAtbTurn = false;
                }
                return;
            }

            var unit = _battleManager.CurrentState.GetUnitById(unitId);
            if (unit == null || unit.IsDead)
            {
                Debug.Log($"[UI_BattleScene] 轮到 {unitId} 时单位已死亡，移除其ATB图标");
                ATB?.RemoveUnitIcon(unitId);
                return;
            }

            // 【公共回合镜像】原子回合开始：把最新调度同步进快照（Core 命令按此判「当前回合的敌人」）。
            ATB?.SyncScheduleToState(_battleManager.CurrentState);

            // 行动到来 = 过载负债已偿还，清零（此后肾上腺素无可拉回）
            unit.AppliedOverloadRoundDelay = 0;

            _isProcessingAtbTurn = true;
            try
            {
                if (isPlayerUnit)
                {
                    // 玩家原子回合：进入出牌阶段并停下等输入。
                    TurnOrderView?.SetActiveUnit(unitId);
                    if (ATB != null) ATB.Pause();
                    _playerPlayedExecutionCardThisAtbTurn = false;
                    ClearHandExecutionSuppression();
                    _battleManager.StartPlayerTurn(unitId, false);
                    DisplayHandCards();
                    UpdateAllUnitsDisplay();
                    UpdateEnergyBarByUnitId(unitId);
                    Debug.Log($"[UI_BattleScene] 玩家回合开始: {unitId} (公共回合 {ATB?.CurrentRound})");
                }
                else
                {
                    ResolveEnemyAtomicTurn(unitId, unit);
                }
            }
            finally
            {
                _isProcessingAtbTurn = false;
            }
        }

        /// <summary>
        /// 天气的原子回合（如雷暴落雷）：劈所有行动排在当前公共回合的真单位 → 检查战斗结束 → 按绝对节拍重排(+Period)。
        /// 走普通伤害管线（吃易伤/减伤/护甲，不吃闪避）；空劈只有日志，节拍照常推进。
        /// 不调用 TriggerNextUnit —— 正处在其循环内，重排后循环自动继续。见 docs/天气系统设计_v1.md。
        /// </summary>
        private void ResolveWeatherAtomicTurn()
        {
            var weather = _battleManager?.CurrentWeather;
            if (weather == null || ATB == null)
            {
                // 防御：无天气本不该有天气图标。兜底重排避免时钟反复选中它卡死。
                ATB?.Reschedule(ATB.WeatherUnitId, weather != null ? weather.Period : 6, 0);
                return;
            }

            // 先取受劈名单再重排（GetUnitIdsAtRound 自带排除天气自身）。
            var victims = ATB.GetUnitIdsAtRound(ATB.CurrentRound);
            var results = _battleManager.ResolveWeatherStrike(victims);

            // 掉血表现：飘字 + 全体状态刷新（v1 无全屏特效）。
            foreach (var kv in results)
            {
                var ui = FindUnitUiTransform(kv.Key);
                if (ui != null)
                {
                    string label = kv.Value > 0 ? $"-{kv.Value}" : "格挡";
                    _animationHandler?.ShowFloatingLabel(ui.position, label, new Color(1f, 0.85f, 0.2f));
                }
            }
            UpdateAllUnitsDisplay();
            Debug.Log($"[UI_BattleScene] [{weather.Name}] 公共回合 {ATB.CurrentRound} 结算：命中 {results.Count} 个单位");

            // 劈死可能终结战斗（全灭=失败 / 最后一个敌人被劈死=胜利）。
            if (_battleManager.CurrentState != null && _battleManager.CurrentState.IsBattleEnded)
            {
                ATB.AutoAdvanceSuspended = true;
                return;
            }

            // 绝对节拍：下次结算 = 当前回合 + Period（k, 2k, 3k, ...）。
            ATB.Reschedule(ATB.WeatherUnitId, weather.Period, 0);
        }

        /// <summary>
        /// 引线的原子回合：玩家在轨执行牌到点结算（真延迟，见规则书 §3 / docs/卡组设计_法师战士主主题.md）。
        /// 施法者已倒下则引线作废（规则书：结算前被击倒则取消）。结算完移除图标，
        /// 不调用 TriggerNextUnit —— 正处在其循环内，移除后循环自动继续。
        /// </summary>
        private void ResolveCastAtomicTurn(string castId)
        {
            // 【条子与演出同步】同敌人回合：结算会立刻移除引线图标/卡片，先冻结快照，
            // 让引线卡在自己的结算演出期间仍显示在条头；下一回合开头解冻。
            TurnOrderView?.FreezeOrder();

            // 结算前同步调度镜像：引线卡的效果可能读「当前回合」（如条件伤害）。
            ATB?.SyncScheduleToState(_battleManager.CurrentState);

            bool resolved = _battleManager.ResolvePendingCast(castId);

            // 图标与行动顺序卡一并移除（引线一次性）。
            ATB?.RemoveUnitIcon(castId);
            TurnOrderView?.RemoveCast(castId);

            if (resolved)
            {
                UpdateAllUnitsDisplay();
                // 引线效果可能带推迟/提前（如凝滞领域、冰封领域），立即落账到调度。
                ApplyPendingScheduleChanges();
            }

            Debug.Log($"[UI_BattleScene] 引线结算{(resolved ? "完成" : "作废")}: {castId} (公共回合 {ATB?.CurrentRound})");

            if (_battleManager.CurrentState != null && _battleManager.CurrentState.IsBattleEnded)
            {
                if (ATB != null) ATB.AutoAdvanceSuspended = true;
            }
        }

        /// <summary>本场的天气 HUD（常驻角标 + 开场横幅）。新战斗初始化时销毁重建。</summary>
        private WeatherHud _weatherHud;

        /// <summary>
        /// 开场预告天气：创建常驻角标（hover 弹天气说明）并播一次开场横幅。
        /// </summary>
        private void ShowWeatherAnnouncement(cfg.WeatherInfo weather)
        {
            if (weather == null) return;

            if (_weatherHud != null)
            {
                Destroy(_weatherHud.gameObject);
                _weatherHud = null;
            }

            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                Debug.LogWarning("[UI_BattleScene] 找不到 Canvas，天气角标/横幅未创建");
                return;
            }

            _weatherHud = WeatherHud.Create(canvas, weather);
            Debug.Log($"[UI_BattleScene] 天气预告: {weather.Name}（每 {weather.Period} 回合结算）");
        }

        /// <summary>按 UnitId 找单位的 UI Transform（先查我方角色再查敌人），找不到返回 null。</summary>
        private Transform FindUnitUiTransform(string unitId)
        {
            var ch = FindCharacterByUnitId(unitId);
            if (ch != null) return ch.transform;
            var en = FindEnemyByUnitId(unitId);
            return en != null ? en.transform : null;
        }

        /// <summary>
        /// 敌人的原子回合：结算其（此前已预告的）意图 → 结束回合 → 重排(Speed+过载) → 预告下一次意图。
        /// 不调用 TriggerNextUnit —— 正处在其循环内，重排后循环会自动继续到下一单位。
        /// 敌人意图选择/动态索敌逻辑沿用 BattleManager 现有实现，不在此改动。
        /// </summary>
        private void ResolveEnemyAtomicTurn(string unitId, UnitState enemyUnit)
        {
            // 【高亮与演出同步】回合一开始就点亮该敌人（金框），且结算后不清除——
            // 它的攻击演出还在异步播放，ATB 会等演出结束才开下一个单位的回合，
            // 届时由下一个单位（玩家/敌人）的回合开头覆盖高亮。
            TurnOrderView?.SetActiveUnit(unitId);

            // 【条子与演出同步】结算马上会同步 Reschedule（条头瞬间翻页成后续单位）——
            // 先冻结顺序条快照（此刻该敌人在条头），演出期间条子保持不动；下一回合开头解冻。
            TurnOrderView?.FreezeOrder();

            // 【回合开始持续伤害】中毒/燃烧等在该敌人自己的原子回合开始结算：掉血 + 数值衰减 1（中毒 V→V-1，归零移除）。
            // 放在最前（晕眩之前）——持续伤害不因晕眩而停。毒发身亡则中止本回合后续行动：
            // UpdateAllUnitsDisplay 会播死亡演出并把它从 ATB/顺序条移除，ATB 循环自动继续下一单位（无需重排）。
            if (_battleManager.ProcessTurnStartBuffs(unitId))
            {
                UpdateAllUnitsDisplay();
                if (_battleManager.CurrentState.IsBattleEnded && ATB != null)
                    ATB.AutoAdvanceSuspended = true;
                return;
            }
            UpdateAllUnitsDisplay(); // 未死也刷新血条/buff 图标，反映本次掉血与衰减

            // 【晕眩】带 Stun buff 的敌人跳过本次行动：扣 1 层、照常重排，预告的意图保留到下次。
            var stun = enemyUnit.GetBuff("Stun");
            if (stun != null)
            {
                stun.Value -= 1f;
                stun.StackCount = Mathf.Max(0, Mathf.RoundToInt(stun.Value));
                if (stun.Value <= 0f)
                {
                    enemyUnit.RemoveBuff("Stun");
                }

                var stunUi = FindEnemyByUnitId(unitId);
                if (stunUi != null)
                    _animationHandler?.ShowFloatingLabel(stunUi.transform.position, "晕眩", new Color(1f, 0.9f, 0.3f));

                Debug.Log($"[UI_BattleScene] {unitId} 晕眩中，跳过本次行动 (剩余 {Mathf.Max(0, Mathf.RoundToInt(stun.Value))} 回合)");

                UpdateAllUnitsDisplay();
                if (ATB != null) ATB.Reschedule(unitId, enemyUnit.Speed, 0);
                if (!_battleManager.HasPendingEnemyIntent(unitId))
                {
                    DeclareEnemyIntent(unitId);
                }
                return;
            }

            // 若尚无预告意图（理论上 bootstrap/上一次已预告），当场补一次。
            if (!_battleManager.HasPendingEnemyIntent(unitId))
            {
                if (!DeclareEnemyIntent(unitId))
                {
                    // 无技能可用：直接重排到下一轮，避免卡死。
                    if (ATB != null) ATB.Reschedule(unitId, enemyUnit.Speed, 0);
                    return;
                }
            }

            // 【动态索敌 + 空排 miss】命中一刻按当前站位重算目标；目标区空排则打空。
            var result = _battleManager.ExecutePendingEnemyAfterExecutionTrack(unitId);
            if (result == Ashlight.Battle.EnemyIntentResolveResult.Missed)
            {
                var missUi = FindEnemyByUnitId(unitId);
                if (missUi != null)
                    _animationHandler?.ShowFloatingLabel(missUi.transform.position, "MISS", Color.white);
            }

            // 【推迟落账】敌技若带推迟效果（推玩家），在此落到 ATB 调度。
            ApplyPendingScheduleChanges();

            // 注意：不在此清除 SetActiveUnit —— 演出还在播，高亮保持到下一个单位的回合开头被覆盖。
            TurnOrderView?.SetExecuting(unitId, false);
            UpdateAllUnitsDisplay();
            UpdateEnergyBarByUnitId(unitId);

            // 结算可能打死玩家 → 战斗结束：停止推进。
            if (_battleManager.CurrentState.IsBattleEnded)
            {
                if (ATB != null) ATB.AutoAdvanceSuspended = true;
                _battleManager.EndCurrentTurn();
                return;
            }

            int overloadDelay = enemyUnit.Overload != null ? enemyUnit.Overload.OverloadCountThisTurn : 0;
            _battleManager.EndCurrentTurn();

            // 【公共回合制】重排：下次行动 = 当前公共回合 + Speed + 过载次数。
            if (ATB != null) ATB.Reschedule(unitId, enemyUnit.Speed, overloadDelay);
            enemyUnit.AppliedOverloadRoundDelay = overloadDelay;

            // 预告下一次意图（telegraph），供行动顺序视图显示。
            DeclareEnemyIntent(unitId);
            UpdateAllUnitsDisplay();
        }

        /// <summary>
        /// 让敌人当场准备并公示下一次意图（刷新能量 → 选技能+锁目标 → 亮意图图标/坐标）。
        /// 仅做「预告」，不涉及回合调度（种子/重排由 ATB 的公共回合负责）。
        /// 敌人意图与选目标逻辑沿用 BattleManager 现有实现，不在此改动。
        /// 返回 false = 无技能可用。
        /// </summary>
        private bool DeclareEnemyIntent(string unitId)
        {
            if (_battleManager?.CurrentState == null) return false;

            var enemy = _battleManager.CurrentState.GetUnitById(unitId);
            if (enemy == null || enemy.IsDead || enemy.IsPlayerUnit) return false;

            // 「预告即准备」：刷新能量后选技能+锁目标。
            _battleManager.StartEnemyTurn(unitId);

            if (!_battleManager.TryPrepareEnemyIntentAfterPlanning(
                    unitId, out int _, out EnemySkillInfo preparedSkill, out string targetUnitId))
            {
                return false;
            }

            // 意图显示：无合法目标（目标区空排，targetUnitId==null）→ 思考态 intention_think，否则攻击意图。
            ApplyEnemyIntentDisplay(unitId, preparedSkill, targetUnitId);

            UpdateAllUnitsDisplay();
            UpdateEnergyBarByUnitId(unitId);
            return true;
        }

        /// <summary>
        /// 判断敌人技能是否是 AOE：
        /// 1) TargetType 为 AllEnemy / AllAlly
        /// 2) 任一 AttackEffect 的 IsAoe 为 true
        /// </summary>
        private static bool SkillIsAoe(EnemySkillInfo skill)
        {
            if (skill == null) return false;
            if (skill.TargetType == cfg.TargetTypeEnum.AllEnemy
                || skill.TargetType == cfg.TargetTypeEnum.AllAlly)
                return true;
            if (skill.Effects != null)
            {
                foreach (var eff in skill.Effects)
                {
                    if (eff is cfg.AttackEffect atk && atk.IsAoe) return true;
                }
            }
            return false;
        }

        private void UpdateEnergyBarByUnitId(string unitId)
        {
            if (_battleManager?.CurrentState == null || EnergyBar == null || string.IsNullOrEmpty(unitId))
            {
                return;
            }

            var unit = _battleManager.CurrentState.GetUnitById(unitId);
            if (unit == null || unit.IsDead)
            {
                return;
            }

            if (EnergyBar.Txt_Energy != null)
            {
                int maxEnergy = Mathf.Max(0, unit.BaseEnergy);
                int currentEnergy = Mathf.Clamp(unit.CurrentEnergy, 0, maxEnergy);
                EnergyBar.Txt_Energy.text = $"{currentEnergy}/{maxEnergy}";
            }

            if (EnergyBar.Icon != null)
            {
                string iconPath = unit.IsPlayerUnit
                    ? AssetPath.GetCharacterIconAssetPath(unit.ConfigId)
                    : AssetPath.GetEnemyIconAssetPath(unit.ConfigId);
                var iconSprite = Resources.Load<Sprite>(iconPath);
                if (iconSprite != null)
                {
                    EnergyBar.Icon.sprite = iconSprite;
                }
            }

            // 能量变化后刷新所有手牌的能量可负担态（左上角变色）
            RefreshHandEnergyAffordability();
        }

        /// <summary>
        /// 通知所有手牌根据当前玩家能量刷新左上角能量数字颜色
        /// </summary>
        private void RefreshHandEnergyAffordability()
        {
            if (_handCards == null)
            {
                return;
            }

            foreach (var cardView in _handCards)
            {
                if (cardView != null)
                {
                    cardView.RefreshEnergyAffordability();
                    cardView.RefreshConditionHighlight(); // 条件牌描边随站位/移动状态刷新
                    cardView.RefreshDynamicDescription(); // 隧穿/铁蒺藜卡面「本回合已移动 N 次」计数刷新
                }
            }
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
        /// 根据UnitId查找敌人UI（委托给管理器）
        /// </summary>
        private Enemy FindEnemyByUnitId(string unitId) => _unitUIManager.FindEnemy(unitId);

        /// <summary>
        /// 根据UnitId查找角色UI（委托给管理器）
        /// </summary>
        private Character FindCharacterByUnitId(string unitId) => _unitUIManager.FindCharacter(unitId);

        /// <summary>
        /// 根据UnitId查找对应的UI GameObject（委托给管理器）
        /// </summary>
        private GameObject FindUnitUIObject(string unitId) => _unitUIManager.FindUnitObject(unitId);

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

            // 委托给动画处理器缓存伤害
            _animationHandler.CacheDamage(evt.AttackerId, evt.TargetId, evt.ActualDamage);
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

            if (evt.SkipBattleAnimation)
            {
                return;
            }

            Debug.Log($"[UI_BattleScene] 卡片执行: {evt.CasterId} -> {evt.TargetId}, 攻击卡片={evt.IsAttackCard}");

            // 委托给动画处理器播放动画
            StartCoroutine(_animationHandler.PlayBattleAnimation(evt));
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
        /// 战斗结束事件处理：暂停 ATB，根据胜负与测试设置决定走自动跳关还是 VictoryPanel
        /// </summary>
        private void OnBattleEnded(BattleEndedEvent evt)
        {
            Debug.Log($"[UI_BattleScene] 收到战斗结束事件，玩家胜利: {evt.IsPlayerVictory}");

            // 战斗结束：清除所有单位残留的 buff/debuff（数据层清空 + 刷新图标），
            // 否则战斗中的增益/减益会残留在胜利结算/升级界面的角色身上（以及同场景复用未重载时的下一场）。
            try
            {
                ClearAllUnitBuffs();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UI_BattleScene] 清除战斗结束 buff/debuff 时异常: {e}");
            }

            // 【关键】先弹结算面板，且每个步骤独立 try/catch：
            // 之前任一前置步骤（暂停 ATB / 停止预测）抛异常都会挡住 WinPanel，导致只看到“收到战斗结束事件”。
            // 把面板放最前面并隔离异常，确保面板一定会尝试弹出；异常也会被完整打印出来定位。
            try
            {
                if (evt.IsPlayerVictory)
                {
                    HandleVictory();
                }
                else
                {
                    HandleDefeat();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UI_BattleScene] 处理胜负面板时异常（这就是没弹 WinPanel 的原因）: {e}");
            }

            // 暂停 ATB（冻结规划轨/执行轨），避免关闭弹窗前继续推进
            try
            {
                if (ATB != null)
                {
                    ATB.Pause();
                    // 【回合制】中止 TriggerNextUnit 的自动连续推进：
                    // 结算中途打死单位触发战斗结束时，循环不应再触发后续回合。
                    ATB.AutoAdvanceSuspended = true;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UI_BattleScene] 暂停 ATB 时异常: {e}");
            }

            // 停止血量预测显示
            try
            {
                if (_battleManager?.PredictionManager != null)
                {
                    _battleManager.PredictionManager.StopPrediction();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UI_BattleScene] 停止血量预测时异常: {e}");
            }
        }

        /// <summary>
        /// 战斗结束时清空所有单位（玩家 + 敌人）的 buff/debuff 数据，并刷新显示让图标立即消失。
        /// 单一入口在 OnBattleEnded 调用，胜负两条分支都会经过，保证结算/升级界面上不再残留战斗中的增益减益。
        /// </summary>
        private void ClearAllUnitBuffs()
        {
            var state = _battleManager?.CurrentState;
            if (state == null) return;

            if (state.PlayerUnits != null)
            {
                foreach (var u in state.PlayerUnits) u?.Buffs?.Clear();
            }
            if (state.EnemyUnits != null)
            {
                foreach (var u in state.EnemyUnits) u?.Buffs?.Clear();
            }

            // 刷新 UI，被清空的 buff 图标随之移除
            UpdateAllUnitsDisplay();
        }

        /// <summary>
        /// 胜利分支：【WinPanel 优先】胜利一律尝试弹出结算面板，由玩家点击「继续」进下一关；
        /// 仅当场景内/Resources 都找不到任何结算面板时，才退回测试自动跳关逻辑。
        /// </summary>
        private void HandleVictory()
        {
            // 新流程：先移除所有敌人 -> 玩家点选一名角色（indicator 指示）-> 弹 ChoosePanel 三选一升级 -> 再走原结算面板。
            // 具体实现见 UI_BattleScene.UpgradeFlow.cs。
            StartVictoryUpgradeFlow();
        }

        /// <summary>
        /// 显示胜利结算面板（原 HandleVictory 逻辑）：WinPanel 优先，其次 VictoryPanel，最后退回测试自动跳关。
        /// 由升级三选一流程结束（或无可选升级/无法点选）后调用。
        /// </summary>
        private void ShowVictorySettlementPanel()
        {
            // WinPanel 优先：自动解析引用（Inspector 未绑定也能复用场景实例或从 Resources 实例化）
            var panel = ResolveWinPanel();
            if (panel != null)
            {
                Debug.Log("[UI_BattleScene] 胜利，弹出 WinPanel");
                panel.Show();
                return;
            }

            if (victoryPanel != null)
            {
                Debug.Log("[UI_BattleScene] 胜利，弹出 VictoryPanel（无 WinPanel）");
                victoryPanel.Show(true);
                return;
            }

            // 没有任何结算面板可用：退回测试自动跳关
            string nextEncounterId = GetNextEncounterId(_currentEncounterId);
            if (testAutoAdvance && !string.IsNullOrEmpty(nextEncounterId))
            {
                Debug.Log($"[UI_BattleScene] 无结算面板，自动跳关：{_currentEncounterId} -> {nextEncounterId}（延迟 {testAutoAdvanceDelay}s）");
                ScheduleSceneReload(nextEncounterId, testAutoAdvanceDelay);
                return;
            }

            Debug.LogWarning("[UI_BattleScene] WinPanel/VictoryPanel 均不可用，且未启用自动跳关；战斗已结束停在原场景");
        }

        /// <summary>
        /// 解析胜利面板引用，保证 HandleVictory 一定能拿到可用面板：
        ///   1. Inspector 已绑定 → 直接用；
        ///   2. 场景内查找（含未激活实例）→ 复用并缓存；
        ///   3. 从 Resources 实例化 WinPanel 预制体作为兜底。
        /// </summary>
        private WinPanel ResolveWinPanel()
        {
            if (winPanel != null)
                return EnsurePanelUnderCanvas(winPanel);

            // 场景里通常已有一个未激活的 WinPanel 实例（prefab 实例），直接复用
            winPanel = FindObjectOfType<WinPanel>(true);
            if (winPanel != null)
            {
                Debug.Log("[UI_BattleScene] winPanel 未在 Inspector 绑定，已自动复用场景内 WinPanel 实例");
                return EnsurePanelUnderCanvas(winPanel);
            }

            // 兜底：从 Resources 实例化（路径对应 Assets/Resources/UI/BattleScene/WinPanel.prefab）
            var prefab = Resources.Load<GameObject>("UI/BattleScene/WinPanel");
            if (prefab != null)
            {
                Transform parent = FindBattleCanvas()?.transform;
                GameObject go = parent != null ? Instantiate(prefab, parent, false) : Instantiate(prefab);
                go.SetActive(false); // 由 WinPanel.Show() 负责激活并播放演出
                winPanel = go.GetComponent<WinPanel>();
                Debug.Log($"[UI_BattleScene] 场景内无 WinPanel，已从 Resources 实例化 WinPanel 预制体 (parent={(parent != null ? parent.name : "无Canvas")})");
                return EnsurePanelUnderCanvas(winPanel);
            }

            Debug.LogError("[UI_BattleScene] 无法解析 WinPanel：Inspector 未绑定、场景内无实例、且 Resources(UI/BattleScene/WinPanel) 加载失败");
            return null;
        }

        /// <summary>
        /// 确保 WinPanel 挂在某个 Canvas 下，否则它不会被渲染（UI 元素必须在 Canvas 子树里）。
        /// 若当前没有 Canvas 祖先，则重挂到战斗场景的 Canvas，并置于最上层。
        /// </summary>
        private WinPanel EnsurePanelUnderCanvas(WinPanel panel)
        {
            if (panel == null) return null;

            if (panel.GetComponentInParent<Canvas>() != null)
            {
                panel.transform.SetAsLastSibling(); // 已在 Canvas 下，仅确保渲染在最上层
                return panel;
            }

            var canvas = FindBattleCanvas();
            if (canvas != null)
            {
                panel.transform.SetParent(canvas.transform, false);
                panel.transform.SetAsLastSibling();
                // 锚定铺满父级，避免预制体自带的局部坐标把它推到屏幕外
                var rt = panel.transform as RectTransform;
                if (rt != null)
                {
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                }
                Debug.Log($"[UI_BattleScene] WinPanel 原本不在 Canvas 下，已重挂到 Canvas: {canvas.name}");
            }
            else
            {
                Debug.LogError("[UI_BattleScene] 找不到任何 Canvas，WinPanel 无法显示！请确认战斗场景里有 Canvas。");
            }
            return panel;
        }

        /// <summary>
        /// 查找战斗场景的 Canvas：UI_BattleScene 本体可能不在 Canvas 下，
        /// 因此优先借用已知 UI 元素（CardContainer / PlayerPosition）的 Canvas，兜底取场景内任意 Canvas。
        /// </summary>
        private Canvas FindBattleCanvas()
        {
            Canvas c = null;
            if (CardContainer != null) c = CardContainer.GetComponentInParent<Canvas>();
            if (c == null && PlayerPosition != null) c = PlayerPosition.GetComponentInParent<Canvas>();
            if (c == null) c = GetComponentInParent<Canvas>();
            if (c == null) c = FindObjectOfType<Canvas>();
            return c;
        }

        /// <summary>
        /// 失败分支：测试模式重开当前关；否则暂留场景（后续接入 DefeatPanel）
        /// </summary>
        private void HandleDefeat()
        {
            if (testAutoRetryOnDefeat && !string.IsNullOrEmpty(_currentEncounterId))
            {
                Debug.Log($"[UI_BattleScene] 失败，重开当前关：{_currentEncounterId}（延迟 {testAutoAdvanceDelay}s）");
                ScheduleSceneReload(_currentEncounterId, testAutoAdvanceDelay);
                return;
            }

            Debug.Log("[UI_BattleScene] 玩家失败，暂未接入失败弹窗");
        }

        /// <summary>
        /// 决定本场战斗的起始 EncounterId。
        /// 优先级：PendingEncounterId（VictoryPanel / 自动跳关写入） &gt; testEncounterSequence[0] &gt; testEncounterId。
        /// </summary>
        private string ResolveStartingEncounterId()
        {
            string pending = BattleManager.ConsumePendingEncounterId();
            if (!string.IsNullOrEmpty(pending))
            {
                Debug.Log($"[UI_BattleScene] 使用 Pending EncounterId: {pending}");
                return pending;
            }

            if (testEncounterSequence != null && testEncounterSequence.Length > 0 && !string.IsNullOrEmpty(testEncounterSequence[0]))
            {
                Debug.Log($"[UI_BattleScene] 使用测试序列首关: {testEncounterSequence[0]}");
                return testEncounterSequence[0];
            }

            Debug.Log($"[UI_BattleScene] 使用 testEncounterId: {testEncounterId}");
            return testEncounterId;
        }

        /// <summary>
        /// 在 testEncounterSequence 里查找 currentId 的下一项；
        /// 越界且 testLoopAfterLast 为 true 则回到首项，否则返回 null（视为通关）。
        /// </summary>
        private string GetNextEncounterId(string currentId)
        {
            if (testEncounterSequence == null || testEncounterSequence.Length == 0)
            {
                return null;
            }

            int idx = System.Array.IndexOf(testEncounterSequence, currentId);

            // 当前 id 不在序列里：从首项开始
            if (idx < 0)
            {
                return testEncounterSequence[0];
            }

            int next = idx + 1;
            if (next < testEncounterSequence.Length)
            {
                return testEncounterSequence[next];
            }

            return testLoopAfterLast ? testEncounterSequence[0] : null;
        }

        /// <summary>
        /// 延迟后写入 PendingEncounterId 并重载 BattleScene。
        /// </summary>
        private void ScheduleSceneReload(string nextEncounterId, float delay)
        {
            if (_autoAdvanceCoroutine != null)
            {
                return; // 已经在跳关途中
            }
            _autoAdvanceCoroutine = StartCoroutine(ReloadSceneAfter(nextEncounterId, delay));
        }

        private IEnumerator ReloadSceneAfter(string nextEncounterId, float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }
            BattleManager.PendingEncounterId = nextEncounterId;
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
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

        #endregion
    }
}

