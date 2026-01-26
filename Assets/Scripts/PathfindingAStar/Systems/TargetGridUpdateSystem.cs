using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace PathfindingAStar
{
    [UpdateInGroup(typeof(InitializationSystemGroup))] 
    [BurstCompile]
    public partial struct TargetGridUpdateSystem : ISystem
    {
        const int threshold = 10;
        
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSettings>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var grid = SystemAPI.GetSingleton<GridSettings>();
        
            foreach (var (transform, targetData) in 
                     SystemAPI.Query<RefRO<LocalTransform>, RefRW<PathTargetData>>())
            {
                int2 newCoord = GridUtils.WorldToCellCoord(transform.ValueRO.Position, grid.Origin);
            
                if (!newCoord.Equals(targetData.ValueRO.CurrentCell))
                {
                    targetData.ValueRW.CurrentCell = newCoord; 
                    
                    int distance = math.abs(newCoord.x - targetData.ValueRO.LastSignificantCell.x) + 
                                   math.abs(newCoord.y - targetData.ValueRO.LastSignificantCell.y);

                    if (distance >= threshold)
                    {
                        targetData.ValueRW.LastSignificantCell = newCoord;
                    }
                }
                
                
            }
        }
    }
}