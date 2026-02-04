using Input;
using PFStar;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Gameplay.Player
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ClickClassificationSystem))]
    [UpdateBefore(typeof(PathRequestUpdateStatusSystem))]
    [BurstCompile]
    public partial struct MoveToCommandSystem : ISystem
    {
        private ComponentLookup<GridBlobReference> _gridBlobLookup;
        private ComponentLookup<MoveToCommand> _moveToCommandLookup;
        private ComponentLookup<LocalTransform> _transformLookup;
        private EntityQuery _commandQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<GridTag>();
            state.RequireForUpdate<PlayerTag>();
            _gridBlobLookup = state.GetComponentLookup<GridBlobReference>(true);
            _moveToCommandLookup = state.GetComponentLookup<MoveToCommand>(false);
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);

            _commandQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<MoveToCommand>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _gridBlobLookup.Update(ref state);
            _moveToCommandLookup.Update(ref state);
            _transformLookup.Update(ref state);
            var gridEntity = SystemAPI.GetSingletonEntity<GridTag>();
            ref var blob = ref _gridBlobLookup[gridEntity].Value.Value;

            if (!SystemAPI.TryGetSingletonEntity<PlayerTag>(out var playerEntity)) return;
            
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);
            
            foreach (var (command, markerEntity) in SystemAPI.Query<RefRO<MoveToCommand>>()
                         .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                         .WithEntityAccess())
            {
              
                if (!_moveToCommandLookup.IsComponentEnabled(markerEntity)) continue;
                
                int2 clickCell = GridUtils.WorldToCellCoord(command.ValueRO.WorldPosition, blob.Origin);

                bool isPathValid = !math.any(clickCell < 0) && !math.any(clickCell >= blob.Dimensions);
                if (isPathValid && IsCellWall(clickCell, ref blob))
                {
                    float3 currentPos = SystemAPI.GetComponent<LocalTransform>(markerEntity).Position;
                    isPathValid = TryFindWalkable(currentPos, command.ValueRO.WorldPosition, ref blob, blob.Origin,
                        out clickCell);
                }

                if (isPathValid)
                {
                    ecb.SetComponent(playerEntity, new PFRequestAgent
                    {
                        Focus = markerEntity,
                        StartCoord = GridUtils.WorldToCellCoord(_transformLookup[playerEntity].Position, blob.Origin),
                        Destination = clickCell,
                        NextAllowedUpdateTime = 0
                    });
                    
                    ecb.SetComponent(playerEntity, new PFAgentState { Flags = (byte)PFAgentStatus.Find });
                    ecb.SetComponent(playerEntity, new PFRequestMetadata { Priority = 255 });
                    
                    float3 targetWorldPos = GridUtils.CellToWorldCoord(clickCell, blob.Origin);
                    ecb.SetComponent(markerEntity, LocalTransform.FromPosition(targetWorldPos));
                    ecb.SetComponentEnabled<TargetChangedTag>(markerEntity, true);
                    ecb.SetComponent(markerEntity, new NavigationTargetGridData { CurrentCell = clickCell });

                }
                _moveToCommandLookup.SetComponentEnabled(markerEntity, false);

            }
        }

        [BurstCompile]
        private bool IsCellWall(int2 cell, ref GridBlob grid)
        {
            if (math.any(cell < 0) || cell.x >= grid.Dimensions.x || cell.y >= grid.Dimensions.y)
                return true;

            int index = GridUtils.CoordToIndex(cell, grid.Dimensions.x);
            return grid.CellsType[index] == CellType.Wall;
        }

        [BurstCompile]
        private bool TryFindWalkable(float3 from, float3 to, ref GridBlob blob, float3 origin, out int2 result)
        {
            float3 dir = math.normalize(from - to);
            for (float step = 0.2f; step < 5f; step += 0.4f)
            {
                int2 testCell = GridUtils.WorldToCellCoord(to + dir * step, origin);
                if (!IsCellWall(testCell, ref blob))
                {
                    result = testCell;
                    return true;
                }
            }

            result = new int2(-1, -1);
            return false;
        }
    }
}