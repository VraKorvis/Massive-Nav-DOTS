using System.Runtime.CompilerServices;
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
        
        private ComponentLookup<NavigationTargetGridData> _targetDataLookup;
        private ComponentLookup<TargetChangedTag> _targetChangedLookup;
        private ComponentLookup<GridBlobReference> _gridBlobLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridTag>();
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<NavigationTargetGridData>();

            _requestsCounter = new NativeArray<int>(1, Allocator.Persistent);
            
            _targetDataLookup = state.GetComponentLookup<NavigationTargetGridData>(true);
            _targetChangedLookup = state.GetComponentLookup<TargetChangedTag>(true);
            _gridBlobLookup = state.GetComponentLookup<GridBlobReference>(true);
        }

        public void OnDestroy(ref SystemState state)
        {
            _requestsCounter.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<NavigationSettings>(out var navSettings)) return;
            
            _requestsCounter[0] = 0;

            _targetDataLookup.Update(ref state);
            _targetChangedLookup.Update(ref state);
            _gridBlobLookup.Update(ref state);
            
            var gridEntity = SystemAPI.GetSingletonEntity<GridTag>();
            var gridBlobRef = _gridBlobLookup[gridEntity].Value;

            var job = new PathRequestStatusJob
            {
                GridBlob = gridBlobRef,
                TargetDataLookup = _targetDataLookup,
                TargetChangedLookup = _targetChangedLookup,
                GridOrigin = SystemAPI.GetSingleton<GridSettings>().Origin,
                CurrentTime = (float)state.WorldUnmanaged.Time.ElapsedTime,
                MaxRequests = navSettings.MaxRequestsPerFrame,

                Counter = _requestsCounter,
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        public partial struct PathRequestStatusJob : IJobEntity
        {
            private const int Threshold = 10;

            [ReadOnly] public ComponentLookup<NavigationTargetGridData> TargetDataLookup;
            [ReadOnly] public ComponentLookup<TargetChangedTag> TargetChangedLookup;

            public float3 GridOrigin;
            public float CurrentTime;
            public int MaxRequests;

            [NativeDisableUnsafePtrRestriction] public NativeArray<int> Counter;
            [ReadOnly] public BlobAssetReference<GridBlob> GridBlob;

            void Execute(
                Entity entity,
                [ChunkIndexInQuery] int chunkIndex,
                RefRO<LocalTransform> transform,
                RefRW<PFRequestAgent> request,
                RefRW<PFAgentState> state)
            {
                var reqRO = request.ValueRO;
                Entity myTarget = reqRO.Focus;

                var flags = state.ValueRO.Flags;

                if ((flags & (byte)PFAgentsStatus.Find) != 0) return;

                bool isUrgent = (flags & (byte)PFAgentsStatus.Significant) != 0;
                bool cooldownOver = CurrentTime > reqRO.NextAllowedUpdateTime;
                bool isIdle = (flags & (byte)PFAgentsStatus.Idle) != 0;

                if (!cooldownOver)
                    return;
                
                if (!TargetDataLookup.HasComponent(myTarget))
                    return;
                
                var currentPos = transform.ValueRO.Position;
                
                float3 destPos = GridUtils.CellToWorldCoord(reqRO.Destination, GridOrigin);
                float distToDest = math.distance(currentPos, destPos);
                
                bool isStuck = !isIdle && distToDest > 0.75f;

                ref var gridBlobValue = ref GridBlob.Value;
                bool targetMoved = TargetChangedLookup.IsComponentEnabled(myTarget);
                bool isAtDestination = math.all(GridUtils.WorldToCellCoord(currentPos, gridBlobValue.Origin) == reqRO.Destination);
                
                if (!TargetDataLookup.TryGetComponent(myTarget, out var targetData)) return;
                
                var actualTargetCell = targetData.CurrentCell; 

                if (!targetMoved && !isUrgent && !isStuck && !(isIdle && (!isAtDestination || !math.all(reqRO.Destination == actualTargetCell)))) 
                {
                    if (!isIdle) return;
                }
                
                if (!isUrgent && !targetMoved)
                {
                    if (!IsTargetTooFar(actualTargetCell, reqRO.Destination, 0)) return;
                }
                
                if (!reqRO.Destination.Equals(actualTargetCell))
                {
                    request.ValueRW.Destination = actualTargetCell;
                }
                else
                {
                    if (!isUrgent)
                    {
                        flags &= (byte)~PFAgentsStatus.Significant;
                        state.ValueRW.Flags = flags;
                        return;
                    }
                }

                int* ptr = (int*)Counter.GetUnsafePtr();
                int currentRequestIndex = System.Threading.Interlocked.Increment(ref ptr[0]);
                if (currentRequestIndex > MaxRequests) return;

                FillRequest(ref request.ValueRW, targetData.CurrentCell, transform.ValueRO.Position, GridOrigin,
                    entity.Index, CurrentTime);
                SetFlagsAfterRequest(ref flags);
                state.ValueRW.Flags = flags;
            }

            [MethodImpl(MethodImplOptions
                .AggressiveInlining)]
            private bool IsTargetTooFar(int2 targetCell, int2 currentDestination, int threshold)
            {
                int dist = math.abs(targetCell.x - currentDestination.x) +
                           math.abs(targetCell.y - currentDestination.y);
                return dist > threshold;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void FillRequest(ref PFRequestAgent req, int2 targetCell, float3 pos, float3 origin,
                int entityIndex, float time)
            {
                req.Destination = targetCell;
                req.StartCoord = GridUtils.WorldToCellCoord(pos, origin);

                float jitter = (entityIndex % 32) * 0.02f;
                req.NextAllowedUpdateTime = time + 0.3f + jitter;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void SetFlagsAfterRequest(ref byte flags)
            {
                flags |= (byte)PFAgentsStatus.Find;
                flags &= (byte)~PFAgentsStatus.Idle;
                flags &= (byte)~PFAgentsStatus.Process;
                flags &= (byte)~PFAgentsStatus.Significant;
            }
        }
    }
}