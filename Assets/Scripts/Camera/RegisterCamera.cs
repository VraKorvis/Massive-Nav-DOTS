using Unity.Entities;
using UnityEngine;

namespace Camera
{
    public class RegisterCamera : MonoBehaviour
    {
        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var entityManager = world.EntityManager;

            Entity cameraEntity = entityManager.CreateEntity();
        
            entityManager.AddComponentData(cameraEntity, new MainCameraTag 
            { 
                CameraTransform = this.transform 
            });
        }
    }
}