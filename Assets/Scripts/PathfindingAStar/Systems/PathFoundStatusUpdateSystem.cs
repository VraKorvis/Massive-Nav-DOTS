using Unity.Burst;
using Unity.Entities;

namespace PathfindingAStar
{
    /// <summary>
    /// Change agent status -> process after finding path
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PathFindingSystem))]
    [BurstCompile]
    public partial struct PathFoundStatusUpdateSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        
            var job = new CheckPathReadyJob
            {
                Ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged)
            };
        
            state.Dependency = job.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(PathAgentStatusFindTag))]
        public partial struct CheckPathReadyJob : IJobEntity
        {
            public EntityCommandBuffer Ecb;

            private void Execute(Entity entity, DynamicBuffer<Waypoint> waypoints, ref PathAgentStatus status)
            {
                if (waypoints.Length > 0)
                {
                    status.Value = AgentStatus.Process;

                    Ecb.RemoveComponent<PathAgentStatusFindTag>(entity);
                }
            }
        }
    }
}