using Unity.Entities;

namespace Core.Camera
{
    public struct CameraOrbitSettings : IComponentData
    {
        public float Yaw;           
        public float Pitch;        
        public float Distance;      
        public float MinDistance;
        public float MaxDistance;
        public float OrbitSpeed;   
        public float ZoomSpeed;     
    }
}