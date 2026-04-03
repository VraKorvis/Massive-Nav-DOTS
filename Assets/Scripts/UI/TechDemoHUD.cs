using UnityEngine;
using TMPro;
using Unity.Entities;
using System.Collections.Generic;
using System.Linq;

namespace UI
{
    public class TechDemoHUD : MonoBehaviour
    {
        [Header("UI Reference")]
        public TextMeshProUGUI StatsText;

        [Header("Settings")]
        [Range(0.1f, 1f)]
        public float UpdateInterval = 0.2f;

        private EntityQuery _agentQuery;
        private float _timer;

        private readonly List<float> _frameTimes = new List<float>();
        private const int MaxSamples = 100;

        void Start()
        {
            _agentQuery = World.DefaultGameObjectInjectionWorld.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Unity.Transforms.LocalTransform>()
            );
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _frameTimes.Add(dt);
            if (_frameTimes.Count > MaxSamples) _frameTimes.RemoveAt(0);

            _timer += dt;
            if (_timer >= UpdateInterval)
            {
                UpdateDisplay();
                _timer = 0f;
            }
        }

        void UpdateDisplay()
        {
            if (StatsText == null) return;

            int entityCount = _agentQuery.CalculateEntityCount();

            float currentDt = _frameTimes.Last();
            float fps = 1f / currentDt;
            float ms = currentDt * 1000f;
            
            var sortedFrames = new List<float>(_frameTimes);
            sortedFrames.Sort();
            float onePercentLow = 1f / sortedFrames.Last();

            StatsText.text =
                $"<color=#00FF00>AGENTS:</color> {entityCount:N0}\n" +
                $"<color=#00FFFF>FPS:</color> {fps:F1} ({ms:F2} ms)\n" +
                $"<color=#FFCC00>1% LOW:</color> {onePercentLow:F1} FPS\n" +
                $"<color=#BBBBBB>ENGINE:</color> Unity 6 (ECS/DOTS)";
        }
    }
}
