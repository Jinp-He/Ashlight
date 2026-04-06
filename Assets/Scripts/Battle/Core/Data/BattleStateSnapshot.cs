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
        /// 检查战斗是否结束
        /// </summary>
        public void CheckBattleEnd()
        {
            bool allPlayersDead = PlayerUnits.All(u => u.IsDead);
            bool allEnemiesDead = EnemyUnits.All(u => u.IsDead);

            if (allPlayersDead || allEnemiesDead)
            {
                IsBattleEnded = true;
                IsPlayerVictory = allEnemiesDead && !allPlayersDead;
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
                PlayerUnits = new List<UnitState>(),
                EnemyUnits = new List<UnitState>()
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

