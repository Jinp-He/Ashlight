using System.Collections.Generic;
using System.Linq;

namespace Ashlight.Battle.Core.Data
{
    /// <summary>
    /// 战场完整快照（NvN架构）
    /// 包含所有战斗单位和时间轴数据
    /// 支持深拷贝，用于预测系统
    /// </summary>
    public class BattleStateSnapshot
    {
        /// <summary>
        /// 玩家方单位列表（支持多角色）
        /// </summary>
        public List<UnitState> PlayerUnits { get; set; }

        /// <summary>
        /// 敌方单位列表（支持多敌人）
        /// </summary>
        public List<UnitState> EnemyUnits { get; set; }

        /// <summary>
        /// 敌人共享时间轨（所有敌人共用一条时间轴）
        /// </summary>
        public TimelineTrack SharedEnemyTrack { get; set; }

        /// <summary>
        /// 战斗卡组系统
        /// </summary>
        public BattleDeckSystem DeckSystem { get; set; }

        /// <summary>
        /// 当前时间指针（0-14）
        /// </summary>
        public int CurrentTimeIndex { get; set; }

        /// <summary>
        /// 战斗是否结束
        /// </summary>
        public bool IsBattleEnded { get; set; }

        /// <summary>
        /// 玩家是否胜利（仅在IsBattleEnded=true时有效）
        /// </summary>
        public bool IsPlayerVictory { get; set; }

        /// <summary>
        /// 是否为预解算状态（用于区分预解算和实际战斗）
        /// 预解算时不触发动画，只显示血量提示
        /// </summary>
        public bool IsPrediction { get; set; }

        // ========== ATB 系统新增字段 ==========

        /// <summary>
        /// 当前回合行动单位ID（ATB 系统中标识谁正在执行回合）
        /// </summary>
        public string CurrentTurnUnitId { get; set; }

        /// <summary>
        /// 总回合数（所有单位的回合累计）
        /// </summary>
        public int TurnCount { get; set; }

        /// <summary>
        /// 全场暂停标志（玩家回合时为true，冻结所有其他单位推进）
        /// </summary>
        public bool IsGlobalPaused { get; set; }

        /// <summary>
        /// 卡牌修正表（升级系统在战斗开始时填充，按 CardId 叠加伤害/护甲/AOE 等）
        /// </summary>
        public CardModifierRegistry CardModifiers { get; set; }

        /// <summary>
        /// 【公共回合镜像】当前公共回合号（真相在 ATB.CurrentRound，UI 层在原子回合开始时同步）。
        /// 与 <see cref="UnitState.NextActionRound"/> 搭配判断「当前回合将行动的单位」。
        /// </summary>
        public int CurrentRound { get; set; }

        /// <summary>
        /// 回合内移动触发器列表（「这回合每次有角色移动就 XX」）。回合结束时清空。
        /// </summary>
        public List<MoveTriggerState> MoveTriggers { get; set; }

        /// <summary>
        /// 本回合（当前原子回合内）全场累计移动次数——任何单位每完成一次换排 +1。
        /// 供隧穿/铁蒺藜卡面动态显示「本回合已移动 N 次」；回合结束清零。
        /// </summary>
        public int MovesThisTurn { get; set; }

        public BattleStateSnapshot()
        {
            PlayerUnits = new List<UnitState>();
            EnemyUnits = new List<UnitState>();
            SharedEnemyTrack = new TimelineTrack();
            DeckSystem = new BattleDeckSystem();
            CurrentTimeIndex = 0;
            IsBattleEnded = false;
            IsPlayerVictory = false;
            IsPrediction = false;
            CurrentTurnUnitId = null;
            TurnCount = 0;
            IsGlobalPaused = false;
            CardModifiers = new CardModifierRegistry();
            CurrentRound = 0;
            MoveTriggers = new List<MoveTriggerState>();
        }

        /// <summary>
        /// 获取所有单位（玩家方+敌方）
        /// </summary>
        public List<UnitState> GetAllUnits()
        {
            var allUnits = new List<UnitState>();
            allUnits.AddRange(PlayerUnits);
            allUnits.AddRange(EnemyUnits);
            return allUnits;
        }

        /// <summary>
        /// 前后排由单位显式存储的 <see cref="UnitState.RowPosition"/> 决定（唯一真相源）。
        /// 移动 = 直接改该单位的 RowPosition，可独立进出前/后排，两区可多人共存，不再靠列表顺序推导。
        /// </summary>
        public BattleRowPosition GetRowPosition(UnitState unit)
        {
            return unit?.RowPosition ?? BattleRowPosition.BackRow;
        }

        /// <summary>
        /// 单位是否处于前排
        /// </summary>
        public bool IsFrontRow(UnitState unit)
        {
            return GetRowPosition(unit) == BattleRowPosition.FrontRow;
        }

        /// <summary>
        /// 根据ID获取单位
        /// </summary>
        public UnitState GetUnitById(string unitId)
        {
            // 优先从玩家方查找
            var unit = PlayerUnits.FirstOrDefault(u => u.UnitId == unitId);
            if (unit != null)
            {
                return unit;
            }

            // 再从敌方查找
            return EnemyUnits.FirstOrDefault(u => u.UnitId == unitId);
        }

        /// <summary>
        /// 获取存活的玩家方单位
        /// </summary>
        public List<UnitState> GetAlivePlayerUnits()
        {
            return PlayerUnits.Where(u => !u.IsDead).ToList();
        }

        /// <summary>
        /// 获取存活的敌方单位
        /// </summary>
        public List<UnitState> GetAliveEnemyUnits()
        {
            return EnemyUnits.Where(u => !u.IsDead).ToList();
        }

        /// <summary>
        /// 【公共回合】从 <paramref name="owner"/> 的视角，取「当前公共回合将行动」的存活敌对单位
        /// （NextActionRound == CurrentRound；我方回合先于敌方，故这些单位是本回合稍后行动者）。
        /// 依赖 UI 层已把 ATB 调度同步进快照（NextActionRound 为 -1 的单位视为未知、不命中）。
        /// </summary>
        public List<UnitState> GetCurrentRoundOpponents(UnitState owner)
        {
            var pool = owner != null && owner.IsPlayerUnit ? EnemyUnits : PlayerUnits;
            return pool.Where(u => !u.IsDead && u.NextActionRound == CurrentRound && u.NextActionRound >= 0).ToList();
        }

        /// <summary>
        /// 检查战斗是否结束
        /// </summary>
        public void CheckBattleEnd()
        {
            // 注意：List.All() 对空列表返回 true。若某一方单位列表为空（异常初始化），
            // 不能据此判定团灭，否则会在战斗一开始就误判结束。
            bool allPlayersDead = PlayerUnits.Count > 0 && PlayerUnits.All(u => u.IsDead);
            bool allEnemiesDead = EnemyUnits.Count > 0 && EnemyUnits.All(u => u.IsDead);

            if (allPlayersDead || allEnemiesDead)
            {
                bool wasEnded = IsBattleEnded;
                IsBattleEnded = true;
                IsPlayerVictory = allEnemiesDead && !allPlayersDead;

                if (!wasEnded)
                {
                    UnityEngine.Debug.Log(
                        $"[BattleEnd] 检测到战斗结束：allPlayersDead={allPlayersDead}, allEnemiesDead={allEnemiesDead}, " +
                        $"玩家胜利={IsPlayerVictory} | 玩家存活 {PlayerUnits.Count(u => !u.IsDead)}/{PlayerUnits.Count}, " +
                        $"敌人存活 {EnemyUnits.Count(u => !u.IsDead)}/{EnemyUnits.Count}");
                }
            }
        }

        /// <summary>
        /// 深拷贝整个战场状态
        /// 用于预测系统，确保不修改真实状态
        /// </summary>
        public BattleStateSnapshot Clone()
        {
            var clone = new BattleStateSnapshot
            {
                CurrentTimeIndex = this.CurrentTimeIndex,
                IsBattleEnded = this.IsBattleEnded,
                IsPlayerVictory = this.IsPlayerVictory,
                IsPrediction = this.IsPrediction,
                CurrentTurnUnitId = this.CurrentTurnUnitId,
                TurnCount = this.TurnCount,
                IsGlobalPaused = this.IsGlobalPaused,
                CurrentRound = this.CurrentRound,
                PlayerUnits = new List<UnitState>(),
                EnemyUnits = new List<UnitState>(),
                MoveTriggers = this.MoveTriggers != null
                    ? this.MoveTriggers.Select(t => t.Clone()).ToList()
                    : new List<MoveTriggerState>(),
                MovesThisTurn = this.MovesThisTurn
            };

            // 深拷贝玩家方单位
            if (this.PlayerUnits != null)
            {
                foreach (var unit in this.PlayerUnits)
                {
                    clone.PlayerUnits.Add(unit.Clone());
                }
            }

            // 深拷贝敌方单位
            if (this.EnemyUnits != null)
            {
                foreach (var unit in this.EnemyUnits)
                {
                    clone.EnemyUnits.Add(unit.Clone());
                }
            }

            // 深拷贝敌人共享时间轨
            if (this.SharedEnemyTrack != null)
            {
                clone.SharedEnemyTrack = this.SharedEnemyTrack.Clone();
            }

            // 深拷贝卡组系统
            if (this.DeckSystem != null)
            {
                clone.DeckSystem = this.DeckSystem.Clone();
            }

            // 深拷贝卡牌修正表
            if (this.CardModifiers != null)
            {
                clone.CardModifiers = this.CardModifiers.Clone();
            }

            return clone;
        }

        /// <summary>
        /// 创建用于测试的默认战场状态
        /// </summary>
        public static BattleStateSnapshot CreateTestSnapshot()
        {
            var snapshot = new BattleStateSnapshot();

            // 添加一个玩家单位
            snapshot.PlayerUnits.Add(new UnitState
            {
                UnitId = "player_1",
                CurrentHp = 100,
                MaxHp = 100,
                Defense = 0,
                IsPlayerUnit = true,
                ConfigId = "warrior"
            });

            // 添加一个敌人单位
            snapshot.EnemyUnits.Add(new UnitState
            {
                UnitId = "enemy_1",
                CurrentHp = 80,
                MaxHp = 80,
                Defense = 0,
                IsPlayerUnit = false,
                ConfigId = "goblin"
            });

            return snapshot;
        }
    }
}

