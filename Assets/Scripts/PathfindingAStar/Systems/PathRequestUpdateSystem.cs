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
        private const int MaxRequestsPerFrame = 256;
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
            
            var job = new PathRequestStatusJob
            {
                TargetDataLookup = SystemAPI.GetComponentLookup<PathTargetData>(true),
                TargetChangedLookup = SystemAPI.GetComponentLookup<TargetChangedTag>(true),

                
                GridOrigin = SystemAPI.GetSingleton<GridSettings>().Origin,
                CurrentTime = (float)state.WorldUnmanaged.Time.ElapsedTime,
                MaxRequests = MaxRequestsPerFrame,

                Counter = _requestsCounter,
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]    
        [BurstCompile]
        public partial struct PathRequestStatusJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<PathTargetData> TargetDataLookup;
            [ReadOnly] public ComponentLookup<TargetChangedTag> TargetChangedLookup;

            public float3 GridOrigin;
            public float CurrentTime;
            public int MaxRequests;

            public NativeArray<int> Counter;

            void Execute(
                Entity entity,
                [ChunkIndexInQuery] int chunkIndex,
                RefRO<LocalTransform> transform,
                RefRW<PathRequestAgent> request,
                EnabledRefRW<PathAgentStatusNoneTag> noneTag,
                EnabledRefRW<PathAgentStatusSignificantMoveTag> sigTag,
                EnabledRefRW<PathAgentStatusFindTag> findTag,
                EnabledRefRW<PathAgentStatusProcessTag> processTag)
            {
                Entity myTarget = request.ValueRO.focus;
                // if (!TargetDataLookup.HasComponent(myTarget)) return;
                // int2 actualTargetCell = TargetDataLookup[myTarget].CurrentCell;

                bool isUrgent = sigTag.ValueRO;;

                if (!isUrgent && CurrentTime < request.ValueRO.NextAllowedUpdateTime) return;
                bool isIdle = noneTag.ValueRO;

                bool targetMoved = TargetChangedLookup.IsComponentEnabled(myTarget);
                bool cooldownOver = CurrentTime >= request.ValueRO.NextAllowedUpdateTime;

                bool needsUpdate = targetMoved || isUrgent || (isIdle && cooldownOver);

                if (!needsUpdate) return;
                
                if (!TargetDataLookup.TryGetComponent(myTarget, out var targetData)) return;

                int2 actualTargetCell = targetData.CurrentCell;
                
                if (request.ValueRO.destination.Equals(actualTargetCell))
                {
                    sigTag.ValueRW = false;
                    return;
                }
                
                int* ptr = (int*)Counter.GetUnsafePtr();
                int currentRequestIndex = System.Threading.Interlocked.Increment(ref ptr[0]);

                if (currentRequestIndex > MaxRequests) return;

                request.ValueRW.destination = targetData.CurrentCell;
                request.ValueRW.startCoord = GridUtils.WorldToCellCoord(transform.ValueRO.Position, GridOrigin);

                float offset = (entity.Index % 50) * 0.01f;
                request.ValueRW.NextAllowedUpdateTime = CurrentTime + 0.5f + offset;
                
                findTag.ValueRW = true;
                noneTag.ValueRW = false;
                processTag.ValueRW = false;
                sigTag.ValueRW = false;
            }
        }
    }
}