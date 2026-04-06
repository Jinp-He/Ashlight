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
    /// 战斗管理器
    /// 负责战斗的初始化、状态管理和核心引擎的协调
    /// </summary>
    public class BattleManager : MonoBehaviour
    {
        public static BattleManager Instance { get; private set; }

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
        private readonly Dictionary<string, (EnemySkillInfo Skill, string TargetUnitId)> _pendingEnemyIntents
            = new Dictionary<string, (EnemySkillInfo, string)>();
        private cfg.Character.CardInfo _pendingPlayerExecutionCard;
        private string _pendingPlayerExecutionTargetUnitId;
        private string _pendingPlayerExecutionCasterUnitId;

        // ========== ATB 引擎组件 ==========

        /// <summary>
        /// 行动条推进解算器
        /// </summary>
        public ActionBarResolver ActionBarResolver { get; private set; }

        /// <summary>
        /// 回合解算器
        /// </summary>
        public TurnResolver TurnResolver { get; private set; }

        /// <summary>
        /// 卡牌结算器
        /// </summary>
        public CardPlayResolver CardPlayResolver { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // 初始化 ATB 核心引擎
            ActionBarResolver = new ActionBarResolver();
            CardPlayResolver = new CardPlayResolver();
            TurnResolver = new TurnResolver(CardPlayResolver, ActionBarResolver);
            Predictor = new BattlePredictor();
            PredictionManager = new BattlePredictionManager(this);

            // 保留旧引擎（过渡期兼容）
            Resolver = new TimelineResolver();
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
            ClearPendingPlayerExecution();

            // 1. 创建玩家单位
            CreatePlayerUnits(battleInfo.PlayerCharacters);

            // 2. 创建敌人单位
            CreateEnemyUnits(battleInfo.EncounterId);

            // 3. 初始化卡组系统
            InitializeDeckSystem(battleInfo.PlayerCharacters, battleInfo.InitialDrawCount);

            // 4. 保存初始快照
            SaveInitialSnapshot();

            // 5. 初始化回合数（不立即开始回合，等待UI初始化完成）
            CurrentRound = 0;
        }

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
                    Overload = new OverloadState()
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
                    IsDead = false,
                    Track = null,
                    Speed = Mathf.Max(1, enemyInfo.Speed),
                    BaseEnergy = 2,
                    BaseDrawCount = 0,
                    ActionBar = new ActionBarState(),
                    Overload = new OverloadState()
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

            if (cardInfo.CardType == CardTypeEnum.Execution)
            {
                Debug.LogWarning($"[BattleManager] 执行牌不能走立即结算入口: {cardInfo.Id}");
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

            int energyCost = GetCardEnergyCost(cardInfo);
            if (owner.CurrentEnergy < energyCost)
            {
                Debug.LogWarning($"[BattleManager] 无法立即执行卡牌：能量不足 owner={ownerId}, 当前={owner.CurrentEnergy}, 需求={energyCost}");
                return false;
            }

            // 发布卡牌执行事件（用于 UI 动画触发）
            var commands = CardPlayResolver.GenerateCommands(cardInfo);
            bool isAttackCard = commands.Any(c => c is DamageCommand);
            // 执行牌：仅打出时尚未进入“执行动作”阶段，不播放战斗演出（与 Timeline 解算时的演出区分）
            bool skipBattleAnimation = cardInfo.CardType == CardTypeEnum.Execution;
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
            bool success = CardPlayResolver.PlayCard(CurrentState, cardInfo, ownerId, targetId);

            if (!success)
            {
                Debug.LogWarning($"[BattleManager] 卡牌结算失败: {cardInfo.Id}");
                return false;
            }

            owner.CurrentEnergy -= energyCost;

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

            if (HasPendingPlayerExecutionCard())
            {
                Debug.LogWarning($"[BattleManager] 当前已有玩家挂起的执行牌，无法再次挂起: owner={ownerId}");
                return false;
            }

            var owner = CurrentState.GetUnitById(ownerId);
            var target = CurrentState.GetUnitById(targetId);
            if (owner == null || owner.IsDead || !owner.IsPlayerUnit)
            {
                Debug.LogWarning($"[BattleManager] 无法挂起执行牌：施法者无效 ownerId={ownerId}");
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

            int energyCost = GetCardEnergyCost(cardInfo);
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

            _pendingPlayerExecutionCard = cardInfo;
            _pendingPlayerExecutionTargetUnitId = targetId;
            _pendingPlayerExecutionCasterUnitId = ownerId;

            if (PredictionManager != null)
            {
                PredictionManager.TriggerPrediction("执行牌挂起");
            }

            return true;
        }

        public bool HasPendingPlayerExecutionCard(string unitId = null)
        {
            if (_pendingPlayerExecutionCard == null || string.IsNullOrEmpty(_pendingPlayerExecutionCasterUnitId))
            {
                return false;
            }

            return string.IsNullOrEmpty(unitId) || _pendingPlayerExecutionCasterUnitId == unitId;
        }

        public int GetPendingPlayerExecutionCost(string unitId)
        {
            if (!HasPendingPlayerExecutionCard(unitId))
            {
                return 1;
            }

            return Mathf.Max(1, _pendingPlayerExecutionCard.ExecutingCost);
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

            ClearPendingPlayerExecution(unitId);

            CurrentRound++;
            CurrentState.CurrentTurnUnitId = unitId;

            playerUnit.CurrentEnergy = Mathf.Max(0, playerUnit.BaseEnergy);
            if (CurrentState.DeckSystem != null)
            {
                // 新玩家回合开始时替换手牌：避免战斗初始抽牌 + 本回合抽牌叠加，或多角色连续行动时手牌累加
                DiscardCurrentHand();
                DrawCardsForPlayerUnit(playerUnit, Mathf.Max(0, playerUnit.BaseDrawCount));
            }

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

            CurrentState.CurrentTurnUnitId = unitId;
            enemyUnit.CurrentEnergy = Mathf.Max(0, enemyUnit.BaseEnergy);
            Debug.Log($"[BattleManager] 敌人回合开始（能量已刷新，伤害等在执行轨结束时结算）: {unitId}, 能量={enemyUnit.CurrentEnergy}");
        }

        /// <summary>
        /// 规划轨结束时调用：随机确定本回合技能与目标，供 UI 展示并进入执行轨；不造成伤害。
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

            _pendingEnemyIntents[unitId] = (selectedSkill, target.UnitId);
            preparedSkill = selectedSkill;
            targetUnitId = target.UnitId;
            executingCost = Mathf.Max(1, selectedSkill.ExecutingCost);
            return true;
        }

        /// <summary>
        /// 执行轨到达终点时调用：结算待执行的敌人技能（伤害与指令）。
        /// </summary>
        public bool ExecutePendingEnemyAfterExecutionTrack(string unitId)
        {
            if (CurrentState == null || string.IsNullOrEmpty(unitId))
            {
                return false;
            }

            if (!_pendingEnemyIntents.TryGetValue(unitId, out var intent))
            {
                return false;
            }

            var enemyUnit = CurrentState.GetUnitById(unitId);
            var target = CurrentState.GetUnitById(intent.TargetUnitId);
            if (enemyUnit == null || enemyUnit.IsDead || target == null || target.IsDead)
            {
                ClearPendingEnemyIntent(unitId);
                return false;
            }

            var skill = intent.Skill;
            ClearPendingEnemyIntent(unitId);
            ExecuteEnemySkillInternal(enemyUnit, skill, target);
            return true;
        }

        /// <summary>
        /// 执行轨到达终点时调用：结算玩家挂起的执行牌（效果与动画在此时发生）。
        /// </summary>
        public bool ExecutePendingPlayerCardAfterExecutionTrack(string unitId)
        {
            if (CurrentState == null || string.IsNullOrEmpty(unitId))
            {
                ClearPendingPlayerExecution();
                return false;
            }

            if (_pendingPlayerExecutionCasterUnitId != unitId || _pendingPlayerExecutionCard == null)
            {
                ClearPendingPlayerExecution();
                return false;
            }

            var owner = CurrentState.GetUnitById(unitId);
            var target = CurrentState.GetUnitById(_pendingPlayerExecutionTargetUnitId);
            if (owner == null || owner.IsDead || target == null || target.IsDead)
            {
                ClearPendingPlayerExecution();
                return false;
            }

            var card = _pendingPlayerExecutionCard;
            string targetId = _pendingPlayerExecutionTargetUnitId;
            ClearPendingPlayerExecution();

            var commands = CardPlayResolver.GenerateCommands(card);
            bool isAttackCard = commands.Any(c => c is DamageCommand);
            GameEvent.Publish(new CardExecutedEvent
            {
                CasterId = owner.UnitId,
                TargetId = targetId,
                CardId = card.Id,
                IsAttackCard = isAttackCard,
                IsPrediction = false,
                SkipBattleAnimation = false
            });

            bool success = CardPlayResolver.PlayCard(CurrentState, card, owner.UnitId, targetId);
            if (!success)
            {
                Debug.LogWarning($"[BattleManager] 执行轨结算玩家执行牌失败: {card.Id}");
                return false;
            }

            CurrentState.CheckBattleEnd();

            if (PredictionManager != null)
            {
                PredictionManager.TriggerPrediction("玩家执行轨结算");
            }

            return true;
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

        private void ClearPendingPlayerExecution(string unitId = null)
        {
            if (!string.IsNullOrEmpty(unitId) && _pendingPlayerExecutionCasterUnitId != unitId)
            {
                return;
            }

            _pendingPlayerExecutionCard = null;
            _pendingPlayerExecutionTargetUnitId = null;
            _pendingPlayerExecutionCasterUnitId = null;
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

            var candidateSkills = new List<EnemySkillInfo>();
            foreach (var intentionGroup in enemyInfo.IntentionSet)
            {
                if (intentionGroup?.EnemyIntentionList == null)
                {
                    continue;
                }

                foreach (var intention in intentionGroup.EnemyIntentionList)
                {
                    if (intention?.EnemySkillIndex_Ref != null)
                    {
                        candidateSkills.Add(intention.EnemySkillIndex_Ref);
                    }
                }
            }

            if (candidateSkills.Count == 0)
            {
                Debug.LogWarning($"[BattleManager] 敌人没有可用技能，跳过行动: {enemyUnit.ConfigId}");
                return false;
            }

            var pickedTarget = CurrentState.PlayerUnits.FirstOrDefault(u => u != null && !u.IsDead);
            if (pickedTarget == null)
            {
                Debug.LogWarning("[BattleManager] 敌人回合未找到可用玩家目标");
                return false;
            }

            int selectedIndex = Random.Range(0, candidateSkills.Count);
            selectedSkill = candidateSkills[selectedIndex];
            if (selectedSkill == null)
            {
                return false;
            }

            target = pickedTarget;
            return true;
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

            bool isAttackSkill = commands.Any(c => c is DamageCommand);
            GameEvent.Publish(new CardExecutedEvent
            {
                CasterId = enemyUnit.UnitId,
                TargetId = target.UnitId,
                CardId = selectedSkill.Id,
                IsAttackCard = isAttackSkill,
                IsPrediction = false
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
                        // 目标选择：默认选择第一个玩家单位（后续可以扩展为更智能的目标选择）
                        string targetId = CurrentState.PlayerUnits.Count > 0 ? CurrentState.PlayerUnits[0].UnitId : null;
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
        /// ATB：推进行动条直到有单位获得行动权
        /// </summary>
        /// <returns>获得行动权的单位ID</returns>
        public string AdvanceActionBarUntilAction()
        {
            if (CurrentState == null || CurrentState.IsBattleEnded)
            {
                return null;
            }

            return ActionBarResolver.AdvanceUntilAction(CurrentState);
        }

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
        /// ATB：获取预测的行动顺序
        /// </summary>
        public List<string> GetPredictedActionOrder(int lookAhead = 5)
        {
            if (CurrentState == null)
            {
                return new List<string>();
            }

            return ActionBarResolver.PredictActionOrder(CurrentState, lookAhead);
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
