using System.Collections;
using Ashlight.Battle;
using Ashlight.Common.Events;
using Ashlight.State.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashlight.Systems.Map
{
    /// <summary>
    /// 跨场景持有一次地图远征的规则状态，并负责地图战斗的场景往返。
    /// MapPanel 是场景 UI，可随场景卸载；本对象只保留 MapSystem 与待结算状态。
    /// </summary>
    public sealed class MapRunSession : MonoBehaviour
    {
        public static MapRunSession Instance { get; private set; }

        public MapSystem System { get; private set; }
        public MapRuntimeState CurrentState => System?.State;
        public MapRunDefinition RunDefinition { get; private set; }

        private string _mapSceneName = "MapScene";
        private string _battleSceneName = "BattleScene";
        private bool _returnToMapAfterBattle;

        public static MapRunSession GetOrCreate(MapRunDefinition definition, string mapSceneName, string battleSceneName)
        {
            if (Instance == null)
            {
                var sessionObject = new GameObject("MapRunSession");
                Instance = sessionObject.AddComponent<MapRunSession>();
            }

            Instance.Configure(definition, mapSceneName, battleSceneName);
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            System = new MapSystem();
        }

        private void OnEnable()
        {
            GameEvent.Subscribe<MapBattleRequestedEvent>(OnMapBattleRequested);
            GameEvent.Subscribe<BattleEndedEvent>(OnBattleEnded);
        }

        private void OnDisable()
        {
            GameEvent.Unsubscribe<MapBattleRequestedEvent>(OnMapBattleRequested);
            GameEvent.Unsubscribe<BattleEndedEvent>(OnBattleEnded);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Configure(MapRunDefinition definition, string mapSceneName, string battleSceneName)
        {
            if (definition != null && RunDefinition == null) RunDefinition = definition;
            if (!string.IsNullOrWhiteSpace(mapSceneName)) _mapSceneName = mapSceneName;
            if (!string.IsNullOrWhiteSpace(battleSceneName)) _battleSceneName = battleSceneName;
        }

        public void StartConfiguredRun()
        {
            if (RunDefinition == null)
            {
                Debug.LogError("[MapRunSession] Missing MapRunDefinition; cannot start map run.", this);
                return;
            }

            System.StartRun(RunDefinition);
        }

        public bool TryPlaceTile(string tileId, int column, int row, int clockwiseQuarterTurns, out string failureReason)
        {
            return System.TryPlaceTile(tileId, new MapGridPosition(column, row), clockwiseQuarterTurns, out failureReason);
        }

        public void ResolvePendingBattle(bool isPlayerVictory)
        {
            System.ResolvePendingBattle(isPlayerVictory);
        }

        private void OnMapBattleRequested(MapBattleRequestedEvent evt)
        {
            if (string.IsNullOrWhiteSpace(evt.EncounterId))
            {
                Debug.LogError("[MapRunSession] Map requested a battle without an EncounterId.", this);
                return;
            }

            _returnToMapAfterBattle = true;
            BattleManager.PendingEncounterId = evt.EncounterId;
            SceneManager.LoadScene(_battleSceneName);
        }

        private void OnBattleEnded(BattleEndedEvent evt)
        {
            if (!_returnToMapAfterBattle) return;

            _returnToMapAfterBattle = false;
            ResolvePendingBattle(evt.IsPlayerVictory);
            StartCoroutine(ReturnToMapAfterBattleEnd());
        }

        private IEnumerator ReturnToMapAfterBattleEnd()
        {
            // Let the BattleScene finish processing BattleEndedEvent before unloading it.
            yield return null;
            SceneManager.LoadScene(_mapSceneName);
        }
    }
}
