using UnityEngine;
using UnityEngine.EventSystems;

namespace Ashlight.UI.Map
{
    /// <summary>Invisible map input surface used for click-to-place.</summary>
    public sealed class MapPlacementSurface : MonoBehaviour, IPointerClickHandler
    {
        private MapPanel _panel;

        public void Initialize(MapPanel panel)
        {
            _panel = panel;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                _panel?.TryPlaceSelectedTile(eventData);
        }
    }
}
