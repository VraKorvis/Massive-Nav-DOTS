using Unity.Burst;
using Unity.Entities;

namespace PathfindingAStar
{
    /// <summary>
    /// Change agent status -> process after finding path
    /// </summary>
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    [UpdateAfter(typeof(PathFindingSystem))]
    [UpdateBefore(typeof(PathAgentStatusSystem))]
    [BurstCompile]
    public partial struct PathFoundStatusUpdateSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // ИСПРАВЛЕНО: убрали DynamicBuffer из Query
            foreach (var (status, entity) in 
                     SystemAPI.Query<RefRW<PathAgentStatus>>()
                         .WithAll<PathAgentStatusFindTag>()
                         .WithEntityAccess())
            {
                // Получаем буфер отдельно
                var waypoints = SystemAPI.GetBuffer<Waypoint>(entity);
                
                // Если путь найден (есть waypoints), меняем статус на Process
                if (waypoints.Length > 0)
                {
                    status.ValueRW.Value = AgentStatus.Process;
                }
            }
        }
    }
}