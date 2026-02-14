using Unity.Entities;
using UnityEngine;

namespace Core.Input
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class ClickCreatorSystem : SystemBase
    {
        private PlayerInputSystemActions _inputActions;

        private UnityEngine.Camera _mainCamera;

        protected override void OnCreate()
        {
            _inputActions = new PlayerInputSystemActions();
            _inputActions.Player.Enable();
            _mainCamera = UnityEngine.Camera.main;
        }

        protected override void OnUpdate()
        {
            if (_mainCamera == null) _mainCamera = UnityEngine.Camera.main;
            if (_mainCamera == null || !_inputActions.Player.Click.WasPressedThisFrame()) return;

            Vector2 mousePos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
            
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return;
            
            var ray = _mainCamera.ScreenPointToRay(mousePos);
    
            if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float dist))
            {
                int button = mouse.leftButton.wasPressedThisFrame ? 0 : 1;
                var worldPos = ray.GetPoint(dist);
                var queue = SystemAPI.GetSingleton<ClickEventQueue>().Queue;
                queue.Enqueue(new ClickEntry()
                {
                    WorldPosition = worldPos,
                    MouseButton = button,
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