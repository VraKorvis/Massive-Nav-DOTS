using Unity.Entities;
using UnityEngine;

namespace Features.OptRenderer
{
    public struct CullingSettings : IComponentData
    {
        public int GlobalThreshold;
        public int MaxAntsPerCell;
        public float SafeDistance;
        public bool EnableCulling;
        public float FadeSpeed;
    }
    
    public class CullingSettingsAuthoring : MonoBehaviour
    {
        public int GlobalThreshold = 50000;
        public int MaxAgentsPerCell = 100;
        public float SafeDistance = 15f;
        public float FadeSpeed = 5f;
        public bool EnableCulling = false;

        public class Baker : Baker<CullingSettingsAuthoring>
        {
            public override void Bake(CullingSettingsAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new CullingSettings
                {
                    GlobalThreshold = authoring.GlobalThreshold,
                    MaxAntsPerCell = authoring.MaxAgentsPerCell,
                    EnableCulling = authoring.EnableCulling,
                    SafeDistance = authoring.SafeDistance,
                    FadeSpeed = authoring.FadeSpeed,
                });
            }
        }
    }
}