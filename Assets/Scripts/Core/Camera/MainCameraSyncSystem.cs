using Unity.Entities;
using Unity.Mathematics;

namespace Core.Camera
{
    public struct MainCameraData : IComponentData
    {
        public float3 Position;
        public float3 Forward;
    }

    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class MainCameraSyncSystem : SystemBase
    {
        protected override void OnCreate()
        {
            EntityManager.CreateSingleton<MainCameraData>();
        }

        protected override void OnUpdate()
        {
            var mainCam = UnityEngine.Camera.main;
            if (mainCam == null) return;

            SystemAPI.SetSingleton(new MainCameraData
            {
                Position = mainCam.transform.position,
                Forward = mainCam.transform.forward
            });
        }
    }
}
