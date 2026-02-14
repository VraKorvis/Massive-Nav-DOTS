using Core.Input;
using Core.PathfindingAStar;
using Map.Grid;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using ClickClassificationSystem = Core.Input.ClickClassificationSystem;

namespace Core.Gameplay
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ClickClassificationSystem))]
    [UpdateBefore(typeof(PathRequestUpdateStatusSystem))]
    [BurstCompile]
    public partial struct MoveToCommandSystem : ISystem
    {
        private ComponentLookup<GridBlobReference> _gridBlobLookup;
        private ComponentLookup<LocalTransform> _transformLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<GridTag>();
            state.RequireForUpdate<PlayerTag>();
            _gridBlobLookup = state.GetComponentLookup<GridBlobReference>(true);
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.CompleteDependency();

            if (!SystemAPI.TryGetSingletonEntity<ClickMarkerTag>(out var markerEntity)) return;

            if (!state.EntityManager.IsComponentEnabled<MoveToCommand>(markerEntity)) return;

            var command = state.EntityManager.GetComponentData<MoveToCommand>(markerEntity);

            _gridBlobLookup.Update(ref state);
            _transformLookup.Update(ref state);
            var gridEntity = SystemAPI.GetSingletonEntity<GridTag>();
            ref var blob = ref _gridBlobLookup[gridEntity].Value.Value;

            if (!SystemAPI.TryGetSingletonEntity<PlayerTag>(out var playerEntity)) return;

            float3 targetPos = command.WorldPosition;
            int2 rawClickCell = GridUtils.WorldToCellCoord(targetPos, blob.Origin, blob.CellSize);

            if (GridUtils.IsInBounds(rawClickCell, blob.Dimensions))
            {
                float3 playerPos = _transformLookup[playerEntity].Position;
                if (IsCellWall(rawClickCell, ref blob))
                {
                    if (!TryFindWalkable(playerPos, targetPos, ref blob, blob.Origin, out var walkableCell))
                    {
                        return;
                    }
                    rawClickCell = walkableCell;

                }

                int2 playerCell = GridUtils.WorldToCellCoord(playerPos, blob.Origin, blob.CellSize);

                state.EntityManager.SetComponentData(playerEntity, new PFRequestAgent
                {
                    Focus = markerEntity,
                    Owner = playerEntity,
                    StartCoord = math.clamp(playerCell, 0, blob.Dimensions - 1),
                    Destination = rawClickCell
                });
                
                state.EntityManager.SetComponentData(playerEntity, new PFAgentState
                {
                    Flags = (byte)PFAgentStatus.Find
                });
                
                state.EntityManager.SetComponentData(playerEntity, new PFRequestMetadata { Priority = 255 });
                
                float3 targetWorldPos = GridUtils.CellToWorldCoord(rawClickCell, blob.Origin, blob.CellSize);
                state.EntityManager.SetComponentData(markerEntity, LocalTransform.FromPosition(targetWorldPos));
                state.EntityManager.SetComponentData(markerEntity, new NavigationTargetGridData { CurrentCell = rawClickCell });
                state.EntityManager.SetComponentEnabled<DynamicTargetTrackingMarkerTag>(playerEntity, false);
            }
            
            state.EntityManager.SetComponentEnabled<MoveToCommand>(markerEntity, false);
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
                int2 testCell = GridUtils.WorldToCellCoord(to + dir * step, origin, blob.CellSize);

                if (!GridUtils.IsInBounds(testCell, blob.Dimensions)) break;

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
