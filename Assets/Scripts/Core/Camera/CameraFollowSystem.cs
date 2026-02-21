using Core.Gameplay;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Core.Camera
{
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
    public partial class CameraFollowSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            
            if (!SystemAPI.TryGetSingletonEntity<PlayerTag>(out Entity player)) return;

            var ltwLookup = SystemAPI.GetComponentLookup<LocalToWorld>(true);
            
            if (!ltwLookup.HasComponent(player)) return;
            float3 targetPos = ltwLookup[player].Position;
            
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