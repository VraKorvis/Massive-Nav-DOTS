using Core.PathfindingAStar;
using Unity.Entities;
using UnityEngine;

namespace Core.Gameplay
{
    public struct MinionTag : IComponentData { }

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
            }
        }
    }
}