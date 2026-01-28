using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.InputSystem;

namespace Gameplay.Player
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public partial struct LeaderMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            float3 moveDir = float3.zero;
            if (keyboard.wKey.isPressed) moveDir.z += 1;
            if (keyboard.sKey.isPressed) moveDir.z -= 1;
            if (keyboard.aKey.isPressed) moveDir.x -= 1;
            if (keyboard.dKey.isPressed) moveDir.x += 1;

            if (math.lengthsq(moveDir) > 0) moveDir = math.normalize(moveDir);

            var moveJob = new MoveJob
            {
                Input = moveDir,
                DeltaTime = SystemAPI.Time.DeltaTime
            };
            state.Dependency = moveJob.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        public partial struct MoveJob : IJobEntity
        {
            public float3 Input;
            public float DeltaTime;

            private void Execute(ref LocalTransform transform, in LeaderControl control)
            {
                transform.Position += Input * control.MoveSpeed * DeltaTime;
            }
        }
    }
}