using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PFStar
{
    public struct MoveSettings : IComponentData
    {
        public float speed;
        public float defaultSpeed;
        public float startDelay;
        public bool isMoving;
        public bool targetCellBlocked;
        public float3 targetCellPos;
    }
    
    public class MoveSettingsAuthoring : MonoBehaviour 
    {
        public bool isMoving;

        [Range(0f, 100f)]
        public float speed;

        [Tooltip("Delay before character will can start to move. Used after full stop")]
        [Range(0.1f, 0.9f)]
        public float startDelay = 0.2f;

        public class MoveSettingsBaker : Baker<MoveSettingsAuthoring> 
        {
            public override void Bake(MoveSettingsAuthoring authoring) 
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                var ms = new MoveSettings() 
                {
                    speed        = authoring.speed,
                    startDelay   = authoring.startDelay,
                    isMoving     = authoring.isMoving,
                    defaultSpeed = authoring.speed,
                    targetCellBlocked = false,
                    targetCellPos     = float3.zero
                };

                AddComponent(entity, ms);
            }
        }
    }
}