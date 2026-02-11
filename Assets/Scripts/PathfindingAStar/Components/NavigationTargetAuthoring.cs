using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PFStar
{
    public struct NavigationTargetGridData : IComponentData
    {
        public int2 CurrentCell;
        public int2 LastSignificantCell;
        public uint Version; 
    }
    
    public struct CrowdThrottlingSettings : IComponentData
    {
        public float RadiusSq; 
        public int RenderEveryNth;    
    }

    public class NavigationTargetAuthoring : MonoBehaviour
    {
        public int2 InitialCell;
    
        [Header("Throttling Settings")]
        public float VisibilityRadius = 5f;
        public int UpdateInterval = 1;
    }

    public class NavigationTargetAuthoringBaker : Baker<NavigationTargetAuthoring>
    {
        public override void Bake(NavigationTargetAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new NavigationTargetGridData 
            { 
                CurrentCell = new int2(int.MinValue),
                Version = 1,
            });
            
            AddComponent(entity, new CrowdThrottlingSettings
            {
                RadiusSq = authoring.VisibilityRadius * authoring.VisibilityRadius,
                RenderEveryNth = authoring.UpdateInterval
            });
            
        }
    }
}