using Unity.Entities;
using UnityEngine;

namespace Core.Input
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateBefore(typeof(ClickClassificationSystem))]
    public partial class ClickCreatorSystem : SystemBase
    {
        private PlayerInputSystemActions _inputActions;

        private UnityEngine.Camera _mainCamera;

        protected override void OnCreate()
        {
            _inputActions = new PlayerInputSystemActions();
            _inputActions.Player.Enable();
            _mainCamera = UnityEngine.Camera.main;
            RequireForUpdate<InputBlockStatus>();
        }

        protected override void OnUpdate()
        {
            if (_mainCamera == null) _mainCamera = UnityEngine.Camera.main;
            if (_mainCamera == null || !_inputActions.Player.Click.WasPressedThisFrame()) return;
            
            if (SystemAPI.GetSingleton<InputBlockStatus>().IsBlocked) return;
            
            if (UnityEngine.InputSystem.Pointer.current == null) return;
            
            Vector2 screenPos = _inputActions.Player.Point.ReadValue<Vector2>();
            var ray = _mainCamera.ScreenPointToRay(screenPos);
            
            if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float dist))
            {
                var worldPos = ray.GetPoint(dist);
                var queue = SystemAPI.GetSingleton<ClickEventQueue>().Queue;
                bool leftPressed = _inputActions.Player.Click.WasPressedThisFrame();
                int buttonIndex = leftPressed ? 0 : 1;
                
                queue.Enqueue(new ClickEntry()
                {
                    WorldPosition = worldPos,
                    MouseButton = buttonIndex,
                });
            }
        }

        protected override void OnDestroy()
        {
            _inputActions.Player.Disable();
            _inputActions.Dispose();
        }
    }
}