using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashlight.Battle.Core.Data;
using Ashlight.Battle.Core.Engine;
using Ashlight.Common.Events;
using Ashlight.Config;
using Ashlight.State.Runtime;
using Ashlight.Systems.Character;
using cfg;
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
        /// 时间轴解算器
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

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // 初始化核心引擎
            Resolver = new TimelineResolver();
            Predictor = new BattlePredictor();
            PredictionManager = new BattlePredictionManager(this);
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
                    Track = new TimelineTrack(characterId) // 玩家单位拥有独立时间轴，记录角色ID
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
                    Track = null // 敌人不使用独立时间轴，使用SharedEnemyTrack
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
            CurrentState.DeckSystem.Initialize(allCards);

            // 洗牌
            CurrentState.DeckSystem.ShuffleDeck();

            // 抽取初始手牌
            CurrentState.DeckSystem.DrawCard(initialDrawCount);
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
        /// 开始玩家回合
        /// 玩家回合开始时，每个敌人从他的IntentionList中选择一个并将其放在EnemySharedTimeTrack中
        /// </summary>
        public void StartPlayerTurn()
        {
            if (CurrentState == null)
            {
                Debug.LogError("[BattleManager] 无法开始玩家回合：当前战斗状态为null");
                return;
            }

            CurrentRound++;

            // 清空敌人共享时间轴（准备放置新的意图）- 使用Clear而不是创建新对象，保持UI引用有效
            if (CurrentState.SharedEnemyTrack != null)
            {
                CurrentState.SharedEnemyTrack.Clear();
            }
            else
            {
                CurrentState.SharedEnemyTrack = new TimelineTrack();
            }

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
                        int totalSlots = skillInfo.Channeling + skillInfo.Duration + skillInfo.Recoil;
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

            // 回合开始时触发预解算，显示本回合的血量预测
            if (PredictionManager != null)
            {
                PredictionManager.TriggerPrediction("回合开始");
            }
            else
            {
                Debug.LogWarning("[BattleManager] PredictionManager为null，无法触发预解算");
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

            // 2. 抽取固定5张新牌
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
                   $"当前时间: {CurrentState.CurrentTimeIndex}, " +
                   $"战斗结束: {CurrentState.IsBattleEnded}, " +
                   $"卡组: {CurrentState.DeckSystem?.GetDebugInfo()}";
        }
    }
}
