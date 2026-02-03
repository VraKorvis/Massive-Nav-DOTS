using Unity.Entities;
using UnityEngine;

namespace Gameplay
{
    public struct AntTag : IComponentData { }

    public class AntTagAuthoring : MonoBehaviour
    {
        public class EnemyAntTagBaker : Baker<AntTagAuthoring>
        {
            public override void Bake(AntTagAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<AntTag>(entity);
            }
        }
    }
}