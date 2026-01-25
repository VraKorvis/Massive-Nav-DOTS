namespace PathfindingAStar
{
using Unity.Burst;
using Unity.Entities;

[UpdateInGroup(typeof(LateSimulationSystemGroup))]
[UpdateAfter(typeof(PathFindingSystem))]
[UpdateBefore(typeof(PathMovementSystem))]
[BurstCompile]
public partial struct PathAgentStatusSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        foreach (var (status, entity) in SystemAPI.Query<RefRO<PathAgentStatus>>().WithEntityAccess())
        {
            switch (status.ValueRO.Value)
            {
                case AgentStatus.Find:
                    if (!SystemAPI.HasComponent<PathAgentStatusFindTag>(entity))
                        ecb.AddComponent<PathAgentStatusFindTag>(entity);
                    
                    ecb.RemoveComponent<PathAgentStatusNoneTag>(entity);
                    ecb.RemoveComponent<PathAgentStatusProcessTag>(entity);
                    break;

                case AgentStatus.Process:
                    if (!SystemAPI.HasComponent<PathAgentStatusProcessTag>(entity))
                        ecb.AddComponent<PathAgentStatusProcessTag>(entity);
                    
                    ecb.RemoveComponent<PathAgentStatusFindTag>(entity);
                    break;

                case AgentStatus.None:
                    if (!SystemAPI.HasComponent<PathAgentStatusNoneTag>(entity))
                        ecb.AddComponent<PathAgentStatusNoneTag>(entity);
                    
                    ecb.RemoveComponent<PathAgentStatusFindTag>(entity);
                    ecb.RemoveComponent<PathAgentStatusProcessTag>(entity);
                    break;
                    
            }
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
}