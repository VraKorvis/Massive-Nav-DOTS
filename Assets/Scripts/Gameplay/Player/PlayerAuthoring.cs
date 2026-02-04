using PFStar;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Gameplay.Player
{
    public struct Player : IComponentData
    {
        public float MoveSpeed;
        public float3 InputDirection;
    }
    
    public struct PlayerTag : IComponentData { }

    public class PlayerAuthoring : MonoBehaviour
    {
        public float speed = 15f;

        public class Baker : Baker<PlayerAuthoring>
        {
            public override void Bake(PlayerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic | TransformUsageFlags.Renderable);
                
                AddComponent(entity, new Player 
                { 
                    MoveSpeed = authoring.speed 
                });
                AddComponent(entity, new PlayerTag());
                AddComponent(entity, new PFAgentState()
                {
                    Flags = (byte)PFAgentStatus.Default
                });
                
                AddComponent(entity, new PFRequestAgent()
                {
                    Owner = entity,
                    Focus = Entity.Null,
                    StartCoord = int2.zero,
                    Destination = int2.zero
                });
                
                AddBuffer<Waypoint>(entity);
                AddComponent(entity, new PFRequestMetadata()
                {
                    Priority = 255,
                });
                
                AddComponent(entity, new DynamicTargetTrackingMarkerTag());

               
            }
        }
    }
}