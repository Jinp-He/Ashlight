using UnityEngine;
using UnityEngine.UI;

namespace Ashlight.UI.Map
{
    /// <summary>White pulse used to indicate a currently legal tile placement cell.</summary>
    [RequireComponent(typeof(Image))]
    public sealed class MapPlacementPulse : MonoBehaviour
    {
        [SerializeField] private float pulseSpeed = 3.5f;
        [SerializeField] private float minAlpha = 0.12f;
        [SerializeField] private float maxAlpha = 0.48f;

        private Image _image;

        private void Awake()
        {
            _image = GetComponent<Image>();
        }

        private void Update()
        {
            if (_image == null) return;
            float t = (Mathf.Sin(Time.unscaledTime * pulseSpeed) + 1f) * 0.5f;
            Color color = _image.color;
            color.a = Mathf.Lerp(minAlpha, maxAlpha, t);
            _image.color = color;
        }
    }
}
