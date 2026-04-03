using UnityEngine;

namespace UI
{
    public class SafeAreaFit : MonoBehaviour
    {
        private RectTransform _rectTransform;
        private Rect _lastSafeArea = Rect.zero;

        void Awake() => _rectTransform = GetComponent<RectTransform>();

        void Update()
        {
            if (_lastSafeArea != Screen.safeArea) ApplySafeArea();
        }

        void ApplySafeArea()
        {
            _lastSafeArea = Screen.safeArea;
            var anchorMin = _lastSafeArea.position;
            var anchorMax = _lastSafeArea.position + _lastSafeArea.size;

            anchorMin.x /= Screen.width;
            anchorMin.y /= Screen.height;
            anchorMax.x /= Screen.width;
            anchorMax.y /= Screen.height;

            _rectTransform.anchorMin = anchorMin;
            _rectTransform.anchorMax = anchorMax;
        }
    }
}
