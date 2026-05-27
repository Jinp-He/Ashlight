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

        [Obsolete("Use OnPlanningComplete instead")]
        public event Action<string, bool> OnIconReachZero
        {
            add => OnPlanningComplete += value;
            remove => OnPlanningComplete -= value;
        }

        public bool IsPaused { get; private set; }

        private class AtbIconRuntime
        {
            public string UnitId;
            public bool IsPlayer;
            public int Speed;
            public RectTransform Rect;
            public Image IconImage;
            public float StartX;
            public AtbTrack CurrentTrack;
            /// <summary>
            /// 进入执行轨后的保护时间：避免 segmentWidth=0 或布局把 anchoredPosition 拉回 0 时，同一帧即触发 OnExecutionComplete。
            /// </summary>
            public float BlockExecutionCompleteUntil;
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

            icon.CurrentTrack = AtbTrack.Executing;
            icon.Rect.SetParent(ExecutingATBSlot.transform, false);

            float w = Mathf.Max(1f, Mathf.Abs(segmentWidth));
            float startX = -w * Mathf.Max(1, executingCost);
            icon.StartX = startX;
            icon.Rect.anchoredPosition = new Vector2(startX, iconBaseY);
            // 进入执行轨后至少经过一帧 + 短延迟才允许触发“执行完成”，与规划结束解耦
            icon.BlockExecutionCompleteUntil = Time.unscaledTime + 0.12f;

            Debug.Log($"[ATB] MoveToExecuting unitId={unitId}, executingCost={executingCost}, startX={startX}");
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

            float startX = CalculateStartXBySpeed(icon.Speed);
            icon.StartX = startX;
            float xOffset = CalculateXOffsetForOverlap(startX);
            icon.Rect.anchoredPosition = new Vector2(startX + xOffset, iconBaseY);
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

            float startX = CalculateStartXBySpeed(speed);
            float xOffset = CalculateXOffsetForOverlap(startX);
            rect.anchoredPosition = new Vector2(startX + xOffset, iconBaseY);

            _activeIcons.Add(new AtbIconRuntime
            {
                UnitId = unitId,
                IsPlayer = isPlayer,
                Speed = Mathf.Max(1, speed),
                Rect = rect,
                IconImage = instance.GetComponent<Image>(),
                StartX = startX,
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

        private float CalculateStartXBySpeed(int speed)
        {
            int safeSpeed = Mathf.Max(1, speed);
            int clampedSpeed = Mathf.Clamp(safeSpeed, 1, Mathf.Max(1, maxSpeedForPosition));
            float w = Mathf.Max(1f, Mathf.Abs(segmentWidth));
            return -w * clampedSpeed;
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
