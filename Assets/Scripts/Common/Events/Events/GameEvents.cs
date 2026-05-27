using cfg;
using cfg.Character;
using cfg.Enemy;
using Scripts.UI;

namespace Ashlight.Common.Events
{
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
}
