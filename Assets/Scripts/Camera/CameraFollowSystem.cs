using Gameplay.Player;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using Unity.Mathematics;

namespace Camera
{
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(LocalToWorldSystem))] 
    public partial class CameraFollowSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Dependency.Complete(); 

            if (!SystemAPI.TryGetSingletonEntity<PlayerTag>(out Entity player)) return;

            var ltw = EntityManager.GetComponentData<LocalToWorld>(player);
            float3 targetPos = ltw.Position;

            if (SystemAPI.ManagedAPI.TryGetSingleton<MainCameraTag>(out var cameraTag))
            {
                if (cameraTag.CameraTransform == null) return;

                Vector3 offset = new Vector3(0, 60, -40);
                Vector3 desiredPos = (Vector3)targetPos + offset;

                Transform camTransform = cameraTag.CameraTransform;
                camTransform.position = Vector3.Lerp(camTransform.position, desiredPos, 0.1f);
                camTransform.LookAt(targetPos);
            }
        }
    }
}