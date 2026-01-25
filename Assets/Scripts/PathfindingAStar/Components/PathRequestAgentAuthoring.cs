using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public struct PathRequestAgent : IComponentData {
    public Entity focus;
    public Entity owner;
    public int2 startCoord;
    public int2 destination;
}

// public class PathRequestAgentAuthoring : MonoBehaviour {
//     
// }
//
// public class PathRequestAgentBaker : Baker<PathRequestAgentAuthoring>
// {
//     public override void Bake(PathRequestAgentAuthoring authoring)
//     {
//         var entity = GetEntity(TransformUsageFlags.Dynamic);
//         
//         AddComponent(entity, new PathRequestAgent());
//         
//         AddComponent(entity, new PathAgentStatusNoneTag());
//         
//         AddBuffer<Waypoint>(entity);
//     }
// }