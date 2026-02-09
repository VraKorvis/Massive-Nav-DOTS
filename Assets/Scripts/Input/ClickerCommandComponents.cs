using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Gameplay.Player
{
    public struct ClickEntry
    {
        public float3 WorldPosition;
        public int MouseButton;
    }
    
    public struct ClickEventQueue : IComponentData
    {
        public NativeQueue<ClickEntry> Queue;
    }
    
    public struct ClickEntityTag : IComponentData { }

    public struct ClickMarkerTag : IComponentData { }
    public struct MoveToCommand : IComponentData, IEnableableComponent { public float3 WorldPosition; }
    public struct GatherCommand : IComponentData { public Entity ResourceEntity; }
}