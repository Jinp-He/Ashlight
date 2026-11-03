using System.Collections.Generic;
using cfg;
using cfg.Character;
using Ashlight.Config;
using UnityEngine;
namespace Ashlight.Battle.Core.Data
{
    public enum BattleRowPosition
    {
        FrontRow = 0,
        BackRow = 1
    }

    /// <summary>
    /// 单个战斗单位的状态（NvN架构）
    /// POCO类，不继承MonoBehaviour
    /// </summary>
    public class UnitState
    {
        /// <summary>
        /// 单位唯一ID
        /// </summary>
        public string UnitId { get; set; }

        /// <summary>
        /// 当前血量
        /// </summary>
        public int CurrentHp { get; set; }

        /// <summary>
        /// 最大血量
        /// </summary>
        public int MaxHp { get; set; }

        /// <summary>
        /// 当前护甲值（减伤）
        /// </summary>
        public int Defense { get; set; }

        /// <summary>
        /// Buff列表
        /// </summary>
        public List<BuffState> Buffs { get; set; }

        /// <summary>
        /// 该单位的独立时间轴（15格）
        /// </summary>
        public TimelineTrack Track { get; set; }

        /// <summary>
        /// 是否死亡
        /// </summary>
        public bool IsDead { get; set; }

        /// <summary>
        /// 是否为玩家方单位
        /// </summary>
        public bool IsPlayerUnit { get; set; }

        /// <summary>
        /// 是否精英/Boss 档敌人（来自 EnemyInfo.IsElite）。
        /// 仅精英在被打断时叠 [坚毅](Resolve)——杂兵随便断，压迫感长在精英身上。
        /// </summary>
        public bool IsElite { get; set; }

        /// <summary>
        /// 角色/敌人配置ID（用于获取基础属性）
        /// </summary>
        public string ConfigId { get; set; }

        // ========== ATB 系统新增字段 ==========

        /// <summary>
        /// 单位速度，决定行动条推进效率
        /// </summary>
        public int Speed { get; set; }

        /// <summary>
        /// 基础能量（每回合刷新值）
        /// </summary>
        public int BaseEnergy { get; set; }

        /// <summary>
        /// 基础抽牌数（每回合抽牌值）
        /// </summary>
        public int BaseDrawCount { get; set; }

        /// <summary>
        /// 当前回合剩余能量
        /// </summary>
        public int CurrentEnergy { get; set; }

        /// <summary>
        /// 行动条状态
        /// </summary>
        public ActionBarState ActionBar { get; set; }

        /// <summary>
        /// 过载状态
        /// </summary>
        public OverloadState Overload { get; set; }

        /// <summary>
        /// 本回合是否已用掉「首张移动免费」（游侠百相 FirstMoveFree）。每回合开始时重置。
        /// </summary>
        public bool FreeMoveUsedThisTurn { get; set; }

        /// <summary>
        /// 本回合是否已用掉战士百相「首张推迟牌免费」。每回合开始时重置。
        /// </summary>
        public bool FreePushUsedThisTurn { get; set; }

        /// <summary>
        /// 本回合是否移动过（换过区）。供条件效果读取（如影袭「本回合移动过则叠毒」）。每回合开始时重置。
        /// </summary>
        public bool HasMovedThisTurn { get; set; }

        /// <summary>
        /// 前后排（显式存储，唯一真相源）。移动 = 改这个值，可独立进出前/后排，无需与他人换位；
        /// 两区可多人共存。默认后排，开局由 BattleManager 设定（战士前排）。
        /// </summary>
        public BattleRowPosition RowPosition { get; set; } = BattleRowPosition.BackRow;

        /// <summary>
        /// 【公共回合镜像】该单位下一次行动的绝对公共回合（真相在 ATB.AtbIconRuntime.NextRound，
        /// 由 UI 层在每个原子回合开始时同步进来）。-1 = 尚未同步。
        /// Core 命令用它判断「当前回合将行动的单位」（NextActionRound == snapshot.CurrentRound）。
        /// </summary>
        public int NextActionRound { get; set; } = -1;

        /// <summary>
        /// 【公共回合镜像】命令结算期间累计的行动推迟回合数（正数 = 延后，负数 = 提前）。
        /// Core 命令只累加这里；UI 层在结算后调用 ATB.ApplyPendingDelays 把它落到真调度并清零。
        /// </summary>
        public int PendingRoundDelay { get; set; }

        /// <summary>
        /// 上次重排时因过载额外加进 NextRound 的回合数（「过载负债」）。
        /// UI 层 Reschedule 后写入；该单位行动到来时清零（负债已偿还）。
        /// ClearOverloadCommand 用它把已落账的过载延迟拉回来（PendingRoundDelay -= 此值）。
        /// </summary>
        public int AppliedOverloadRoundDelay { get; set; }

        /// <summary>
        /// 护甲首次耗尽时要执行的移动。由 MoveOnArmorBreakEffect 注册；空表示没有监听。
        /// TakeDamage 只把触发标记置起，实际移动由伤害命令在持有 BattleStateSnapshot 时结算。
        /// </summary>
        public string ArmorBreakMoveMode { get; set; }

        public bool ArmorBreakMovePending { get; set; }
        public int LastArmorDamage { get; private set; }

        // ========== 敌人意图轴/执行轴字段 ==========

        /// <summary>
        /// 当前敌人行动阶段（仅敌人单位使用）
        /// </summary>
        public EnemyPhase CurrentPhase { get; set; }

        /// <summary>
        /// 意图轴总长度（格数，来自技能配置）
        /// </summary>
        public int IntentAxisLength { get; set; }

        /// <summary>
        /// 意图轴当前进度（0 到 IntentAxisLength）
        /// </summary>
        public int IntentAxisProgress { get; set; }

        /// <summary>
        /// 执行轴总长度（格数，默认1）
        /// </summary>
        public int ExecuteAxisLength { get; set; }

        /// <summary>
        /// 执行轴当前进度（0 到 ExecuteAxisLength）
        /// </summary>
        public int ExecuteAxisProgress { get; set; }

        /// <summary>
        /// 是否处于硬直状态（所有推进冻结）
        /// </summary>
        public bool IsStunned { get; set; }

        /// <summary>
        /// 硬直剩余tick数
        /// </summary>
        public int StunRemainingTicks { get; set; }

        /// <summary>
        /// 待执行技能ID（敌人意图轴中暂存）
        /// </summary>
        public string PendingSkillId { get; set; }

        /// <summary>
        /// 待执行技能的目标ID（敌人意图轴中暂存）
        /// </summary>
        public string PendingTargetId { get; set; }

        public UnitState()
        {
            Buffs = new List<BuffState>();
            Track = new TimelineTrack();
            ActionBar = new ActionBarState();
            Overload = new OverloadState();
            IsDead = false;
            // 【公共回合制】Speed = 每隔几个公共回合行动一次（数字越大越慢）。仅当配置缺失时用此兜底默认。
            Speed = 2;
            BaseEnergy = 3;
            BaseDrawCount = 5;
            CurrentEnergy = 0;
            // 敌人意图轴/执行轴默认值
            CurrentPhase = EnemyPhase.None;
            IntentAxisLength = 0;
            IntentAxisProgress = 0;
            ExecuteAxisLength = 1;
            ExecuteAxisProgress = 0;
            IsStunned = false;
            StunRemainingTicks = 0;
            PendingSkillId = null;
            PendingTargetId = null;
        }

        /// <summary>
        /// 受到伤害
        /// </summary>
        /// <param name="damage">伤害值</param>
        /// <param name="canBeDodged">false = 环境伤害（如天气落雷）：不消耗闪避层、不能被闪避免除，但仍吃易伤/减伤/护甲</param>
        /// <returns>实际受到的伤害</returns>
        public int TakeDamage(int damage, bool canBeDodged = true)
        {
            LastArmorDamage = 0;
            if (damage <= 0)
            {
                return 0;
            }

            // 0. 闪避：消耗一层，完全免除这一次攻击（在护甲/易伤之前结算）。
            //    仅拦截攻击伤害——中毒/燃烧走 TurnResolver 直接扣血、不经此处，天然不被闪避；
            //    环境伤害（canBeDodged=false，如天气落雷）语义上不是"被瞄准的攻击"，同样不吃闪避。
            var dodge = canBeDodged ? GetBuff("Dodge") : null;
            if (dodge != null && dodge.Value >= 1f)
            {
                dodge.Value -= 1f;
                dodge.StackCount = Mathf.Max(0, Mathf.RoundToInt(dodge.Value));
                if (dodge.Value <= 0f)
                {
                    RemoveBuff("Dodge");
                }
                Debug.Log($"[UnitState] {UnitId} [闪避] 免除一次攻击 (剩余闪避层数: {Mathf.Max(0, Mathf.RoundToInt(dodge.Value))})");
                return 0;
            }

            // 1. Buff 修正：先放大（易伤），再衰减（减伤）；配置里 Value 是百分比，如 50 表示 50%
            float modified = damage;

            var vulnerable = GetBuff("Vulnerable");
            if (vulnerable != null)
            {
                modified *= 1f + vulnerable.Value / 100f;
            }

            var reduceDmg = GetBuff("ReduceDmg");
            if (reduceDmg != null)
            {
                modified *= Mathf.Max(0f, 1f - reduceDmg.Value / 100f);
            }

            int adjusted = Mathf.Max(0, Mathf.RoundToInt(modified));

            // 2. 护甲吸收
            int actualDamage = adjusted;
            if (Defense > 0)
            {
                int defenseBefore = Defense;
                if (Defense >= adjusted)
                {
                    Defense -= adjusted;
                    LastArmorDamage = defenseBefore - Defense;
                    MarkArmorBreakMoveIfNeeded();
                    return 0; // 完全被护甲吸收
                }
                else
                {
                    actualDamage = adjusted - Defense;
                    Defense = 0;
                    LastArmorDamage = defenseBefore;
                    MarkArmorBreakMoveIfNeeded();
                }
            }

            // 3. 扣血
            CurrentHp -= actualDamage;
            if (CurrentHp <= 0)
            {
                CurrentHp = 0;
                IsDead = true;
            }

            // 4. 打断系 buff 计数（Stagger=累计伤害, Block=受击次数）
            if (!IsDead)
            {
                ProcessStaggerOnHit(actualDamage);
                ProcessBlockOnHit(actualDamage);
            }

            return actualDamage;
        }

        public void RegisterArmorBreakMove(string mode)
        {
            ArmorBreakMoveMode = string.IsNullOrWhiteSpace(mode) ? "Toggle" : mode.Trim();
            ArmorBreakMovePending = false;
        }

        public string ConsumePendingArmorBreakMove()
        {
            if (!ArmorBreakMovePending || string.IsNullOrEmpty(ArmorBreakMoveMode))
            {
                return null;
            }

            string mode = ArmorBreakMoveMode;
            ArmorBreakMoveMode = null;
            ArmorBreakMovePending = false;
            return mode;
        }

        private void MarkArmorBreakMoveIfNeeded()
        {
            if (Defense == 0 && !string.IsNullOrEmpty(ArmorBreakMoveMode))
            {
                ArmorBreakMovePending = true;
            }
        }

        /// <summary>
        /// Stagger（破韧）：每次受到伤害扣对应 Value，归 0 时打断行动
        /// </summary>
        private void ProcessStaggerOnHit(int actualDamage)
        {
            if (actualDamage <= 0) return;
            var stagger = GetBuff("Stagger");
            if (stagger == null) return;

            stagger.Value -= actualDamage;
            if (stagger.Value <= 0)
            {
                RemoveBuff("Stagger");
                TryInterruptByBuff("Stagger");
            }
        }

        /// <summary>
        /// Block（格挡）：每次成功受到伤害扣 1 次数，归 0 时打断行动
        /// </summary>
        private void ProcessBlockOnHit(int actualDamage)
        {
            if (actualDamage <= 0) return;
            var block = GetBuff("Block");
            if (block == null) return;

            block.Value -= 1f;
            if (block.Value <= 0)
            {
                RemoveBuff("Block");
                TryInterruptByBuff("Block");
            }
        }

        /// <summary>
        /// 由打断系 buff 触发的中断（仅对处于意图轴/执行轴的敌人生效）
        /// </summary>
        private void TryInterruptByBuff(string source)
        {
            if (CurrentPhase == EnemyPhase.None) return;

            Debug.Log($"[UnitState] {UnitId} 被 [{source}] 打断 (原阶段={CurrentPhase}, 技能={PendingSkillId})");

            // 精英被断 → 永久 +1 层 [坚毅](Resolve)：下次施法的打断阈值提高（结算在挂 Stagger/Block 时）。
            // 打断本身全额生效（技能作废），坚毅只涨"下一次"的价钱——打断从可再生答案变成消耗品。
            if (!IsPlayerUnit && IsElite)
            {
                AddBuff(new BuffState { BuffId = "Resolve", Value = 1, RemainingDuration = -1 });
                Debug.Log($"[UnitState] {UnitId} 因被打断叠加坚毅，当前 {GetBuff("Resolve")?.Value ?? 0} 层");
            }

            CurrentPhase = EnemyPhase.None;
            IntentAxisLength = 0;
            IntentAxisProgress = 0;
            ExecuteAxisLength = 1;
            ExecuteAxisProgress = 0;
            PendingSkillId = null;
            PendingTargetId = null;
            IsStunned = false;
            StunRemainingTicks = 0;
            ActionBar?.Restart();
        }

        /// <summary>
        /// 治疗
        /// </summary>
        /// <param name="healAmount">治疗量</param>
        /// <returns>实际治疗量</returns>
        public int Heal(int healAmount)
        {
            if (IsDead)
            {
                return 0; // 死亡单位无法治疗
            }

            int actualHeal = healAmount;
            CurrentHp += healAmount;

            if (CurrentHp > MaxHp)
            {
                actualHeal = healAmount - (CurrentHp - MaxHp);
                CurrentHp = MaxHp;
            }

            return actualHeal;
        }

        /// <summary>
        /// 增加护甲
        /// </summary>
        public void AddDefense(int amount)
        {
            // 仅对正向加护甲应用 Dex/Frail（负值是直接扣，比如卡片清空护甲等）
            if (amount > 0)
            {
                float modified = amount;
                var dex = GetBuff("Dexterity");
                if (dex != null)
                {
                    modified += dex.Value;
                }
                var frail = GetBuff("Frail");
                if (frail != null)
                {
                    modified *= Mathf.Max(0f, 1f - frail.Value / 100f);
                }
                amount = Mathf.Max(0, Mathf.RoundToInt(modified));
            }

            Defense += amount;
            if (Defense < 0)
            {
                Defense = 0;
            }
        }

        /// <summary>
        /// 添加/叠加 Buff。
        /// 同名 Buff 不再新增第二条，而是按配置合并到已有的那一条上：
        /// · 可叠加（BuffInfo.MaxStack &gt; 1，如中毒/闪避/冰冻）→ 把层数累加到同一条 Buff 的 Value 上，封顶 MaxStack；
        ///   Value 即层数（中毒每回合按 Value 掉血，闪避按 Value 计次），StackCount 与之同步供 UI 显示。
        /// · 不可叠加（MaxStack ≤ 1，如易伤/减伤/力量）→ 只保留一条，数值取较大者，不叠层。
        /// 两种情况都按 RefreshOnReapply 决定是否刷新剩余回合。
        /// </summary>
        public void AddBuff(BuffState buff)
        {
            if (buff == null) return;

            var info = ConfigLoader.Tables?.TbBuffInfo?.GetOrDefault(buff.BuffId);
            int maxStack = info != null ? info.MaxStack : 1;
            bool refresh = info == null || info.RefreshOnReapply;
            bool stackable = maxStack > 1;

            var existing = GetBuff(buff.BuffId);
            if (existing == null)
            {
                // 首次施加：可叠加 Buff 的层数封顶，并让 StackCount 反映当前层数
                if (stackable)
                {
                    buff.Value = Mathf.Min(buff.Value, maxStack);
                    buff.StackCount = Mathf.Max(1, Mathf.RoundToInt(buff.Value));
                }
                else
                {
                    buff.StackCount = 1;
                }
                Buffs.Add(buff);
                return;
            }

            if (stackable)
            {
                // 累加层数到同一条 Buff，封顶 MaxStack
                existing.Value = Mathf.Min(existing.Value + buff.Value, maxStack);
                existing.StackCount = Mathf.Max(1, Mathf.RoundToInt(existing.Value));
            }
            else
            {
                // 不可叠加：取较大数值，避免被更弱的一次覆盖降级
                existing.Value = Mathf.Max(existing.Value, buff.Value);
                existing.StackCount = 1;
            }

            // 刷新持续时间
            if (refresh)
            {
                existing.RemainingDuration = buff.RemainingDuration;
            }
            else if (buff.RemainingDuration == -1)
            {
                existing.RemainingDuration = -1;
            }
            else if (existing.RemainingDuration != -1)
            {
                existing.RemainingDuration = Mathf.Max(existing.RemainingDuration, buff.RemainingDuration);
            }
        }

        /// <summary>
        /// 移除Buff
        /// </summary>
        public void RemoveBuff(string buffId)
        {
            Buffs.RemoveAll(b => b.BuffId == buffId);
        }

        /// <summary>
        /// 获取指定Buff
        /// </summary>
        public BuffState GetBuff(string buffId)
        {
            return Buffs.Find(b => b.BuffId == buffId);
        }

        /// <summary>
        /// 检查是否拥有指定Buff
        /// </summary>
        public bool HasBuff(string buffId)
        {
            return Buffs.Exists(b => b.BuffId == buffId);
        }

        /// <summary>
        /// 更新Buff状态（每回合末调用）
        /// 移除过期的Buff
        /// </summary>
        public void UpdateBuffs()
        {
            Buffs.RemoveAll(buff => buff.DecreaseDuration());
        }

        /// <summary>
        /// 获取角色ID（仅对玩家单位有效）
        /// </summary>
        /// <returns>角色枚举，如果不是玩家单位或解析失败则返回null</returns>
        public CharacterEnum? GetCharacterId()
        {
            if (!IsPlayerUnit || string.IsNullOrEmpty(ConfigId))
            {
                return null;
            }

            if (System.Enum.TryParse<CharacterEnum>(ConfigId, out var characterId))
            {
                return characterId;
            }

            return null;
        }

        /// <summary>
        /// 获取角色配置信息（仅对玩家单位有效）
        /// </summary>
        /// <returns>角色配置信息，如果不是玩家单位或未找到则返回null</returns>
        public CharaterInfo GetCharacterInfo()
        {
            var characterId = GetCharacterId();
            if (characterId == null)
            {
                return null;
            }

            return ConfigLoader.Tables?.TbCharaterInfo?.GetOrDefault(characterId.Value);
        }

        /// <summary>
        /// 深拷贝
        /// </summary>
        public UnitState Clone()
        {
            var clone = new UnitState
            {
                UnitId = this.UnitId,
                CurrentHp = this.CurrentHp,
                MaxHp = this.MaxHp,
                Defense = this.Defense,
                IsDead = this.IsDead,
                IsPlayerUnit = this.IsPlayerUnit,
                IsElite = this.IsElite,
                ConfigId = this.ConfigId,
                Speed = this.Speed,
                BaseEnergy = this.BaseEnergy,
                BaseDrawCount = this.BaseDrawCount,
                CurrentEnergy = this.CurrentEnergy,
                Buffs = new List<BuffState>(),
                Track = this.Track?.Clone(),
                ActionBar = this.ActionBar?.Clone() ?? new ActionBarState(),
                Overload = this.Overload?.Clone() ?? new OverloadState(),
                FreeMoveUsedThisTurn = this.FreeMoveUsedThisTurn,
                FreePushUsedThisTurn = this.FreePushUsedThisTurn,
                HasMovedThisTurn = this.HasMovedThisTurn,
                RowPosition = this.RowPosition,
                NextActionRound = this.NextActionRound,
                PendingRoundDelay = this.PendingRoundDelay,
                AppliedOverloadRoundDelay = this.AppliedOverloadRoundDelay,
                ArmorBreakMoveMode = this.ArmorBreakMoveMode,
                ArmorBreakMovePending = this.ArmorBreakMovePending,
                // 敌人意图轴/执行轴
                CurrentPhase = this.CurrentPhase,
                IntentAxisLength = this.IntentAxisLength,
                IntentAxisProgress = this.IntentAxisProgress,
                ExecuteAxisLength = this.ExecuteAxisLength,
                ExecuteAxisProgress = this.ExecuteAxisProgress,
                IsStunned = this.IsStunned,
                StunRemainingTicks = this.StunRemainingTicks,
                PendingSkillId = this.PendingSkillId,
                PendingTargetId = this.PendingTargetId
            };

            if (this.Buffs != null)
            {
                foreach (var buff in this.Buffs)
                {
                    clone.Buffs.Add(buff.Clone());
                }
            }

            return clone;
        }
    }
}

