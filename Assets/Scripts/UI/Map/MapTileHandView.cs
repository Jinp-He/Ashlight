using Ashlight.UI.Map;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Ashlight.State.Runtime;
using Ashlight.Systems.Map;
using TMPro;

namespace Ashlight.UI.Map
{
    /// <summary>手牌拼图的拖拽输入组件；落点合法性始终由 MapSystem 决定。</summary>
    public class MapTileHandView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler
    {
        public string TileId { get; private set; }
        public int ClockwiseQuarterTurns { get; private set; }

        [Header("Prefab bindings (optional)")]
        [SerializeField] private Image raycastImage;
        [SerializeField] private Image shapeImage;
        [SerializeField] private Image contentImage;
        [SerializeField] private TMP_Text contentLabel;

        private MapPanel _panel;
        private Image _raycastImage;
        private RectTransform _rectTransform;
        private Canvas _canvas;
        private Transform _originalParent;
        private int _originalSiblingIndex;
        private Vector2 _originalPosition;
        private CanvasGroup _canvasGroup;
        private GameObject _layoutPlaceholder;
        private bool _isDragging;

        public void Initialize(MapPanel panel, MapTileDefinition tile, Image fallbackRaycastImage = null)
        {
            _panel = panel;
            TileId = tile.Id;
            ClockwiseQuarterTurns = 0;
            transform.localRotation = Quaternion.identity;
            _raycastImage = raycastImage != null ? raycastImage : fallbackRaycastImage;
            _rectTransform = transform as RectTransform;
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            if (_raycastImage == null) _raycastImage = GetComponent<Image>();
            if (_raycastImage != null)
            {
                _raycastImage.raycastTarget = true;
                EnsureBlackOutline();
            }
            if (contentLabel == null) contentLabel = GetComponentInChildren<TMP_Text>(true);

            if (shapeImage != null)
            {
                shapeImage.sprite = Resources.Load<Sprite>(MapAssetPath.GetTileShapePath(tile.Shape));
                shapeImage.raycastTarget = false;
            }
            if (contentImage != null)
            {
                contentImage.sprite = Resources.Load<Sprite>(MapAssetPath.GetContentIconPath(tile.Content));
                contentImage.enabled = contentImage.sprite != null;
                contentImage.raycastTarget = false;
            }
            if (contentLabel != null)
                contentLabel.text = tile.Content == MapTileContent.EmptyRoad ? string.Empty : tile.Content.ToString();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || _panel == null || _rectTransform == null) return;
            _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null) return;
            _originalParent = _rectTransform.parent;
            _originalSiblingIndex = _rectTransform.GetSiblingIndex();
            _originalPosition = _rectTransform.anchoredPosition;
            CreateLayoutPlaceholder();
            _rectTransform.SetParent(_canvas.transform, true);
            _rectTransform.SetAsLastSibling();
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.alpha = 0.8f;
            _isDragging = true;
            _panel.BeginTileDrag(this);
        }

        private void Update()
        {
            if (_isDragging && Input.GetMouseButtonDown(1)) RotateClockwise();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_canvas == null || _rectTransform == null) return;
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(_canvas.transform as RectTransform, eventData.position, _canvas.worldCamera, out Vector3 world))
                _rectTransform.position = world;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _isDragging = false;
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.alpha = 1f;
            bool placed = _panel != null && _panel.EndTileDrag(this, eventData);
            if (placed) RemoveLayoutPlaceholder();
            else ReturnToHand();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Right) return;
            RotateClockwise();
        }

        private void RotateClockwise()
        {
            ClockwiseQuarterTurns = (ClockwiseQuarterTurns + 1) % 4;
            transform.localRotation = Quaternion.Euler(0f, 0f, -90f * ClockwiseQuarterTurns);
        }

        private void EnsureBlackOutline()
        {
            var outline = GetComponent<Outline>();
            if (outline == null) outline = gameObject.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = false;
        }

        private void ReturnToHand()
        {
            if (_rectTransform == null || _originalParent == null) return;
            RemoveLayoutPlaceholder();
            _rectTransform.SetParent(_originalParent, false);
            _rectTransform.SetSiblingIndex(_originalSiblingIndex);
            _rectTransform.anchoredPosition = _originalPosition;
            RebuildHandLayout();
        }

        private void CreateLayoutPlaceholder()
        {
            if (_originalParent == null || _layoutPlaceholder != null) return;
            _layoutPlaceholder = new GameObject("TileDragPlaceholder", typeof(RectTransform), typeof(LayoutElement));
            var placeholderRect = (RectTransform)_layoutPlaceholder.transform;
            placeholderRect.SetParent(_originalParent, false);
            placeholderRect.SetSiblingIndex(_originalSiblingIndex);

            var layout = _layoutPlaceholder.GetComponent<LayoutElement>();
            layout.minWidth = _rectTransform.rect.width;
            layout.minHeight = _rectTransform.rect.height;
            layout.preferredWidth = _rectTransform.rect.width;
            layout.preferredHeight = _rectTransform.rect.height;
            layout.flexibleWidth = 0f;
            layout.flexibleHeight = 0f;
            RebuildHandLayout();
        }

        private void RemoveLayoutPlaceholder()
        {
            if (_layoutPlaceholder == null) return;
            _layoutPlaceholder.SetActive(false);
            Destroy(_layoutPlaceholder);
            _layoutPlaceholder = null;
        }

        private void RebuildHandLayout()
        {
            if (_originalParent is RectTransform handRect)
                LayoutRebuilder.ForceRebuildLayoutImmediate(handRect);
        }
    }
}
