using System.Collections;
using Ashlight.Battle;
using Ashlight.Common.Events;
using Ashlight.State.Runtime;
using Ashlight.Systems.Core;
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
        private bool _isTransitioning;
        private float _battleTilePlacedAt = -1f;
        private string _timedSceneName;
        private Coroutine _queuedTileBattle;
        private AsyncOperation _battleScenePreload;
        private float _battleScenePreloadStartedAt = -1f;

        private const float TileRoadRevealDuration = 0.35f;

        /// <summary>Latest elapsed real time from placing a battle tile to activating its battle scene.</summary>
        public float LastBattleTileToSceneSeconds { get; private set; } = -1f;

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
            GameEvent.Subscribe<MapTilePlacedEvent>(OnMapTilePlaced);
            GameEvent.Subscribe<MapTileEncounterQueuedEvent>(OnMapTileEncounterQueued);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            GameEvent.Unsubscribe<MapBattleRequestedEvent>(OnMapBattleRequested);
            GameEvent.Unsubscribe<BattleEndedEvent>(OnBattleEnded);
            GameEvent.Unsubscribe<MapTilePlacedEvent>(OnMapTilePlaced);
            GameEvent.Unsubscribe<MapTileEncounterQueuedEvent>(OnMapTileEncounterQueued);
            SceneManager.sceneLoaded -= OnSceneLoaded;
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
            // Warm the cutscene frames while the player is still on the map so their
            // synchronous Resources load cannot interrupt the map → battle transition.
            SceneTransitionOverlay.GetOrCreate();
            BeginBattleScenePreload();
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
            if (_isTransitioning) return;
            if (string.IsNullOrWhiteSpace(evt.EncounterId))
            {
                Debug.LogError("[MapRunSession] Map requested a battle without an EncounterId.", this);
                return;
            }

            _returnToMapAfterBattle = true;
            BattleManager.PendingEncounterId = evt.EncounterId;
            _timedSceneName = _battleSceneName;
            string preloadProgress = _battleScenePreload == null ? "none" : _battleScenePreload.progress.ToString("F3");
            Debug.Log($"[MapRunSession] Battle preload progress at request: {preloadProgress}", this);
            StartCoroutine(LoadSceneWithTransition(_battleSceneName));
        }

        private void OnMapTilePlaced(MapTilePlacedEvent evt)
        {
            if (evt.Tile == null || (evt.Tile.Content != MapTileContent.Battle && evt.Tile.Content != MapTileContent.Elite)) return;
            _battleTilePlacedAt = Time.realtimeSinceStartup;
            LastBattleTileToSceneSeconds = -1f;
        }

        private void OnMapTileEncounterQueued(MapTileEncounterQueuedEvent evt)
        {
            if (_queuedTileBattle != null || string.IsNullOrWhiteSpace(evt.EncounterId)) return;
            _queuedTileBattle = StartCoroutine(RequestQueuedTileBattle(evt.EncounterId));
        }

        private IEnumerator RequestQueuedTileBattle(string encounterId)
        {
            // The state-changed event renders the placed road this frame; wait briefly so
            // the player sees the placement before the loading transition begins.
            yield return null;
            yield return new WaitForSecondsRealtime(TileRoadRevealDuration);
            _queuedTileBattle = null;
            GameEvent.Publish(new MapBattleRequestedEvent
            {
                Kind = MapBattleKind.TileEncounter,
                EncounterId = encounterId
            });
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_battleTilePlacedAt < 0f || scene.name != _timedSceneName) return;

            LastBattleTileToSceneSeconds = Time.realtimeSinceStartup - _battleTilePlacedAt;
            Debug.Log($"[MapRunSession] Battle tile placement → {scene.name} activation: {LastBattleTileToSceneSeconds:F3}s", this);
            _battleTilePlacedAt = -1f;
            _timedSceneName = null;
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
            yield return LoadSceneWithTransition(_mapSceneName);
        }

        private IEnumerator LoadSceneWithTransition(string sceneName)
        {
            _isTransitioning = true;
            bool useCutscene = sceneName == _battleSceneName;
            SceneTransitionOverlay transition = useCutscene ? SceneTransitionOverlay.GetOrCreate() : null;
            bool usedPreload = sceneName == _battleSceneName && _battleScenePreload != null;
            float loadRequestedAt = Time.realtimeSinceStartup;
            AsyncOperation loadOperation = TakeBattleScenePreload(sceneName) ?? SceneManager.LoadSceneAsync(sceneName);
            if (loadOperation == null)
            {
                _isTransitioning = false;
                yield break;
            }

            if (useCutscene)
            {
                // Let asset loading run while the curtain closes. A preloaded BattleScene is
                // already held at 90%; a new operation is held here until the sequence ends.
                if (loadOperation.progress < 0.9f) loadOperation.allowSceneActivation = false;
                yield return transition.FadeToBlack();
            }

            if (useCutscene)
            {
                while (loadOperation.progress < 0.9f)
                    yield return null;
            }

            Debug.Log($"[MapRunSession] {sceneName} load-ready in {Time.realtimeSinceStartup - loadRequestedAt:F3}s (preload={usedPreload}).", this);

            if (useCutscene) loadOperation.allowSceneActivation = true;
            while (!loadOperation.isDone)
                yield return null;

            if (sceneName == _mapSceneName) BeginBattleScenePreload();
            yield return null;
            if (useCutscene) yield return transition.FadeFromBlack();
            _isTransitioning = false;
        }

        private void BeginBattleScenePreload()
        {
            if (string.IsNullOrWhiteSpace(_battleSceneName) ||
                (_battleScenePreload != null && !_battleScenePreload.isDone)) return;

            _battleScenePreload = SceneManager.LoadSceneAsync(_battleSceneName);
            if (_battleScenePreload != null)
            {
                _battleScenePreload.allowSceneActivation = false;
                _battleScenePreloadStartedAt = Time.realtimeSinceStartup;
                Debug.Log($"[MapRunSession] Started BattleScene preload.", this);
            }
        }

        private AsyncOperation TakeBattleScenePreload(string sceneName)
        {
            if (sceneName != _battleSceneName || _battleScenePreload == null) return null;

            AsyncOperation preload = _battleScenePreload;
            if (_battleScenePreloadStartedAt >= 0f)
                Debug.Log($"[MapRunSession] Consuming BattleScene preload after {Time.realtimeSinceStartup - _battleScenePreloadStartedAt:F3}s at progress {preload.progress:F3}.", this);
            _battleScenePreload = null;
            _battleScenePreloadStartedAt = -1f;
            return preload;
        }
    }
}
