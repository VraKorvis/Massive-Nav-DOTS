using Unity.Entities;
using UnityEngine;

namespace Camera
{
    public class MainCameraTag : IComponentData
    {
        public Transform CameraTransform; 
    }
}