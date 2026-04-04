using Core.Gameplay;
using Core.Input;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Core.Camera
{
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
    public partial class CameraOrbitSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton<RawInputFrame>(out var input)) return;
            if (!SystemAPI.TryGetSingleton<CameraOrbitSettings>(out var orbit)) return;
            if (!SystemAPI.ManagedAPI.TryGetSingleton<MainCameraTag>(out var cam)) return;

            float3 target = float3.zero;
            if (SystemAPI.TryGetSingletonEntity<PlayerTag>(out var player))
            {
                var ltwLookup = SystemAPI.GetComponentLookup<LocalToWorld>(true);
                if (ltwLookup.HasComponent(player))
                    target = ltwLookup[player].Position;
            }

            if (input.RightHeld)
            {
                orbit.Yaw += input.OrbitDelta.x * orbit.OrbitSpeed;
                orbit.Yaw %= 360f;
            }

            orbit.Distance = math.clamp(
                orbit.Distance - input.ScrollDelta * orbit.ZoomSpeed,
                orbit.MinDistance,
                orbit.MaxDistance);

            SystemAPI.SetSingleton(orbit);

            float yawRad = math.radians(orbit.Yaw);
            float pitchRad = math.radians(orbit.Pitch);

            float3 offset = new float3(
                orbit.Distance * math.sin(yawRad) * math.cos(pitchRad),
                orbit.Distance * math.sin(pitchRad),
                -orbit.Distance * math.cos(yawRad) * math.cos(pitchRad));

            Transform camTransform = cam.CameraTransform;

            float damping = math.saturate(SystemAPI.Time.DeltaTime * 12f);
            camTransform.position = Vector3.Lerp(
                camTransform.position,
                target + offset,
                damping);

            camTransform.LookAt(target);
        }
    }
}