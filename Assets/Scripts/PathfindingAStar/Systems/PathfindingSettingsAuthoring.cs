using UnityEngine;
using Unity.Entities;

namespace PFStar
{
    public struct PathfindingSettings : IComponentData
    {
        public int MaxRequestsPerFrame;
        public int MaxPossibleAgents;
        public int MaxPerFrame;
        public int IterationLimit;
        public float GreedyCoef;
        public int InnerLoopBatchSize;
    }
    
    public class PathfindingSettingsAuthoring : MonoBehaviour
    {
        [Header("Requests (PathRequestUpdateSystem)")]
        [Tooltip("Hard limit on how many agents can issue a pathfinding request in a single frame. Prevents buffer overflow. Recommended: 1000-5000.")]
        [Range(100, 10000)]
        public int maxRequestsPerFrame = 5000;

        [Header("A* Logic (PathFindingSystem)")]
        [Tooltip("Max entities to pre-allocate memory for. Recommended: Matches your average unit count (e.g., 1024-5000).")]
        [Range(100, 10000)]
        public int maxPossibleAgents = 1024;

        [Tooltip("Max requests processed per frame. Recommended: 128-512 (higher = smoother movement, lower = higher FPS).")]
        [Range(100, 10000)]
        public int maxPerFrame = 512;

        [Tooltip("A* search depth limit. Recommended: 500-2000 depending on obstacle or map complexity.")]
        [Range(100, 10000)]
        public int iterationLimit = 1000;

        [Tooltip("Heuristic multiplier. Recommended: 1.0 (perfect path) to 2.0 (fastest search).")]
        [Range(1, 5)]
        public float greedyCoef = 1.5f;

        [Tooltip("Entities per worker thread. Recommended: 32, 64, or 128.")]
        [Range(1, 512)]
        public int innerLoopBatchSize = 64;

        public class Baker : Baker<PathfindingSettingsAuthoring>
        {
            public override void Bake(PathfindingSettingsAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new PathfindingSettings
                {
                    MaxRequestsPerFrame = authoring.maxRequestsPerFrame,
                    MaxPossibleAgents = authoring.maxPossibleAgents,
                    MaxPerFrame = authoring.maxPerFrame,
                    IterationLimit = authoring.iterationLimit,
                    GreedyCoef = authoring.greedyCoef,
                    InnerLoopBatchSize = authoring.innerLoopBatchSize
                });
            }
        }
    }
}