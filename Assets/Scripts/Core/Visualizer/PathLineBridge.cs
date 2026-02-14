using UnityEngine;

namespace Core.Visualizer
{
    public class PathLineBridge : MonoBehaviour
    {
        public LineRenderer lineRenderer;
        public static PathLineBridge Instance;

        private void Awake()
        {
            Instance = this;
            if (lineRenderer == null) lineRenderer = GetComponent<LineRenderer>();
        }
        public void UpdatePath(Vector3[] positions)
        {
            lineRenderer.enabled = true;
            lineRenderer.positionCount = positions.Length;
            lineRenderer.SetPositions(positions);
        }

        public void ClearPath() => lineRenderer.enabled = false;
    }
}