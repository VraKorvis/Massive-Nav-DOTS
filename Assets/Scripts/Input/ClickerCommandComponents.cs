using Unity.Entities;
using Unity.Mathematics;

namespace Gameplay.Player
{
    public struct ClickEntityTag : IComponentData { }

    public struct ClickEventData : IComponentData
    {
        public float3 WorldPosition;
        public int MouseButton;
    }
    public struct IsClickTag : IComponentData, IEnableableComponent {}
    
    
    
    public struct ClickMarkerTag : IComponentData { }
    public struct MoveToCommand : IComponentData, IEnableableComponent { public float3 WorldPosition; }
    public struct GatherCommand : IComponentData { public Entity ResourceEntity; }
}