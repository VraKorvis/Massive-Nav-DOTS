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
        public int globalThreshold = 50000;
        public int maxAgentsPerCell = 100;
        public float safeDistance = 15f;
        public float fadeSpeed = 5f;
        public bool enableCulling = true;

        public class Baker : Baker<CullingSettingsAuthoring>
        {
            public override void Bake(CullingSettingsAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new CullingSettings
                {
                    GlobalThreshold = authoring.globalThreshold,
                    MaxAntsPerCell = authoring.maxAgentsPerCell,
                    EnableCulling = authoring.enableCulling,
                    SafeDistance = authoring.safeDistance,
                    FadeSpeed = authoring.fadeSpeed,
                });
            }
        }
    }
}