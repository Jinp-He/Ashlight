using System.Collections.Generic;
using Ashlight.Common.Events;
using Ashlight.State.Runtime;
using Ashlight.Systems.Map;
using Ashlight.Systems.Core;
using Ashlight.Systems.Character;
using Ashlight.Config;
using cfg;
using cfg.Character;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using MapCharacter = Scripts.UI.Character;

namespace Ashlight.UI.Map
{
    /// <summary>
    /// 地图面板。它只渲染 MapSystem 的状态并发送放置命令；场景 Canvas、地图区、手牌区和文本均由 UI 制作手动铺设。
    /// </summary>
    public class MapPanel : MonoBehaviour
    {
        [Header("Backend")]
        [SerializeField] private MapSystemHost mapSystemHost;
        [SerializeField] private bool startRunOnStart = true;
        [SerializeField, Tooltip("你制作 Tile prefab 后拖入；留空则使用运行时灰盒 Tile。")]
        private MapTileHandView tileHandPrefab;

        [Header("Optional UI references")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private RectTransform mapArea;
        [SerializeField] private RectTransform tileLayer;
        [SerializeField] private RectTransform handArea;
        [SerializeField] private RectTransform characterContainer;
        [SerializeField, Tooltip("留空时优先加载 Resources/Map/Prefab/MapCharacterPrefab。")]
        private MapCharacter characterPrefab;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text budgetText;

        private readonly List<GameObject> _mapObjects = new List<GameObject>();
        private readonly List<GameObject> _handObjects = new List<GameObject>();
        private readonly List<GameObject> _characterObjects = new List<GameObject>();
        private MapTileHandView _draggingTile;

        private void Awake()
        {
            if (mapSystemHost == null) mapSystemHost = GetComponent<MapSystemHost>();
            if (tileHandPrefab == null)
                tileHandPrefab = Resources.Load<MapTileHandView>("Map/Prefab/TilePrefab");
            // Map mode always uses its dedicated presentation prefab. The scene may still
            // contain an older CharacterPrefab reference from before this variant existed.
            MapCharacter mapCharacterPrefab = Resources.Load<MapCharacter>("Map/Prefab/MapCharacterPrefab");
            if (mapCharacterPrefab != null)
                characterPrefab = mapCharacterPrefab;
            if (characterPrefab == null)
                characterPrefab = Resources.Load<MapCharacter>("Characters/Prefab/CharacterPrefab");
        }

        private void OnEnable()
        {
            GameEvent.Subscribe<MapRunStateChangedEvent>(OnMapStateChanged);
        }

        private void Start()
        {
            if (!ValidateUiBindings()) return;
            if (mapSystemHost == null)
            {
                SetStatus("未找到 MapSystemHost。请将 MapPanel 与 MapSystemHost 放在同一对象，或在 Inspector 绑定。");
                return;
            }

            if (startRunOnStart && mapSystemHost.CurrentState == null)
                mapSystemHost.StartConfiguredRun();

            if (mapSystemHost.CurrentState != null) Render(mapSystemHost.CurrentState);
        }

        private void OnDisable()
        {
            GameEvent.Unsubscribe<MapRunStateChangedEvent>(OnMapStateChanged);
        }

        public void BeginTileDrag(MapTileHandView tile)
        {
            _draggingTile = tile;
        }

        public bool EndTileDrag(MapTileHandView tile, PointerEventData eventData)
        {
            if (tile == null || tile != _draggingTile || mapSystemHost == null || mapSystemHost.CurrentState == null)
                return false;

            _draggingTile = null;
            if (!TryGetGridPosition(eventData.position, out MapGridPosition position))
            {
                SetStatus("请将拼图放到地图格内。");
                return false;
            }

            bool placed = mapSystemHost.TryPlaceTile(tile.TileId, position.Column, position.Row, tile.ClockwiseQuarterTurns, out string failureReason);
            if (!placed) SetStatus(failureReason);
            return placed;
        }

        private void OnMapStateChanged(MapRunStateChangedEvent evt)
        {
            Render(evt.State);
        }

        private void Render(MapRuntimeState state)
        {
            if (state == null || !ValidateUiBindings()) return;
            Clear(_mapObjects);
            Clear(_handObjects);

            RenderAnchor(state.StartPosition, "起点", MapAssetPath.Canterbury, state);
            RenderAnchor(state.AncientRuinsPosition, state.AncientRuinsCompleted ? "古国遗迹 ✓" : "古国遗迹", MapAssetPath.AncientRuins, state);
            RenderAnchor(state.FinalPosition, "灰雾深处", MapAssetPath.AshMistDepths, state);
            foreach (MapPlacedTileState tile in state.PlacedTiles) RenderPlacedTile(tile, state);
            foreach (MapTileDefinition tile in state.TileHand) RenderHandTile(tile);
            RenderCharacters();

            budgetText.text = state.RemainingTileBudget.ToString();
            SetStatus(GetStageText(state.Stage));
        }

        private void RenderAnchor(MapGridPosition position, string label, string spritePath, MapRuntimeState state)
        {
            var root = CreateUiObject($"Anchor_{label}", tileLayer);
            root.anchoredPosition = GridToFixedLocationCenter(position, state);
            root.sizeDelta = GetTileSize() * MapRuntimeState.FixedLocationSize;
            _mapObjects.Add(root.gameObject);

            var image = CreateImage("Art", root, spritePath, new Color(1f, 1f, 1f, 0.9f));
            Stretch(image.rectTransform);
            image.preserveAspect = true;
            image.raycastTarget = false;
            var text = CreateTmpText("Label", root, label, 14, Color.white);
            text.rectTransform.anchorMin = new Vector2(0f, 0f);
            text.rectTransform.anchorMax = new Vector2(1f, 0f);
            text.rectTransform.pivot = new Vector2(0.5f, 1f);
            text.rectTransform.anchoredPosition = new Vector2(0f, -4f);
            text.rectTransform.sizeDelta = new Vector2(0f, 22f);
        }

        private void RenderPlacedTile(MapPlacedTileState tile, MapRuntimeState state)
        {
            var root = CreateUiObject($"Tile_{tile.TileId}", tileLayer);
            root.anchoredPosition = GridToLocalPosition(tile.Position, state);
            root.sizeDelta = CellSize(state);
            root.localRotation = Quaternion.Euler(0f, 0f, -90f * tile.ClockwiseQuarterTurns);
            _mapObjects.Add(root.gameObject);

            var image = CreateImage("Shape", root, MapAssetPath.GetTileShapePath(tile.Shape), Color.white);
            Stretch(image.rectTransform);
            image.preserveAspect = true;
            image.raycastTarget = false;

            string iconPath = GetContentIconPath(tile.Content);
            if (!string.IsNullOrEmpty(iconPath))
            {
                var icon = CreateImage("Content", root, iconPath, Color.white);
                icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                icon.rectTransform.sizeDelta = CellSize(state) * 0.42f;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
            }
        }

        private void RenderHandTile(MapTileDefinition tile)
        {
            if (tileHandPrefab != null)
            {
                MapTileHandView view = Instantiate(tileHandPrefab, handArea, false);
                view.name = $"Hand_{tile.Id}";
                view.Initialize(this, tile);
                _handObjects.Add(view.gameObject);
                return;
            }

            var root = CreateUiObject($"Hand_{tile.Id}", handArea);
            root.sizeDelta = new Vector2(86f, 104f);
            _handObjects.Add(root.gameObject);

            var background = CreateImage("Frame", root, MapAssetPath.TileFrame, Color.white);
            Stretch(background.rectTransform);
            background.raycastTarget = true;

            var shape = CreateImage("Shape", root, MapAssetPath.GetTileShapePath(tile.Shape), Color.white);
            shape.rectTransform.anchorMin = new Vector2(0.1f, 0.28f);
            shape.rectTransform.anchorMax = new Vector2(0.9f, 0.94f);
            shape.rectTransform.offsetMin = shape.rectTransform.offsetMax = Vector2.zero;
            shape.preserveAspect = true;
            shape.raycastTarget = false;

            string iconPath = GetContentIconPath(tile.Content);
            if (!string.IsNullOrEmpty(iconPath))
            {
                var icon = CreateImage("Content", root, iconPath, Color.white);
                icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, 0.56f);
                icon.rectTransform.sizeDelta = new Vector2(28f, 28f);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
            }

            var label = CreateTmpText("Label", root, tile.Content.ToString(), 12, Color.white);
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(1f, 0f);
            label.rectTransform.pivot = new Vector2(0.5f, 0f);
            label.rectTransform.anchoredPosition = new Vector2(0f, 4f);
            label.rectTransform.sizeDelta = new Vector2(0f, 18f);

            var drag = root.gameObject.AddComponent<MapTileHandView>();
            drag.Initialize(this, tile, background);
        }

        private bool TryGetGridPosition(Vector2 screenPosition, out MapGridPosition position)
        {
            position = default;
            MapRuntimeState state = mapSystemHost.CurrentState;
            if (state == null || tileLayer == null) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(tileLayer, screenPosition, canvas.worldCamera, out Vector2 local)) return false;

            Vector2 tileSize = GetTileSize();
            int column = Mathf.FloorToInt(local.x / tileSize.x);
            int row = Mathf.FloorToInt(local.y / tileSize.y);
            if (column < 0 || column >= state.Width || row < 0 || row >= state.Height) return false;
            position = new MapGridPosition(column, row);
            return true;
        }

        private Vector2 GridToLocalPosition(MapGridPosition position, MapRuntimeState state)
        {
            Vector2 size = CellSize(state);
            return new Vector2(size.x * (position.Column + 0.5f), size.y * (position.Row + 0.5f));
        }

        private Vector2 GridToFixedLocationCenter(MapGridPosition bottomLeft, MapRuntimeState state)
        {
            Vector2 cellSize = CellSize(state);
            float offset = MapRuntimeState.FixedLocationSize * 0.5f;
            return new Vector2(
                cellSize.x * (bottomLeft.Column + offset),
                cellSize.y * (bottomLeft.Row + offset));
        }

        private Vector2 CellSize(MapRuntimeState state)
        {
            return GetTileSize();
        }

        /// <summary>
        /// TileLayer 的局部原点是格子 (0,0) 的左下角；每个格子的长度直接取 TilePrefab 根 RectTransform。
        /// 因此地图不会依赖 TileLayer 总尺寸，手动扩展地图范围也不改变已放置地块坐标。
        /// </summary>
        private Vector2 GetTileSize()
        {
            RectTransform tileRect = tileHandPrefab != null ? tileHandPrefab.transform as RectTransform : null;
            if (tileRect != null && tileRect.sizeDelta.x > 0f && tileRect.sizeDelta.y > 0f)
                return tileRect.sizeDelta;
            return new Vector2(100f, 100f);
        }

        private bool ValidateUiBindings()
        {
            if (canvas == null || mapArea == null || tileLayer == null || handArea == null || characterContainer == null || statusText == null || budgetText == null)
            {
                Debug.LogError("[MapPanel] UI 绑定不完整：请在 Inspector 依次绑定 Canvas、Map Area、Tile Layer、Hand Area、Character Container、Status Text、Budget Text。", this);
                return false;
            }
            if (tileHandPrefab == null)
            {
                Debug.LogError("[MapPanel] 未找到 TilePrefab：请绑定 Tile Hand Prefab，或放入 Resources/Map/Prefab/TilePrefab。", this);
                return false;
            }
            return true;
        }

        private static RectTransform CreateUiObject(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static Image CreateImage(string name, RectTransform parent, string resourcePath, Color color)
        {
            var rect = CreateUiObject(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = Resources.Load<Sprite>(resourcePath);
            image.color = color;
            return image;
        }

        private static TMP_Text CreateTmpText(string name, RectTransform parent, string content, int fontSize, Color color)
        {
            var rect = CreateUiObject(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            return text;
        }

        private void RenderCharacters()
        {
            Clear(_characterObjects);
            if (characterPrefab == null)
            {
                Debug.LogError("[MapPanel] 未找到 MapCharacterPrefab：请绑定 Character Prefab，或确认 Resources/Map/Prefab/MapCharacterPrefab 存在。", this);
                return;
            }

            GameManager.EnsureInstance();
            List<CharacterRuntimeState> team = CharacterSystem.GetActiveTeam();
            foreach (CharacterRuntimeState runtimeState in team)
            {
                if (runtimeState == null) continue;
                CharaterInfo characterInfo = ConfigLoader.Tables?.TbCharaterInfo?.GetOrDefault(runtimeState.CharacterId);
                if (characterInfo == null)
                {
                    Debug.LogWarning($"[MapPanel] 找不到角色配置：{runtimeState.CharacterId}", this);
                    continue;
                }

                MapCharacter character = Instantiate(characterPrefab, characterContainer, false);
                character.name = $"MapCharacter_{runtimeState.CharacterId}";
                character.Initialize(characterInfo);
                character.UpdateHp(runtimeState.CurrentHp, characterInfo.BaseHp);
                character.ShowExpBar(true);
                character.UpdateExpDisplay();
                _characterObjects.Add(character.gameObject);
            }
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Clear(List<GameObject> objects)
        {
            foreach (GameObject item in objects)
            {
                if (item != null) Destroy(item);
            }
            objects.Clear();
        }

        private static string GetContentIconPath(MapTileContent content)
        {
            return MapAssetPath.GetContentIconPath(content);
        }

        private void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
        }

        private static string GetStageText(MapRunStage stage)
        {
            switch (stage)
            {
                case MapRunStage.ExploringBeforeRuins: return "目标：古国遗迹";
                case MapRunStage.ExploringAfterRuins: return "目标：灰雾深处";
                case MapRunStage.AwaitingAncientRuinsBattle: return "古国遗迹战斗待结算";
                case MapRunStage.AwaitingFinalBoss: return "终点 Boss 战斗待结算";
                case MapRunStage.AwaitingChaseBoss: return "黑雾追上了队伍";
                case MapRunStage.Completed: return "本局完成";
                case MapRunStage.Failed: return "本局失败";
                default: return "遭遇结算中";
            }
        }
    }
}
