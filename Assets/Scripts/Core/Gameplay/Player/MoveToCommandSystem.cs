using Core.Input;
using Core.PathfindingAStar;
using Map.Grid;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Core.Gameplay
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(ClickDispatchSystem))]
    [BurstCompile]
    public partial struct MoveToCommandSystem : ISystem
    {
        private ComponentLookup<LocalTransform> _transformLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridBlobReference>();
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<GridTag>();
            state.RequireForUpdate<PlayerTag>();
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            if (!SystemAPI.TryGetSingletonEntity<ClickMarkerTag>(out var markerEntity)) return;
            if (!SystemAPI.TryGetSingletonEntity<PlayerTag>(out var playerEntity)) return;

            _transformLookup.Update(ref state);
            
            state.Dependency.Complete(); 

            if (!SystemAPI.IsComponentEnabled<MoveToCommand>(markerEntity)) return;
            
            var command = SystemAPI.GetComponent<MoveToCommand>(markerEntity);
            var gridBlobRef = SystemAPI.GetSingleton<GridBlobReference>().Value;

            ref var blob = ref gridBlobRef.Value;
            
            ecb.SetComponentEnabled<MoveToCommand>(markerEntity, false);

            float3 targetPos = command.WorldPosition;
            int2 rawClickCell = GridUtils.WorldToCellCoord(targetPos, blob.Origin, blob.CellSize);

            if (GridUtils.IsInBounds(rawClickCell, blob.Dimensions))
            {
                float3 playerPos = _transformLookup[playerEntity].Position;
                if (IsCellWall(rawClickCell, ref blob))
                {
                    if (!TryFindWalkable(playerPos, targetPos, ref blob, blob.Origin, out var walkableCell)) return;
                    rawClickCell = walkableCell;
                }

                int2 playerCell = GridUtils.WorldToCellCoord(playerPos, blob.Origin, blob.CellSize);

                ecb.SetComponent(playerEntity, new PFRequestAgent
                {
                    Focus = markerEntity,
                    Owner = playerEntity,
                    StartCoord = math.clamp(playerCell, 0, blob.Dimensions - 1),
                    Destination = rawClickCell
                });

                ecb.SetComponent(playerEntity, new PFAgentState
                {
                    Flags = (byte)PFAgentStatus.Find
                });
                ecb.SetComponent(playerEntity, new PFRequestMetadata
                {
                    Priority = 255
                });

                float3 targetWorldPos = GridUtils.CellToWorldCoord(rawClickCell, blob.Origin, blob.CellSize);
                ecb.SetComponent(markerEntity, LocalTransform.FromPosition(targetWorldPos));
                ecb.SetComponent(markerEntity, new NavigationTargetGridData
                {
                    CurrentCell = rawClickCell
                });
                ecb.SetComponentEnabled<DynamicTargetTrackingMarkerTag>(playerEntity, false);
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
