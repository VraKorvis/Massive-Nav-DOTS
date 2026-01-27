using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe; 
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace PFStar
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public unsafe partial struct PathRequestUpdateSystem : ISystem
    {
        private const int MaxRequestsPerFrame = 32;
        private NativeArray<int> _requestsCounter;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<PathTargetData>();

            _requestsCounter = new NativeArray<int>(1, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            _requestsCounter.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _requestsCounter[0] = 0;

            var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();

            var job = new PathRequestStatusJob
            {
                TargetDataLookup = SystemAPI.GetComponentLookup<PathTargetData>(true),
                TargetChangedLookup = SystemAPI.GetComponentLookup<TargetChangedTag>(true),
                NoneTagLookup = SystemAPI.GetComponentLookup<PathAgentStatusNoneTag>(true),
                SignificantMoveLookup = SystemAPI.GetComponentLookup<PathAgentStatusSignificantMoveTag>(true),

                GridOrigin = SystemAPI.GetSingleton<GridSettings>().Origin,
                CurrentTime = (float)state.WorldUnmanaged.Time.ElapsedTime,
                MaxRequests = MaxRequestsPerFrame,

                Counter = _requestsCounter,
                ECB = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter()
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        [WithAny(typeof(PathAgentStatusNoneTag), typeof(PathAgentStatusSignificantMoveTag), typeof(TargetChangedTag))]
        [BurstCompile]
        public partial struct PathRequestStatusJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<PathTargetData> TargetDataLookup;
            [ReadOnly] public ComponentLookup<TargetChangedTag> TargetChangedLookup;
            [ReadOnly] public ComponentLookup<PathAgentStatusNoneTag> NoneTagLookup;
            [ReadOnly] public ComponentLookup<PathAgentStatusSignificantMoveTag> SignificantMoveLookup;
            public float3 GridOrigin;
            public float CurrentTime;
            public int MaxRequests;

            public NativeArray<int> Counter;
            public EntityCommandBuffer.ParallelWriter ECB;

            void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndex,
                RefRO<LocalTransform> transform,
                RefRW<PathRequestAgent> request,
                RefRW<PathAgentStatus> status)
            {
                Entity myTarget = request.ValueRO.focus;
                if (!TargetDataLookup.HasComponent(myTarget)) return;

                int2 actualTargetCell = TargetDataLookup[myTarget].CurrentCell;

                bool isIdle = NoneTagLookup.IsComponentEnabled(entity);
                bool targetMoved = TargetChangedLookup.IsComponentEnabled(myTarget);
                bool isUrgent = SignificantMoveLookup.IsComponentEnabled(entity);
                bool isSignificant = SignificantMoveLookup.IsComponentEnabled(entity);
                
                bool cooldownOver = CurrentTime >= request.ValueRO.NextAllowedUpdateTime;
                
                bool needsUpdate = targetMoved || isUrgent || (isIdle && cooldownOver);
                
                if (!needsUpdate) return;
                
                if (request.ValueRO.destination.Equals(actualTargetCell))
                {
                    ECB.SetComponentEnabled<PathAgentStatusSignificantMoveTag>(chunkIndex, entity, false);
                    return;
                }

                if (!isSignificant && CurrentTime < request.ValueRO.NextAllowedUpdateTime) return;

                int* ptr = (int*)Counter.GetUnsafePtr();
                int currentRequestIndex = System.Threading.Interlocked.Increment(ref ptr[0]);

                if (currentRequestIndex > MaxRequests) return;
                
                request.ValueRW.destination = TargetDataLookup[myTarget].CurrentCell;
                request.ValueRW.startCoord = GridUtils.WorldToCellCoord(transform.ValueRO.Position, GridOrigin);

                var random = new Random((uint)(entity.Index + (uint)(CurrentTime * 1000)) + 1);
                request.ValueRW.NextAllowedUpdateTime = CurrentTime + 0.5f + random.NextFloat(0.0f, 1.0f);

                status.ValueRW.Value = AgentStatus.Find;

                ECB.SetComponentEnabled<PathAgentStatusFindTag>(chunkIndex, entity, true);
                ECB.SetComponentEnabled<PathAgentStatusNoneTag>(chunkIndex, entity, false);
                ECB.SetComponentEnabled<PathAgentStatusProcessTag>(chunkIndex, entity, false);

                ECB.SetComponentEnabled<PathAgentStatusSignificantMoveTag>(chunkIndex, entity, false);
                
            }
        }
    }
}