using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PathfindingAStar
{
    public struct Heading : IComponentData {
        public int2 VectorDirection;
    }
    
    public class HeadingAuthoring : MonoBehaviour 
    {
        [Tooltip("Direction of motion character")]
        public MoveDirection direction;

        public class HeadingBaker : Baker<HeadingAuthoring> 
        {
            public override void Bake(HeadingAuthoring authoring) 
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                var headingData = new Heading 
                {
                    VectorDirection = GridUtils.GetDir(authoring.direction)
                };

                AddComponent(entity, headingData);
            }
        }
    }
    
}