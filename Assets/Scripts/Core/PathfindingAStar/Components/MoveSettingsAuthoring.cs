using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace Core.PathfindingAStar
{
    public struct MoveSettings : IComponentData
    {
        public float Speed;
        public float3 Velocity;
        public float StartDelay;
        public float3 TargetCellPos;
        
        public float PivotOffset;
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
        [Range(0f, 100f)] public float Speed = 10f;

        [Tooltip("Delay before character can start to move. Helps to avoid 'jitter' on rapid path changes.")]
        [Range(0f, 2f)] public float StartDelay = 0.1f;
        
        [Header("Physics & Feeling")]
        [Tooltip("Height Offset")]
        public float PivotOffset;

        [Tooltip("How fast the character rotates towards movement direction.")]
        public float RotSpeed = 10f;

        [Tooltip("Responsiveness of movement. High values = instant start/stop, Low values = heavy/slippery feeling.")]
        public float Acceleration = 12f;

        [Header("Terrain Interaction")]
        [Tooltip("Multiplier for speed on difficult terrain (high weights in Grid).")]
        public float MinSpeedMul = 0.7f;
        [Tooltip("Multiplier for speed on 'fast' terrain (low weights in Grid).")]
        public float MaxSpeedMul = 2.0f;

        [Tooltip("Speed of height adjustment. Prevents snapping when walking on bumpy surfaces.")]
        public float VerticalSmoothSpeed = 6f;

        [Tooltip("Maximum height difference the agent can step up/down per second, relative to cell size.")]
        public float MaxClimbRateFactor = 1.2f;

        [Header("Path Following")]
        [Tooltip("Distance to waypoint to consider it reached. Multiplied by Grid Cell Size.")]
        public float ArrivalRadiusFactor = 0.45f;
        
        public class MoveSettingsBaker : Baker<MoveSettingsAuthoring> 
        {
            public override void Bake(MoveSettingsAuthoring authoring) 
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                var ms = new MoveSettings() 
                {
                    Speed        = authoring.Speed,
                    StartDelay = authoring.StartDelay,
                    PivotOffset = authoring.PivotOffset,
                    RotSpeed = authoring.RotSpeed,
                    TargetCellPos     = authoring.transform.position,
                    Acceleration = authoring.Acceleration,
                    MinSpeedMul = authoring.MinSpeedMul,
                    MaxSpeedMul = authoring.MaxSpeedMul,
                    VerticalSmoothSpeed = authoring.VerticalSmoothSpeed,
                    ArrivalRadiusFactor = authoring.ArrivalRadiusFactor,
                    MaxClimbRateFactor = authoring.MaxClimbRateFactor,
                };

                AddComponent(entity, ms);
            }
        }
    }
}