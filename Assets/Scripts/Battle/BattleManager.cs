using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashlight.Battle.Core.Commands;
using Ashlight.Battle.Core.Data;
using Ashlight.Battle.Core.Engine;
using Ashlight.Common.Events;
using Ashlight.Config;
using Ashlight.State.Runtime;
using Ashlight.Systems.Character;
using cfg;
using cfg.Enemy;
using UnityEngine;

namespace Ashlight.Battle
{
    /// <summary>
    /// 敌人待执行意图在「命中一刻」的动态索敌结算结果。
    /// </summary>
    public enum EnemyIntentResolveResult
    {
        /// <summary>解算到有效目标并已造成效果。</summary>
        Hit,
        /// <summary>目标区当前空排，打空 miss（无任何效果）。</summary>
        Missed,
        /// <summary>无待执行意图 / 施法者已亡等，本次无操作。</summary>
        Aborted
    }

    /// <summary>
    /// 战斗管理器
    /// 负责战斗的初始化、状态管理和核心引擎的协调
    /// </summary>
    public class BattleManager : MonoBehaviour
    {
        private const string DivinationCurseCardId = "Extra006";

        public static BattleManager Instance { get; private set; }

        /// <summary>
        /// 待加载的遭遇战 ID：VictoryPanel "继续" 按钮设置后重载 BattleScene，
        /// 新的 UI_BattleScene 启动时会优先使用此 ID（消费后清空）。
        /// 静态字段跨场景重载持续存在。
        /// </summary>
        public static string PendingEncounterId { get; set; }

        /// <summary>
        /// 消费并返回待加载的遭遇战 ID（消费后清空）。
        /// </summary>
        public static string ConsumePendingEncounterId()
        {
            string id = PendingEncounterId;
            PendingEncounterId = null;
            return id;
        }

        /// <summary>
        /// BattleEndedEvent 是否已发送过，用于保证仅发送一次。
        /// </summary>
        private bool _battleEndEventRaised;

        /// <summary>
        /// 当前战斗状态快照
        /// </summary>
        public BattleStateSnapshot CurrentState { get; private set; }

        /// <summary>
        /// 初始战斗快照（用于重置战斗）
        /// </summary>
        public BattleStateSnapshot InitialSnapshot { get; private set; }

        /// <summary>
        /// 时间轴解算器（过渡期保留，后续清理）
        /// </summary>
        public TimelineResolver Resolver { get; private set; }

        /// <summary>
        /// 战斗预测器
        /// </summary>
        public BattlePredictor Predictor { get; private set; }

        /// <summary>
        /// 战斗预测管理器
        /// </summary>
        public BattlePredictionManager PredictionManager { get; private set; }

        /// <summary>
        /// 当前战斗信息
        /// </summary>
        public BattleInfo BattleInfo { get; private set; }

        /// <summary>
        /// 当前回合数
        /// </summary>
        public int CurrentRound { get; private set; }

        /// <summary>
        /// 敌人规划轨结束、执行轨尚未结算时暂存的技能与目标（按 unitId 索引，支持多敌人同时在执行轨）
        /// </summary>
        /// <summary>
        /// 敌人固定循环出招的游标（按 UnitId 索引）：A* 槽按表序循环，取代旧的全池随机。
        /// 可预测的出招节奏是取舍压迫的地基（docs/敌人压迫感设计_v1.md §2）。
        /// </summary>
        private readonly Dictionary<string, int> _enemyRotationIndex = new Dictionary<string, int>();

        /// <summary>
        /// 已触发过的 S* 条件槽（key = unitId:槽位）。S 槽在 HP&lt;50% 时插队施放，每场每槽一次。
        /// </summary>
        private readonly HashSet<string> _firedConditionalSlots = new HashSet<string>();

        private readonly Dictionary<string, (EnemySkillInfo Skill, string TargetUnitId)> _pendingEnemyIntents
            = new Dictionary<string, (EnemySkillInfo, string)>();
        /// <summary>
        /// 玩家在轨执行牌（引线）。挂出后作为虚拟单位排进 ATB 时钟，第 ResolveRound 回合结算。
        /// 允许多发同时在轨（跨回合）；普通角色每回合限挂 1 张，法师百相可提升到 2 张。
        /// </summary>
        public class PendingPlayerCast
        {
            public string CastId;
            public int Sequence;
            public string CasterUnitId;
            public cfg.Character.CardInfo Card;
            public string TargetUnitId;
            /// <summary>结算所在的绝对公共回合（= 挂起回合 + max(1, ExecutingCost)）。</summary>
            public int ResolveRound;
            /// <summary>被己方卡牌顺延的累计格数；终焉倒数读取该值。</summary>
            public int AddedDelay;
            public int DamageBonus;
            public int ResolveDrawCount;
            public string ResolveBuffId;
            public float ResolveBuffValue;
            public int EchoDelay;
            public float EchoMultiplier;
            public bool NumericOnly;
            public float NumericScale = 1f;
            public int ImmediateSourceRound = -1;
        }

        /// <summary>玩家执行牌虚拟单位的 CastId 前缀（ATB/UI 用它区分引线图标与真单位）。</summary>
        public const string CastIdPrefix = "__cast__";

        /// <summary>在轨引线注册表（castId → cast）。</summary>
        private readonly Dictionary<string, PendingPlayerCast> _pendingPlayerCasts
            = new Dictionary<string, PendingPlayerCast>();

        /// <summary>被提前到当前公共回合、等待当前卡牌完整结算后立即兑现的引线。</summary>
        private readonly List<string> _queuedImmediateCastIds = new List<string>();
        private bool _isResolvingImmediateCasts;

        /// <summary>本回合各角色已挂起的执行牌数量；StartPlayerTurn 时清除该角色计数。</summary>
        private readonly Dictionary<string, int> _executionHungCountThisTurn = new Dictionary<string, int>();

        public class PendingPlayerCharge
        {
            public string CasterUnitId;
            public cfg.Character.CardInfo Card;
            public string TargetUnitId;
            public int StartRound;
            public readonly Dictionary<string, BuffState> PreviousWhileBuffs = new Dictionary<string, BuffState>();
        }

        private readonly Dictionary<string, PendingPlayerCharge> _pendingPlayerCharges
            = new Dictionary<string, PendingPlayerCharge>();
        private readonly HashSet<string> _chargeStartedThisTurn = new HashSet<string>();

        /// <summary>CastId 自增序号（同一场战斗内唯一）。</summary>
        private int _castSequence;

        /// <summary>最近一次成功挂起的引线（UI 在 TryQueuePlayerExecutionCard 返回 true 后立刻读取，用于挂 ATB 图标）。</summary>
        public PendingPlayerCast LastQueuedCast { get; private set; }

        // ========== ATB 引擎组件 ==========

        /// <summary>
        /// 回合解算器
        /// </summary>
        public TurnResolver TurnResolver { get; private set; }

        /// <summary>
        /// 卡牌结算器
        /// </summary>
        public CardPlayResolver CardPlayResolver { get; private set; }

        /// <summary>
        /// 敌人意图轴/执行轴推进解算器
        /// </summary>
        public EnemyIntentAxisResolver EnemyIntentAxisResolver { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _battleEndEventRaised = false;

            // 初始化 ATB 核心引擎
            CardPlayResolver = new CardPlayResolver();
            TurnResolver = new TurnResolver(CardPlayResolver);
            EnemyIntentAxisResolver = new EnemyIntentAxisResolver();
            Predictor = new BattlePredictor();
            PredictionManager = new BattlePredictionManager(this);

            // 保留旧引擎（过渡期兼容）
            Resolver = new TimelineResolver();
        }

        private void Update()
        {
            // 任何时候、任意伤害来源（直伤/反伤/持续伤害/连锁/Buff 结算等）导致单位死亡，
            // 都能被检测到：每帧重新评估战斗结束条件，不依赖散落在各 Command 里的 CheckBattleEnd。
            // 战斗尚未结束时才需要评估；已结束并发过事件后停止，避免无谓开销。
            if (CurrentState != null && !_battleEndEventRaised)
            {
                CurrentState.CheckBattleEnd();
            }

            RaiseBattleEndedIfNeeded();
        }

        /// <summary>
        /// 检测 IsBattleEnded 是否刚刚从 false 翻到 true，是则发送一次 BattleEndedEvent。
        /// 集中在 Update 里轮询，避免在散落各处的 CheckBattleEnd 后重复埋点。
        /// </summary>
        private void RaiseBattleEndedIfNeeded()
        {
            if (_battleEndEventRaised || CurrentState == null || !CurrentState.IsBattleEnded)
            {
                return;
            }

            _battleEndEventRaised = true;
            GameEvent.Publish(new BattleEndedEvent
            {
                IsPlayerVictory = CurrentState.IsPlayerVictory
            });
            Debug.Log($"[BattleManager] 战斗结束事件已发布，玩家胜利: {CurrentState.IsPlayerVictory}");
        }

        /// <summary>
        /// 初始化战斗
        /// </summary>
        /// <param name="battleInfo">战斗初始化参数</param>
        public void InitializeBattle(BattleInfo battleInfo)
        {
            if (battleInfo == null || !battleInfo.IsValid())
            {
                Debug.LogError("[BattleManager] 战斗初始化失败：BattleInfo无效");
                return;
            }

            BattleInfo = battleInfo;

            // 创建新的战斗状态快照
            CurrentState = new BattleStateSnapshot();
            ClearPendingEnemyIntent();
            CancelPendingCasts();
            CancelPendingCharges();
            _executionHungCountThisTurn.Clear();
            _chargeStartedThisTurn.Clear();
            _castSequence = 0;
            LastQueuedCast = null;
            _battleEndEventRaised = false;

            // 1. 创建玩家单位
            CreatePlayerUnits(battleInfo.PlayerCharacters);

            // 2. 创建敌人单位
            CreateEnemyUnits(battleInfo.EncounterId);

            // 3. 初始化卡组系统
            InitializeDeckSystem(battleInfo.PlayerCharacters, battleInfo.InitialDrawCount);

            // 3.5 应用已获得的升级效果（贴永久buff / 改单位属性 / 写卡牌修正表）
            UpgradeEffectApplier.Apply(CurrentState, battleInfo.PlayerCharacters);

            // 4. 保存初始快照
            SaveInitialSnapshot();

            // 5. 初始化回合数（不立即开始回合，等待UI初始化完成）
            CurrentRound = 0;

            // 6. 摇天气（全局权重池，v1 只有雷暴必中；表为空则本场无天气）
            RollWeather();
        }

        // ────────────────────────────────────────────────────────────────
        #region 天气系统

        /// <summary>
        /// 本场战斗的天气（战斗开始时从天气表按 Weight 全局随机）。null = 无天气。
        /// 天气在时钟里是虚拟单位（第三方阵营，无 UnitState），调度由 UI 侧 ATB 负责，
        /// 这里只持有配置与结算逻辑。见 docs/天气系统设计_v1.md。
        /// </summary>
        public WeatherInfo CurrentWeather { get; private set; }

        /// <summary>
        /// 战斗开始时按 Weight 从天气表随机一个天气。表为空/未加载时本场无天气（防御性，不报错）。
        /// </summary>
        private void RollWeather()
        {
            CurrentWeather = null;

            var table = ConfigLoader.Tables?.TbWeatherInfo;
            if (table == null || table.DataList == null || table.DataList.Count == 0)
            {
                Debug.Log("[BattleManager] 天气表为空，本场无天气");
                return;
            }

            float totalWeight = 0f;
            foreach (var w in table.DataList)
            {
                if (w != null && w.Weight > 0f) totalWeight += w.Weight;
            }
            if (totalWeight <= 0f)
            {
                Debug.Log("[BattleManager] 天气表权重全为 0，本场无天气");
                return;
            }

            float roll = Random.Range(0f, totalWeight);
            foreach (var w in table.DataList)
            {
                if (w == null || w.Weight <= 0f) continue;
                roll -= w.Weight;
                if (roll <= 0f)
                {
                    CurrentWeather = w;
                    break;
                }
            }
            if (CurrentWeather == null) CurrentWeather = table.DataList[table.DataList.Count - 1];

            Debug.Log($"[BattleManager] 本场天气: {CurrentWeather.Name} (每 {CurrentWeather.Period} 回合结算，伤害 {CurrentWeather.Damage})");
        }

        /// <summary>
        /// 天气落雷结算：对给定单位（= 行动排在雷击回合的单位）各造成 CurrentWeather.Damage 点环境伤害。
        /// 走普通伤害管线（吃易伤/减伤/护甲），**不吃闪避**（canBeDodged=false）。
        /// 劈死走正常死亡流程（TakeDamage 内置）+ CheckBattleEnd。
        /// </summary>
        /// <param name="unitIds">被劈单位 Id 列表（死亡/不存在的自动跳过）</param>
        /// <returns>unitId → 实际造成的伤害（被护甲全挡也会有条目，值为 0），供 UI 飘字</returns>
        public Dictionary<string, int> ResolveWeatherStrike(IReadOnlyList<string> unitIds)
        {
            var results = new Dictionary<string, int>();
            if (CurrentState == null || CurrentWeather == null || unitIds == null)
            {
                return results;
            }

            bool guardActive = CurrentState.WeatherGuardArmor > 0
                               && CurrentState.WeatherGuardRound == CurrentState.CurrentRound;
            foreach (var id in unitIds)
            {
                var unit = CurrentState.GetUnitById(id);
                if (unit == null || unit.IsDead) continue;

                if (guardActive && unit.IsPlayerUnit)
                {
                    unit.Defense += CurrentState.WeatherGuardArmor;
                    Debug.Log($"[BattleManager] [风暴眼] {id} 在天气伤害前获得 {CurrentState.WeatherGuardArmor} 点护甲");
                }

                int dealt = unit.TakeDamage(CurrentWeather.Damage, canBeDodged: false);
                ArmorBreakMoveProcessor.ResolvePending(CurrentState, unit);
                results[id] = dealt;
                Debug.Log($"[BattleManager] [{CurrentWeather.Name}] 劈中 {id}：{dealt} 伤害 (剩余 HP: {unit.CurrentHp}{(unit.IsDead ? "，死亡" : "")})");
            }

            if (results.Count == 0)
            {
                Debug.Log($"[BattleManager] [{CurrentWeather.Name}] 本次结算无人在场（空劈），节拍照常推进");
            }

            CurrentState.LastWeatherResolvedRound = CurrentState.CurrentRound;
            if (CurrentState.WeatherGuardRound <= CurrentState.CurrentRound)
            {
                CurrentState.WeatherGuardRound = -1;
                CurrentState.WeatherGuardArmor = 0;
            }
            CurrentState.CheckBattleEnd();
            return results;
        }

        #endregion

        /// <summary>
        /// 创建玩家单位
        /// </summary>
        private void CreatePlayerUnits(List<CharacterEnum> characters)
        {
            int playerIndex = 0;
            foreach (var characterId in characters)
            {
                // 从配置表获取角色基础信息
                var characterConfig = ConfigLoader.Tables.TbCharaterInfo.GetOrDefault(characterId);
                if (characterConfig == null)
                {
                    Debug.LogError($"[BattleManager] 未找到角色配置: {characterId}");
                    continue;
                }

                // 尝试从角色系统获取运行时状态
                var characterState = CharacterSystem.GetCharacterState(characterId);
                int currentHp = characterConfig.BaseHp; // 默认使用配置表的最大血量

                if (characterState != null)
                {
                    // 如果找到运行时状态，使用运行时的当前血量
                    currentHp = characterState.CurrentHp;
                }
                else
                {
                    Debug.LogWarning($"[BattleManager] 未找到角色运行时状态: {characterId}，使用配置表默认值");
                }

                // 创建战斗单位状态
                var unitState = new UnitState
                {
                    UnitId = $"player_{playerIndex}",
                    ConfigId = characterId.ToString(),
                    MaxHp = characterConfig.BaseHp,
                    CurrentHp = currentHp,
                    Defense = 0,
                    IsPlayerUnit = true,
                    IsDead = false,
                    Track = new TimelineTrack(characterId),
                    Speed = Mathf.Max(1, characterConfig.Speed),
                    BaseEnergy = Mathf.Max(0, characterConfig.Energy),
                    BaseDrawCount = Mathf.Max(0, characterConfig.Draw),
                    ActionBar = new ActionBarState(),
                    Overload = new OverloadState(),
                    // 开局站位：战士(Rocket)前排，其余后排
                    RowPosition = characterId == CharacterEnum.Rocket
                        ? BattleRowPosition.FrontRow
                        : BattleRowPosition.BackRow
                };

                CurrentState.PlayerUnits.Add(unitState);
                playerIndex++;
            }
        }

        /// <summary>
        /// 创建敌人单位
        /// </summary>
        private void CreateEnemyUnits(string encounterId)
        {
            // 从配置表获取遭遇战信息
            var encounter = ConfigLoader.Tables.TbEncounter.GetOrDefault(encounterId);
            if (encounter == null)
            {
                Debug.LogError($"[BattleManager] 未找到遭遇战配置: {encounterId}");
                return;
            }

            // 新战斗：重置出招循环游标与 S 槽触发记录
            _enemyRotationIndex.Clear();
            _firedConditionalSlots.Clear();

            int enemyIndex = 0;
            foreach (var enemyInfo in encounter.EnemySet_Ref)
            {
                if (enemyInfo == null)
                {
                    Debug.LogWarning($"[BattleManager] 敌人配置引用为null，跳过");
                    continue;
                }

                // 创建敌人战斗单位状态
                var unitState = new UnitState
                {
                    UnitId = $"enemy_{enemyIndex}",
                    ConfigId = enemyInfo.Id,
                    MaxHp = enemyInfo.Hp,
                    CurrentHp = enemyInfo.Hp,
                    Defense = 0,
                    IsPlayerUnit = false,
                    IsElite = enemyInfo.IsElite,
                    IsDead = false,
                    Track = null,
                    Speed = Mathf.Max(1, enemyInfo.Speed),
                    BaseEnergy = 2,
                    BaseDrawCount = 0,
                    ActionBar = new ActionBarState(),
                    Overload = new OverloadState(),
                    // 开局站位来自表（EnemyInfo.StartRow）：近战前排、法系/射手后排；Any 视为前排
                    RowPosition = enemyInfo.StartRow == cfg.TargetZoneEnum.Back
                        ? BattleRowPosition.BackRow
                        : BattleRowPosition.FrontRow
                };

                CurrentState.EnemyUnits.Add(unitState);
                enemyIndex++;
            }

            // 初始化敌人共享时间轴
            CurrentState.SharedEnemyTrack = new TimelineTrack();
        }

        /// <summary>
        /// 初始化卡组系统
        /// </summary>
        private void InitializeDeckSystem(List<CharacterEnum> characters, int initialDrawCount)
        {
            // 收集所有参战角色的卡组
            var allCards = new List<CardRuntimeState>();

            foreach (var characterId in characters)
            {
                var characterState = CharacterSystem.GetCharacterState(characterId);
                if (characterState != null && characterState.CurrentDeck != null && characterState.CurrentDeck.Count > 0)
                {
                    allCards.AddRange(characterState.CurrentDeck);
                }
                else
                {
                    Debug.LogWarning($"[BattleManager] 角色 {characterId} 没有卡组，尝试创建默认测试卡组");
                    var testDeck = CreateTestDeck(characterId);
                    if (testDeck.Count > 0)
                    {
                        allCards.AddRange(testDeck);
                    }
                }
            }

            // 如果没有任何卡牌，至少创建一些基础卡牌以便测试
            if (allCards.Count == 0)
            {
                Debug.LogWarning($"[BattleManager] 没有找到任何卡牌，创建最小测试卡组");
                allCards = CreateMinimalTestDeck();
            }

            // 初始化卡组系统
            CurrentState.DeckSystem.Initialize(allCards, characters);

            // 抽取初始手牌
            //CurrentState.DeckSystem.DrawCard(initialDrawCount);
        }

        /// <summary>
        /// 为指定角色创建测试卡组（使用CharacterInfo中的BaseDeck）
        /// </summary>
        private List<CardRuntimeState> CreateTestDeck(CharacterEnum characterId)
        {
            var testDeck = new List<CardRuntimeState>();

            // 从配置表获取角色信息
            var characterConfig = ConfigLoader.Tables.TbCharaterInfo.GetOrDefault(characterId);
            if (characterConfig == null)
            {
                Debug.LogWarning($"[BattleManager] 未找到角色配置: {characterId}");
                return testDeck;
            }

            // 使用角色的BaseDeck创建卡组
            if (characterConfig.BaseDeck == null || characterConfig.BaseDeck.Count == 0)
            {
                Debug.LogWarning($"[BattleManager] 角色 {characterId} 的BaseDeck为空");
                return testDeck;
            }

            // 为每张BaseDeck中的卡牌创建运行时状态
            foreach (var cardId in characterConfig.BaseDeck)
            {
                var cardState = CardRuntimeState.CreateDefault(cardId);
                testDeck.Add(cardState);
            }

            return testDeck;
        }

        /// <summary>
        /// 创建最小测试卡组（当完全没有卡牌时使用）
        /// </summary>
        private List<CardRuntimeState> CreateMinimalTestDeck()
        {
            var testDeck = new List<CardRuntimeState>();

            // 获取配置表中的第一张卡牌
            var allCardConfigs = ConfigLoader.Tables.TbCardInfo.DataList;

            if (allCardConfigs != null && allCardConfigs.Count > 0)
            {
                var firstCard = allCardConfigs[0];
                // 添加5张相同的卡牌
                for (int i = 0; i < 5; i++)
                {
                    var cardState = CardRuntimeState.CreateDefault(firstCard.Id);
                    testDeck.Add(cardState);
                }
            }
            else
            {
                Debug.LogError($"[BattleManager] 无法创建测试卡组：配置表中没有卡牌");
            }

            return testDeck;
        }

        /// <summary>
        /// 保存初始快照
        /// </summary>
        private void SaveInitialSnapshot()
        {
            InitialSnapshot = CurrentState.Clone();
        }

        /// <summary>
        /// 重置战斗到初始状态
        /// </summary>
        public void ResetBattle()
        {
            if (InitialSnapshot == null)
            {
                Debug.LogError("[BattleManager] 无法重置战斗：初始快照不存在");
                return;
            }

            CurrentState = InitialSnapshot.Clone();
            CancelPendingCasts();
            CancelPendingCharges();
            _executionHungCountThisTurn.Clear();
            _chargeStartedThisTurn.Clear();
        }

        /// <summary>
        /// 解算单个时间格
        /// </summary>
        public void ResolveStep(int timeIndex)
        {
            if (CurrentState == null)
            {
                Debug.LogError("[BattleManager] 无法解算：当前战斗状态为null");
                return;
            }

            Resolver.ResolveStep(CurrentState, timeIndex);
        }

        /// <summary>
        /// 解算完整时间轴
        /// </summary>
        public void ResolveFullTimeline()
        {
            if (CurrentState == null)
            {
                Debug.LogError("[BattleManager] 无法解算：当前战斗状态为null");
                return;
            }

            Resolver.ResolveFullTimeline(CurrentState);
        }

        /// <summary>
        /// 预测卡牌使用效果
        /// </summary>
        public PredictionResult SimulateCard(
            cfg.Character.CardInfo cardInfo,
            string casterId,
            string targetId,
            int timeIndex)
        {
            if (CurrentState == null)
            {
                Debug.LogError("[BattleManager] 无法预测：当前战斗状态为null");
                return null;
            }

            return Predictor.Simulate(CurrentState, cardInfo, casterId, targetId, timeIndex);
        }

        /// <summary>
        /// 立即执行卡牌（ATB 版本）
        /// 通过 CardPlayResolver 直接结算，不再经过 Timeline 时间轴
        /// </summary>
        /// <param name="cardInfo">卡牌配置</param>
        /// <param name="ownerId">施法者单位ID（如 player_0）</param>
        /// <param name="targetId">目标单位ID</param>
        /// <param name="instanceId">卡牌实例ID（用于从手牌精确移除）</param>
        /// <returns>是否执行成功</returns>
        public bool TryPlayCardImmediately(cfg.Character.CardInfo cardInfo, string ownerId, string targetId, string instanceId)
        {
            if (CurrentState == null)
            {
                Debug.LogError("[BattleManager] 无法立即执行卡牌：CurrentState 为 null");
                return false;
            }

            if (cardInfo == null)
            {
                Debug.LogError("[BattleManager] 无法立即执行卡牌：cardInfo 为 null");
                return false;
            }

            if (cardInfo.CardType == CardTypeEnum.Execution || cardInfo.CardType == CardTypeEnum.Charge)
            {
                Debug.LogWarning($"[BattleManager] 延时牌不能走立即结算入口: {cardInfo.Id}");
                return false;
            }

            if (string.IsNullOrEmpty(ownerId))
            {
                Debug.LogError("[BattleManager] 无法立即执行卡牌：ownerId 为空");
                return false;
            }

            var owner = CurrentState.GetUnitById(ownerId);
            if (owner == null || owner.IsDead)
            {
                Debug.LogWarning($"[BattleManager] 无法立即执行卡牌：施法者无效或已死亡 ownerId={ownerId}");
                return false;
            }

            if (!string.IsNullOrEmpty(CurrentState.CurrentTurnUnitId) && CurrentState.CurrentTurnUnitId != ownerId)
            {
                Debug.LogWarning($"[BattleManager] 无法立即执行卡牌：当前回合单位为 {CurrentState.CurrentTurnUnitId}，非 {ownerId}");
                return false;
            }

            // 【前排/后排】打出站位限制：卡牌声明 CastZone 时施法者必须站在对应排
            if (!ZoneTargeting.CanCastFromCurrentRow(cardInfo, owner))
            {
                Debug.LogWarning($"[BattleManager] 无法立即执行卡牌：站位不满足 CastZone={cardInfo.CastZone}, owner={ownerId}");
                return false;
            }

            // TimeSlot 的 targetId 是在轨执行牌的 CastId，而非 UnitId；
            // 必须走专用校验，不能把它当作战场单位再做分区/单位目标检查。
            bool targetsPendingCast = cardInfo.TargetType == TargetTypeEnum.TimeSlot;
            if (targetsPendingCast && !IsFriendlyPendingCast(
                    targetId,
                    ownerId,
                    requireDamage: CastHasDamage(cardInfo),
                    requireNumeric: cardInfo.Effects != null && cardInfo.Effects.Any(e => e is CastEchoEffect)))
            {
                Debug.LogWarning($"[BattleManager] 无法立即执行卡牌：在轨执行牌目标无效 card={cardInfo.Id}, castId={targetId}");
                return false;
            }

            // 【近战/远程】单体牌索敌分区限制：目标必须站在卡牌声明的排
            bool isMultiAllyTarget = cardInfo.Id == "Zhouzhou023";
            var zoneTarget = string.IsNullOrEmpty(targetId) ? null : CurrentState.GetUnitById(targetId.Split('|')[0]);
            if (!targetsPendingCast && isMultiAllyTarget && !AreMultiAllyTargetsValid(owner, targetId, 3))
            {
                Debug.LogWarning($"[BattleManager] 无法立即执行卡牌：多目标不合法 card={cardInfo.Id}, target={targetId}");
                return false;
            }
            if (!targetsPendingCast && !ZoneTargeting.IsSingleTargetZoneValid(cardInfo, zoneTarget))
            {
                Debug.LogWarning($"[BattleManager] 无法立即执行卡牌：目标不在限制分区 TargetZone={cardInfo.TargetZone}, target={targetId}");
                return false;
            }

            if (!targetsPendingCast && !IsCardSpecificTargetValid(cardInfo, owner, zoneTarget))
            {
                Debug.LogWarning($"[BattleManager] 无法立即执行卡牌：目标不满足卡牌专属限制 card={cardInfo.Id}, owner={ownerId}, target={targetId}");
                return false;
            }

            int energyCost = GetEffectiveEnergyCost(cardInfo, ownerId, instanceId);
            // 百相减费已在 GetEffectiveEnergyCost 内处理；解签诅咒会在减费后追加费用。
            bool isFreeMove = IsFreeMoveForOwner(owner, cardInfo);
            bool isFreePush = IsFreePushForOwner(owner, cardInfo);
            // 能量不足时尝试过载（每人每回合限 1 次；代价 = 结束回合重排额外 +1 格）
            bool willOverload = false;
            if (owner.CurrentEnergy < energyCost)
            {
                bool canOverload = owner.Overload != null && !owner.Overload.EnergyOverdraftUsedThisTurn;
                if (!canOverload)
                {
                    Debug.LogWarning($"[BattleManager] 无法立即执行卡牌：能量不足且不可过载 owner={ownerId}, 当前={owner.CurrentEnergy}, 需求={energyCost}");
                    return false;
                }
                willOverload = true;
            }

            // 发布卡牌执行事件（用于 UI 动画触发）
            var commands = CardPlayResolver.GenerateCommands(cardInfo, CurrentState?.CardModifiers?.Get(cardInfo.Id));
            bool isAttackCard = commands.Any(IsAttackCommand);
            // 执行牌：仅打出时尚未进入“执行动作”阶段，不播放战斗演出（与 Timeline 解算时的演出区分）
            bool skipBattleAnimation = cardInfo.CardType == CardTypeEnum.Execution
                                       || cardInfo.TargetType == TargetTypeEnum.TimeSlot;
            GameEvent.Publish(new CardExecutedEvent
            {
                CasterId = ownerId,
                TargetId = targetId,
                CardId = cardInfo.Id,
                IsAttackCard = isAttackCard,
                IsPrediction = false,
                SkipBattleAnimation = skipBattleAnimation
            });

            // 通过 CardPlayResolver 直接结算卡牌效果
            bool success = CardPlayResolver.PlayCard(
                CurrentState, cardInfo, ownerId, targetId, BuildCardResolutionContext(instanceId));

            if (!success)
            {
                Debug.LogWarning($"[BattleManager] 卡牌结算失败: {cardInfo.Id}");
                return false;
            }

            if (willOverload)
            {
                owner.Overload.OverloadCountThisTurn++; // 本回合已过载 → 结束回合重排 +1 格
                owner.Overload.EnergyOverdraftUsedThisTurn = true; // 透支额度每回合限 1 次（与卡牌 [过载] 分开计）
                owner.Overload.IsOverloaded = true;
                owner.CurrentEnergy = 0;                // 透支：能量清空
                Debug.Log($"[BattleManager] {ownerId} 过载打出 {cardInfo.Id}，本回合结束重排将 +1 格");
            }
            else
            {
                owner.CurrentEnergy -= energyCost;
                if (isFreeMove)
                {
                    owner.FreeMoveUsedThisTurn = true; // 用掉本回合的免费移动额度
                }
                if (isFreePush)
                {
                    owner.FreePushUsedThisTurn = true; // 用掉本回合的免费推迟额度
                }
            }

            // 从手牌消费这张卡
            bool consumed = false;
            if (!string.IsNullOrEmpty(instanceId))
            {
                consumed = CurrentState.DeckSystem.UseCardByInstanceId(instanceId);
            }

            if (!consumed)
            {
                consumed = CurrentState.DeckSystem.UseCardByCardId(cardInfo.Id);
            }

            if (!consumed)
            {
                Debug.LogWarning($"[BattleManager] 卡牌执行成功但手牌消费失败: cardId={cardInfo.Id}, instanceId={instanceId}");
            }

            // 提前至当前回合的引线必须等当前卡牌的全部效果（含过载等）完成后再依原顺序结算。
            ResolveQueuedImmediateCasts();
            CurrentState.CheckBattleEnd();

            if (PredictionManager != null)
            {
                PredictionManager.TriggerPrediction("卡牌立即执行");
            }

            return true;
        }

        /// <summary>
        /// 挂起一张执行牌：出牌阶段仅消耗资源并锁定动作，真正效果在执行轨结束时触发。
        /// </summary>
        public bool TryQueuePlayerExecutionCard(
            cfg.Character.CardInfo cardInfo,
            string ownerId,
            string targetId,
            string instanceId,
            out int executingCost)
        {
            executingCost = 1;

            if (CurrentState == null)
            {
                Debug.LogError("[BattleManager] 无法挂起执行牌：CurrentState 为 null");
                return false;
            }

            if (cardInfo == null || cardInfo.CardType != CardTypeEnum.Execution)
            {
                Debug.LogError("[BattleManager] 无法挂起执行牌：cardInfo 无效或不是执行牌");
                return false;
            }

            if (string.IsNullOrEmpty(ownerId) || string.IsNullOrEmpty(targetId))
            {
                Debug.LogError("[BattleManager] 无法挂起执行牌：ownerId 或 targetId 为空");
                return false;
            }

            var owner = CurrentState.GetUnitById(ownerId);
            var target = CurrentState.GetUnitById(targetId);
            if (owner == null || owner.IsDead || !owner.IsPlayerUnit)
            {
                Debug.LogWarning($"[BattleManager] 无法挂起执行牌：施法者无效 ownerId={ownerId}");
                return false;
            }

            int hungCount = _executionHungCountThisTurn.TryGetValue(ownerId, out int count) ? count : 0;
            int executionLimit = GetExecutionLimitForOwner(owner);
            if (hungCount >= executionLimit)
            {
                Debug.LogWarning($"[BattleManager] 该角色本回合执行牌已达上限 {executionLimit}: owner={ownerId}");
                return false;
            }

            if (target == null || target.IsDead)
            {
                Debug.LogWarning($"[BattleManager] 无法挂起执行牌：目标无效 targetId={targetId}");
                return false;
            }

            if (!string.IsNullOrEmpty(CurrentState.CurrentTurnUnitId) && CurrentState.CurrentTurnUnitId != ownerId)
            {
                Debug.LogWarning($"[BattleManager] 无法挂起执行牌：当前回合单位为 {CurrentState.CurrentTurnUnitId}，非 {ownerId}");
                return false;
            }

            // 【前排/后排】打出站位限制
            if (!ZoneTargeting.CanCastFromCurrentRow(cardInfo, owner))
            {
                Debug.LogWarning($"[BattleManager] 无法挂起执行牌：站位不满足 CastZone={cardInfo.CastZone}, owner={ownerId}");
                return false;
            }

            // 【近战/远程】单体牌索敌分区限制（挂起时按当前站位校验；执行时的动态站位由结算逻辑处理）
            if (!ZoneTargeting.IsSingleTargetZoneValid(cardInfo, target))
            {
                Debug.LogWarning($"[BattleManager] 无法挂起执行牌：目标不在限制分区 TargetZone={cardInfo.TargetZone}, target={targetId}");
                return false;
            }

            int energyCost = GetEffectiveEnergyCost(cardInfo, ownerId, instanceId);
            bool isFreePush = IsFreePushForOwner(owner, cardInfo);
            if (owner.CurrentEnergy < energyCost)
            {
                Debug.LogWarning($"[BattleManager] 无法挂起执行牌：能量不足 owner={ownerId}, 当前={owner.CurrentEnergy}, 需求={energyCost}");
                return false;
            }

            owner.CurrentEnergy -= energyCost;
            executingCost = Mathf.Max(1, cardInfo.ExecutingCost);

            bool consumed = false;
            if (!string.IsNullOrEmpty(instanceId))
            {
                consumed = CurrentState.DeckSystem.UseCardByInstanceId(instanceId);
            }

            if (!consumed)
            {
                consumed = CurrentState.DeckSystem.UseCardByCardId(cardInfo.Id);
            }

            if (!consumed)
            {
                Debug.LogWarning($"[BattleManager] 执行牌挂起失败：手牌消费失败 cardId={cardInfo.Id}, instanceId={instanceId}");
                owner.CurrentEnergy += energyCost;
                return false;
            }

            // 【真延迟】挂轨：结算回合 = 挂起回合 + max(1, ExecutingCost)。
            // CurrentState.CurrentRound 由 ATB.SyncScheduleToState 在原子回合开始时同步。
            _castSequence++;
            var cast = new PendingPlayerCast
            {
                CastId = $"{CastIdPrefix}{_castSequence}_{cardInfo.Id}",
                Sequence = _castSequence,
                CasterUnitId = ownerId,
                Card = cardInfo,
                TargetUnitId = targetId,
                ResolveRound = CurrentState.CurrentRound + executingCost
            };
            _pendingPlayerCasts[cast.CastId] = cast;
            _executionHungCountThisTurn[ownerId] = hungCount + 1;
            if (isFreePush)
            {
                owner.FreePushUsedThisTurn = true;
            }
            LastQueuedCast = cast;
            Debug.Log($"[BattleManager] 执行牌挂轨: {cardInfo.Name} ({cast.CastId}) 将于公共回合 {cast.ResolveRound} 结算 (当前 {CurrentState.CurrentRound})");

            if (PredictionManager != null)
            {
                PredictionManager.TriggerPrediction("执行牌挂起");
            }

            return true;
        }

        /// <summary>
        /// 开始蓄力：本次行动只可打出一张；0 费，立即结算开始/期间效果，完成效果在该角色下次行动开始时结算。
        /// 每经过一个公共回合获得一层蓄力，因此行动被推迟会自然提高层数。
        /// </summary>
        public bool TryStartPlayerChargeCard(
            cfg.Character.CardInfo cardInfo,
            string ownerId,
            string targetId,
            string instanceId)
        {
            if (CurrentState == null || cardInfo == null || cardInfo.CardType != CardTypeEnum.Charge)
            {
                Debug.LogWarning("[BattleManager] 无法开始蓄力：状态无效或不是蓄力牌");
                return false;
            }

            var owner = CurrentState.GetUnitById(ownerId);
            var target = CurrentState.GetUnitById(targetId);
            if (owner == null || owner.IsDead || !owner.IsPlayerUnit || target == null || target.IsDead)
            {
                Debug.LogWarning($"[BattleManager] 无法开始蓄力：施法者或目标无效 owner={ownerId}, target={targetId}");
                return false;
            }

            if ((!string.IsNullOrEmpty(CurrentState.CurrentTurnUnitId) && CurrentState.CurrentTurnUnitId != ownerId)
                || _chargeStartedThisTurn.Contains(ownerId)
                || _pendingPlayerCharges.ContainsKey(ownerId))
            {
                Debug.LogWarning($"[BattleManager] 本次行动已使用蓄力牌或仍在蓄力: {ownerId}");
                return false;
            }

            if (!ZoneTargeting.CanCastFromCurrentRow(cardInfo, owner)
                || !ZoneTargeting.IsSingleTargetZoneValid(cardInfo, target))
            {
                Debug.LogWarning($"[BattleManager] 蓄力牌站位或目标分区不合法: {cardInfo.Id}");
                return false;
            }

            bool consumed = !string.IsNullOrEmpty(instanceId)
                && CurrentState.DeckSystem.UseCardByInstanceId(instanceId);
            if (!consumed)
            {
                consumed = CurrentState.DeckSystem.UseCardByCardId(cardInfo.Id);
            }
            if (!consumed)
            {
                Debug.LogWarning($"[BattleManager] 蓄力牌消费失败: {cardInfo.Id}");
                return false;
            }

            var charge = new PendingPlayerCharge
            {
                CasterUnitId = ownerId,
                Card = cardInfo,
                TargetUnitId = targetId,
                StartRound = CurrentState.CurrentRound
            };
            _pendingPlayerCharges[ownerId] = charge;
            _chargeStartedThisTurn.Add(ownerId);

            CaptureChargeWhileBuffs(charge, owner);
            CardPlayResolver.PlayEffects(CurrentState, cardInfo, cardInfo.ChargeStartEffects, ownerId, targetId);
            CardPlayResolver.PlayEffects(CurrentState, cardInfo, cardInfo.ChargeWhileEffects, ownerId, ownerId);

            GameEvent.Publish(new CardExecutedEvent
            {
                CasterId = ownerId,
                TargetId = targetId,
                CardId = cardInfo.Id,
                IsAttackCard = false,
                IsPrediction = false,
                SkipBattleAnimation = true
            });

            PredictionManager?.TriggerPrediction("开始蓄力");
            return true;
        }

        /// <summary>该角色本回合是否已挂起执行牌（每回合限 1 张的判定）；unitId 为空 = 是否有任何在轨引线。</summary>
        public bool HasPendingPlayerExecutionCard(string unitId = null)
        {
            if (string.IsNullOrEmpty(unitId))
            {
                return _pendingPlayerCasts.Count > 0;
            }

            return _executionHungCountThisTurn.TryGetValue(unitId, out int count) && count > 0;
        }

        /// <summary>返回指定角色本回合还可以挂起的执行牌数量。</summary>
        public int GetRemainingExecutionSlots(string unitId)
        {
            var owner = CurrentState?.GetUnitById(unitId);
            if (owner == null || owner.IsDead || !owner.IsPlayerUnit)
            {
                return 0;
            }

            int hungCount = _executionHungCountThisTurn.TryGetValue(unitId, out int count) ? count : 0;
            return Mathf.Max(0, GetExecutionLimitForOwner(owner) - hungCount);
        }

        /// <summary>按 CastId 取在轨引线（不存在返回 null）。</summary>
        public PendingPlayerCast GetPendingCast(string castId)
        {
            return !string.IsNullOrEmpty(castId) && _pendingPlayerCasts.TryGetValue(castId, out var cast)
                ? cast
                : null;
        }

        /// <summary>返回在轨引线的稳定快照，供行动顺序 UI 同步图标与回合。</summary>
        public List<PendingPlayerCast> GetPendingCasts()
        {
            return _pendingPlayerCasts.Values
                .Where(c => c != null)
                .OrderBy(c => c.ResolveRound)
                .ThenBy(c => c.Sequence)
                .ToList();
        }

        public bool IsFriendlyPendingCast(string castId, string ownerId, bool requireDamage = false, bool requireNumeric = false)
        {
            if (!_pendingPlayerCasts.TryGetValue(castId ?? string.Empty, out var cast) || cast?.Card == null)
                return false;
            var owner = CurrentState?.GetUnitById(ownerId);
            var caster = CurrentState?.GetUnitById(cast.CasterUnitId);
            if (owner == null || caster == null || owner.IsDead || caster.IsDead || owner.IsPlayerUnit != caster.IsPlayerUnit)
                return false;
            if (requireDamage && !CastHasDamage(cast.Card)) return false;
            if (requireNumeric && !CastHasNumericPayload(cast.Card)) return false;
            return true;
        }

        public bool ShiftPendingCast(string castId, string ownerId, int shiftValue)
        {
            if (shiftValue == 0 || !IsFriendlyPendingCast(castId, ownerId)) return false;
            var cast = _pendingPlayerCasts[castId];
            int before = cast.ResolveRound;
            cast.ResolveRound = Mathf.Max(CurrentState.CurrentRound, before + shiftValue);
            if (shiftValue > 0) cast.AddedDelay += shiftValue;
            if (cast.ResolveRound <= CurrentState.CurrentRound) QueueImmediateCast(cast, before);
            Debug.Log($"[BattleManager] 引线位移: {castId} {before} -> {cast.ResolveRound} ({shiftValue:+#;-#;0})");
            return true;
        }

        public bool AddPendingCastDamageBonus(string castId, string ownerId, int value)
        {
            if (value == 0 || !IsFriendlyPendingCast(castId, ownerId, requireDamage: true)) return false;
            _pendingPlayerCasts[castId].DamageBonus += value;
            return true;
        }

        public bool AddPendingCastResolveDraw(string castId, string ownerId, int count)
        {
            if (count <= 0 || !IsFriendlyPendingCast(castId, ownerId)) return false;
            _pendingPlayerCasts[castId].ResolveDrawCount += count;
            return true;
        }

        public bool AddPendingCastResolveBuff(string castId, string ownerId, string buffId, float value)
        {
            if (string.IsNullOrEmpty(buffId) || value <= 0f || !IsFriendlyPendingCast(castId, ownerId)) return false;
            var cast = _pendingPlayerCasts[castId];
            cast.ResolveBuffId = buffId;
            cast.ResolveBuffValue += value;
            return true;
        }

        public bool AddPendingCastEcho(string castId, string ownerId, int delay, float multiplier)
        {
            if (delay <= 0 || multiplier <= 0f || !IsFriendlyPendingCast(castId, ownerId, requireNumeric: true)) return false;
            var cast = _pendingPlayerCasts[castId];
            cast.EchoDelay = delay;
            cast.EchoMultiplier = multiplier;
            return true;
        }

        public bool MarkPendingCastImmediate(string castId, string ownerId)
        {
            if (!IsFriendlyPendingCast(castId, ownerId)) return false;
            var cast = _pendingPlayerCasts[castId];
            int before = cast.ResolveRound;
            cast.ResolveRound = CurrentState.CurrentRound;
            QueueImmediateCast(cast, before);
            return true;
        }

        public int ShiftAllPendingCasts(string ownerId, int shiftValue)
        {
            if (shiftValue == 0) return 0;
            var ids = GetPendingCasts()
                .Where(c => IsFriendlyPendingCast(c.CastId, ownerId))
                .Select(c => c.CastId)
                .ToList();
            int changed = 0;
            foreach (string id in ids)
                if (ShiftPendingCast(id, ownerId, shiftValue)) changed++;
            return changed;
        }

        public bool HasFriendlyPendingCastAtRound(string ownerId, int round)
        {
            return _pendingPlayerCasts.Values.Any(c => c != null
                && c.ResolveRound == round
                && IsFriendlyPendingCast(c.CastId, ownerId));
        }

        private void QueueImmediateCast(PendingPlayerCast cast, int sourceRound)
        {
            if (cast == null || _queuedImmediateCastIds.Contains(cast.CastId)) return;
            cast.ImmediateSourceRound = sourceRound;
            _queuedImmediateCastIds.Add(cast.CastId);
            _queuedImmediateCastIds.Sort((a, b) =>
            {
                bool hasA = _pendingPlayerCasts.TryGetValue(a, out var ca);
                bool hasB = _pendingPlayerCasts.TryGetValue(b, out var cb);
                if (!hasA || !hasB) return hasA ? -1 : hasB ? 1 : 0;
                int byRound = ca.ImmediateSourceRound.CompareTo(cb.ImmediateSourceRound);
                return byRound != 0 ? byRound : ca.Sequence.CompareTo(cb.Sequence);
            });
        }

        private void ResolveQueuedImmediateCasts()
        {
            if (_isResolvingImmediateCasts || _queuedImmediateCastIds.Count == 0) return;
            _isResolvingImmediateCasts = true;
            try
            {
                while (_queuedImmediateCastIds.Count > 0 && CurrentState != null && !CurrentState.IsBattleEnded)
                {
                    string castId = _queuedImmediateCastIds[0];
                    _queuedImmediateCastIds.RemoveAt(0);
                    if (_pendingPlayerCasts.ContainsKey(castId))
                        ResolvePendingCast(castId);
                }
            }
            finally
            {
                _isResolvingImmediateCasts = false;
            }
        }

        private static bool CastHasDamage(cfg.Character.CardInfo card)
        {
            return card?.Effects != null && card.Effects.Any(e => e is AttackEffect
                || e is WeatherConditionalAttackEffect || e is DelayScaledAttackEffect);
        }

        private static bool CastHasNumericPayload(cfg.Character.CardInfo card)
        {
            return card?.Effects != null && card.Effects.Any(e => e is AttackEffect
                || e is WeatherConditionalAttackEffect || e is DelayScaledAttackEffect
                || e is DefenseEffect || e is HealEffect);
        }

        /// <summary>
        /// 开始玩家回合（兼容旧入口：默认选择第一个存活玩家）
        /// </summary>
        public void StartPlayerTurn()
        {
            if (CurrentState == null)
            {
                Debug.LogError("[BattleManager] 无法开始玩家回合：当前战斗状态为null");
                return;
            }

            var firstAlivePlayer = CurrentState.PlayerUnits.FirstOrDefault(u => u != null && !u.IsDead);
            if (firstAlivePlayer == null)
            {
                Debug.LogWarning("[BattleManager] 没有可行动的玩家单位");
                return;
            }

            StartPlayerTurn(firstAlivePlayer.UnitId, true);
        }

        /// <summary>
        /// 开始指定玩家单位回合，抽牌和能量由角色基础数据决定
        /// </summary>
        public void StartPlayerTurn(string unitId, bool generateEnemyIntentions)
        {
            if (CurrentState == null)
            {
                Debug.LogError("[BattleManager] 无法开始玩家回合：当前战斗状态为null");
                return;
            }

            var playerUnit = CurrentState.GetUnitById(unitId);
            if (playerUnit == null || playerUnit.IsDead || !playerUnit.IsPlayerUnit)
            {
                Debug.LogWarning($"[BattleManager] 无法开始玩家回合，单位无效: {unitId}");
                return;
            }

            // 新回合重置「每回合限挂 1 张执行牌」标记（在轨引线保留，不受新回合影响）
            _executionHungCountThisTurn.Remove(unitId);
            _chargeStartedThisTurn.Remove(unitId);
            CleanupInvalidPendingCharges();

            // 全场暂停：冻结所有其他单位推进
            CurrentState.IsGlobalPaused = true;

            CurrentRound++;
            CurrentState.CurrentTurnUnitId = unitId;

            playerUnit.CurrentEnergy = Mathf.Max(0, playerUnit.BaseEnergy);
            var energized = playerUnit.GetBuff("Energized");
            if (energized != null)
            {
                int bonusEnergy = Mathf.Max(0, Mathf.RoundToInt(energized.Value));
                playerUnit.CurrentEnergy += bonusEnergy;
                playerUnit.RemoveBuff("Energized");
                Debug.Log($"[BattleManager] {unitId} 消耗充能，获得 {bonusEnergy} 点额外能量");
            }
            playerUnit.FreeMoveUsedThisTurn = false; // 每回合重置「首张移动免费」
            playerUnit.FreePushUsedThisTurn = false; // 每回合重置「首张推迟牌免费」
            playerUnit.HasMovedThisTurn = false;     // 每回合重置「本回合移动过」标记
            if (playerUnit.Overload != null)
            {
                playerUnit.Overload.OverloadCountThisTurn = 0; // 每回合重置过载额度
                playerUnit.Overload.EnergyOverdraftUsedThisTurn = false;
                playerUnit.Overload.IsOverloaded = false;
            }
            if (CurrentState.DeckSystem != null)
            {
                // 新玩家回合开始时替换手牌：避免战斗初始抽牌 + 本回合抽牌叠加，或多角色连续行动时手牌累加
                DiscardCurrentHand();
                DrawCardsForPlayerUnit(playerUnit, Mathf.Max(0, playerUnit.BaseDrawCount));
                // 回合开始注入本角色的基础移动牌（虚无+消耗：打出/未用都自动清除，不污染牌库）
            }

            // 新行动的资源/标记与手牌准备完成后再结算蓄力：过载、能量、抽牌等完成效果会保留在本次行动中。
            ResolvePendingCharge(unitId);

            if (generateEnemyIntentions)
            {
                GenerateEnemyIntentionsForCurrentRound();
            }

            if (PredictionManager != null)
            {
                PredictionManager.TriggerPrediction("回合开始");
            }
            else
            {
                Debug.LogWarning("[BattleManager] PredictionManager为null，无法触发预解算");
            }
        }

        public void StartPlayerTurn(string unitId)
        {
            StartPlayerTurn(unitId, true);
        }

        /// <summary>
        /// 回合开始给该角色注入其基础移动牌（Id = 角色枚举名 + "000"，如 Zhouzhou000）。
        /// 该牌带虚无(IsEthereal)+消耗(IsExhaust)：打出即除、未用则回合末消失，不污染牌库。
        /// </summary>
        private void InjectBasicMoveCard(UnitState playerUnit)
        {
            var cid = playerUnit?.GetCharacterId();
            if (cid == null || CurrentState?.DeckSystem == null)
            {
                return;
            }

            string moveCardId = $"{cid.Value}000";
            if (ConfigLoader.Tables?.TbCardInfo?.GetOrDefault(moveCardId) == null)
            {
                Debug.LogWarning($"[BattleManager] 未找到基础移动牌配置: {moveCardId}，跳过注入");
                return;
            }

            CurrentState.DeckSystem.AddCardToHand(moveCardId, 1);
            Debug.Log($"[BattleManager] 回合开始注入基础移动牌: {moveCardId} -> {playerUnit.UnitId}");
        }

        /// <summary>卡牌是否「带移动」（Effects 含 MovePositionEffect）。</summary>
        private static bool CardHasMoveEffect(cfg.Character.CardInfo card)
        {
            if (card?.Effects != null && card.Effects.Any(e => e is MovePositionEffect || e is MoveSelfEffect || e is MoveRowEffect))
            {
                return true;
            }

            switch (card?.Id)
            {
                case "Zhouzhou013":
                case "Zhouzhou014":
                case "Zhouzhou015":
                case "Zhouzhou018":
                case "Zhouzhou023":
                case "Extra005":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 该 owner 打这张牌是否触发「首张移动免费」（游侠百相 FirstMoveFree）：
        /// 拥有该 Trait + 是移动牌 + 本回合尚未用掉免费额度。
        /// </summary>
        private bool IsFreeMoveForOwner(UnitState owner, cfg.Character.CardInfo card)
        {
            if (owner == null || owner.FreeMoveUsedThisTurn || !CardHasMoveEffect(card))
            {
                return false;
            }

            var info = owner.GetCharacterInfo();
            return info != null && info.Trait == "FirstMoveFree";
        }

        private static bool IsCardSpecificTargetValid(cfg.Character.CardInfo card, UnitState owner, UnitState target)
        {
            switch (card?.Id)
            {
                case "Zhouzhou004": // 踏歌
                case "Zhouzhou005": // 挽袖同行
                case "Zhouzhou015": // 雁双飞
                    return target != null && !target.IsDead
                           && target.UnitId != owner?.UnitId
                           && owner.IsPlayerUnit == target.IsPlayerUnit;
                default:
                    return true;
            }
        }

        private bool AreMultiAllyTargetsValid(UnitState owner, string rawTargetIds, int maxCount)
        {
            if (owner == null || string.IsNullOrEmpty(rawTargetIds)) return false;
            var ids = rawTargetIds.Split('|').Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            if (ids.Count == 0 || ids.Count > maxCount) return false;
            return ids.All(id =>
            {
                var target = CurrentState.GetUnitById(id);
                return target != null && !target.IsDead && target.IsPlayerUnit == owner.IsPlayerUnit;
            });
        }

        private static bool CardHasPushEffect(cfg.Character.CardInfo card)
        {
            return card?.Effects != null && card.Effects.Any(e => e is PushCollisionEffect);
        }

        /// <summary>战士百相 FirstPushFree：本回合第一张单体推迟牌费用为 0。</summary>
        private bool IsFreePushForOwner(UnitState owner, cfg.Character.CardInfo card)
        {
            if (owner == null || owner.FreePushUsedThisTurn || !CardHasPushEffect(card))
            {
                return false;
            }

            var info = owner.GetCharacterInfo();
            return info != null && info.Trait == "FirstPushFree";
        }

        /// <summary>法师百相 DoubleExecution：每回合可挂 2 张执行牌；其他角色保持 1 张。</summary>
        private static int GetExecutionLimitForOwner(UnitState owner)
        {
            var info = owner?.GetCharacterInfo();
            return info != null && info.Trait == "DoubleExecution" ? 2 : 1;
        }

        /// <summary>
        /// 卡牌对指定施法者的「有效能量费用」：命中游侠百相 FirstMoveFree（本回合首张移动牌）时为 0，否则为基础费用。
        /// 供 UI 显示卡面费用 / 判定是否打得起时调用，保证显示与实际扣费一致。
        /// </summary>
        public int GetEffectiveEnergyCost(cfg.Character.CardInfo card, string ownerUnitId)
        {
            return GetEffectiveEnergyCost(card, ownerUnitId, null);
        }

        public int GetEffectiveEnergyCost(cfg.Character.CardInfo card, string ownerUnitId, string instanceId)
        {
            if (card == null)
            {
                return 0;
            }

            int baseCost = GetCardEnergyCost(card);
            var instance = FindHandCardInstance(instanceId);
            if (instance != null && instance.EnergyOverride >= 0)
            {
                baseCost = instance.EnergyOverride;
            }
            var owner = CurrentState?.GetUnitById(ownerUnitId);
            if (owner != null && CardHasMoveEffect(card)
                && CurrentState?.MoveCardCostOverrideByOwner != null
                && CurrentState.MoveCardCostOverrideByOwner.TryGetValue(ownerUnitId, out int overrideCost))
            {
                baseCost = Mathf.Max(0, overrideCost);
            }
            if (owner != null && (IsFreeMoveForOwner(owner, card) || IsFreePushForOwner(owner, card)))
            {
                baseCost = 0;
            }

            // 解签本身固定为 1 费，不受自身影响。只要手牌中存在至少一张解签，
            // 其余所有卡牌在各类覆盖/百相减费结算后统一 +1；多张解签不叠加。
            bool hasDivinationCurse = CurrentState?.DeckSystem?.Hand?.Any(
                handCard => handCard != null && handCard.CardId == DivinationCurseCardId) == true;
            if (hasDivinationCurse && card.Id != DivinationCurseCardId)
            {
                baseCost += 1;
            }

            return Mathf.Max(0, baseCost);
        }

        private CardRuntimeState FindHandCardInstance(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return null;
            return CurrentState?.DeckSystem?.Hand?.FirstOrDefault(card => card != null && card.InstanceId == instanceId);
        }

        private CardResolutionContext BuildCardResolutionContext(string instanceId)
        {
            var instance = FindHandCardInstance(instanceId);
            return instance != null && instance.IsFlashback
                ? new CardResolutionContext { SuppressMoveHistory = true }
                : null;
        }

        /// <summary>
        /// 玩家手动结束回合：结算执行牌 → 弃手牌 → 解除全场暂停 → 重置ATB
        /// </summary>
        public void OnPlayerEndTurn(string unitId)
        {
            if (CurrentState == null || string.IsNullOrEmpty(unitId))
            {
                return;
            }

            var playerUnit = CurrentState.GetUnitById(unitId);
            if (playerUnit == null || playerUnit.IsDead || !playerUnit.IsPlayerUnit)
            {
                return;
            }

            // 1. 执行牌不在此结算——已作为引线挂在 ATB 时钟上，到 ResolveRound 由 ResolvePendingCast 结算。

            // 2. 弃掉剩余手牌
            DiscardCurrentHand();

            // 3. 清除回合标记
            CurrentState.CurrentTurnUnitId = null;

            // 4. 解除全场暂停（重排由 ATB 公共回合调度负责，不再重置行动条段位）
            CurrentState.IsGlobalPaused = false;

            Debug.Log($"[BattleManager] 玩家回合结束，全场恢复: {unitId}");
        }

        /// <summary>
        /// 开始指定敌方单位回合
        /// </summary>
        public void StartEnemyTurn(string unitId)
        {
            if (CurrentState == null)
            {
                Debug.LogError("[BattleManager] 无法开始敌人回合：当前战斗状态为null");
                return;
            }

            var enemyUnit = CurrentState.GetUnitById(unitId);
            if (enemyUnit == null || enemyUnit.IsDead || enemyUnit.IsPlayerUnit)
            {
                Debug.LogWarning($"[BattleManager] 无法开始敌人回合，单位无效: {unitId}");
                return;
            }

            CleanupInvalidPendingCharges();
            CurrentState.CurrentTurnUnitId = unitId;
            enemyUnit.CurrentEnergy = Mathf.Max(0, enemyUnit.BaseEnergy);
            Debug.Log($"[BattleManager] 敌人回合开始（能量已刷新，伤害等在执行轨结束时结算）: {unitId}, 能量={enemyUnit.CurrentEnergy}");
        }

        /// <summary>
        /// 结算指定单位「回合开始」的持续性 Buff（中毒/燃烧/再生）：掉血/回血 + 数值衰减。
        /// ATB 原子回合起点调用（当前用于敌人回合开始，见 ResolveEnemyAtomicTurn）。
        /// </summary>
        /// <returns>该单位是否在结算后死亡（调用方据此中止其本回合后续行动）。</returns>
        public bool ProcessTurnStartBuffs(string unitId)
        {
            var unit = CurrentState?.GetUnitById(unitId);
            if (unit == null || unit.IsDead) return false;

            TurnResolver.ProcessTurnStartBuffs(CurrentState, unit);
            CurrentState.CheckBattleEnd();
            return unit.IsDead;
        }

        /// <summary>
        /// 敌人ATB到达行动点后：选定技能，进入意图轴
        /// 替代旧的 TryPrepareEnemyIntentAfterPlanning + 等待执行轨 的流程
        /// </summary>
        /// <returns>是否成功进入意图轴</returns>
        public bool StartEnemyIntentAxis(string unitId, out EnemySkillInfo preparedSkill, out string targetUnitId)
        {
            preparedSkill = null;
            targetUnitId = null;

            if (CurrentState == null)
            {
                return false;
            }

            var enemyUnit = CurrentState.GetUnitById(unitId);
            if (enemyUnit == null || enemyUnit.IsDead || enemyUnit.IsPlayerUnit)
            {
                return false;
            }

            if (!TryPickEnemySkillAndTarget(enemyUnit, out var selectedSkill, out var target) || target == null)
            {
                // target 可能为 null（目标区空排未锁人）；旧意图轴路径不支持空载体，直接放弃本次准备。
                return false;
            }

            int intentAxisLength = Mathf.Max(1, selectedSkill.ExecutingCost);
            int executeAxisLength = 1;

            EnemyIntentAxisResolver.StartIntentAxis(
                enemyUnit, selectedSkill.Id, target.UnitId,
                intentAxisLength, executeAxisLength
            );

            // 自施型 Buff（如 Stagger 破韧）在进入意图轴时立刻挂给施法者本身，
            // 使整段引导期内玩家都能通过造成伤害打断该次施法。
            ApplySelfCastBuffsOnIntentStart(enemyUnit, selectedSkill);

            // 同时存入旧字典以保持向后兼容（UI层可能仍在读取）
            _pendingEnemyIntents[unitId] = (selectedSkill, target.UnitId);

            preparedSkill = selectedSkill;
            targetUnitId = target.UnitId;
            return true;
        }

        /// <summary>
        /// 每 tick 推进所有敌人的意图轴/执行轴，并对完成的敌人执行技能效果
        /// UI 层在主循环中调用此方法
        /// </summary>
        /// <returns>本 tick 内触发了技能效果的敌人ID列表</returns>
        public List<string> TickEnemyAxes()
        {
            var firedEnemyIds = new List<string>();

            if (CurrentState == null || CurrentState.IsBattleEnded || EnemyIntentAxisResolver == null)
            {
                return firedEnemyIds;
            }

            var completedEnemies = EnemyIntentAxisResolver.AdvanceEnemyAxes(CurrentState);
            foreach (var enemy in completedEnemies)
            {
                if (ExecuteCompletedEnemySkill(enemy))
                {
                    firedEnemyIds.Add(enemy.UnitId);
                }

                // 重置阶段（旧意图轴系统；重排由 ATB 公共回合调度负责）
                EnemyIntentAxisResolver.ResetPhase(enemy);

                if (CurrentState.IsBattleEnded)
                {
                    break;
                }
            }

            return firedEnemyIds;
        }

        /// <summary>
        /// 执行完成执行轴的敌人技能（从 UnitState 中读取待执行数据）
        /// </summary>
        private bool ExecuteCompletedEnemySkill(UnitState enemy)
        {
            if (enemy == null || string.IsNullOrEmpty(enemy.PendingSkillId) || string.IsNullOrEmpty(enemy.PendingTargetId))
            {
                return false;
            }

            // 从旧字典获取完整技能信息
            if (!_pendingEnemyIntents.TryGetValue(enemy.UnitId, out var intent))
            {
                Debug.LogWarning($"[BattleManager] 敌人执行轴完成但未找到待执行技能: {enemy.UnitId}");
                return false;
            }

            var skill = intent.Skill;

            // 动态索敌：目标在「执行这一刻」按 技能分区 + 当前站位 重新解算。
            // 原先 telegraph 的目标若仍在有效区内则继续打它；否则（被移出该区/死亡）改选该区当前的其他人。
            var target = ResolveExecutionTarget(enemy, skill, enemy.PendingTargetId);
            if (target == null)
            {
                // 目标区当前无可选单位——本次施法落空。TODO(索敌): 补 miss 表现（当前 FilterByZone 会回退全体，通常不至于到这里）。
                Debug.Log($"[BattleManager] 敌人 {enemy.UnitId} 技能 {skill?.Id} 执行时目标区无人，落空");
                ClearPendingEnemyIntent(enemy.UnitId);
                return false;
            }

            ClearPendingEnemyIntent(enemy.UnitId);
            ExecuteEnemySkillInternal(enemy, skill, target);

            // 施法完成（未被打断）：清除本次引导挂上的自施型 Buff，避免残留影响后续行动
            foreach (var buffId in EnemySkillToTimelineConverter.SelfCastBuffIds)
            {
                enemy.RemoveBuff(buffId);
            }
            return true;
        }

        /// <summary>
        /// 进入意图轴时，将技能中的自施型 Buff（如 Stagger）挂到施法者本身。
        /// 这些 Buff 不会随技能命中目标（已在 EnemySkillToTimelineConverter 中跳过）。
        /// </summary>
        private void ApplySelfCastBuffsOnIntentStart(UnitState enemyUnit, EnemySkillInfo skill)
        {
            if (enemyUnit == null || skill?.Effects == null)
            {
                return;
            }

            foreach (var effect in skill.Effects)
            {
                if (effect is BuffEffect buffEffect &&
                    EnemySkillToTimelineConverter.SelfCastBuffIds.Contains(buffEffect.BuffId))
                {
                    float value = buffEffect.Value;

                    // [坚毅](Resolve)：每层使打断阈值提高 50%——只放大打断系阈值，不影响其他自施 buff
                    if (buffEffect.BuffId == "Stagger" || buffEffect.BuffId == "Block")
                    {
                        float resolveStacks = enemyUnit.GetBuff("Resolve")?.Value ?? 0f;
                        if (resolveStacks > 0f)
                        {
                            value = Mathf.Ceil(value * (1f + 0.5f * resolveStacks));
                        }
                    }

                    enemyUnit.AddBuff(new BuffState
                    {
                        BuffId = buffEffect.BuffId,
                        Value = value,
                        RemainingDuration = -1 // 持续到被消耗（打断）或施法完成时清除
                    });
                    Debug.Log($"[BattleManager] {enemyUnit.UnitId} 进入意图轴自施 Buff: {buffEffect.BuffId}={value}");
                }
            }
        }

        /// <summary>
        /// 规划轨结束时调用：随机确定本回合技能与目标，供 UI 展示并进入执行轨；不造成伤害。
        /// [旧接口，保留向后兼容，新代码应使用 StartEnemyIntentAxis]
        /// </summary>
        /// <returns>是否成功生成待执行意图</returns>
        public bool TryPrepareEnemyIntentAfterPlanning(string unitId, out int executingCost, out EnemySkillInfo preparedSkill, out string targetUnitId)
        {
            executingCost = 1;
            preparedSkill = null;
            targetUnitId = null;

            if (CurrentState == null)
            {
                return false;
            }

            var enemyUnit = CurrentState.GetUnitById(unitId);
            if (enemyUnit == null || enemyUnit.IsDead || enemyUnit.IsPlayerUnit)
            {
                return false;
            }

            if (!TryPickEnemySkillAndTarget(enemyUnit, out var selectedSkill, out var target))
            {
                return false;
            }

            const int skillEnergyCost = 1;
            if (enemyUnit.CurrentEnergy < skillEnergyCost)
            {
                Debug.LogWarning($"[BattleManager] 敌人能量不足，无法准备技能: {enemyUnit.UnitId}");
                return false;
            }

            // target 可能为 null（预告时目标区暂无人）——意图仍照常公示，命中前 ResolveExecutionTarget 会重算；
            // 此处仅当有具体载体时做一次「技能能生成指令」的健全性检查。
            if (target != null)
            {
                var converter = new EnemySkillToTimelineConverter();
                var blocks = converter.ConvertEnemySkill(selectedSkill, enemyUnit.UnitId, target.UnitId);
                var commandList = blocks
                    .Where(b => b != null && b.Commands != null && b.Commands.Count > 0)
                    .SelectMany(b => b.Commands)
                    .Where(c => c != null)
                    .OrderByDescending(c => c.GetPriority())
                    .ToList();

                if (commandList.Count == 0)
                {
                    Debug.LogWarning($"[BattleManager] 敌人技能没有可执行指令，无法准备: {selectedSkill.Id}");
                    return false;
                }
            }

            // 载体 UnitId 允许为 null（空区预告），命中时按当前站位重算
            targetUnitId = target?.UnitId;
            _pendingEnemyIntents[unitId] = (selectedSkill, targetUnitId);
            preparedSkill = selectedSkill;
            executingCost = Mathf.Max(1, selectedSkill.ExecutingCost);
            return true;
        }

        /// <summary>
        /// 执行轨到达终点时调用：结算待执行的敌人技能（伤害与指令）。
        /// 【动态索敌 + 空排 miss】命中前按当前站位重算目标：
        ///   · 原 telegraph 目标仍在目标区 → 打它；被移出该区/死亡 → 区内改选。
        ///   · 目标区当前无人（整排空）→ 返回 <see cref="EnemyIntentResolveResult.Missed"/>，本次落空不造成任何效果。
        /// </summary>
        public EnemyIntentResolveResult ExecutePendingEnemyAfterExecutionTrack(string unitId)
        {
            if (CurrentState == null || string.IsNullOrEmpty(unitId))
            {
                return EnemyIntentResolveResult.Aborted;
            }

            if (!_pendingEnemyIntents.TryGetValue(unitId, out var intent))
            {
                return EnemyIntentResolveResult.Aborted;
            }

            var enemyUnit = CurrentState.GetUnitById(unitId);
            if (enemyUnit == null || enemyUnit.IsDead)
            {
                ClearPendingEnemyIntent(unitId);
                return EnemyIntentResolveResult.Aborted;
            }

            var skill = intent.Skill;
            var target = ResolveExecutionTarget(enemyUnit, skill, intent.TargetUnitId);
            ClearPendingEnemyIntent(unitId);

            if (target == null)
            {
                // 目标区当前无人 → 打空 miss（不造成任何效果，回合照常结束）
                Debug.Log($"[BattleManager] 敌人 {unitId} 技能 {skill?.Id} 命中时 {skill?.TargetZone} 区空排，打空 miss");
                return EnemyIntentResolveResult.Missed;
            }

            ExecuteEnemySkillInternal(enemyUnit, skill, target);
            return EnemyIntentResolveResult.Hit;
        }

        /// <summary>
        /// 引线到点结算：轮到该 castId 的 ATB 虚拟回合时调用。
        /// 施法者死亡 → 引线作废（规则书：结算前被击倒则取消）；
        /// 原目标死亡/丢失 → 单体敌方牌改选第一个存活敌人，单体友方牌回落到施法者，其余照常（AOE 自展开）。
        /// </summary>
        /// <returns>true = 效果已结算；false = 引线作废（不存在/施法者死亡/无合法目标）。</returns>
        public bool ResolvePendingCast(string castId)
        {
            if (CurrentState == null || !_pendingPlayerCasts.TryGetValue(castId, out var cast) || cast.Card == null)
            {
                _pendingPlayerCasts.Remove(castId);
                return false;
            }

            _pendingPlayerCasts.Remove(castId);

            var owner = CurrentState.GetUnitById(cast.CasterUnitId);
            if (owner == null || owner.IsDead)
            {
                Debug.Log($"[BattleManager] 引线作废（施法者已倒下）: {cast.Card.Name} ({castId})");
                return false;
            }

            var card = cast.Card;
            string targetId = ResolveCastTargetId(card, owner, cast.TargetUnitId);
            if (string.IsNullOrEmpty(targetId))
            {
                Debug.Log($"[BattleManager] 引线落空（无合法目标）: {card.Name} ({castId})");
                return false;
            }

            var context = new CardResolutionContext
            {
                AddedDelay = cast.AddedDelay,
                DamageBonus = cast.DamageBonus,
                NumericOnly = cast.NumericOnly,
                NumericScale = cast.NumericScale <= 0f ? 1f : cast.NumericScale
            };
            var commands = CardPlayResolver.GenerateCommands(
                card, CurrentState?.CardModifiers?.Get(card.Id), 1, context);
            bool isAttackCard = commands.Any(IsAttackCommand);
            GameEvent.Publish(new CardExecutedEvent
            {
                CasterId = owner.UnitId,
                TargetId = targetId,
                CardId = card.Id,
                IsAttackCard = isAttackCard,
                IsPrediction = false,
                SkipBattleAnimation = false,
                UseCenterStage = true // 玩家执行牌结算：中央舞台演出
            });

            bool success = CardPlayResolver.PlayCard(CurrentState, card, owner.UnitId, targetId, context);
            if (!success)
            {
                Debug.LogWarning($"[BattleManager] 引线结算失败: {card.Id} ({castId})");
                return false;
            }

            if (cast.ResolveDrawCount > 0)
                new DrawCommand(cast.ResolveDrawCount).Execute(CurrentState, owner.UnitId, owner.UnitId);
            if (!string.IsNullOrEmpty(cast.ResolveBuffId) && cast.ResolveBuffValue > 0f)
                new BuffCommand(cast.ResolveBuffId, cast.ResolveBuffValue).Execute(CurrentState, owner.UnitId, owner.UnitId);

            if (!cast.NumericOnly && cast.EchoDelay > 0 && cast.EchoMultiplier > 0f)
            {
                _castSequence++;
                var echo = new PendingPlayerCast
                {
                    CastId = $"{CastIdPrefix}{_castSequence}_{card.Id}_echo",
                    Sequence = _castSequence,
                    CasterUnitId = cast.CasterUnitId,
                    Card = card,
                    TargetUnitId = cast.TargetUnitId,
                    ResolveRound = CurrentState.CurrentRound + cast.EchoDelay,
                    AddedDelay = cast.AddedDelay,
                    DamageBonus = cast.DamageBonus,
                    NumericOnly = true,
                    NumericScale = cast.EchoMultiplier
                };
                _pendingPlayerCasts[echo.CastId] = echo;
                Debug.Log($"[BattleManager] 回声挂轨: {echo.CastId} 将于公共回合 {echo.ResolveRound} 结算");
            }

            CurrentState.CheckBattleEnd();
            ResolveQueuedImmediateCasts();

            if (PredictionManager != null)
            {
                PredictionManager.TriggerPrediction("玩家引线结算");
            }

            return true;
        }

        private bool ResolvePendingCharge(string ownerId)
        {
            if (!_pendingPlayerCharges.TryGetValue(ownerId, out var charge) || charge?.Card == null)
            {
                return false;
            }

            _pendingPlayerCharges.Remove(ownerId);
            var owner = CurrentState?.GetUnitById(ownerId);
            RemoveChargeWhileBuffs(charge, owner);
            if (owner == null || owner.IsDead)
            {
                return false;
            }

            string targetId = ResolveCastTargetId(charge.Card, owner, charge.TargetUnitId);
            if (string.IsNullOrEmpty(targetId))
            {
                Debug.Log($"[BattleManager] 蓄力完成但没有合法目标: {charge.Card.Id}");
                return false;
            }

            int chargeLevel = Mathf.Max(1, CurrentState.CurrentRound - charge.StartRound);
            var commands = CardPlayResolver.GenerateCommands(
                charge.Card, CurrentState.CardModifiers?.Get(charge.Card.Id), chargeLevel);
            GameEvent.Publish(new CardExecutedEvent
            {
                CasterId = ownerId,
                TargetId = targetId,
                CardId = charge.Card.Id,
                IsAttackCard = commands.Any(IsAttackCommand),
                IsPrediction = false,
                SkipBattleAnimation = false,
                UseCenterStage = true
            });

            bool success = CardPlayResolver.PlayEffects(
                CurrentState, charge.Card, charge.Card.Effects, ownerId, targetId, chargeLevel);
            CurrentState.CheckBattleEnd();
            PredictionManager?.TriggerPrediction("蓄力完成");
            Debug.Log($"[BattleManager] 蓄力完成: {charge.Card.Id}, 层数={chargeLevel}");
            return success;
        }

        private static IEnumerable<string> GetChargeWhileBuffIds(cfg.Character.CardInfo card)
        {
            if (card?.ChargeWhileEffects == null)
            {
                yield break;
            }

            foreach (var effect in card.ChargeWhileEffects)
            {
                if (effect is TauntEffect)
                {
                    yield return "Taunt";
                }
                else if (effect is BuffEffect buffEffect && !string.IsNullOrEmpty(buffEffect.BuffId))
                {
                    yield return buffEffect.BuffId;
                }
            }
        }

        private static void CaptureChargeWhileBuffs(PendingPlayerCharge charge, UnitState owner)
        {
            if (charge == null || owner == null)
            {
                return;
            }

            foreach (string buffId in GetChargeWhileBuffIds(charge.Card).Distinct())
            {
                charge.PreviousWhileBuffs[buffId] = owner.GetBuff(buffId)?.Clone();
            }
        }

        private static void RemoveChargeWhileBuffs(PendingPlayerCharge charge, UnitState owner)
        {
            if (charge == null || owner == null)
            {
                return;
            }

            foreach (string buffId in GetChargeWhileBuffIds(charge.Card).Distinct())
            {
                owner.RemoveBuff(buffId);
                if (charge.PreviousWhileBuffs.TryGetValue(buffId, out var previous) && previous != null)
                {
                    owner.AddBuff(previous.Clone());
                }
            }
        }

        private void CleanupInvalidPendingCharges()
        {
            if (CurrentState == null || _pendingPlayerCharges.Count == 0)
            {
                return;
            }

            var invalid = _pendingPlayerCharges
                .Where(kv => CurrentState.GetUnitById(kv.Key) == null || CurrentState.GetUnitById(kv.Key).IsDead)
                .Select(kv => kv.Key)
                .ToList();
            foreach (string ownerId in invalid)
            {
                var owner = CurrentState.GetUnitById(ownerId);
                RemoveChargeWhileBuffs(_pendingPlayerCharges[ownerId], owner);
                _pendingPlayerCharges.Remove(ownerId);
            }
        }

        public void CancelPendingCharges(string casterUnitId = null)
        {
            var targets = _pendingPlayerCharges
                .Where(kv => string.IsNullOrEmpty(casterUnitId) || kv.Key == casterUnitId)
                .Select(kv => kv.Key)
                .ToList();
            foreach (string ownerId in targets)
            {
                RemoveChargeWhileBuffs(_pendingPlayerCharges[ownerId], CurrentState?.GetUnitById(ownerId));
                _pendingPlayerCharges.Remove(ownerId);
            }
        }

        /// <summary>
        /// 引线结算时的目标兜底：原目标仍合法则沿用；否则单体敌方牌改选第一个存活敌人，
        /// 友方/自身牌回落到施法者（AOE/全体牌的目标由 Command 自行展开，主目标仅作锚点）。
        /// </summary>
        private string ResolveCastTargetId(cfg.Character.CardInfo card, UnitState owner, string originalTargetId)
        {
            var target = CurrentState.GetUnitById(originalTargetId);
            if (target != null && !target.IsDead)
            {
                return originalTargetId;
            }

            bool targetsEnemy = card.TargetType == cfg.TargetTypeEnum.SingleEnemy
                                || card.TargetType == cfg.TargetTypeEnum.AllEnemy;
            if (targetsEnemy)
            {
                var fallback = CurrentState.GetAliveEnemyUnits().FirstOrDefault();
                if (fallback != null)
                {
                    Debug.Log($"[BattleManager] 引线目标已死亡，改选 {fallback.UnitId}: {card.Name}");
                    return fallback.UnitId;
                }
                return null;
            }

            // 友方/自身牌：目标丢失时回落到施法者
            return owner.UnitId;
        }

        private void ClearPendingEnemyIntent()
        {
            _pendingEnemyIntents.Clear();
        }

        private void ClearPendingEnemyIntent(string unitId)
        {
            if (!string.IsNullOrEmpty(unitId))
                _pendingEnemyIntents.Remove(unitId);
        }

        public bool HasPendingEnemyIntent(string unitId)
        {
            return !string.IsNullOrEmpty(unitId) && _pendingEnemyIntents.ContainsKey(unitId);
        }

        /// <summary>取当前待执行敌人意图的技能与锁定目标（targetUnitId 可能为 null）。</summary>
        public bool TryGetPendingEnemyIntent(string unitId, out EnemySkillInfo skill, out string targetUnitId)
        {
            skill = null;
            targetUnitId = null;
            if (string.IsNullOrEmpty(unitId) || !_pendingEnemyIntents.TryGetValue(unitId, out var intent))
            {
                return false;
            }
            skill = intent.Skill;
            targetUnitId = intent.TargetUnitId;
            return true;
        }

        /// <summary>
        /// 【动态索敌·预告期】重解所有待执行敌人意图的锁定目标（玩家移动站位后调用，保持预告诚实）。
        /// 规则（复用 <see cref="ResolveExecutionTarget"/> 口径）：
        ///   · 原锁定目标仍存活且仍在目标区 → 保持不变；
        ///   · 否则按当前站位在目标区重新随机锁定；目标区空排 → 清空锁定（targetUnitId=null）。
        /// 返回本次发生变化的敌人（UnitId → 新目标 UnitId，可能为 null），供 UI 刷新意图指示 / 抛物线。
        /// </summary>
        public Dictionary<string, string> RefreshPendingEnemyIntentTargets()
        {
            var changed = new Dictionary<string, string>();
            if (CurrentState == null || _pendingEnemyIntents.Count == 0)
            {
                return changed;
            }

            // 先快照 key，避免遍历中改字典
            var enemyIds = _pendingEnemyIntents.Keys.ToList();
            foreach (var enemyId in enemyIds)
            {
                if (!_pendingEnemyIntents.TryGetValue(enemyId, out var intent))
                {
                    continue;
                }

                var enemy = CurrentState.GetUnitById(enemyId);
                if (enemy == null || enemy.IsDead || intent.Skill == null)
                {
                    continue;
                }

                string oldTarget = intent.TargetUnitId;
                var resolved = ResolveExecutionTarget(enemy, intent.Skill, oldTarget); // 有效则保持、失效则区内改选、空区返回 null
                string newTarget = resolved?.UnitId;

                if (newTarget != oldTarget)
                {
                    _pendingEnemyIntents[enemyId] = (intent.Skill, newTarget);
                    changed[enemyId] = newTarget;
                }
            }

            return changed;
        }

        /// <summary>
        /// 取消在轨引线：casterUnitId 为空 = 全部取消（战斗初始化）；否则只取消该施法者的（施法者死亡时）。
        /// 返回被取消的 castId 列表，供 UI 同步移除 ATB 图标。
        /// </summary>
        public List<string> CancelPendingCasts(string casterUnitId = null)
        {
            var removed = new List<string>();
            foreach (var kv in _pendingPlayerCasts)
            {
                if (string.IsNullOrEmpty(casterUnitId) || kv.Value.CasterUnitId == casterUnitId)
                {
                    removed.Add(kv.Key);
                }
            }

            foreach (var id in removed)
            {
                _pendingPlayerCasts.Remove(id);
                _queuedImmediateCastIds.Remove(id);
            }

            if (string.IsNullOrEmpty(casterUnitId))
                _queuedImmediateCastIds.Clear();

            if (removed.Count > 0)
            {
                Debug.Log($"[BattleManager] 取消引线 {removed.Count} 条 (caster={casterUnitId ?? "ALL"})");
            }
            return removed;
        }

        private bool TryPickEnemySkillAndTarget(UnitState enemyUnit, out EnemySkillInfo selectedSkill, out UnitState target)
        {
            selectedSkill = null;
            target = null;

            if (CurrentState == null || enemyUnit == null || enemyUnit.IsDead)
            {
                return false;
            }

            var enemyInfo = ConfigLoader.Tables?.TbEnemyInfo?.GetOrDefault(enemyUnit.ConfigId);
            if (enemyInfo == null || enemyInfo.IntentionSet == null || enemyInfo.IntentionSet.Count == 0)
            {
                Debug.LogWarning($"[BattleManager] 敌人缺少意图配置，跳过行动: {enemyUnit.ConfigId}");
                return false;
            }

            // 出招选择（docs/敌人压迫感设计_v1.md §2/§3）：
            //   A* 槽 = 固定循环，按表序轮转（每单位独立游标）——玩家两次遭遇就能背出节奏，意图是契约；
            //   S* 槽 = 条件插队，HP<50% 时优先施放，每场每槽只触发一次，之后回到循环。
            var rotationSkills = new List<EnemySkillInfo>();
            var conditionalSlots = new List<(string SlotKey, EnemySkillInfo Skill)>();
            foreach (var intentionGroup in enemyInfo.IntentionSet)
            {
                if (intentionGroup?.EnemyIntentionList == null)
                {
                    continue;
                }

                foreach (var intention in intentionGroup.EnemyIntentionList)
                {
                    if (intention?.EnemySkillIndex_Ref == null)
                    {
                        continue;
                    }

                    bool isConditional = intention.EnemyIntentionType == cfg.EnemyIntentionEnum.Skill0
                                         || intention.EnemyIntentionType == cfg.EnemyIntentionEnum.Skill1;
                    if (isConditional)
                    {
                        conditionalSlots.Add(($"{enemyUnit.UnitId}:{intention.EnemyIntentionType}", intention.EnemySkillIndex_Ref));
                    }
                    else
                    {
                        rotationSkills.Add(intention.EnemySkillIndex_Ref);
                    }
                }
            }

            if (rotationSkills.Count == 0 && conditionalSlots.Count == 0)
            {
                Debug.LogWarning($"[BattleManager] 敌人没有可用技能，跳过行动: {enemyUnit.ConfigId}");
                return false;
            }

            bool belowHalfHp = enemyUnit.CurrentHp * 2 < enemyUnit.MaxHp;
            if (belowHalfHp)
            {
                foreach (var (slotKey, skill) in conditionalSlots)
                {
                    if (_firedConditionalSlots.Contains(slotKey))
                    {
                        continue;
                    }
                    _firedConditionalSlots.Add(slotKey);
                    selectedSkill = skill;
                    Debug.Log($"[BattleManager] {enemyUnit.UnitId} HP<50% 触发条件槽插队: {skill.Id}");
                    break;
                }
            }

            if (selectedSkill == null)
            {
                // 只配了 S 槽的敌人（异常配置）：未触发条件时退化为循环使用 S 槽技能
                var pool = rotationSkills.Count > 0
                    ? rotationSkills
                    : conditionalSlots.ConvertAll(c => c.Skill);
                int cursor = _enemyRotationIndex.TryGetValue(enemyUnit.UnitId, out var idx) ? idx : 0;
                selectedSkill = pool[cursor % pool.Count];
                _enemyRotationIndex[enemyUnit.UnitId] = cursor + 1;
            }

            if (selectedSkill == null)
            {
                return false;
            }

            // 目标选择：先按 TargetZone 过滤到目标分区（strict：空区不回退全体），再随机锁一个「承载 UnitId」。
            //   SingleEnemy（单体）→ 该载体就是真实受害者
            //   AllEnemy（全体）  → 载体仅用于意图显示/演出，AOE 真正铺开（同样按分区）在 DamageCommand 里
            //   TargetZone: Front/Back 过滤到对应区；Any/Conditional(未实装) 不过滤
            // 【预告即锁定】此处只是宣告意图时的随机锁定；真正命中前会由 ResolveExecutionTarget 按当前站位重算。
            //   目标区当前无人 → target 保持 null（意图照常公示，只是不画抛物线/不点亮坐标），命中时若仍空则 miss。
            var alivePlayers = CurrentState.PlayerUnits
                .Where(u => u != null && !u.IsDead)
                .ToList();
            if (alivePlayers.Count == 0)
            {
                Debug.LogWarning("[BattleManager] 敌人回合未找到可用玩家目标");
                return false;
            }

            // 嘲讽优先：预告阶段也让单体意图锁定嘲讽者（保持预告与命中一致、诚实）
            var tauntLock = GetTauntRedirectTarget(alivePlayers, selectedSkill);
            if (tauntLock != null)
            {
                target = tauntLock;
            }
            else
            {
                var zonePool = ZoneTargeting.FilterByZone(CurrentState, alivePlayers, selectedSkill.TargetZone, strict: true);
                target = zonePool.Count > 0 ? zonePool[Random.Range(0, zonePool.Count)] : null;
            }
            Debug.Log($"[BattleManager] 敌人技能 {selectedSkill.Id} 选定目标 {target?.UnitId ?? "(空区·暂无)"} " +
                      $"(TargetType={selectedSkill.TargetType}, TargetZone={selectedSkill.TargetZone})");
            return true;
        }

        /// <summary>
        /// 【动态索敌】按「技能分区 + 当前站位」重新解算单体载体目标。
        /// · <paramref name="lockedTargetId"/> 指向预告时锁定的目标：若仍存活且仍在该技能的目标区内 → 继续打它（尊重预告）。
        /// · 否则（被玩家移出该区 / 已死亡 / 预告时未锁人）→ 从该区当前存活单位里随机改选。
        /// · 该区当前无人 → 返回 null（空排落空 miss）。
        /// AOE 的真正扩散由 DamageCommand 在执行时按同一分区口径现算，此处返回的载体仅用于演出/事件。
        /// </summary>
        /// <summary>
        /// 嘲讽重定向：若存在存活且带 [嘲讽](Taunt) 的玩家，单体敌人技能强制改打嘲讽者
        /// （**无视分区**——这正是嘲讽「替队友扛下攻击、省去走位」的价值）。群体/自施技能不受影响；无嘲讽者返回 null。
        /// 多名嘲讽者时取第一个（起手体系里同时只会有一个战士上嘲讽）。
        /// </summary>
        private UnitState GetTauntRedirectTarget(List<UnitState> alivePlayers, EnemySkillInfo skill)
        {
            if (skill == null || skill.TargetType != TargetTypeEnum.SingleEnemy || alivePlayers == null)
            {
                return null;
            }
            return alivePlayers.FirstOrDefault(u => u != null && !u.IsDead && u.HasBuff("Taunt"));
        }

        /// <summary>
        /// 客观回合推进一格（整排 slot = 一个客观回合）时调用：全体存活单位的 [闪避](Dodge) 各掉 1 层。
        /// 由 UI 侧 ATB 的 <c>OnObjectiveRoundAdvanced</c> 事件触发——即 TriggerNextUnit 中「所有单位 Slots 一起减 minSlots」
        /// （minSlots &gt; 0，跨入下一排 slot）的那一刻；同格内多个单位连续行动 **不** 重复扣。
        /// 这对齐「掉层按客观回合、不按某个角色自己的回合」的口径。
        /// （闪避受击免伤在 <see cref="UnitState.TakeDamage"/> 里单独结算，与此处的自然衰减相互独立。）
        /// </summary>
        public void DecayAllDodgeOneObjectiveRound()
        {
            if (CurrentState == null)
            {
                return;
            }

            var affected = new List<UnitState>();
            affected.AddRange(CurrentState.GetAlivePlayerUnits());
            affected.AddRange(CurrentState.GetAliveEnemyUnits());

            foreach (var unit in affected)
            {
                if (unit == null || unit.IsDead)
                {
                    continue;
                }
                var dodge = unit.GetBuff("Dodge");
                if (dodge == null)
                {
                    continue;
                }
                dodge.Value -= 1f;
                dodge.StackCount = Mathf.Max(0, Mathf.RoundToInt(dodge.Value));
                if (dodge.Value <= 0f)
                {
                    unit.RemoveBuff("Dodge");
                }
                Debug.Log($"[BattleManager] 客观回合 +1：{unit.UnitId} 闪避 -1 (剩余 {Mathf.Max(0, Mathf.RoundToInt(dodge.Value))})");
            }
        }

        private UnitState ResolveExecutionTarget(UnitState enemy, EnemySkillInfo skill, string lockedTargetId)
        {
            if (CurrentState == null || skill == null)
            {
                return null;
            }

            var alivePlayers = CurrentState.PlayerUnits
                .Where(u => u != null && !u.IsDead)
                .ToList();
            if (alivePlayers.Count == 0)
            {
                return null;
            }

            // 嘲讽优先：有存活嘲讽者时，单体技能强制打它（无视分区，命中重算与预告口径一致）
            var tauntTarget = GetTauntRedirectTarget(alivePlayers, skill);
            if (tauntTarget != null)
            {
                return tauntTarget;
            }

            // strict：目标区当前无人 → 返回空 → 上层判定为 miss（不回退全体）
            var zonePool = ZoneTargeting.FilterByZone(CurrentState, alivePlayers, skill.TargetZone, strict: true);
            if (zonePool.Count == 0)
            {
                return null;
            }

            // 原 telegraph 目标仍在有效区内 → 继续打它（lockedTargetId 可能为 null，即预告时空区未锁人）
            var locked = CurrentState.GetUnitById(lockedTargetId);
            if (locked != null && !locked.IsDead && zonePool.Contains(locked))
            {
                return locked;
            }

            // 目标已移出该区 / 死亡 / 未锁人 → 按当前站位随机改选
            var reselected = zonePool[Random.Range(0, zonePool.Count)];
            Debug.Log($"[BattleManager] 动态索敌重选：{enemy.UnitId} 的技能 {skill.Id} 原锁定目标 {lockedTargetId ?? "(无)"} 不在 {skill.TargetZone} 区，改打 {reselected.UnitId}");
            return reselected;
        }

        private void ExecuteEnemySkillInternal(UnitState enemyUnit, EnemySkillInfo selectedSkill, UnitState target)
        {
            if (CurrentState == null || enemyUnit == null || target == null || selectedSkill == null)
            {
                return;
            }

            const int skillEnergyCost = 1;
            if (enemyUnit.CurrentEnergy < skillEnergyCost)
            {
                Debug.LogWarning($"[BattleManager] 敌人能量不足，无法执行技能: {enemyUnit.UnitId}");
                return;
            }

            var converter = new EnemySkillToTimelineConverter();
            var blocks = converter.ConvertEnemySkill(selectedSkill, enemyUnit.UnitId, target.UnitId);
            var commands = blocks
                .Where(b => b != null && b.Commands != null && b.Commands.Count > 0)
                .SelectMany(b => b.Commands)
                .Where(c => c != null)
                .OrderByDescending(c => c.GetPriority())
                .ToList();

            if (commands.Count == 0)
            {
                Debug.LogWarning($"[BattleManager] 敌人技能没有可执行指令: {selectedSkill.Id}");
                return;
            }

            bool isAttackSkill = commands.Any(IsAttackCommand);
            GameEvent.Publish(new CardExecutedEvent
            {
                CasterId = enemyUnit.UnitId,
                TargetId = target.UnitId,
                CardId = selectedSkill.Id,
                IsAttackCard = isAttackSkill,
                IsPrediction = false,
                UseCenterStage = true // 敌人技能执行：中央舞台演出
            });

            enemyUnit.CurrentEnergy -= skillEnergyCost;
            foreach (var command in commands)
            {
                command.Execute(CurrentState, enemyUnit.UnitId, target.UnitId);
                if (CurrentState.IsBattleEnded)
                {
                    break;
                }
            }

            CurrentState.CheckBattleEnd();

            if (PredictionManager != null)
            {
                PredictionManager.TriggerPrediction("敌人执行轨结算");
            }
        }

        public void EndCurrentTurn()
        {
            if (CurrentState == null)
            {
                return;
            }

            CurrentState.CurrentTurnUnitId = null;

            // 回合内移动触发器（铁蒺藜/隧穿效应）与全局移动计数只存活一个原子回合
            if (CurrentState.MoveTriggers != null && CurrentState.MoveTriggers.Count > 0)
            {
                Debug.Log($"[BattleManager] 回合结束，清空移动触发器 x{CurrentState.MoveTriggers.Count}");
                CurrentState.MoveTriggers.Clear();
            }
            CurrentState.MovesThisTurn = 0;
            CurrentState.MovesByUnitThisTurn?.Clear();
            CurrentState.LastMoveMainCardByOwner?.Clear();
            CurrentState.MoveCardCostOverrideByOwner?.Clear();

            // 安全兜底：确保全场暂停被解除（兼容旧 UI 调用路径）
            if (CurrentState.IsGlobalPaused)
            {
                CurrentState.IsGlobalPaused = false;
            }
        }

        public void DiscardCurrentHand()
        {
            if (CurrentState?.DeckSystem == null)
            {
                return;
            }

            CurrentState.DeckSystem.DiscardAllHand();
        }

        private void DrawCardsForPlayerUnit(UnitState playerUnit, int drawCount)
        {
            if (CurrentState?.DeckSystem == null || playerUnit == null || drawCount <= 0)
            {
                return;
            }

            var deck = CurrentState.DeckSystem;
            var characterId = playerUnit.GetCharacterId();
            if (!characterId.HasValue)
            {
                deck.DrawCard(drawCount);
                return;
            }

            deck.DrawCardForCharacter(characterId.Value, drawCount);
        }

        private int GetCardEnergyCost(cfg.Character.CardInfo card)
        {
            return card == null ? 0 : card.Energy;
        }

        private static bool IsAttackCommand(ICommand command)
        {
            return command is DamageCommand
                   || command is AttackExtraCommand
                   || command is AttackConditionalCommand
                   || command is AttackCurrentRoundCommand
                   || command is WeatherConditionalDamageCommand
                   || command is RepeatAttackByOwnMoveCommand;
        }

        private void GenerateEnemyIntentionsForCurrentRound()
        {
            // 确保敌人共享时间轴存在（不清空，保留上回合未执行完的技能）
            if (CurrentState.SharedEnemyTrack == null)
            {
                CurrentState.SharedEnemyTrack = new TimelineTrack();
            }
            // 注意：不再调用 Clear()，让已放置的敌人技能继续在时间轴上推进直到执行完毕

            // 敌人技能转换器
            var converter = new EnemySkillToTimelineConverter();

            // 计算本回合应该出现的意图时间点
            // 公式：(10 + CurrentRound) % 10 == TimeSlot 时，该意图本回合出现
            int triggerTimeSlot = (TimelineTrack.TrackLength + CurrentRound) % TimelineTrack.TrackLength;

            // 遍历所有敌人单位
            foreach (var enemyUnit in CurrentState.EnemyUnits)
            {
                if (enemyUnit.IsDead)
                {
                    continue;
                }

                // 从配置表获取敌人信息
                var enemyInfo = ConfigLoader.Tables.TbEnemyInfo.GetOrDefault(enemyUnit.ConfigId);
                if (enemyInfo == null)
                {
                    Debug.LogWarning($"[BattleManager] 未找到敌人配置: {enemyUnit.ConfigId}");
                    continue;
                }

                // 检查是否有意图集合
                if (enemyInfo.IntentionSet == null || enemyInfo.IntentionSet.Count == 0)
                {
                    Debug.LogWarning($"[BattleManager] 敌人 {enemyInfo.Name} 没有意图集合");
                    continue;
                }

                // 遍历所有意图集合，找到本回合应该触发的意图
                foreach (var intentionGroup in enemyInfo.IntentionSet)
                {
                    if (intentionGroup == null || intentionGroup.EnemyIntentionList == null)
                        continue;

                    foreach (var intention in intentionGroup.EnemyIntentionList)
                    {
                        if (intention == null)
                            continue;

                        // 检查意图的TimeSlot是否与本回合触发时间点匹配
                        if (intention.TimeSlot != triggerTimeSlot)
                        {
                            continue;
                        }

                        // 检查技能引用是否已解析
                        if (intention.EnemySkillIndex_Ref == null)
                        {
                            Debug.LogWarning($"[BattleManager] 敌人 {enemyInfo.Name} 的意图技能引用未解析: {intention.EnemySkillIndex}");
                            continue;
                        }

                        var skillInfo = intention.EnemySkillIndex_Ref;
                        // 计算技能总长度
                        int totalSlots = skillInfo.ExecutingCost;
                        // 意图出现在时间轴最右方（从右边往左边推进）
                        int placePosition = TimelineTrack.TrackLength - totalSlots;

                        // 将技能转换为 TimelineBlock 列表
                        // 目标选择：在活着的玩家中随机选一个；AOE 由 DamageCommand 内部展开
                        var alivePlayers = CurrentState.PlayerUnits
                            .Where(u => u != null && !u.IsDead)
                            .ToList();
                        string targetId = alivePlayers.Count > 0
                            ? alivePlayers[Random.Range(0, alivePlayers.Count)].UnitId
                            : null;
                        var blocks = converter.ConvertEnemySkill(skillInfo, enemyUnit.UnitId, targetId);

                        // 检查时间轴位置是否可用
                        if (placePosition < 0 || placePosition + totalSlots > TimelineTrack.TrackLength)
                        {
                            Debug.LogWarning($"[BattleManager] 敌人 {enemyInfo.Name} 的技能长度 {totalSlots} 超出时间轴范围，跳过");
                            continue;
                        }

                        // 检查位置是否冲突（从位置0开始）
                        int actualPosition = placePosition;
                        if (!CurrentState.SharedEnemyTrack.CanPlaceCard(actualPosition, totalSlots))
                        {
                            // 尝试向后推移
                            int newSlot = FindAvailableSlot(CurrentState.SharedEnemyTrack, actualPosition, totalSlots);
                            if (newSlot >= 0)
                            {
                                actualPosition = newSlot;
                            }
                            else
                            {
                                Debug.LogWarning($"[BattleManager] 敌人 {enemyInfo.Name} 无法找到可用位置，跳过");
                                continue;
                            }
                        }

                        // 放置到敌人共享时间轴
                        CurrentState.SharedEnemyTrack.PlaceCard(actualPosition, blocks);

                        // 发布敌人意图选择事件（让UI层创建EnemyTimeSlot）
                        // 注意：必须在 PlaceCard 成功后发布，使用 actualPosition 确保 UI 和数据位置一致
                        GameEvent.Publish(new EnemyIntentionSelectedEvent
                        {
                            EnemyUnitId = enemyUnit.UnitId,
                            SkillInfo = skillInfo,
                            TimeSlotPosition = actualPosition,
                            TargetUnitId = targetId
                        });
                    }
                }
            }
        }

        /// <summary>
        /// 查找可用位置（从指定位置开始向后查找）
        /// </summary>
        private int FindAvailableSlot(TimelineTrack track, int startSlot, int slotCount)
        {
            for (int i = startSlot; i <= TimelineTrack.TrackLength - slotCount; i++)
            {
                if (track.CanPlaceCard(i, slotCount))
                {
                    return i;
                }
            }
            return -1; // 未找到可用位置
        }


        /// <summary>
        /// 只弃牌抽牌（不清空时间轴）
        /// 用于回合结束时保留时间轴上已放置的卡牌
        /// </summary>
        public void DiscardHandAndDraw()
        {
            if (CurrentState == null)
            {
                Debug.LogError("[BattleManager] 无法弃牌抽牌：当前战斗状态为null");
                return;
            }

            // 1. 弃掉所有手牌
            CurrentState.DeckSystem.DiscardAllHand();

            // 2. 抽取固定5张新牌（按当前回合玩家所属角色牌堆）
            var turnUnitId = CurrentState.CurrentTurnUnitId;
            if (!string.IsNullOrEmpty(turnUnitId))
            {
                var unit = CurrentState.GetUnitById(turnUnitId);
                var cid = unit?.GetCharacterId();
                if (cid.HasValue)
                {
                    CurrentState.DeckSystem.DrawCardForCharacter(cid.Value, 5);
                    return;
                }
            }

            CurrentState.DeckSystem.DrawCard(5);
        }

        /// <summary>
        /// 解算完整时间轴（协程版本，支持动画）
        /// </summary>
        public IEnumerator ResolveFullTimelineCoroutine()
        {
            if (CurrentState == null)
            {
                Debug.LogError("[BattleManager] 无法解算：当前战斗状态为null");
                yield break;
            }

            yield return Resolver.ResolveFullTimelineCoroutine(CurrentState);
        }

        /// <summary>
        /// 标记动画完成（由UI层调用）
        /// </summary>
        public void SignalAnimationComplete()
        {
            Resolver.SignalAnimationComplete();
        }

        /// <summary>
        /// 前进一步（解算第一格并向前移动时间轴）
        /// 协程版本，支持动画等待
        /// </summary>
        public IEnumerator AdvanceOneStepCoroutine()
        {
            if (CurrentState == null)
            {
                Debug.LogError("[BattleManager] 无法前进：当前战斗状态为null");
                yield break;
            }

            // 1. 收集第一格（索引0）的所有Blocks，用于标记被执行的卡片
            var executedCards = new List<ExecutedCardInfo>();

            // 收集玩家时间轴第一格的卡片
            foreach (var playerUnit in CurrentState.PlayerUnits)
            {
                if (playerUnit.Track != null && !playerUnit.IsDead)
                {
                    var block = playerUnit.Track.GetBlock(0);
                    if (block != null && !block.IsEmpty() && !string.IsNullOrEmpty(block.SourceCardId))
                    {
                        executedCards.Add(new ExecutedCardInfo
                        {
                            SourceCardId = block.SourceCardId,
                            OwnerId = block.OwnerId
                        });
                    }
                }
            }

            // 收集敌人时间轴第一格的技能
            if (CurrentState.SharedEnemyTrack != null)
            {
                var enemyBlock = CurrentState.SharedEnemyTrack.GetBlock(0);
                if (enemyBlock != null && !enemyBlock.IsEmpty() && !string.IsNullOrEmpty(enemyBlock.SourceCardId))
                {
                    executedCards.Add(new ExecutedCardInfo
                    {
                        SourceCardId = enemyBlock.SourceCardId,
                        OwnerId = enemyBlock.OwnerId
                    });
                }
            }

            // 2. 发布事件通知UI标记将被执行的卡片
            if (executedCards.Count > 0)
            {
                GameEvent.Publish(new BeforeTimelineAdvanceEvent
                {
                    ExecutedCards = executedCards
                });
            }

            // 3. 解算第一格（协程版本，支持动画）
            yield return Resolver.ResolveStepCoroutine(CurrentState, 0);

            // 3.5 检查玩家时间轴上即将被移出的卡牌（IsLastBlock），将其移入弃牌堆
            foreach (var playerUnit in CurrentState.PlayerUnits)
            {
                if (playerUnit.Track != null)
                {
                    var block = playerUnit.Track.GetBlock(0);
                    if (block != null && !block.IsEmpty() && block.IsLastBlock && !string.IsNullOrEmpty(block.SourceCardId))
                    {
                        // 这是卡牌的最后一个Block，卡牌执行完毕，移入弃牌堆
                        CurrentState.DeckSystem.FinishPlayingCard(block.SourceCardId);
                    }
                }
            }

            // 4. 向前移动所有时间轴
            // 移动所有玩家时间轴
            foreach (var playerUnit in CurrentState.PlayerUnits)
            {
                if (playerUnit.Track != null)
                {
                    playerUnit.Track.ShiftBlocks(0, -1);
                }
            }

            // 移动敌人共享时间轴
            if (CurrentState.SharedEnemyTrack != null)
            {
                CurrentState.SharedEnemyTrack.ShiftBlocks(0, -1);
            }

            // 5. 发布事件通知UI更新显示
            GameEvent.Publish(new AfterTimelineAdvanceEvent());

            // 6. 打印所有时间轴状态（调试用）
            PrintAllTimelineStatus();
        }

        /// <summary>
        /// 打印所有时间轴的当前状态（调试用）
        /// </summary>
        private void PrintAllTimelineStatus()
        {
            Debug.Log("[Timeline]========== 【回合结束 - 时间轴状态汇总】==========");

            // 打印敌人共享时间轴
            if (CurrentState.SharedEnemyTrack != null)
            {
                Debug.Log($"[Timeline]【敌人共享时间轴】");
                bool hasAnyBlock = false;
                for (int i = 0; i < TimelineTrack.TrackLength; i++)
                {
                    var block = CurrentState.SharedEnemyTrack.GetBlock(i);
                    if (block != null)
                    {
                        hasAnyBlock = true;
                        Debug.Log($"  [Timeline][{i}] {block.SourceCardId} | Owner:{block.OwnerId} | Phase:{block.Phase} | Cmds:{block.Commands?.Count ?? 0}</color>");
                    }
                }
                if (!hasAnyBlock)
                {
                    Debug.Log("  [Timeline](空)");
                }
            }

            Debug.Log("<color=cyan>========== 【时间轴状态汇总结束】==========");
        }

        // ========== ATB 新增方法 ==========

        /// <summary>
        /// ATB：执行指定单位的完整回合
        /// </summary>
        public bool ExecuteUnitTurn(string unitId, List<CardAction> cardActions)
        {
            if (CurrentState == null || CurrentState.IsBattleEnded)
            {
                return false;
            }

            CurrentRound++;
            return TurnResolver.ExecuteTurn(CurrentState, unitId, cardActions);
        }

        /// <summary>
        /// ATB：为当前行动单位执行过载
        /// </summary>
        public bool RequestOverload(string unitId, int bonusEnergy = 2)
        {
            if (CurrentState == null)
            {
                return false;
            }

            var unit = CurrentState.GetUnitById(unitId);
            if (unit == null || unit.IsDead)
            {
                return false;
            }

            return TurnResolver.ProcessOverload(CurrentState, unit, bonusEnergy);
        }

        /// <summary>
        /// ATB：预测卡牌效果
        /// </summary>
        public PredictionResult PredictCardEffect(cfg.Character.CardInfo cardInfo, string ownerId, string targetId)
        {
            if (CurrentState == null || cardInfo == null)
            {
                return new PredictionResult();
            }

            return Predictor.SimulateCard(CurrentState, cardInfo, ownerId, targetId);
        }

        /// <summary>
        /// 获取调试信息
        /// </summary>
        public string GetDebugInfo()
        {
            if (CurrentState == null)
            {
                return "战斗未初始化";
            }

            return $"回合: {CurrentRound}, " +
                   $"玩家单位: {CurrentState.PlayerUnits.Count}, " +
                   $"敌人单位: {CurrentState.EnemyUnits.Count}, " +
                   $"ATB回合数: {CurrentState.TurnCount}, " +
                   $"当前行动: {CurrentState.CurrentTurnUnitId ?? "无"}, " +
                   $"战斗结束: {CurrentState.IsBattleEnded}, " +
                   $"卡组: {CurrentState.DeckSystem?.GetDebugInfo()}";
        }
    }
}
