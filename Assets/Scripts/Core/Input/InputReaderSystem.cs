using Core.PathfindingAStar;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Core.Input
{
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    public partial class InputReaderSystem : SystemBase
    {
        private PlayerInputSystemActions _actions;
        private UnityEngine.Camera _camera;

        private static readonly Plane GroundPlane = new Plane(Vector3.up, Vector3.zero);

        protected override void OnCreate()
        {
            _actions = new PlayerInputSystemActions();
            _actions.Player.Enable();

            var marker = EntityManager.CreateEntity();
            EntityManager.AddComponent<ClickMarkerTag>(marker);
            EntityManager.AddComponentData(marker, LocalTransform.FromScale(1f));
            EntityManager.AddComponent<NavigationTargetGridData>(marker);
            EntityManager.AddComponent<MoveToCommand>(marker);
            EntityManager.SetComponentEnabled<MoveToCommand>(marker, false);
            
            EntityManager.CreateSingleton<RawInputFrame>();
            EntityManager.CreateSingleton(new InputBlockStatus { IsBlocked = false });

            var queueEntity = EntityManager.CreateSingleton(new ClickEventQueue
            {
                Queue = new Unity.Collections.NativeQueue<float3>(Unity.Collections.Allocator.Persistent)
            });
            EntityManager.AddComponent<ClickEntityTag>(queueEntity);
        }

        protected override void OnUpdate()
        {
            if (_camera == null)
            {
                _camera = UnityEngine.Camera.main;
                if (_camera == null) return;
            }

            var mouse = Mouse.current;
            if (mouse == null) return;

            var frame = new RawInputFrame();

            bool clickedThisFrame = _actions.Player.Click.WasPressedThisFrame();
            bool leftHeld         = mouse.leftButton.isPressed;
            bool isBlocked        = SystemAPI.GetSingleton<InputBlockStatus>().IsBlocked;

            if ((clickedThisFrame || leftHeld) && !mouse.rightButton.isPressed && !isBlocked)
            {
                Vector2 screenPos = _actions.Player.Point.ReadValue<Vector2>();
                Ray ray = _camera.ScreenPointToRay(screenPos);

                if (GroundPlane.Raycast(ray, out float dist))
                {
                    frame.ClickThisFrame  = true;
                    frame.ClickWorldPos   = ray.GetPoint(dist);
                    frame.ClickWorldValid = true;
                }
            }
            
            frame.RightHeld   = mouse.rightButton.isPressed;
            frame.OrbitDelta  = frame.RightHeld ? mouse.delta.ReadValue()
                                                : float2.zero;
            
            float rawScroll  = mouse.scroll.ReadValue().y;
            frame.ScrollDelta = rawScroll * 0.01f; 

            SystemAPI.SetSingleton(frame);
        }

        protected override void OnDestroy()
        {
            _actions.Player.Disable();
            _actions.Dispose();

            if (SystemAPI.TryGetSingleton<ClickEventQueue>(out var clickEvents) && clickEvents.Queue.IsCreated)
                clickEvents.Queue.Dispose();
        }
    }
}