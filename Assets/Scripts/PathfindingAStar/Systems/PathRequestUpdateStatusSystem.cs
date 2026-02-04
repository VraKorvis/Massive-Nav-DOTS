using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace PFStar
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public unsafe partial struct PathRequestUpdateStatusSystem : ISystem
    {
        private uint _frameCount;
        private NativeArray<int> _requestsCounter;
        private int _staggerStep; 
        
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
            _requestsCounter[0] = 0;
            _frameCount = 0;
            _staggerStep = 10;

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

            _targetChangedLookup.Update(ref state);

            _frameCount++;
            _requestsCounter[0] = 0;
           
            int currentStaggerIndex = (int)(_frameCount % _staggerStep);

            var job = new PathRequestStatusJob
            {
                StaggerStep = _staggerStep,
                CurrentStaggerIndex = currentStaggerIndex,
                MaxRequests = navSettings.MaxRequestsPerFrame,
                TargetChangedLookup = _targetChangedLookup,
                CurrentTime = (float)state.WorldUnmanaged.Time.ElapsedTime,
                Counter = _requestsCounter,

                EntityLookup = SystemAPI.GetEntityStorageInfoLookup()
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        public partial struct PathRequestStatusJob : IJobEntity
        {
            public int MaxRequests;

            [NativeDisableUnsafePtrRestriction] public NativeArray<int> Counter;

            [ReadOnly] public ComponentLookup<TargetChangedTag> TargetChangedLookup;

            public float CurrentTime;

            public int CurrentStaggerIndex;
            public int StaggerStep;
            public EntityStorageInfoLookup EntityLookup;

            void Execute(
                Entity entity,
                RefRW<PFAgentState> state,
                RefRO<PFRequestAgent> request)
            {
                var flags = state.ValueRO.Flags;

                if (!EntityLookup.Exists(request.ValueRO.Focus))
                {
                    state.ValueRW.Flags = (byte)PFAgentsStatus.Idle;
                    return;
                }
                
                if ((flags & (byte)(PFAgentsStatus.Find | PFAgentsStatus.Process)) != 0)
                    return;

                bool isForce = (flags & (byte)PFAgentsStatus.ForceUpdate) != 0;
                if (!isForce && CurrentTime < request.ValueRO.NextAllowedUpdateTime)
                    return;

                bool targetMoved = TargetChangedLookup.IsComponentEnabled(request.ValueRO.Focus);
                bool isUrgent = (flags & (byte)PFAgentsStatus.Significant) != 0;

                bool isScheduledFrame = (entity.Index % StaggerStep) == CurrentStaggerIndex;

                if (isForce || isUrgent || targetMoved || isScheduledFrame)
                {
                    int* ptr = (int*)Counter.GetUnsafePtr();
                    if (System.Threading.Interlocked.Increment(ref ptr[0]) <= MaxRequests)  
                    {
                        state.ValueRW.Flags |= (byte)PFAgentsStatus.Find;
                    }
                }
            }
            
            
        }
    }
}