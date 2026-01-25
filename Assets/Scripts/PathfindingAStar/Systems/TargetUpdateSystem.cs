using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;

namespace PathfindingAStar
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public partial struct TargetUpdateSystem : ISystem 
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSettings>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state) 
        {
            if (!SystemAPI.TryGetSingletonEntity<PathTargetTag>(out Entity targetEntity)) return;

            float3 targetPos = SystemAPI.GetComponent<LocalTransform>(targetEntity).Position;
            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            int2 targetCoord = GridUtils.WorldToCellCoord(targetPos, gridSettings.Origin);

            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

            foreach (var (transform, request, entity) in 
                     SystemAPI.Query<RefRO<LocalTransform>, RefRW<PathRequestAgent>>()
                         .WithEntityAccess()) 
            {
                // ОБНОВЛЯЕМ startCoord на основе текущей позиции
                float3 currentPos = transform.ValueRO.Position;
                int2 currentCoord = GridUtils.WorldToCellCoord(currentPos, gridSettings.Origin);
                
                request.ValueRW.startCoord = currentCoord;
        
                // Если цель изменилась, запускаем поиск пути
                if (!request.ValueRO.destination.Equals(targetCoord)) 
                {
                    request.ValueRW.destination = targetCoord;
            
                    var status = SystemAPI.GetComponentRW<PathAgentStatus>(entity);
                    status.ValueRW.Value = AgentStatus.Find;
            
                    if (!SystemAPI.HasComponent<PathAgentStatusFindTag>(entity))
                    {
                        ecb.AddComponent<PathAgentStatusFindTag>(entity);
                    }
                }
            }
    
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}