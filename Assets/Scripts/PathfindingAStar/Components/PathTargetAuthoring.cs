using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PathfindingAStar
{
    
    public struct PathTargetData : IComponentData
    {
        public int2 CurrentCell;
        public int2 LastSignificantCell;
    }

    public class PathTargetAuthoring : MonoBehaviour { }

    public class PathTargetBaker : Baker<PathTargetAuthoring>
    {
        public override void Bake(PathTargetAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new PathTargetData 
            { 
                CurrentCell = new int2(int.MinValue) 
            });
        }
    }
}