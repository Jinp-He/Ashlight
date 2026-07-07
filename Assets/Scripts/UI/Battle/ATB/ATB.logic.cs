using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Ashlight.Battle.Core.Data;
using Ashlight.Common.Utils;
using System;

namespace Scripts.UI
{
    public enum AtbTrack { Planning, Executing }

    /// <summary>
    /// 双轨 ATB 条：PlanningATBSlot（规划轨）+ ExecutingATBSlot（执行轨）
    /// 所有单位在规划轨前进；到达终点后，敌人/打出执行牌的玩家进入执行轨
    /// </summary>
    // ═══════════════════════════════════════════════════════════════════════════
    // 【设计说明：ATB 系统的演化——从实时推进到离散回合制】
    //
    // ▌原始设计（实时 ATB / Real-Time Active Time Battle）
    //   每帧调用 Tick(deltaTime)，所有单位图标在规划轨（PlanningATBSlot）
    //   以 advanceSpeed 的速度向 X=0 推进；到达终点后触发 OnPlanningComplete，
    //   玩家进入出牌阶段（ATB.Pause），敌人则进入执行轨（ExecutingATBSlot），
    //   执行完成后由 OnExecutionComplete 回调结算，再调用 ATB.Resume() 恢复前进。
    //   图标的初始 X 位置由 speed 字段决定：speed 越高起点越靠近 0（先手）。
    //
    // ▌新设计（离散回合制 / Discrete Turn-Based）
    //   游戏节奏从"连续流动"改为"逐回合触发"，类似 STS（Slay the Spire）的行动顺序列表。
    //   核心变化：
    //     1. 不再每帧调用 Tick()（或仅对执行轨保留 Tick）。
    //        规划轨图标**不自动前进**，由外部显式触发。
    //     2. TriggerNextUnit()
    //        找到规划轨中距终点最近的图标，瞬移至 X=0，触发 OnPlanningComplete。
    //        用于：战斗开始时触发第一个单位、每次回合结束后触发下一个单位。
    //     3. SetPositionBySlots(unitId, slots)
    //        将指定单位的规划轨图标设置到 -slots * segmentWidth 处。
    //        表示"该单位在 N 个行动之后才再次行动"。
    //        用于：玩家/敌人回合结束时，根据打出的卡牌消耗格数重新排队。
    //     4. AtbIconRuntime.LastExecutingCost
    //        记录最近一次进入执行轨的消耗格数，供 ResetIconToPlanning 决定新位置。
    //
    //   外部调用约定（UI_BattleScene.Logic.cs 应遵循）：
    //     · 战斗初始化：InitializeByUnits → ATB.Pause() → ATB.TriggerNextUnit()
    //     · 玩家结束回合（Swift 牌/跳过）：
    //         ATB.SkipExecutingTrack → ATB.SetPositionBehindAll(id,1) → ATB.TriggerNextUnit()
    //     · 玩家打出执行牌，回合结束：
    //         ATB.MoveToExecutingTrack → ATB.Resume()（执行轨仍需 Tick 驱动）
    //         执行完成后不再调用 Resume，由 StartPlayerTurn 重新开启回合
    //     · 敌人执行完成：
    //         ATB.TriggerNextUnit() → (Tick 随后调用 ResetIconToPlanning，用 SetPositionBehindAll 逻辑排队)
    //
    //   执行轨（ExecutingATBSlot）仍然依赖 Tick() 推进，因此 Update 中保留了
    //   ATB.Tick(deltaTime) 的调用，但规划轨推进改为显式触发。
    // ═══════════════════════════════════════════════════════════════════════════
    public partial class ATB : MonoBehaviour
    {
        private const float DefaultAdvanceSpeed = 180f;

        public GameObject PlayerIconPrefab;
        public GameObject EnemyIconPrefab;

        private readonly List<GameObject> _playerIconInstances = new List<GameObject>();
        private readonly List<GameObject> _enemyIconInstances = new List<GameObject>();
        private readonly List<AtbIconRuntime> _activeIcons = new List<AtbIconRuntime>();

        [SerializeField]
        [Tooltip("ATB图标统一前进速度（像素/秒）")]
        private float advanceSpeed = DefaultAdvanceSpeed;

        [SerializeField]
        [Tooltip("ATB每格宽度")]
        private float segmentWidth = 77f;

        [SerializeField]
        [Tooltip("用于计算初始位置的速度上限，避免图标出生过远")]
        private int maxSpeedForPosition = 8;

        [SerializeField]
        [Tooltip("所有ATB图标统一的Y轴基准位置（像素）")]
        private float iconBaseY = 0f;

        [SerializeField]
        [Tooltip("重叠判定阈值：两图标X距离小于该值时视为重叠（像素）")]
        private float overlapThreshold = 15f;

        [SerializeField]
        [Tooltip("重叠时每级X轴错开距离（像素）")]
        private float overlapSeparation = 20f;

        /// <summary>
        /// 规划轨到达终点时触发（玩家进入出牌阶段 / 敌人生成意图）
        /// </summary>
        public event Action<string, bool> OnPlanningComplete;

        /// <summary>
        /// 执行轨到达终点时触发（执行行动生效）
        /// </summary>
        public event Action<string, bool> OnExecutionComplete;

        /// <summary>
        /// 客观回合推进一格时触发：<see cref="TriggerNextUnit"/> 里「所有单位 Slots 一起减去 minSlots」（minSlots &gt; 0）
        /// 的那一刻——即时间真正快进、跨入下一排 slot。整排同格单位连续行动只在跨格那一次触发一次。
        /// 上层据此推进「每客观回合」的逐回合效果（如闪避掉层）。
        /// </summary>
        public event Action OnObjectiveRoundAdvanced;

        [Obsolete("Use OnPlanningComplete instead")]
        public event Action<string, bool> OnIconReachZero
        {
            add => OnPlanningComplete += value;
            remove => OnPlanningComplete -= value;
        }

        public bool IsPaused { get; private set; }

        /// <summary>
        /// 【回合制】中止 TriggerNextUnit 的自动连续推进。
        /// 战斗结束时置 true，避免结算中途打死玩家/敌人后循环仍继续触发后续回合。
        /// 战斗初始化（RebuildIconsFromUnits）时重置为 false。
        /// </summary>
        public bool AutoAdvanceSuspended { get; set; }

        /// <summary>
        /// 【回合制】死亡单位判定回调。由 UI_BattleScene 注入，使 ATB 能在推进队列时
        /// 主动跳过/移除已死亡单位的图标——单位死亡时其图标不会自动从队列移除，
        /// 若不跳过，TriggerNextUnit 会反复触发死亡单位的回合导致卡死。
        /// 返回 true 表示该 unitId 已死亡（或已不存在）。
        /// </summary>
        public Func<string, bool> IsUnitDeadPredicate;

        /// <summary>
        /// 图标被移除时触发（含死亡清理、TriggerNextUnit 跳过死亡单位）。
        /// 供 UI 同步移除行动顺序视图（TurnOrderView）中对应的卡片，保持两者一致。
        /// </summary>
        public event Action<string> OnIconRemoved;

        private class AtbIconRuntime
        {
            public string UnitId;
            public bool IsPlayer;
            public int Speed;
            public RectTransform Rect;
            public Image IconImage;
            public float StartX;       // 仅用于视觉：= -Slots * segmentWidth
            public AtbTrack CurrentTrack;
            /// <summary>
            /// 进入执行轨后的保护时间：避免 segmentWidth=0 或布局把 anchoredPosition 拉回 0 时，同一帧即触发 OnExecutionComplete。
            /// </summary>
            public float BlockExecutionCompleteUntil;

            /// <summary>
            /// 【回合制 · 核心】当前离行动还有几格（整数）。
            /// · 规划轨：Slots = 下次行动需等待的格数；1 = 最近，越大越晚。
            /// · 执行轨：Slots = 本次执行牌的消耗格数（用于执行完成后重置位置）。
            /// · 视觉位置由此换算：anchoredPosition.x = -Slots × segmentWidth。
            /// </summary>
            public int Slots;

            /// <summary>
            /// 【回合制】最近一次进入执行轨时所用的消耗格数，执行完成后 ResetIconToPlanning 读取它来设置 Slots。
            /// </summary>
            public int LastExecutingCost;
        }

        private void Awake()
        {
            InitUIBindings();
            EnsureTrackSlotsBound();
        }

        public void RebuildIconsFromUnits(IReadOnlyList<UnitState> playerUnits, IReadOnlyList<UnitState> enemyUnits)
        {
            ClearPlayerIcons();
            ClearEnemyIcons();
            _activeIcons.Clear();
            AutoAdvanceSuspended = false;

            if (playerUnits != null)
            {
                foreach (var u in playerUnits)
                {
                    if (u == null || u.IsDead) continue;
                    AddPlayerIcon(u.ConfigId, u.UnitId, u.Speed);
                }
            }

            if (enemyUnits != null)
            {
                foreach (var u in enemyUnits)
                {
                    if (u == null || u.IsDead) continue;
                    AddEnemyIcon(u.ConfigId, u.UnitId, u.Speed);
                }
            }
        }

        public void InitializeByUnits(IReadOnlyList<UnitState> playerUnits, IReadOnlyList<UnitState> enemyUnits)
        {
            RebuildIconsFromUnits(playerUnits, enemyUnits);
        }

        /// <summary>
        /// 【旧：实时 ATB 推进驱动器】每帧由外部（Update）调用，驱动所有图标向 X=0 前进。
        ///
        /// ── 回合制模式下的使用约定 ──
        ///   规划轨（Planning）：不再依赖此方法自动推进。
        ///     外部改为调用 TriggerNextUnit() 触发下一个单位。
        ///   执行轨（Executing）：仍然需要此方法推进（ATB.Resume() 后 Tick 运行 → 图标向 0 滑动 → 触发 OnExecutionComplete）。
        ///
        ///   因此在回合制模式下，Update 仍保留 ATB.Tick(Time.deltaTime)，但规划轨图标
        ///   由于始终处于 IsPaused=true 状态而不会自行移动；只在执行轨阶段调用 ATB.Resume() 后才会推进。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (IsPaused || _activeIcons.Count == 0 || deltaTime <= 0f)
                return;

            float step = Mathf.Max(1f, advanceSpeed) * deltaTime;
            for (int i = 0; i < _activeIcons.Count; i++)
            {
                var icon = _activeIcons[i];
                if (icon?.Rect == null)
                    continue;

                var pos = icon.Rect.anchoredPosition;
                pos.x = Mathf.MoveTowards(pos.x, 0f, step);
                icon.Rect.anchoredPosition = pos;

                if (Mathf.Abs(pos.x) > 0.01f)
                    continue;

                if (icon.CurrentTrack == AtbTrack.Planning)
                {
                    OnPlanningComplete?.Invoke(icon.UnitId, icon.IsPlayer);
                    // 若回调中已将图标移入执行轨，则不得再拉回规划轨
                    if (icon.CurrentTrack == AtbTrack.Planning)
                        ResetIconToPlanning(icon);
                }
                else
                {
                    if (Time.unscaledTime < icon.BlockExecutionCompleteUntil)
                        continue;

                    OnExecutionComplete?.Invoke(icon.UnitId, icon.IsPlayer);
                    icon.BlockExecutionCompleteUntil = 0f;
                    ResetIconToPlanning(icon);
                }

                // 若事件回调中触发了 Pause（回合开始），立即中断本帧的循环，
                // 防止同帧内其他图标也触发回合，确保同一时刻只有一个回合启动
                if (IsPaused)
                    break;
            }
        }

        /// <summary>
        /// 将指定单位的图标从规划轨移入执行轨，位置由 executingCost 决定
        /// </summary>
        public void MoveToExecutingTrack(string unitId, int executingCost)
        {
            EnsureTrackSlotsBound();
            var icon = FindIcon(unitId);
            if (icon == null)
            {
                Debug.LogWarning($"[ATB] MoveToExecutingTrack 未找到图标: {unitId}");
                return;
            }

            if (ExecutingATBSlot == null)
            {
                Debug.LogError("[ATB] ExecutingATBSlot 未绑定");
                return;
            }

            // 【离散回合制】进入执行轨 = 宣告意图，排到第 executingCost 格等待。
            // 不再用像素位置 + Tick 实时滑动，而是用 Slots 参与统一队列；
            // 当 TriggerNextUnit 推进到它（Slots 归零）时，触发 OnExecutionComplete 结算。
            icon.CurrentTrack = AtbTrack.Executing;
            icon.Rect.SetParent(ExecutingATBSlot.transform, false);
            icon.Slots = Mathf.Max(1, executingCost);
            icon.LastExecutingCost = executingCost;
            icon.BlockExecutionCompleteUntil = 0f;
            SyncVisualFromSlots(icon);

            //Debug.Log($"[ATB] MoveToExecuting unitId={unitId}, executingCost={executingCost}, slots={icon.Slots}");
        }

        /// <summary>
        /// 【敌人单轨制·开局】把敌人移入执行轨，位置排到「所有玩家当前单位之后」再 + executingCost，
        /// 保证开局每个玩家角色都先行动一轮，敌人的首次意图才落地（避免快敌抢在玩家前手起手落）。
        /// 之后的重报意图仍用 <see cref="MoveToExecutingTrack"/>（正常 = ExecutingCost 距离）。
        /// </summary>
        public void MoveToExecutingTrackBehindPlayers(string unitId, int executingCost)
        {
            EnsureTrackSlotsBound();
            var icon = FindIcon(unitId);
            if (icon == null)
            {
                Debug.LogWarning($"[ATB] MoveToExecutingTrackBehindPlayers 未找到图标: {unitId}");
                return;
            }
            if (ExecutingATBSlot == null)
            {
                Debug.LogError("[ATB] ExecutingATBSlot 未绑定");
                return;
            }

            // 取所有玩家单位当前最大的 Slots（开局即其初始格）
            int maxPlayerSlots = 0;
            for (int i = 0; i < _activeIcons.Count; i++)
            {
                var o = _activeIcons[i];
                if (o?.Rect == null || !o.IsPlayer) continue;
                if (o.Slots > maxPlayerSlots) maxPlayerSlots = o.Slots;
            }

            icon.CurrentTrack = AtbTrack.Executing;
            icon.Rect.SetParent(ExecutingATBSlot.transform, false);
            icon.Slots = maxPlayerSlots + Mathf.Max(1, executingCost);
            icon.LastExecutingCost = executingCost;
            icon.BlockExecutionCompleteUntil = 0f;
            SyncVisualFromSlots(icon);
        }

        /// <summary>
        /// 玩家跳过执行轨（只用了迅捷牌或跳过），直接回到规划轨起点
        /// </summary>
        public void SkipExecutingTrack(string unitId)
        {
            var icon = FindIcon(unitId);
            if (icon == null) return;

            ResetIconToPlanning(icon);
            Debug.Log($"[ATB] SkipExecuting unitId={unitId}, reset to planning");
        }

        public void Pause() => IsPaused = true;
        public void Resume() => IsPaused = false;

        public bool IsInExecutionTrack(string unitId)
        {
            var icon = FindIcon(unitId);
            return icon != null && icon.CurrentTrack == AtbTrack.Executing;
        }

        public GameObject AddPlayerIcon(string configId, string unitId = null, int speed = 1)
        {
            return InstantiateIcon(PlayerIconPrefab, PlanningATBSlot, configId, unitId, speed, true, _playerIconInstances);
        }

        public GameObject AddEnemyIcon(string configId, string unitId = null, int speed = 1)
        {
            return InstantiateIcon(EnemyIconPrefab, PlanningATBSlot, configId, unitId, speed, false, _enemyIconInstances);
        }

        public void ClearPlayerIcons() => DestroyIconList(_playerIconInstances);
        public void ClearEnemyIcons() => DestroyIconList(_enemyIconInstances);

        public void ClearAllIcons()
        {
            ClearPlayerIcons();
            ClearEnemyIcons();
            _activeIcons.Clear();
        }

        /// <summary>
        /// 【回合制】移除指定单位的 ATB 图标（单位死亡时调用）。
        /// 同时从运行时队列 _activeIcons 与对应的实例列表移除，并销毁 GameObject。
        /// 找不到对应图标时静默返回（可能已被移除）。
        /// </summary>
        public void RemoveUnitIcon(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return;

            var icon = FindIcon(unitId);
            if (icon == null) return;

            _activeIcons.Remove(icon);

            var go = icon.Rect != null ? icon.Rect.gameObject : null;
            if (go != null)
            {
                _playerIconInstances.Remove(go);
                _enemyIconInstances.Remove(go);
                Destroy(go);
            }

            Debug.Log($"[ATB] RemoveUnitIcon: 已移除单位图标 {unitId}，剩余 {_activeIcons.Count} 个");
            OnIconRemoved?.Invoke(unitId);
        }

        // ────────────────────────────────────────────────────────────────
        #region 公共查询 API

        /// <summary>
        /// 行动顺序条目（供 TurnOrderView 使用）
        /// </summary>
        public struct TurnOrderEntry
        {
            public string UnitId;
            public bool   IsPlayer;
            /// <summary>排序键（越小越靠前/越先行动）；执行轨单位为负数区间，按剩余进度细分</summary>
            public float  DistanceTo0;
            public AtbTrack Track;
            /// <summary>
            /// 分组键（供 TurnOrderView 判断是否同一组、是否插分隔条）。
            /// 执行轨单位统一为 -1（共享同一条执行轨，聚成一组不分隔）；
            /// 规划轨单位 = Slots（同格同组）。
            /// </summary>
            public int    GroupKey;
            /// <summary>
            /// 第几次回合：0 = 当前真实回合；1 = 预测的第二次回合（TurnOrderView 用半透明幽灵卡显示）。
            /// </summary>
            public int    Cycle;
        }

        // ────────────────────────────────────────────────────────────────
        // 【回合制驱动 API】
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 【回合制驱动 · 单轨离散队列】推进到下一个该行动的单位。
        ///
        /// 规划轨和执行轨共用同一个 Slots 队列（Slots 越小越先行动）：
        ///   · 规划轨单位轮到 → 触发 OnPlanningComplete（玩家出牌 / 敌人宣告意图进执行轨）。
        ///   · 执行轨单位轮到 → 触发 OnExecutionComplete（结算其宣告的攻击）。
        ///
        /// 每次推进会先“快进时间”：把所有单位 Slots 一起减去最小值，使当前回合归零。
        ///
        /// 本方法是一个循环，会自动连续处理“无需玩家输入”的回合（敌人宣告意图、敌人执行结算、
        /// 敌人无技能跳过），直到轮到一个需要玩家操作的回合才停下返回；
        /// 因此回调里 **不要** 再调用 TriggerNextUnit（否则重入）。
        ///
        /// 停下条件：
        ///   · 规划轨触发后该单位仍是玩家且仍在规划轨（= 玩家出牌回合）。
        ///   · 执行轨触发后该单位是玩家（= 玩家执行牌结算后开新回合）。
        /// </summary>
        /// <returns>成功停在某个玩家回合返回 true；无单位可触发返回 false。</returns>
        public bool TriggerNextUnit()
        {
            const int Safety = 256; // 防止全员敌人时无限自动推进
            for (int guard = 0; guard < Safety; guard++)
            {
                // 战斗已结束（结算中途触发）→ 立即停止自动推进
                if (AutoAdvanceSuspended)
                    return false;

                // 找 Slots 最小的单位（两条轨一起参与）
                AtbIconRuntime next = null;
                int minSlots = int.MaxValue;
                for (int i = 0; i < _activeIcons.Count; i++)
                {
                    var icon = _activeIcons[i];
                    if (icon?.Rect == null) continue;
                    if (icon.Slots < minSlots)
                    {
                        minSlots = icon.Slots;
                        next = icon;
                    }
                }

                if (next == null)
                {
                    Debug.LogWarning("[ATB] TriggerNextUnit: 无可触发单位");
                    return false;
                }

                // 【死亡保护】单位死亡时图标不会自动出队；若选中的是死亡单位，
                // 直接移除其图标并继续循环，避免反复触发死亡单位回合导致卡死。
                if (IsUnitDeadPredicate != null && IsUnitDeadPredicate(next.UnitId))
                {
                    Debug.Log($"[ATB] TriggerNextUnit: 跳过并移除死亡单位图标 {next.UnitId}");
                    RemoveUnitIcon(next.UnitId);
                    continue;
                }

                // 【时间推进】所有单位 Slots 一起减去 minSlots，当前回合归零
                if (minSlots > 0)
                {
                    for (int i = 0; i < _activeIcons.Count; i++)
                    {
                        var icon = _activeIcons[i];
                        if (icon?.Rect == null) continue;
                        icon.Slots -= minSlots;
                        SyncVisualFromSlots(icon);
                    }

                    // 跨入下一排 slot = 前进一个客观回合（同格连续行动 minSlots==0 不会走到这里）
                    OnObjectiveRoundAdvanced?.Invoke();
                }

                next.Slots = 0;
                SyncVisualFromSlots(next);

                if (next.CurrentTrack == AtbTrack.Executing)
                {
                    // 轮到执行轨单位 → 结算它宣告的攻击
                    Debug.Log($"[ATB] TriggerNextUnit → {next.UnitId} 执行结算 (advanced {minSlots})");
                    bool wasPlayer = next.IsPlayer;
                    OnExecutionComplete?.Invoke(next.UnitId, next.IsPlayer);
                    // 玩家执行牌结算后会开启新回合 → 停下等玩家；敌人结算后自动继续
                    if (wasPlayer) return true;
                    continue;
                }
                else
                {
                    Debug.Log($"[ATB] TriggerNextUnit → {next.UnitId} 规划完成 (advanced {minSlots})");
                    OnPlanningComplete?.Invoke(next.UnitId, next.IsPlayer);
                    // 玩家出牌回合：单位仍在规划轨且是玩家 → 停下等玩家操作
                    if (next.IsPlayer && next.CurrentTrack == AtbTrack.Planning)
                        return true;
                    // 敌人（宣告意图进执行轨 / 无技能已重排）→ 自动继续
                    continue;
                }
            }

            Debug.LogWarning("[ATB] TriggerNextUnit: 达到安全上限，停止自动推进（可能没有玩家单位）");
            return true;
        }

        /// <summary>
        /// 【回合制驱动】将指定单位的规划轨图标设置到距终点 <paramref name="slots"/> 格处（绝对位置）。
        /// 注意：此方法使用绝对格数，不考虑其他单位的当前位置。
        /// 若要将单位放到队列末尾（保证不早于其他人行动），应使用 <see cref="SetPositionBehindAll"/>。
        /// </summary>
        public void SetPositionBySlots(string unitId, int slots)
        {
            EnsureTrackSlotsBound();
            var icon = FindIcon(unitId);
            if (icon == null)
            {
                Debug.LogWarning($"[ATB] SetPositionBySlots 未找到图标: {unitId}");
                return;
            }

            // 若不在规划轨则先拉回
            if (icon.CurrentTrack != AtbTrack.Planning)
            {
                icon.CurrentTrack = AtbTrack.Planning;
                if (PlanningATBSlot != null)
                    icon.Rect.SetParent(PlanningATBSlot.transform, false);
                icon.BlockExecutionCompleteUntil = 0f;
            }

            // 允许 0（= 立刻行动，如玩家执行牌结算后开新回合）
            icon.Slots = Mathf.Max(0, slots);
            SyncVisualFromSlots(icon);
            Debug.Log($"[ATB] SetPositionBySlots unitId={unitId}, slots={icon.Slots}");
        }

        /// <summary>
        /// 【回合制驱动】将指定单位排到当前所有规划轨单位的最后面，
        /// 再额外后退 <paramref name="costSlots"/> 格。
        ///
        /// 这是回合制的标准"回合结束"操作：
        ///   · 无论其他单位在哪里，本单位一定排在最后
        ///   · costSlots = 卡牌消耗格数，表示"等 N 轮才能再行动"
        ///
        /// 解决了 SetPositionBySlots 的固定值问题：
        ///   如果所有单位都在 -77，SetPositionBySlots(id,1) 会把本单位放回 -77（并列第一），
        ///   而 SetPositionBehindAll 会把它放到 -77-77 = -154（队列末尾）。
        /// </summary>
        public void SetPositionBehindAll(string unitId, int costSlots = 1)
        {
            EnsureTrackSlotsBound();
            var icon = FindIcon(unitId);
            if (icon == null)
            {
                Debug.LogWarning($"[ATB] SetPositionBehindAll 未找到图标: {unitId}");
                return;
            }

            // 找规划轨中（排除自身）Slots 最大的
            int maxSlots = 0;
            for (int i = 0; i < _activeIcons.Count; i++)
            {
                var other = _activeIcons[i];
                if (other == icon || other?.Rect == null) continue;
                if (other.CurrentTrack != AtbTrack.Planning) continue;
                if (other.Slots > maxSlots) maxSlots = other.Slots;
            }

            // 若不在规划轨则先拉回
            if (icon.CurrentTrack != AtbTrack.Planning)
            {
                icon.CurrentTrack = AtbTrack.Planning;
                if (PlanningATBSlot != null)
                    icon.Rect.SetParent(PlanningATBSlot.transform, false);
                icon.BlockExecutionCompleteUntil = 0f;
            }

            icon.Slots = maxSlots + Mathf.Max(1, costSlots);
            SyncVisualFromSlots(icon);
            Debug.Log($"[ATB] SetPositionBehindAll unitId={unitId}, costSlots={costSlots}, newSlots={icon.Slots} (maxOtherSlots={maxSlots})");
        }

        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 返回当前所有单位按行动先后排列的列表（最先行动的在最前）
        /// </summary>
        public List<TurnOrderEntry> GetSortedTurnOrder()
        {
            var result = new List<TurnOrderEntry>(_activeIcons.Count);

            for (int i = 0; i < _activeIcons.Count; i++)
            {
                var icon = _activeIcons[i];
                if (icon?.Rect == null) continue;

                // 【离散回合制】两条轨统一用 Slots 排序/分组。
                // 执行轨单位（已宣告意图）也停在第 Slots 格，由攻击图标区分，
                // 而不是跳到最前——这样“待执行的敌人排在第 N 个回合上”才正确体现。
                result.Add(new TurnOrderEntry
                {
                    UnitId      = icon.UnitId,
                    IsPlayer    = icon.IsPlayer,
                    DistanceTo0 = icon.Slots,
                    Track       = icon.CurrentTrack,
                    GroupKey    = icon.Slots
                });
            }

            // 稳定排序：主键相等时按原始注册顺序（i）兜底，避免每帧顺序乱跳导致卡片闪烁
            var indexed = new List<(TurnOrderEntry e, int idx)>(result.Count);
            for (int i = 0; i < result.Count; i++) indexed.Add((result[i], i));
            indexed.Sort((a, b) =>
            {
                int c = a.e.DistanceTo0.CompareTo(b.e.DistanceTo0);
                return c != 0 ? c : a.idx.CompareTo(b.idx);
            });
            for (int i = 0; i < result.Count; i++) result[i] = indexed[i].e;
            return result;
        }

        /// <summary>模拟第二次回合时用的临时条目</summary>
        private struct SimEntry
        {
            public string   UnitId;
            public bool     IsPlayer;
            public int      Speed;
            public int      Slots;
            public AtbTrack Track;
            public int      Idx;       // 注册序，稳定排序兜底
            public int      Appeared;  // 已在模拟中行动过几次
        }

        /// <summary>
        /// 返回"含未来"的行动顺序：Cycle 0 = 当前真实顺序，Cycle 1 = 每个单位第二次回合的预测位置。
        ///
        /// 预测按"速度基础节奏"向前模拟：反复取 Slots 最小者行动，行动后把它重排到所有人之后再加
        /// CalculateInitialSlots(speed)（模拟 SetPositionBehindAll 的标准回合结束），直到每个单位都
        /// 拿到第二次回合。由于真实重入距离取决于该回合的卡牌/技能消耗（预测时未知），Cycle 1 仅为
        /// 近似，用于 UI 幽灵卡预览。所有 Cycle 1 的 Slots 必大于任意 Cycle 0，故排序后未来卡在最后。
        /// </summary>
        public List<TurnOrderEntry> GetTurnOrderWithFuture()
        {
            var result = new List<TurnOrderEntry>();
            var work   = new List<SimEntry>(_activeIcons.Count);

            for (int i = 0; i < _activeIcons.Count; i++)
            {
                var ic = _activeIcons[i];
                if (ic?.Rect == null) continue;
                work.Add(new SimEntry
                {
                    UnitId = ic.UnitId, IsPlayer = ic.IsPlayer, Speed = ic.Speed,
                    Slots = ic.Slots, Track = ic.CurrentTrack, Idx = i, Appeared = 0
                });
                // Cycle 0：当前真实位置
                result.Add(new TurnOrderEntry
                {
                    UnitId = ic.UnitId, IsPlayer = ic.IsPlayer,
                    DistanceTo0 = ic.Slots, Track = ic.CurrentTrack, GroupKey = ic.Slots, Cycle = 0
                });
            }

            int need = work.Count;             // 还有多少单位没拿到第二次回合
            int cap  = work.Count * 4 + 8;      // 安全上限，防止异常时死循环
            for (int guard = 0; guard < cap && need > 0; guard++)
            {
                // 选 Slots 最小者（并列按注册序）
                int mi = -1;
                for (int i = 0; i < work.Count; i++)
                {
                    if (mi < 0 || work[i].Slots < work[mi].Slots ||
                        (work[i].Slots == work[mi].Slots && work[i].Idx < work[mi].Idx))
                        mi = i;
                }
                if (mi < 0) break;

                var e = work[mi];
                e.Appeared++;
                if (e.Appeared == 2)
                {
                    result.Add(new TurnOrderEntry
                    {
                        UnitId = e.UnitId, IsPlayer = e.IsPlayer,
                        DistanceTo0 = e.Slots, Track = AtbTrack.Planning, GroupKey = e.Slots, Cycle = 1
                    });
                    need--;
                }

                // 重排到所有人之后 + 速度基础节奏（模拟 SetPositionBehindAll）
                int maxOther = 0; bool has = false;
                for (int i = 0; i < work.Count; i++)
                {
                    if (i == mi) continue;
                    if (!has || work[i].Slots > maxOther) { maxOther = work[i].Slots; has = true; }
                }
                e.Slots = maxOther + CalculateInitialSlots(e.Speed);
                work[mi] = e;
            }

            // 稳定排序（同 GetSortedTurnOrder）：主键 Slots，相等按插入序
            var indexed = new List<(TurnOrderEntry e, int idx)>(result.Count);
            for (int i = 0; i < result.Count; i++) indexed.Add((result[i], i));
            indexed.Sort((a, b) =>
            {
                int c = a.e.DistanceTo0.CompareTo(b.e.DistanceTo0);
                return c != 0 ? c : a.idx.CompareTo(b.idx);
            });
            for (int i = 0; i < result.Count; i++) result[i] = indexed[i].e;
            return result;
        }

        #endregion
        // ────────────────────────────────────────────────────────────────

        #region Internal

        private AtbIconRuntime FindIcon(string unitId)
        {
            for (int i = 0; i < _activeIcons.Count; i++)
            {
                if (_activeIcons[i].UnitId == unitId)
                    return _activeIcons[i];
            }
            return null;
        }

        private void ResetIconToPlanning(AtbIconRuntime icon)
        {
            if (icon == null) return;

            EnsureTrackSlotsBound();

            icon.CurrentTrack = AtbTrack.Planning;

            if (PlanningATBSlot != null && icon.Rect.parent != PlanningATBSlot.transform)
                icon.Rect.SetParent(PlanningATBSlot.transform, false);

            // 【回合制】用 Slots 重置位置（整数格，视觉坐标由 SyncVisualFromSlots 换算）
            // · LastExecutingCost > 0：执行牌结算后，Slots = 执行牌消耗格数（绝对位置，不依赖他人）
            // · LastExecutingCost == 0：Swift/跳过路径，按速度公式决定 Slots
            if (icon.LastExecutingCost > 0)
                icon.Slots = icon.LastExecutingCost;
            else
                icon.Slots = CalculateInitialSlots(icon.Speed);

            SyncVisualFromSlots(icon);
            icon.BlockExecutionCompleteUntil = 0f;
        }

        private GameObject InstantiateIcon(GameObject prefab, GameObject slot, string configId, string unitId, int speed, bool isPlayer, List<GameObject> instanceList)
        {
            EnsureTrackSlotsBound();
            if (prefab == null || slot == null)
            {
                if (slot == null)
                    Debug.LogError("[ATB] ATB 槽位未绑定");
                return null;
            }

            var go = Instantiate(prefab, slot.transform, false);
            instanceList.Add(go);
            TryApplyConfigIcon(go, configId, isPlayer);
            TryRegisterRuntime(go, unitId, speed, isPlayer);
            return go;
        }

        private void TryRegisterRuntime(GameObject instance, string unitId, int speed, bool isPlayer)
        {
            if (instance == null || string.IsNullOrEmpty(unitId))
                return;

            var rect = instance.GetComponent<RectTransform>();
            if (rect == null)
            {
                Debug.LogWarning($"[ATB] 图标实例缺少RectTransform: {instance.name}");
                return;
            }

            int initialSlots = CalculateInitialSlots(speed);
            float startX     = -initialSlots * Mathf.Max(1f, Mathf.Abs(segmentWidth));
            float xOffset    = CalculateXOffsetForOverlap(startX);
            rect.anchoredPosition = new Vector2(startX + xOffset, iconBaseY);

            _activeIcons.Add(new AtbIconRuntime
            {
                UnitId    = unitId,
                IsPlayer  = isPlayer,
                Speed     = Mathf.Max(1, speed),
                Slots     = initialSlots,   // 【回合制】格子数是逻辑本体
                Rect      = rect,
                IconImage = instance.GetComponent<Image>(),
                StartX    = startX,
                CurrentTrack = AtbTrack.Planning,
                BlockExecutionCompleteUntil = 0f
            });
        }

        /// <summary>
        /// 根据 targetX 附近已有图标数量，计算左右错开的 X 偏移
        /// 错开规律：0→0, 1→+sep, 2→-sep, 3→+2*sep, ...
        /// </summary>
        private float CalculateXOffsetForOverlap(float targetX)
        {
            int nearbyCount = 0;
            for (int i = 0; i < _activeIcons.Count; i++)
            {
                if (Mathf.Abs(_activeIcons[i].StartX - targetX) < overlapThreshold)
                    nearbyCount++;
            }

            if (nearbyCount == 0)
                return 0f;

            int level = (nearbyCount + 1) / 2;
            float sign = (nearbyCount % 2 == 1) ? 1f : -1f;
            return sign * level * overlapSeparation;
        }

        /// <summary>
        /// 将图标的视觉坐标同步为 Slots × segmentWidth，Slots 是唯一逻辑来源。
        /// </summary>
        private void SyncVisualFromSlots(AtbIconRuntime icon)
        {
            float w = Mathf.Max(1f, Mathf.Abs(segmentWidth));
            float x = -icon.Slots * w;
            icon.StartX = x;
            icon.Rect.anchoredPosition = new Vector2(x, iconBaseY);
        }

        /// <summary>
        /// 根据速度计算初始 Slots（速度越高起点格越靠前）
        /// </summary>
        private int CalculateInitialSlots(int speed)
        {
            return Mathf.Clamp(Mathf.Max(1, speed), 1, Mathf.Max(1, maxSpeedForPosition));
        }

        private float CalculateStartXBySpeed(int speed)
        {
            float w = Mathf.Max(1f, Mathf.Abs(segmentWidth));
            return -w * CalculateInitialSlots(speed);
        }

        private static void DestroyIconList(List<GameObject> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null)
                    Destroy(list[i]);
            }
            list.Clear();
        }

        private static void TryApplyConfigIcon(GameObject instance, string configId, bool isPlayer)
        {
            if (instance == null || string.IsNullOrEmpty(configId)) return;

            string path = isPlayer
                ? AssetPath.GetCharacterIconAssetPath(configId)
                : AssetPath.GetEnemyIconAssetPath(configId);

            var sprite = Resources.Load<Sprite>(path);
            if (sprite == null)
            {
                Debug.LogWarning($"[ATB] 未找到图标 Sprite: {path}");
                return;
            }

            var selfImage = instance.GetComponent<Image>();
            if (selfImage != null)
                selfImage.sprite = sprite;
            else
                Debug.LogWarning($"[ATB] 预制体根物体无 Image 组件: {instance.name}");
        }

        private void EnsureTrackSlotsBound()
        {
            if (PlanningATBSlot == null)
            {
                PlanningATBSlot = FindSlotByName("PlanningATBSlot");
            }

            if (ExecutingATBSlot == null)
            {
                ExecutingATBSlot = FindSlotByName("ExecutingATBSlot");
            }
        }

        private GameObject FindSlotByName(string slotName)
        {
            var transforms = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null && transforms[i].name == slotName)
                {
                    return transforms[i].gameObject;
                }
            }

            return null;
        }

        #endregion
    }
}
