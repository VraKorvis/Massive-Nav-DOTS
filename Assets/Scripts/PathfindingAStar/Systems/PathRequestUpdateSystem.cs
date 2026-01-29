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
        private NativeArray<int> _requestsCounter;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridTag>();
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<NavigationTargetGridData>();

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
            if (!SystemAPI.TryGetSingleton<PathfindingSettings>(out var pfSettings)) return;

            var gridEntity = SystemAPI.GetSingletonEntity<GridTag>();
            var gridBlobRef = SystemAPI.GetComponent<GridBlobReference>(gridEntity).Value;
            
            var job = new PathRequestStatusJob
            {
                TargetDataLookup = SystemAPI.GetComponentLookup<NavigationTargetGridData>(true),
                TargetChangedLookup = SystemAPI.GetComponentLookup<TargetChangedTag>(true),

                GridBlob = gridBlobRef, 
                GridOrigin = SystemAPI.GetSingleton<GridSettings>().Origin,
                CurrentTime = (float)state.WorldUnmanaged.Time.ElapsedTime,
                MaxRequests = pfSettings.MaxRequestsPerFrame,

                Counter = _requestsCounter,
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        public partial struct PathRequestStatusJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<NavigationTargetGridData> TargetDataLookup;
            [ReadOnly] public ComponentLookup<TargetChangedTag> TargetChangedLookup;

            public float3 GridOrigin;
            public float CurrentTime;
            public int MaxRequests;

            [NativeDisableUnsafePtrRestriction]
            public NativeArray<int> Counter;

            public BlobAssetReference<GridBlob> GridBlob;

            void Execute(
                Entity entity,
                [ChunkIndexInQuery] int chunkIndex,
                RefRO<LocalTransform> transform,
                RefRW<PathRequestAgent> request,
                RefRW<PFAgentState> state)
            {
                
                var reqRO = request.ValueRO;
                Entity myTarget = reqRO.focus;
                
                var flags = state.ValueRO.Flags;
                
                if ((flags & (byte)PFAgentsStatus.Find) != 0) return;
                
                bool isUrgent = (flags & (byte)PFAgentsStatus.Significant) != 0;
                
                if (!isUrgent && CurrentTime < reqRO.NextAllowedUpdateTime)
                    return;
                
                bool isIdle = (flags & (byte)PFAgentsStatus.None) != 0; 
                
                if (!TargetDataLookup.HasComponent(myTarget))
                    return;

                bool targetMoved = TargetChangedLookup.IsComponentEnabled(myTarget);
                bool cooldownOver = CurrentTime >= reqRO.NextAllowedUpdateTime;

                bool needsUpdate = targetMoved || isUrgent || (isIdle && cooldownOver);

                if (!needsUpdate) return;
                
                if (!TargetDataLookup.TryGetComponent(myTarget, out var targetData)) return;
                
                float3 targetWorldPos = GridUtils.CoordToWorld(targetData.CurrentCell, GridBlob.Value.Origin, GridBlob.Value.CellSize);
                float distToTargetSq = math.distancesq(transform.ValueRO.Position, targetWorldPos);
                
                if (distToTargetSq < 2.0f) 
                {
                    flags &= (byte)~PFAgentsStatus.Significant;
                    flags &= (byte)~PFAgentsStatus.Find;
                    state.ValueRW.Flags = flags;
                    return;
                }

                int2 actualTargetCell = targetData.CurrentCell;
                
                if (reqRO.destination.Equals(actualTargetCell))
                {
                    flags &= (byte)~PFAgentsStatus.Significant;
                    state.ValueRW.Flags = flags;
                    return;
                }
                
                int* ptr = (int*)Counter.GetUnsafePtr();
                int currentRequestIndex = System.Threading.Interlocked.Increment(ref ptr[0]);

                if (currentRequestIndex > MaxRequests) return;

                request.ValueRW.destination = targetData.CurrentCell;
                request.ValueRW.startCoord = GridUtils.WorldToCellCoord(transform.ValueRO.Position, GridOrigin);

                float offset = (entity.Index % 50) * 0.01f;
                request.ValueRW.NextAllowedUpdateTime = CurrentTime + 0.5f + offset;
                
                flags |= (byte)PFAgentsStatus.Find;
                flags &= (byte)~PFAgentsStatus.None;
                flags &= (byte)~PFAgentsStatus.Process;
                flags &= (byte)~PFAgentsStatus.Significant;

                state.ValueRW.Flags = flags;
            }
        }
    }
}