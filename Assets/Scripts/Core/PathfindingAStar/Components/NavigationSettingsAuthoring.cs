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
        [Range(1, 50000)]
        public int MaxRequestsPerFrame = 3000;

        [Header("A* Logic (PathFindingSystem)")]
        [Tooltip("Max entities to pre-allocate memory for. Recommended: Matches your average unit count (e.g., 100-2000).")]
        [Range(1, 10000)]
        public int MaxPerFrame = 1024;

        [Tooltip("A* search depth limit. Recommended: 500-2000 depending on obstacle or map complexity.")]
        [Range(100, 10000)]
        public int IterationLimit = 1000;

        [Tooltip("Heuristic multiplier. Recommended: 1.0 (perfect path) to 2.0 (fastest search).")]
        [Range(1, 5)]
        public float GreedyCoef = 1.5f;

        [Tooltip("Entities per worker thread. Recommended: 32, 64, or 128.")]
        [Range(1, 512)]
        public int InnerLoopBatchSize = 64;
        
        [Header("Crowd Steering")]
        [Tooltip("The radius of the agent's personal zone. If another agent enters this radius, the ant will start to steer away.")]
        [Range(1f, 10.0f)]
        public float SeparationRadius = 3f;

        [Tooltip("The strength of the crowd's influence on the course. 0.0 - ignore everyone, 1.0 - very strong repulsion.")]
        [Range(0.0f, 10.0f)]
        public float SeparationWeight = 2f;

        [Tooltip("Spatial Hash cell size. Recommended: separationRadius * 2.0 for optimal performance.")]
        [Range(1f, 4.0f)]
        public float SpatialCellSize = 3f;
        
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
                    MaxRequestsPerFrame = authoring.MaxRequestsPerFrame,
                    MaxPerFrame = authoring.MaxPerFrame,
                    IterationLimit = authoring.IterationLimit,
                    GreedyCoef = authoring.GreedyCoef,
                    InnerLoopBatchSize = authoring.InnerLoopBatchSize,
                    SeparationRadius = authoring.SeparationRadius,
                    SeparationWeight = authoring.SeparationWeight,
                    SpatialCellSize = authoring.SpatialCellSize,
                    
                    TargetJitterRange = authoring.TargetJitterRange,
                });
            }
        }
    }
}