using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace PFStar
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PathFindingSystem))]
    [BurstCompile] 
    public partial struct PathMovementSystem : ISystem 
    {
        private const float Threshold = 0.001f;
        private const float RotSpeed = 10f;
        
        [BurstCompile]
        public void OnUpdate(ref SystemState state) 
        {
            var moveJob = new PathMoveJob { DeltaTime = SystemAPI.Time.DeltaTime };
            state.Dependency = moveJob.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        public partial struct PathMoveJob : IJobEntity
        {
            public float DeltaTime;

            private void Execute(
                ref DynamicBuffer<Waypoint> way,
                ref MoveSettings moveData,
                ref LocalTransform transform)
            {
                if (way.IsEmpty) return;

                int lastIndex = way.Length - 1;
                float3 targetPos = way[lastIndex].point;
                float3 currentPos = transform.Position;

                float3 toTarget = targetPos - currentPos;
                
                float distSq = math.lengthsq(toTarget);
                
                if (distSq > Threshold) 
                {
                    float3 direction = toTarget * math.rsqrt(distSq);
                    if (distSq < 100f) 
                    {
                        quaternion targetRotation = quaternion.LookRotationSafe(direction, math.up());
                        transform.Rotation = math.slerp(transform.Rotation, targetRotation, DeltaTime * RotSpeed);
                    }
                    
                    float step = moveData.speed * DeltaTime;

                    if (distSq <= step * step)
                    {
                        transform.Position = targetPos;
                        way.RemoveAt(lastIndex);
                    }
                    else
                    {
                        transform.Position = currentPos + direction * step;
                        moveData.targetCellPos = targetPos;
                    }
                }
                else
                {
                    way.RemoveAt(lastIndex);
                }
            }
        }
    }
}
