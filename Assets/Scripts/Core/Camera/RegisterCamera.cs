using System;
using Unity.Entities;
using UnityEngine;

namespace Core.Camera
{
    public class RegisterCamera : MonoBehaviour
    {

        public float Yaw = 0f;
        public float Pitch =  45f;
        public float Distance = 25f;
        public float MinDistance = 6f;
        public float MaxDistance = 80f;
        public float OrbitSpeed = 0.35f;
        public float ZoomSpeed = 4f;
        
        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var entityManager = world.EntityManager;

            Entity cameraEntity = entityManager.CreateEntity();
        
            entityManager.AddComponentData(cameraEntity, new MainCameraTag 
            { 
                CameraTransform = transform 
            });
            
            entityManager.AddComponentData(cameraEntity, new CameraOrbitSettings 
            { 
                Yaw = Yaw,
                Pitch = Pitch,
                Distance = Distance,
                MinDistance = MinDistance,
                MaxDistance = MaxDistance,
                OrbitSpeed = OrbitSpeed,
                ZoomSpeed = ZoomSpeed 
            });
        }
    }
}