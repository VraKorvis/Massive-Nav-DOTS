using Unity.Entities;
using UnityEngine;

namespace PathfindingAStar
{
    public class TargetAuthoring : MonoBehaviour {
        public class Baker : Baker<TargetAuthoring> {
            public override void Bake(TargetAuthoring authoring) {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new PathTargetTag()); 
            }
        }
    }
    public struct PathTargetTag : IComponentData {}
}