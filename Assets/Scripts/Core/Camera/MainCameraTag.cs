using Unity.Entities;
using UnityEngine;

namespace Core.Camera
{
    public class MainCameraTag : IComponentData
    {
        public Transform CameraTransform; 
    }
}