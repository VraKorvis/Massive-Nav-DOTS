using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace PathfindingAStar
{
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
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
                ref Heading headingAuthoring,
                ref LocalTransform transform, 
                in PathAgentStatusProcessTag status)
            {
                if (way.Length == 0) return;

                int lastIndex = way.Length - 1;
                float3 targetPos = way[lastIndex].point;
                float3 currentPos = transform.Position;
            
                float3 dir = math.normalizesafe(targetPos - currentPos);
                headingAuthoring.VectorDirection = new int2((int)math.round(dir.x), (int)math.round(dir.z));

                float3 nextPos = currentPos + dir * moveData.speed * DeltaTime;
            
                float distSq = math.distancesq(targetPos, currentPos);
                float step = moveData.speed * DeltaTime;

                if (distSq <= step * step)
                {
                    transform.Position = targetPos;
                    way.RemoveAt(lastIndex);
                }
                else
                {
                    transform.Position = nextPos;
                }
            
                moveData.targetCellPos = targetPos;
            }
        }
    }
}
