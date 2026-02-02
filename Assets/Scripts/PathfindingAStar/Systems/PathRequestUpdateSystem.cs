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

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridTag>();
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<NavigationTargetGridData>();

            _requestsCounter = new NativeArray<int>(1, Allocator.Persistent);
            
            _targetDataLookup = state.GetComponentLookup<NavigationTargetGridData>(true);
            _targetChangedLookup = state.GetComponentLookup<TargetChangedTag>(true);
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

            var job = new PathRequestStatusJob
            {
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

                if (!isUrgent && CurrentTime < reqRO.NextAllowedUpdateTime)
                    return;

                bool isIdle = (flags & (byte)PFAgentsStatus.None) != 0;

                if (!TargetDataLookup.HasComponent(myTarget))
                    return;

                bool targetMoved = TargetChangedLookup.IsComponentEnabled(myTarget);
                bool cooldownOver = CurrentTime >= reqRO.NextAllowedUpdateTime;

                bool needsUpdate = targetMoved || isUrgent || (isIdle && cooldownOver);

                NavigationTargetGridData targetData;
                int2 actualTargetCell;
                if (!needsUpdate)
                {
                    if (TargetDataLookup.TryGetComponent(myTarget, out targetData))
                    {
                        actualTargetCell = targetData.CurrentCell;
                        if (IsTargetTooFar(actualTargetCell, reqRO.Destination, Threshold)) return;
                    }
                }
                else
                {
                    if (!TargetDataLookup.TryGetComponent(myTarget, out targetData)) return;
                }

                actualTargetCell = targetData.CurrentCell;

                if (reqRO.Destination.Equals(actualTargetCell))
                {
                    flags &= (byte)~PFAgentsStatus.Significant;
                    state.ValueRW.Flags = flags;
                    return;
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
                flags &= (byte)~PFAgentsStatus.None;
                flags &= (byte)~PFAgentsStatus.Process;
                flags &= (byte)~PFAgentsStatus.Significant;
            }
        }
    }
}