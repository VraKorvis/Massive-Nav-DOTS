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
        public int MaxRequestsPerFrame = 10000;
        
        [Header("A* Logic (PathFindingSystem)")]
        public int MaxPossibleAgents = 1024;
        public int MaxPerFrame = 512;
        public int IterationLimit = 1000;
        public float GreedyCoef = 1.5f;
        public int InnerLoopBatchSize = 64;

        public class Baker : Baker<PathfindingSettingsAuthoring>
        {
            public override void Bake(PathfindingSettingsAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new PathfindingSettings
                {
                    MaxRequestsPerFrame = authoring.MaxRequestsPerFrame,
                    MaxPossibleAgents = authoring.MaxPossibleAgents,
                    MaxPerFrame = authoring.MaxPerFrame,
                    IterationLimit = authoring.IterationLimit,
                    GreedyCoef = authoring.GreedyCoef,
                    InnerLoopBatchSize = authoring.InnerLoopBatchSize
                });
            }
        }
    }
}