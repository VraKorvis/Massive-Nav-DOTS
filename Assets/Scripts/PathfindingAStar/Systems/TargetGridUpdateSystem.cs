using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace PFStar
{
    [UpdateInGroup(typeof(InitializationSystemGroup))] 
    [BurstCompile]
    public partial struct TargetGridUpdateSystem : ISystem
    {
        private const int Threshold = 10;
        
        private EntityQuery _allAgentsQuery;
        
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<GridSettings>();
            _allAgentsQuery = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new [] { ComponentType.ReadWrite<PathAgentStatusSignificantMoveTag>() },
                None = new [] { ComponentType.ReadOnly<PathAgentStatusFindTag>() }
            });
            
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var grid = SystemAPI.GetSingleton<GridSettings>();
            bool anyTargetMoved = false;
            
            foreach (var (transform, targetData, entity) in 
                     SystemAPI.Query<RefRO<LocalTransform>, RefRW<NavigationTargetGridData>>()
                         .WithEntityAccess())
            {
                int2 newCoord = GridUtils.WorldToCellCoord(transform.ValueRO.Position, grid.Origin);
            
                if (!newCoord.Equals(targetData.ValueRO.CurrentCell))
                {
                    targetData.ValueRW.CurrentCell = newCoord;
            
                    int distance = math.abs(newCoord.x - targetData.ValueRO.LastSignificantCell.x) + 
                                   math.abs(newCoord.y - targetData.ValueRO.LastSignificantCell.y);

                    if (distance >= Threshold)
                    {
                        targetData.ValueRW.LastSignificantCell = newCoord;
                        SystemAPI.SetComponentEnabled<TargetChangedTag>(entity, true);
                        anyTargetMoved = true;
                    }
                    else
                    {
                        SystemAPI.SetComponentEnabled<TargetChangedTag>(entity, false);
                    }
                }
                else
                {
                    SystemAPI.SetComponentEnabled<TargetChangedTag>(entity, false);
                }
                
            }
            
            if (anyTargetMoved)
            {
                state.EntityManager.SetComponentEnabled<PathAgentStatusSignificantMoveTag>(_allAgentsQuery, true);
            }
            
        }
    }
}