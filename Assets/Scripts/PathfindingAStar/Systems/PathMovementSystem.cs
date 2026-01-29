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
                float step = moveData.speed * DeltaTime;

                if (distSq <= step * step)
                {
                    transform.Position = targetPos;
                    way.RemoveAt(lastIndex);
                }
                else
                {
                    float3 dir = toTarget * math.rsqrt(distSq);
                    transform.Position = currentPos + dir * step;

                    moveData.targetCellPos = targetPos;
                }
            }
        }
    }
}
