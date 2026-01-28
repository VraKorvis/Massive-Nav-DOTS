using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Gameplay.Player
{
    public struct LeaderControl : IComponentData
    {
        public float MoveSpeed;
        public float3 InputDirection;
    }

    public class LeaderControlAuthoring : MonoBehaviour
    {
        public float speed = 15f;

        public class Baker : Baker<LeaderControlAuthoring>
        {
            public override void Bake(LeaderControlAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic | TransformUsageFlags.Renderable);
                
                AddComponent(entity, new LeaderControl 
                { 
                    MoveSpeed = authoring.speed 
                });
            }
        }
    }
}