using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace PFStar
{
    public struct MoveSettings : IComponentData
    {
        public float Speed;
        public float3 Velocity;
        public float StartDelay;
        public float3 TargetCellPos;

        public float RotSpeed;
        public float Acceleration;
        public float MinSpeedMul;
        public float MaxSpeedMul;
        public float VerticalSmoothSpeed;
        public float ArrivalRadiusFactor;
        public float MaxClimbRateFactor;
    }
    
    public class MoveSettingsAuthoring : MonoBehaviour 
    {
        [Header("Base Movement")]
        public bool isMoving;
        [Range(0f, 100f)] public float speed = 10f;

        [Tooltip("Delay before character can start to move. Helps to avoid 'jitter' on rapid path changes.")]
        [Range(0f, 2f)] public float startDelay = 0.1f;
        
        [Header("Physics & Feeling")]
        [Tooltip("How fast the character rotates towards movement direction.")]
        public float rotSpeed = 10f;

        [Tooltip("Responsiveness of movement. High values = instant start/stop, Low values = heavy/slippery feeling.")]
        public float acceleration = 12f;

        [Header("Terrain Interaction")]
        [Tooltip("Multiplier for speed on difficult terrain (high weights in Grid).")]
        public float minSpeedMul = 0.7f;
        [Tooltip("Multiplier for speed on 'fast' terrain (low weights in Grid).")]
        public float maxSpeedMul = 2.0f;

        [Tooltip("Speed of height adjustment. Prevents snapping when walking on bumpy surfaces.")]
        public float verticalSmoothSpeed = 6f;

        [Tooltip("Maximum height difference the agent can step up/down per second, relative to cell size.")]
        public float maxClimbRateFactor = 1.2f;

        [Header("Path Following")]
        [Tooltip("Distance to waypoint to consider it reached. Multiplied by Grid Cell Size.")]
        public float arrivalRadiusFactor = 0.45f;
        
        public class MoveSettingsBaker : Baker<MoveSettingsAuthoring> 
        {
            public override void Bake(MoveSettingsAuthoring authoring) 
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                var ms = new MoveSettings() 
                {
                    Speed        = authoring.speed,
                    StartDelay = authoring.startDelay,
                    RotSpeed = authoring.rotSpeed,
                    TargetCellPos     = authoring.transform.position,
                    Acceleration = authoring.acceleration,
                    MinSpeedMul = authoring.minSpeedMul,
                    MaxSpeedMul = authoring.maxSpeedMul,
                    VerticalSmoothSpeed = authoring.verticalSmoothSpeed,
                    ArrivalRadiusFactor = authoring.arrivalRadiusFactor,
                    MaxClimbRateFactor = authoring.maxClimbRateFactor,
                };

                AddComponent(entity, ms);
            }
        }
    }
}