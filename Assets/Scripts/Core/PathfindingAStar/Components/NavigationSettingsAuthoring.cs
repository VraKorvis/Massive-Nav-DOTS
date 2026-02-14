using UnityEngine;
using Unity.Entities;

namespace Core.PathfindingAStar
{
    public struct NavigationSettings : IComponentData
    {
        public int MaxRequestsPerFrame;
        public int MaxPerFrame;
        public int IterationLimit;
        public float GreedyCoef;
        public int InnerLoopBatchSize;
        
        public int TargetJitterRange;
        
        public float SeparationRadius;
        public float SeparationWeight;
        public float SpatialCellSize;
        
    }
    
    public class NavigationSettingsAuthoring : MonoBehaviour
    {
        [Header("Requests (PathRequestUpdateSystem)")]
        [Tooltip("Hard limit on how many agents can issue a pathfinding request in a single frame. Prevents buffer overflow. Recommended: 1000-5000.")]
        [Range(100, 50000)]
        public int maxRequestsPerFrame = 10000;

        [Header("A* Logic (PathFindingSystem)")]
        [Tooltip("Max entities to pre-allocate memory for. Recommended: Matches your average unit count (e.g., 100-2000).")]
        [Range(100, 10000)]
        public int maxPerFrame = 1024;

        [Tooltip("A* search depth limit. Recommended: 500-2000 depending on obstacle or map complexity.")]
        [Range(100, 10000)]
        public int iterationLimit = 1000;

        [Tooltip("Heuristic multiplier. Recommended: 1.0 (perfect path) to 2.0 (fastest search).")]
        [Range(1, 5)]
        public float greedyCoef = 1.5f;

        [Tooltip("Entities per worker thread. Recommended: 32, 64, or 128.")]
        [Range(1, 512)]
        public int innerLoopBatchSize = 64;
        
        [Header("Crowd Steering")]
        [Tooltip("The radius of the agent's personal zone. If another agent enters this radius, the ant will start to steer away.")]
        [Range(0.1f, 2.0f)]
        public float separationRadius = 1.5f;

        [Tooltip("The strength of the crowd's influence on the course. 0.0 - ignore everyone, 1.0 - very strong repulsion.")]
        [Range(0.0f, 1.0f)]
        public float separationWeight = 0.2f;

        [Tooltip("Spatial Hash cell size. Recommended: separationRadius * 2.0 for optimal performance.")]
        [Range(0.2f, 4.0f)]
        public float spatialCellSize = 3f;
        
        [Tooltip("Target Jitter Range")]
        [Range(0, 100)]
        public int TargetJitterRange = 1;


        public class Baker : Baker<NavigationSettingsAuthoring>
        {
            public override void Bake(NavigationSettingsAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new NavigationSettings
                {
                    MaxRequestsPerFrame = authoring.maxRequestsPerFrame,
                    MaxPerFrame = authoring.maxPerFrame,
                    IterationLimit = authoring.iterationLimit,
                    GreedyCoef = authoring.greedyCoef,
                    InnerLoopBatchSize = authoring.innerLoopBatchSize,
                    SeparationRadius = authoring.separationRadius,
                    SeparationWeight = authoring.separationWeight,
                    SpatialCellSize = authoring.spatialCellSize,
                    
                    TargetJitterRange = authoring.TargetJitterRange,
                });
            }
        }
    }
}