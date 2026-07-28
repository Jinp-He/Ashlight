using Ashlight.State.Runtime;
using UnityEngine;

namespace Ashlight.Systems.Map
{
    /// <summary>
    /// Unity 入口。MapPanel 可引用本组件调用 System；规则本身保留在纯 C# 的 MapSystem 中。
    /// </summary>
    public class MapSystemHost : MonoBehaviour
    {
        [SerializeField] private MapRunDefinition runDefinition;
        [SerializeField] private string mapSceneName = "MapScene";
        [SerializeField] private string battleSceneName = "BattleScene";

        private MapRunSession _session;

        public MapSystem System => _session?.System;
        public MapRunDefinition RunDefinition => _session?.RunDefinition;
        public MapRuntimeState CurrentState => _session?.CurrentState;

        private void Awake()
        {
            _session = MapRunSession.GetOrCreate(runDefinition, mapSceneName, battleSceneName);
        }

        public void StartConfiguredRun()
        {
            if (_session == null || _session.RunDefinition == null)
            {
                Debug.LogError("[MapSystemHost] 未配置 MapRunDefinition，无法开始地图。", this);
                return;
            }
            _session.StartConfiguredRun();
        }

        public bool TryPlaceTile(string tileId, int column, int row, int clockwiseQuarterTurns, out string failureReason)
        {
            if (_session == null)
            {
                failureReason = "地图运行会话尚未初始化。";
                return false;
            }
            return _session.TryPlaceTile(tileId, column, row, clockwiseQuarterTurns, out failureReason);
        }

        public void ResolvePendingBattle(bool isPlayerVictory)
        {
            _session?.ResolvePendingBattle(isPlayerVictory);
        }
    }
}
