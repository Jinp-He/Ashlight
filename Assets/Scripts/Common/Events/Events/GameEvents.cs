using cfg;
using cfg.Character;
using cfg.Enemy;
using Scripts.UI;

namespace Ashlight.Common.Events
{
    /// <summary>拼图成功放置后发布；MapPanel 用于创建或刷新地块视图。</summary>
    public struct MapTilePlacedEvent
    {
        public Ashlight.State.Runtime.MapPlacedTileState Tile;
    }

    /// <summary>非战斗地块的即时内容结算通知。</summary>
    public struct MapContentResolvedEvent
    {
        public Ashlight.State.Runtime.MapTileContent Content;
        public Ashlight.State.Runtime.MapGridPosition Position;
    }

    /// <summary>地图请求进入战斗；场景/流程层决定如何加载 BattleScene。</summary>
    public struct MapBattleRequestedEvent
    {
        public Ashlight.State.Runtime.MapBattleKind Kind;
        public string EncounterId;
    }

    /// <summary>地图状态变更；UI 只能读取此状态，不得直接修改。</summary>
    public struct MapRunStateChangedEvent
    {
        public Ashlight.State.Runtime.MapRuntimeState State;
    }

    public struct SelectCardToDeckEvent
    {
        public CardInfo cardInfo;
    }

    public struct DeleteCardFromDeckEvent
    {
        public SCardViewController sCardView;
        public CardInfo cardInfo;
    }

    public struct CardLibraryChangeByCharacterEvent
    {
        public CharacterEnum character;
    }

    public struct ClearCharacterDeckEvent
    {
        public CharacterEnum character;
    }

    /// <summary>
    /// 敌人选择意图事件
    /// 当敌人在回合开始时选择技能和时间槽位置时发布
    /// </summary>
    public struct EnemyIntentionSelectedEvent
    {
        public string EnemyUnitId;          // 敌人单位ID（如 "enemy_0"）
        public EnemySkillInfo SkillInfo;    // 选择的技能信息
        public int TimeSlotPosition;        // 时间槽位置（0-14）
        public string TargetUnitId;         // 目标单位ID（如 "player_0"）
    }

    /// <summary>
    /// 攻击执行事件
    /// 当伤害指令执行时发布，用于触发攻击动画和伤害数字显示
    /// </summary>
    public struct AttackExecutedEvent
    {
        /// <summary>
        /// 攻击者单位ID
        /// </summary>
        public string AttackerId;

        /// <summary>
        /// 目标单位ID
        /// </summary>
        public string TargetId;

        /// <summary>
        /// 实际造成的伤害值（已扣除护甲）
        /// </summary>
        public int ActualDamage;

        /// <summary>
        /// 是否为AOE伤害
        /// </summary>
        public bool IsAoe;

        /// <summary>
        /// 是否为预解算模式（预解算时不播放动画）
        /// </summary>
        public bool IsPrediction;
    }

    /// <summary>
    /// 卡牌放置事件
    /// 当卡牌被放置到时间轴时发布，用于触发预解算
    /// </summary>
    public struct CardPlacedEvent
    {
        /// <summary>
        /// 卡牌ID
        /// </summary>
        public string CardId;

        /// <summary>
        /// 所有者单位ID
        /// </summary>
        public string OwnerId;

        /// <summary>
        /// 目标单位ID
        /// </summary>
        public string TargetId;
    }

    /// <summary>
    /// 卡牌移除事件
    /// 当卡牌从时间轴移除时发布，用于触发预解算
    /// </summary>
    public struct CardRemovedEvent
    {
        /// <summary>
        /// 卡牌ID
        /// </summary>
        public string CardId;

        /// <summary>
        /// 所有者单位ID
        /// </summary>
        public string OwnerId;
    }

    /// <summary>
    /// 血量预测事件
    /// 当预解算完成后发布，包含所有单位的预测血量
    /// </summary>
    public struct HpPredictionEvent
    {
        /// <summary>
        /// 单位ID -> 预测的最终血量
        /// </summary>
        public System.Collections.Generic.Dictionary<string, int> PredictedHpMap;
    }

    /// <summary>
    /// 停止血量预测显示事件
    /// 当预测显示时间结束时发布
    /// </summary>
    public struct HpPredictionStopEvent
    {
        // 空事件，只用作信号
    }

    /// <summary>
    /// 卡片执行事件（用于触发战斗演出动画）
    /// 所有卡片执行时发布，不仅限于攻击
    /// </summary>
    public struct CardExecutedEvent
    {
        /// <summary>
        /// 施法者单位ID
        /// </summary>
        public string CasterId;

        /// <summary>
        /// 目标单位ID
        /// </summary>
        public string TargetId;

        /// <summary>
        /// 卡片ID
        /// </summary>
        public string CardId;

        /// <summary>
        /// 是否是攻击类卡片
        /// </summary>
        public bool IsAttackCard;

        /// <summary>
        /// 是否为预解算模式（预解算时不播放动画）
        /// </summary>
        public bool IsPrediction;

        /// <summary>
        /// 为 true 时不播放战斗演出（例如 ATB 下打出执行牌仅宣告出牌，效果在执行轨再结算）
        /// </summary>
        public bool SkipBattleAnimation;

        /// <summary>
        /// 为 true 时使用"中央舞台"演出（双方抽离到屏幕中央播放），否则使用原地播放。
        /// 玩家执行牌结算、敌人技能执行时为 true；迅捷牌等普通打牌保持原地。
        /// </summary>
        public bool UseCenterStage;
    }

    /// <summary>
    /// 时间轴前进前事件
    /// 在时间轴解算第一格之前发布，用于收集将被执行的卡片信息
    /// </summary>
    public struct BeforeTimelineAdvanceEvent
    {
        /// <summary>
        /// 将被执行的卡片源ID列表（SourceCardId）
        /// </summary>
        public System.Collections.Generic.List<ExecutedCardInfo> ExecutedCards;
    }

    /// <summary>
    /// 被执行的卡片信息
    /// </summary>
    public struct ExecutedCardInfo
    {
        public string SourceCardId;  // 卡片ID
        public string OwnerId;        // 所有者ID
    }

    /// <summary>
    /// 时间轴前进后事件
    /// 在时间轴向前移动一格后发布，用于通知UI更新
    /// </summary>
    public struct AfterTimelineAdvanceEvent
    {
        // 空事件，只用作信号
    }

    /// <summary>
    /// 战斗结束事件
    /// 当 BattleStateSnapshot.IsBattleEnded 首次从 false 变为 true 时由 BattleManager 发布一次
    /// </summary>
    public struct BattleEndedEvent
    {
        /// <summary>
        /// 玩家是否胜利（true=全部敌人死亡且至少一个玩家存活）
        /// </summary>
        public bool IsPlayerVictory;
    }

    /// <summary>
    /// 换位事件
    /// 当 MovePositionCommand 让施法者和一名友方交换前/后排时发布，用于驱动 UI 交换二者的
    /// sibling 顺序（由 HorizontalLayoutGroup 完成实际重排）。
    /// 逻辑层（阵营列表顺序，索引 0 = 前排）是唯一真相源，UI 收到事件后只做视觉交换。
    /// </summary>
    public struct PositionSwappedEvent
    {
        /// <summary>
        /// 交换双方的单位ID（A 通常是施法者）
        /// </summary>
        public string UnitIdA;
        public string UnitIdB;

        /// <summary>
        /// 是否为预解算模式（预解算时不实际移动角色）
        /// </summary>
        public bool IsPrediction;
    }
}
