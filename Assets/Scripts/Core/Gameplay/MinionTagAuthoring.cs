using Core.PathfindingAStar;
using Features.OptRenderer;
using Unity.Entities;
using UnityEngine;

namespace Core.Gameplay
{
    public struct MinionTag : IComponentData {}

    public class MinionTagAuthoring : MonoBehaviour
    {
        public class EnemyAntTagBaker : Baker<MinionTagAuthoring>
        {
            public override void Bake(MinionTagAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<MinionTag>(entity);
                AddComponent<DynamicTargetTrackingMarkerTag>(entity);
                SetComponentEnabled<DynamicTargetTrackingMarkerTag>(entity, true);
                
                AddComponent(entity, new DensityCullingData
                {
                    Visibility = 1f,
                    ShouldBeVisible = true
                });
                
                AddComponent(entity, new GpuVisibilityProperty { Value = 1.0f });
                AddComponent(entity, new GpuAgentVisualParams { Value = 1.0f });
            }
        }
    }
}