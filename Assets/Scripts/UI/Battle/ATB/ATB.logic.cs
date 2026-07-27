using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Ashlight.Battle.Core.Data;
using Ashlight.Common.Utils;
using System;
using System.Linq;

namespace Scripts.UI
{
    /// <summary>单步推进结果：无单位/已停 / 轮到玩家（停下等输入）/ 敌人已结算（可继续但要留节奏）。</summary>
    public enum AtbStepResult { None, PlayerTurn, EnemyResolved }


    /// <summary>
    /// 【公共回合调度器 / Discrete Common-Round Scheduler】
    ///
    /// ▌模型
    ///   · 全局公共回合计数器 <see cref="CurrentRound"/>，从 0 开始，是一条“真实时钟”：
    ///     每跨入一个整数回合（含没有单位行动的空回合）都推进一格并触发 <see cref="OnObjectiveRoundAdvanced"/>。
    ///   · 每个单位有一个绝对回合 <see cref="AtbIconRuntime.NextRound"/>：它下一次行动落在哪个公共回合。
    ///   · 单位速度 Speed = “每隔几个公共回合行动一次”（数字越大越慢）。
    ///       - 我方：开局 NextRound = 1（第 1 回合全体行动）。
    ///       - 敌方：开局 NextRound = Speed（无第 1 回合免费行动）。
    ///       - 行动结束后重排：NextRound = CurrentRound + Speed + 额外延迟(过载次数等)。
    ///
    /// ▌回合是原子的（单轨，无执行轨）
    ///   轮到一个单位就触发 <see cref="OnUnitTurn"/>：玩家 → 进入出牌阶段并停下等输入；
    ///   敌人 → 回调里立即结算并重排，循环自动继续到下一个单位。
    ///
    /// ▌同回合先后
    ///   同一公共回合内多单位：我方先于敌方 → Speed 小的先 → 注册序。
    /// </summary>
    public partial class ATB : MonoBehaviour
    {
        public GameObject PlayerIconPrefab;
        public GameObject EnemyIconPrefab;

        [Tooltip("天气虚拟单位的 ATB 图标 prefab；留空则复用 EnemyIconPrefab（sprite 会被天气图标覆盖）")]
        public GameObject WeatherIconPrefab;

        /// <summary>天气虚拟单位在时钟里的固定 UnitId（全场最多一个天气）。</summary>
        public const string WeatherUnitId = "__weather__";

        private readonly List<GameObject> _playerIconInstances = new List<GameObject>();
        private readonly List<GameObject> _enemyIconInstances = new List<GameObject>();
        private readonly List<GameObject> _weatherIconInstances = new List<GameObject>();
        private readonly List<GameObject> _castIconInstances = new List<GameObject>();
        private readonly List<AtbIconRuntime> _activeIcons = new List<AtbIconRuntime>();

        [SerializeField]
        [Tooltip("ATB每格宽度（视觉：一个公共回合的像素间距）")]
        private float segmentWidth = 77f;

        [SerializeField]
        [Tooltip("所有ATB图标统一的Y轴基准位置（像素）")]
        private float iconBaseY = 0f;

        [SerializeField]
        [Tooltip("重叠判定阈值：两图标X距离小于该值时视为重叠（像素）")]
        private float overlapThreshold = 15f;

        [SerializeField]
        [Tooltip("重叠时每级X轴错开距离（像素）")]
        private float overlapSeparation = 20f;

        [SerializeField]
        [Tooltip("连续敌人回合之间的节奏间隔（秒）：每个敌人结算后等待此时长再推进下一个，保证按顺序、动画不叠。")]
        private float enemyTurnPacing = 0.6f;

        /// <summary>正在运行的自动推进协程（连续敌人回合逐个结算）。null = 未运行。</summary>
        private Coroutine _autoStepRoutine;

        /// <summary>
        /// 战斗演出忙碌判定（由 UI_BattleScene 注入，读 BattleAnimationHandler.IsAnimating）。
        /// 敌人结算后，推进循环会等它返回 false 再开下一个单位的回合——
        /// 保证「上一个敌人动画播完，下一个才行动」，行动顺序条的高亮也随之与演出一致。
        /// </summary>
        public System.Func<bool> AnimationBusyPredicate;

        /// <summary>演出等待的兜底上限（秒）：动画丢失完成信号时防止战斗永久卡住。</summary>
        private const float AnimationWaitTimeout = 8f;

        /// <summary>
        /// 有图标的视觉同步被推迟（演出期间 Reschedule 不动图标条，演出结束统一 SyncAllVisuals）。
        /// 否则结算瞬间图标就跳去未来回合，比动画早 N 秒，观感是「ATB 先跳了」。
        /// </summary>
        private bool _visualSyncDeferred;

        /// <summary>
        /// 全局公共回合计数器（真实时钟）。开局为 0，第一次 <see cref="TriggerNextUnit"/> 会推进到第一个有单位行动的回合。
        /// </summary>
        public int CurrentRound { get; private set; }

        /// <summary>
        /// 轮到某单位行动（原子回合）：玩家 → 出牌阶段；敌人 → 立即结算其预告意图。
        /// 参数：(unitId, isPlayerUnit)。
        /// </summary>
        public event Action<string, bool> OnUnitTurn;

        /// <summary>
        /// 每跨入一个新的公共回合触发一次（含没有单位行动的空回合）。
        /// 上层据此推进“每公共回合”的逐回合效果（如闪避掉层）。
        /// </summary>
        public event Action OnObjectiveRoundAdvanced;

        /// <summary>
        /// 图标被移除时触发（含死亡清理、TriggerNextUnit 跳过死亡单位）。
        /// 供 UI 同步移除行动顺序视图中的对应卡片。
        /// </summary>
        public event Action<string> OnIconRemoved;

        /// <summary>兼容旧字段：是否暂停（回合制下不再驱动引擎，仅作状态位保留，避免大面积改调用点）。</summary>
        public bool IsPaused { get; private set; }

        /// <summary>
        /// 中止 TriggerNextUnit 的自动连续推进。战斗结束时置 true，避免结算中途继续触发后续回合。
        /// 战斗初始化（RebuildIconsFromUnits）时重置为 false。
        /// </summary>
        public bool AutoAdvanceSuspended { get; set; }

        /// <summary>
        /// 死亡单位判定回调。由 UI_BattleScene 注入，使 ATB 推进队列时主动跳过/移除已死亡单位的图标。
        /// 返回 true 表示该 unitId 已死亡（或已不存在）。
        /// </summary>
        public Func<string, bool> IsUnitDeadPredicate;

        private class AtbIconRuntime
        {
            public string UnitId;
            public bool IsPlayer;
            /// <summary>天气虚拟单位（第三方阵营）：无 UnitState、豁免死亡判定、同回合排最先。</summary>
            public bool IsWeather;
            /// <summary>玩家引线虚拟单位（在轨执行牌）：无 UnitState、豁免死亡判定、一次性（结算后移除）、同回合排在天气之后、真单位之前。</summary>
            public bool IsCast;
            /// <summary>速度 = 每隔几个公共回合行动一次（数字越大越慢）。重排时实时同步。</summary>
            public int Speed;
            public RectTransform Rect;
            public Image IconImage;
            /// <summary>视觉用：上次同步到的 StartX（重叠错开判定）。</summary>
            public float StartX;
            /// <summary>【核心】该单位下一次行动落在的绝对公共回合。</summary>
            public int NextRound;
        }

        private void Awake()
        {
            InitUIBindings();
            EnsureTrackSlotsBound();
        }

        public void RebuildIconsFromUnits(IReadOnlyList<UnitState> playerUnits, IReadOnlyList<UnitState> enemyUnits)
        {
            if (_autoStepRoutine != null)
            {
                StopCoroutine(_autoStepRoutine);
                _autoStepRoutine = null;
            }

            ClearPlayerIcons();
            ClearEnemyIcons();
            ClearWeatherIcons();
            ClearCastIcons();
            _activeIcons.Clear();
            AutoAdvanceSuspended = false;
            CurrentRound = 0;

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

        /// <summary>兼容旧实时模式：回合制下不再驱动引擎，空操作。</summary>
        public void Tick(float deltaTime) { }

        public void Pause() => IsPaused = true;
        public void Resume() => IsPaused = false;

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
        public void ClearWeatherIcons() => DestroyIconList(_weatherIconInstances);
        public void ClearCastIcons() => DestroyIconList(_castIconInstances);

        public void ClearAllIcons()
        {
            ClearPlayerIcons();
            ClearEnemyIcons();
            ClearWeatherIcons();
            ClearCastIcons();
            _activeIcons.Clear();
        }

        /// <summary>
        /// 【引线虚拟单位】把玩家挂起的执行牌排进公共回合时钟：到 resolveRound 回合结算（一次性，结算后由上层 RemoveUnitIcon 移除）。
        /// 第三方标记：无 UnitState、豁免死亡判定；同回合排在天气之后、真单位之前（预告好的引线先落地，再轮到单位行动）。
        /// </summary>
        /// <param name="castId">引线唯一 Id（BattleManager.CastIdPrefix 前缀）</param>
        /// <param name="iconResourcePath">图标 Resources 路径（通常是卡牌 MiniSprite），缺图保留 prefab 默认图</param>
        /// <param name="resolveRound">结算所在的绝对公共回合</param>
        public GameObject AddCastIcon(string castId, string iconResourcePath, int resolveRound)
        {
            if (string.IsNullOrEmpty(castId)) return null;

            var prefab = PlayerIconPrefab != null ? PlayerIconPrefab : EnemyIconPrefab;
            // configId 传 null：跳过角色图标解析；isPlayer=false 让 StepOnce 把它当「非玩家步」处理（结算后循环自动继续）。
            var go = InstantiateIcon(prefab, PlanningATBSlot, null, castId, 1, false, _castIconInstances);
            if (go == null) return null;

            var icon = FindIcon(castId);
            if (icon != null)
            {
                icon.IsCast = true;
                icon.NextRound = Mathf.Max(CurrentRound + 1, resolveRound);
                SyncVisualFromRounds(icon);
            }

            if (!string.IsNullOrEmpty(iconResourcePath))
            {
                var sprite = Resources.Load<Sprite>(iconResourcePath.Replace('\\', '/'));
                if (sprite != null)
                {
                    var img = go.GetComponent<Image>();
                    if (img != null) img.sprite = sprite;
                }
            }

            Debug.Log($"[ATB] AddCastIcon: 引线 {castId} 挂钟 → 回合 {(icon != null ? icon.NextRound : resolveRound)} (当前 {CurrentRound})");
            return go;
        }

        /// <summary>
        /// 【天气虚拟单位】把天气挂进公共回合时钟：Speed=Period，首次行动=第 Period 回合。
        /// 第三方阵营：无 UnitState、豁免死亡判定、同回合永远排最先（先劈雷，再轮到单位行动）。
        /// </summary>
        /// <param name="iconPath">天气图标 Resources 路径（表 IconPath），缺图时保留 prefab 默认图</param>
        /// <param name="period">天气周期（每几个公共回合结算一次）</param>
        public GameObject AddWeatherIcon(string iconPath, int period)
        {
            var prefab = WeatherIconPrefab != null ? WeatherIconPrefab : EnemyIconPrefab;
            // configId 传 null：跳过角色/敌人图标解析，天气 sprite 走下面的专用加载。
            // isPlayer=false 恰好让 TryRegisterRuntime 的开局种子 = Speed = Period（首雷第 Period 回合）。
            var go = InstantiateIcon(prefab, PlanningATBSlot, null, WeatherUnitId, period, false, _weatherIconInstances);
            if (go == null) return null;

            var icon = FindIcon(WeatherUnitId);
            if (icon != null) icon.IsWeather = true;

            if (!string.IsNullOrEmpty(iconPath))
            {
                var sprite = Resources.Load<Sprite>(iconPath);
                if (sprite != null)
                {
                    var img = go.GetComponent<Image>();
                    if (img != null) img.sprite = sprite;
                }
                else
                {
                    Debug.LogWarning($"[ATB] 天气图标缺失（先用 prefab 默认图顶替）: {iconPath}");
                }
            }

            Debug.Log($"[ATB] AddWeatherIcon: 天气挂钟 period={period}，首次结算=回合 {Mathf.Max(1, period)}");
            return go;
        }

        /// <summary>返回排在指定公共回合行动的所有真单位 Id（不含天气/引线虚拟单位）。落雷结算用。</summary>
        public List<string> GetUnitIdsAtRound(int round)
        {
            var result = new List<string>();
            for (int i = 0; i < _activeIcons.Count; i++)
            {
                var icon = _activeIcons[i];
                if (icon == null || icon.IsWeather || icon.IsCast) continue;
                if (icon.NextRound == round) result.Add(icon.UnitId);
            }
            return result;
        }

        /// <summary>
        /// 【回合制】移除指定单位的 ATB 图标（单位死亡时调用）。
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
                _castIconInstances.Remove(go);
                Destroy(go);
            }

            Debug.Log($"[ATB] RemoveUnitIcon: 已移除单位图标 {unitId}，剩余 {_activeIcons.Count} 个");
            OnIconRemoved?.Invoke(unitId);
        }

        /// <summary>当前已挂入 ATB 的全部执行牌虚拟单位 Id。</summary>
        public List<string> GetCastIds()
        {
            return _activeIcons
                .Where(icon => icon != null && icon.IsCast)
                .Select(icon => icon.UnitId)
                .ToList();
        }

        // ────────────────────────────────────────────────────────────────
        #region 回合制驱动 API

        /// <summary>
        /// 行动顺序条目（供 TurnOrderView 使用）。
        /// </summary>
        public struct TurnOrderEntry
        {
            public string UnitId;
            public bool   IsPlayer;
            /// <summary>天气虚拟单位的条目（TurnOrderView 用天气卡渲染，不查单位登记表）。</summary>
            public bool   IsWeather;
            /// <summary>玩家引线（在轨执行牌）的条目（TurnOrderView 用卡牌图渲染，不查单位登记表）。</summary>
            public bool   IsCast;
            /// <summary>该次行动所在的绝对公共回合（分隔条按此标号；相同 = 同组不分隔）。</summary>
            public int    Round;
            /// <summary>分组键 = Round（TurnOrderView 判断是否同组、是否插分隔条）。</summary>
            public int    GroupKey;
        }

        /// <summary>
        /// 【回合制驱动 · 单轨】启动自动推进：逐个结算连续的敌人回合（每个之间留 <see cref="enemyTurnPacing"/>
        /// 秒节奏，保证按顺序、动画不叠），轮到玩家回合就停下等输入。
        ///
        /// 用协程逐步推进（而非一帧内同步跑完），这样多个敌人在同一公共回合内会被玩家逐个看到。
        /// 玩家结束回合时应再次调用本方法继续推进。回调里 **不要** 再调用 TriggerNextUnit。
        /// </summary>
        /// <summary>
        /// 备用协程宿主（由 UI_BattleScene 注入自己）。ATB 节点在场景里可能是隐藏的
        /// （旧图标条 UI 已弃用），未激活的组件跑不了协程——没有宿主时 TriggerNextUnit
        /// 会退化成同步一帧跑完：无节奏、无演出闸门，敌人动画全部并发。
        /// </summary>
        public MonoBehaviour CoroutineHost;

        public void TriggerNextUnit()
        {
            if (_autoStepRoutine != null) return; // 已在推进中

            var host = ResolveCoroutineHost();
            if (host != null)
            {
                _autoStepRoutine = host.StartCoroutine(AutoStepRoutine());
            }
            else
            {
                // 兜底：找不到任何激活宿主时，退化为同步连续推进（无节奏、无演出闸门）。
                Debug.LogWarning("[ATB] 无可用协程宿主（ATB 节点未激活且未注入 CoroutineHost），退化为同步推进——敌人演出将并发");
                while (StepOnce() == AtbStepResult.EnemyResolved) { }
            }
        }

        /// <summary>优先用自己（激活时），否则用注入的宿主；都不可用返回 null。</summary>
        private MonoBehaviour ResolveCoroutineHost()
        {
            if (isActiveAndEnabled) return this;
            if (CoroutineHost != null && CoroutineHost.isActiveAndEnabled) return CoroutineHost;
            return null;
        }

        private IEnumerator AutoStepRoutine()
        {
            while (true)
            {
                var r = StepOnce();
                if (r != AtbStepResult.EnemyResolved) break; // 轮到玩家 / 无人可动 → 停

                // 敌人已结算：先等它的战斗演出播完（伤害结算是同步的，演出是异步协程），
                // 再留出节奏间隔推进下一个——下一个单位的回合绝不在上一个动画结束前开始。
                if (AnimationBusyPredicate != null && AnimationBusyPredicate())
                {
                    float waitStart = Time.time;
                    float guard = AnimationWaitTimeout;
                    Debug.Log($"[ATB] 等待战斗演出… (t={waitStart:F2})");
                    while (AnimationBusyPredicate() && guard > 0f)
                    {
                        guard -= Time.deltaTime;
                        yield return null;
                    }
                    if (guard <= 0f)
                    {
                        Debug.LogWarning("[ATB] 等待战斗演出超时，强制推进下一个单位");
                    }
                    Debug.Log($"[ATB] 演出等待结束，等了 {Time.time - waitStart:F2}s");
                }

                // 演出结束：把被推迟的图标条视觉一次性同步到最新时刻表。
                if (_visualSyncDeferred)
                {
                    _visualSyncDeferred = false;
                    SyncAllVisuals();
                }

                yield return new WaitForSeconds(Mathf.Max(0f, enemyTurnPacing));
            }
            _autoStepRoutine = null;
        }

        /// <summary>
        /// 单步推进：选出下一个该行动的单位（NextRound 最小 → 我方优先 → Speed 小 → 注册序），
        /// 把公共回合时钟逐格快进到它的回合（每格含空回合触发一次 <see cref="OnObjectiveRoundAdvanced"/>），
        /// 触发一次 <see cref="OnUnitTurn"/>。返回该步类型。
        /// </summary>
        private AtbStepResult StepOnce()
        {
            if (AutoAdvanceSuspended) return AtbStepResult.None;

            // 选出一个存活单位（途中遇到死亡单位就移除后重选）。
            AtbIconRuntime next;
            while (true)
            {
                next = SelectNextIcon();
                if (next == null) return AtbStepResult.None;
                // 天气/引线虚拟单位没有 UnitState，死亡判定查不到会误判"已不存在"→ 必须豁免。
                if (!next.IsWeather && !next.IsCast && IsUnitDeadPredicate != null && IsUnitDeadPredicate(next.UnitId))
                {
                    Debug.Log($"[ATB] StepOnce: 跳过并移除死亡单位图标 {next.UnitId}");
                    RemoveUnitIcon(next.UnitId);
                    continue;
                }
                break;
            }

            // 【时间快进】把公共回合时钟逐格推进到该单位的回合。
            if (next.NextRound > CurrentRound)
            {
                while (CurrentRound < next.NextRound)
                {
                    CurrentRound++;
                    OnObjectiveRoundAdvanced?.Invoke();
                }
                SyncAllVisuals();
            }

            bool isPlayer = next.IsPlayer;
            Debug.Log($"[ATB] 回合{CurrentRound}：当前行动角色 = {next.UnitId} ({(isPlayer ? "我方" : "敌方")}, Speed={next.Speed})  | 后续顺序 {DescribeUpcomingOrder()}");
            OnUnitTurn?.Invoke(next.UnitId, isPlayer);
            return isPlayer ? AtbStepResult.PlayerTurn : AtbStepResult.EnemyResolved;
        }

        /// <summary>
        /// 把接下来的行动顺序按公共回合分组拼成可读字符串，便于 Debug：
        /// 例如「回合1:[player_0,player_1,player_2] 回合2:[enemy_0] 回合3:[player_0,player_1,player_2]」。
        /// </summary>
        private string DescribeUpcomingOrder(int count = 12)
        {
            var order = GetTurnOrderWithFuture(count);
            if (order.Count == 0) return "(空)";

            var sb = new System.Text.StringBuilder();
            int prev = int.MinValue;
            for (int i = 0; i < order.Count; i++)
            {
                var e = order[i];
                if (e.Round != prev)
                {
                    if (prev != int.MinValue) sb.Append("] ");
                    sb.Append($"回合{e.Round}:[");
                    prev = e.Round;
                }
                else
                {
                    sb.Append(",");
                }
                sb.Append(e.UnitId);
            }
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// 选出下一个该行动的图标：NextRound 最小 → 我方优先 → Speed 小 → 注册序。
        /// </summary>
        private AtbIconRuntime SelectNextIcon()
        {
            AtbIconRuntime best = null;
            int bestIdx = -1;
            for (int i = 0; i < _activeIcons.Count; i++)
            {
                var icon = _activeIcons[i];
                if (icon?.Rect == null) continue;
                if (best == null || CompareTurnOrder(icon, i, best, bestIdx) < 0)
                {
                    best = icon;
                    bestIdx = i;
                }
            }
            return best;
        }

        /// <summary>行动先后比较：Round 小优先 → 天气最先 → 我方优先 → Speed 小优先 → 注册序小优先。返回 &lt;0 表示 a 更靠前。</summary>
        private static int CompareTurnOrder(AtbIconRuntime a, int aIdx, AtbIconRuntime b, int bIdx)
        {
            if (a.NextRound != b.NextRound) return a.NextRound - b.NextRound;
            if (a.IsWeather != b.IsWeather) return a.IsWeather ? -1 : 1; // 天气最先（跨入回合即结算，行动前可能被劈死）
            if (a.IsCast != b.IsCast) return a.IsCast ? -1 : 1;       // 引线次先（预告好的执行牌先落地，再轮到单位行动）
            if (a.IsPlayer != b.IsPlayer) return a.IsPlayer ? -1 : 1; // 我方先
            if (a.Speed != b.Speed) return a.Speed - b.Speed;         // 快（Speed 小）先
            return aIdx - bIdx;                                       // 注册序
        }

        /// <summary>
        /// 【回合制驱动】单位行动结束后重排：NextRound = CurrentRound + Speed + extraDelay。
        /// Speed 实时读取（中途加速/减速从这次重排起生效）；extraDelay 用于过载次数等额外延迟。
        /// </summary>
        public void Reschedule(string unitId, int speed, int extraDelay = 0)
        {
            var icon = FindIcon(unitId);
            if (icon == null)
            {
                Debug.LogWarning($"[ATB] Reschedule 未找到图标: {unitId}");
                return;
            }

            icon.Speed = Mathf.Max(1, speed);
            icon.NextRound = CurrentRound + icon.Speed + Mathf.Max(0, extraDelay);
            SyncVisualOrDefer(icon);
            Debug.Log($"[ATB] Reschedule {unitId}: speed={icon.Speed}, extra={extraDelay} → 回合 {icon.NextRound} (当前 {CurrentRound})");
        }

        /// <summary>
        /// 演出播放期间不动图标条（推迟到演出结束统一同步），否则立即同步。
        /// 数据（NextRound）始终即时生效，被推迟的只是视觉位置。
        /// </summary>
        private void SyncVisualOrDefer(AtbIconRuntime icon)
        {
            if (AnimationBusyPredicate != null && AnimationBusyPredicate())
            {
                _visualSyncDeferred = true;
                return;
            }
            SyncVisualFromRounds(icon);
        }

        /// <summary>
        /// 【回合制驱动】把单位排到某个绝对回合（clamp 到 &gt;= CurrentRound）。用于兜底/特殊排队。
        /// </summary>
        public void SetNextRound(string unitId, int absoluteRound)
        {
            var icon = FindIcon(unitId);
            if (icon == null) return;
            icon.NextRound = Mathf.Max(CurrentRound, absoluteRound);
            SyncVisualOrDefer(icon);
        }

        /// <summary>
        /// 【公共回合镜像】把调度同步进战场快照：CurrentRound + 各单位 NextActionRound。
        /// Core 命令（预知一击/示现/全知全闪、[执行]中判定等）只读这份镜像。
        /// 在每个原子回合开始、以及 ApplyPendingDelays 落账后调用。
        /// </summary>
        public void SyncScheduleToState(BattleStateSnapshot state)
        {
            if (state == null) return;

            state.CurrentRound = CurrentRound;
            var weather = FindIcon(WeatherUnitId);
            state.NextWeatherRound = weather != null ? weather.NextRound : -1;
            foreach (var unit in state.GetAllUnits())
            {
                if (unit == null) continue;
                var icon = FindIcon(unit.UnitId);
                unit.NextActionRound = icon != null ? icon.NextRound : -1;
            }
        }

        /// <summary>
        /// 【公共回合镜像】把命令结算累计的 <see cref="UnitState.PendingRoundDelay"/> 落到真调度
        /// （NextRound += 延迟，clamp 到不早于当前回合）并清零。返回是否有任何调度变化。
        /// 推迟类效果（PushCollision/TimeShiftAll → ActionBarShiftCommand）由此真正生效。
        /// </summary>
        public bool ApplyPendingDelays(BattleStateSnapshot state)
        {
            if (state == null) return false;

            bool changed = false;
            foreach (var unit in state.GetAllUnits())
            {
                if (unit == null || unit.PendingRoundDelay == 0) continue;

                int delay = unit.PendingRoundDelay;
                unit.PendingRoundDelay = 0;

                if (unit.IsDead) continue;

                var icon = FindIcon(unit.UnitId);
                if (icon == null) continue;

                int before = icon.NextRound;
                icon.NextRound = Mathf.Max(CurrentRound, icon.NextRound + delay);
                SyncVisualOrDefer(icon);
                unit.NextActionRound = icon.NextRound;
                changed = true;
                Debug.Log($"[ATB] 推迟落账: {unit.UnitId} 回合 {before} -> {icon.NextRound} (延迟 {delay})");
            }

            if (state.PendingWeatherDelay != 0)
            {
                int shift = state.PendingWeatherDelay;
                state.PendingWeatherDelay = 0;
                var weather = FindIcon(WeatherUnitId);
                if (weather != null)
                {
                    int before = weather.NextRound;
                    weather.NextRound = Mathf.Max(CurrentRound, weather.NextRound + shift);
                    if (state.WeatherGuardRound == before)
                        state.WeatherGuardRound = weather.NextRound;
                    SyncVisualOrDefer(weather);
                    state.NextWeatherRound = weather.NextRound;
                    changed = true;
                    Debug.Log($"[ATB] 天气顺延落账: 回合 {before} -> {weather.NextRound} ({shift:+#;-#;0})");
                }
            }

            if (changed)
            {
                state.CurrentRound = CurrentRound;
            }
            return changed;
        }

        /// <summary>
        /// 返回“含未来”的行动顺序：从当前状态起，按“速度基础节奏”向前模拟，平铺出接下来 <paramref name="count"/> 个行动。
        /// 同一单位可能重复出现多次（快单位）。真实重排距离受该回合过载/技能影响（模拟时未知），故为近似预览。
        /// </summary>
        public List<TurnOrderEntry> GetTurnOrderWithFuture(int count = 10)
        {
            var result = new List<TurnOrderEntry>();

            // 拷贝一份可推进的模拟态
            var work = new List<AtbIconRuntime>(_activeIcons.Count);
            var idxOf = new Dictionary<AtbIconRuntime, int>();
            for (int i = 0; i < _activeIcons.Count; i++)
            {
                var ic = _activeIcons[i];
                if (ic?.Rect == null) continue;
                if (!ic.IsWeather && !ic.IsCast && IsUnitDeadPredicate != null && IsUnitDeadPredicate(ic.UnitId)) continue;
                var copy = new AtbIconRuntime
                {
                    UnitId = ic.UnitId, IsPlayer = ic.IsPlayer, IsWeather = ic.IsWeather, IsCast = ic.IsCast,
                    Speed = Mathf.Max(1, ic.Speed), NextRound = ic.NextRound
                };
                idxOf[copy] = i;
                work.Add(copy);
            }
            if (work.Count == 0) return result;

            int simRound = CurrentRound;
            for (int k = 0; k < count; k++)
            {
                // 选最靠前者
                AtbIconRuntime pick = null; int pickIdx = -1;
                for (int i = 0; i < work.Count; i++)
                {
                    var w = work[i];
                    if (pick == null || CompareTurnOrder(w, idxOf[w], pick, pickIdx) < 0)
                    {
                        pick = w; pickIdx = idxOf[w];
                    }
                }
                if (pick == null) break;

                if (pick.NextRound > simRound) simRound = pick.NextRound;

                result.Add(new TurnOrderEntry
                {
                    UnitId = pick.UnitId, IsPlayer = pick.IsPlayer, IsWeather = pick.IsWeather, IsCast = pick.IsCast,
                    Round = pick.NextRound, GroupKey = pick.NextRound
                });

                // 引线是一次性的：结算后不再重排，从模拟队列移除（否则预览里会按 Speed 反复出现）。
                if (pick.IsCast)
                {
                    work.Remove(pick);
                    continue;
                }

                // 模拟重排：下一次 = 当前回合 + Speed（过载/技能延迟预测时未知，忽略）
                pick.NextRound = simRound + pick.Speed;
            }

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

            int spd = Mathf.Max(1, speed);
            // 开局种子：我方第 1 回合行动；敌方第 Speed 回合行动（无第 1 回合免费行动）。
            int seedRound = isPlayer ? 1 : spd;

            var icon = new AtbIconRuntime
            {
                UnitId    = unitId,
                IsPlayer  = isPlayer,
                Speed     = spd,
                Rect      = rect,
                IconImage = instance.GetComponent<Image>(),
                NextRound = seedRound
            };
            _activeIcons.Add(icon);

            float startX  = -(seedRound - CurrentRound) * Mathf.Max(1f, Mathf.Abs(segmentWidth));
            float xOffset = CalculateXOffsetForOverlap(startX);
            rect.anchoredPosition = new Vector2(startX + xOffset, iconBaseY);
            icon.StartX = startX;
        }

        /// <summary>
        /// 根据 targetX 附近已有图标数量，计算左右错开的 X 偏移。0→0, 1→+sep, 2→-sep, 3→+2*sep, ...
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

        /// <summary>把图标视觉坐标同步为 (NextRound - CurrentRound) × segmentWidth。</summary>
        private void SyncVisualFromRounds(AtbIconRuntime icon)
        {
            if (icon?.Rect == null) return;
            float w = Mathf.Max(1f, Mathf.Abs(segmentWidth));
            float x = -(icon.NextRound - CurrentRound) * w;
            icon.StartX = x;
            icon.Rect.anchoredPosition = new Vector2(x, iconBaseY);
        }

        private void SyncAllVisuals()
        {
            for (int i = 0; i < _activeIcons.Count; i++)
                SyncVisualFromRounds(_activeIcons[i]);
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
