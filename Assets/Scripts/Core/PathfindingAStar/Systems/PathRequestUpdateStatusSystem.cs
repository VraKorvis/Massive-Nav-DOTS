using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Core.PathfindingAStar
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(PathFindingSystem))]
    [BurstCompile]
    public unsafe partial struct PathRequestUpdateStatusSystem : ISystem
    {
        private ComponentLookup<NavigationTargetGridData> _navigationTargetLookup;
        private BufferLookup<Waypoint> _waypointLookup;
        
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NavigationSettings>();

            state.RequireForUpdate<GridTag>();
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<NavigationTargetGridData>();
            
            _navigationTargetLookup = state.GetComponentLookup<NavigationTargetGridData>(true);
            _waypointLookup = state.GetBufferLookup<Waypoint>(true);
        }
        
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<NavigationSettings>(out var navSettings)) return;

            int maxRequests = navSettings.MaxRequestsPerFrame;

            _navigationTargetLookup.Update(ref state);
            _waypointLookup.Update(ref state);

            var updateWeightsJob = new UpdateWeightsJob
            {
                TargetLookup = SystemAPI.GetComponentLookup<NavigationTargetGridData>(true),
                WaypointLookup = SystemAPI.GetBufferLookup<Waypoint>(true),
                CurrentTime = (float)state.WorldUnmanaged.Time.ElapsedTime
            }.ScheduleParallel(state.Dependency);

            state.Dependency = updateWeightsJob;
        }

        [BurstCompile]
        [WithAll(typeof(DynamicTargetTrackingMarkerTag))]
        public partial struct UpdateWeightsJob : IJobEntity
        {
            [ReadOnly]
            public ComponentLookup<NavigationTargetGridData> TargetLookup;
            [ReadOnly]
            public BufferLookup<Waypoint> WaypointLookup;
            public float CurrentTime;

            void Execute(Entity entity, ref PFRequestMetadata metadata, ref PFAgentState state, in PFRequestAgent request)
            {
                if (!TargetLookup.HasComponent(request.Focus)) return;

                if (CurrentTime < metadata.NextAllowedUpdateTime) return;

                if ((state.Flags & (byte)PFAgentStatus.Processing) != 0) return;
                
                var targetData = TargetLookup[request.Focus];
                float weight = 0;

                uint versionDiff = targetData.Version - metadata.LastProcessedVersion;
                weight += versionDiff * 15f;

                bool hasNoPath = !WaypointLookup.HasBuffer(entity) || WaypointLookup[entity].IsEmpty;
                if (hasNoPath) weight += 100f;

                float timeSinceUpdate = CurrentTime - metadata.RequestTime;
                weight += timeSinceUpdate * 2f;

                metadata.Weight = weight;

                if (metadata.Weight > 5f)
                {
                    state.Flags = (byte)PFAgentStatus.Find;
                }
            }
        }
    }
}