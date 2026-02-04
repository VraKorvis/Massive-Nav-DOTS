using Gameplay;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace PFStar
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PathFindingSystem))]
    [BurstCompile]
    public partial struct PathMovementSystemWithSpatialHash : ISystem
    {
        private EntityQuery _agentQuery;
        private NativeParallelMultiHashMap<int, int> _spatialMap;
        private ComponentLookup<LocalTransform> _transformLookup;
        private ComponentLookup<GridBlobReference> _gridBlobLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridTag>();
            _transformLookup = state.GetComponentLookup<LocalTransform>(false);
            _agentQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PFAgentState>()
                .WithAll<LocalTransform>()
                .WithAll<MoveSettings>()
                .WithAll<MinionTag>()
                .Build(ref state);

            _gridBlobLookup = state.GetComponentLookup<GridBlobReference>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<NavigationSettings>(out var navSettings)) return;

            _transformLookup.Update(ref state);
            _gridBlobLookup.Update(ref state);

            var gridEntity = SystemAPI.GetSingletonEntity<GridTag>();
            var gridSettings = SystemAPI.GetComponent<GridSettings>(gridEntity);
            var gridBlobRef = _gridBlobLookup[gridEntity].Value;

            int count = _agentQuery.CalculateEntityCount();

            if (!_spatialMap.IsCreated || _spatialMap.Capacity < count)
            {
                if (_spatialMap.IsCreated) _spatialMap.Dispose();
                _spatialMap = new NativeParallelMultiHashMap<int, int>(count, Allocator.Persistent);
            }

            _spatialMap.Clear();

            var allPositions = new NativeArray<float3>(count, Allocator.TempJob);
            // var allEntities = _agentQuery.ToEntityArray(Allocator.TempJob);

            var copyJobHandle = new CopyPositionsJob
            {
                Positions = allPositions
            }.ScheduleParallel(_agentQuery, state.Dependency);

            var hashJobHandle = new HashToMultiMapJob
            {
                SpatialMap = _spatialMap.AsParallelWriter(),
                CellSize = navSettings.SpatialCellSize
            }.ScheduleParallel(_agentQuery, copyJobHandle);

            var moveJobHandle = new PathMoveJob
            {
                GridBlob = gridBlobRef,
                SpatialMap = _spatialMap,
                // AllEntities = allEntities,
                AllPositions = allPositions,
                AllTransforms = _transformLookup,
                DeltaTime = SystemAPI.Time.DeltaTime,
                CellSize = navSettings.SpatialCellSize,
                SeparationRadius = navSettings.SeparationRadius,
                SeparationWeight = navSettings.SeparationWeight,
            }.ScheduleParallel(hashJobHandle);

            state.Dependency = JobHandle.CombineDependencies(
                moveJobHandle,
                // allEntities.Dispose(moveJobHandle),
                allPositions.Dispose(moveJobHandle)
            );
        }

        [BurstCompile]
        public partial struct CopyPositionsJob : IJobEntity
        {
            [WriteOnly] public NativeArray<float3> Positions;

            private void Execute([EntityIndexInQuery] int index, in LocalTransform transform)
            {
                Positions[index] = transform.Position;
            }
        }

        [BurstCompile]
        public partial struct HashToMultiMapJob : IJobEntity
        {
            public float CellSize;
            [WriteOnly] public NativeParallelMultiHashMap<int, int>.ParallelWriter SpatialMap;

            private void Execute([EntityIndexInQuery] int entityIndex, in LocalTransform transform)
            {
                int key = GridUtils.GetSpatialHashKey(transform.Position, CellSize);
                SpatialMap.Add(key, entityIndex);
            }
        }

        [BurstCompile]
        public partial struct PathMoveJob : IJobEntity
        {
            public float DeltaTime;
            [ReadOnly] public NativeParallelMultiHashMap<int, int> SpatialMap;
            [ReadOnly] public NativeArray<float3> AllPositions;
            [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> AllTransforms;
            [ReadOnly] public BlobAssetReference<GridBlob> GridBlob;

            public float CellSize;
            public float SeparationRadius;
            public float SeparationWeight;

            private void Execute(
                [EntityIndexInQuery] int myIndex,
                Entity entity,
                ref DynamicBuffer<Waypoint> way,
                ref MoveSettings moveData, ref PFAgentState agentState)
            {
                if (way.IsEmpty)
                {
                    moveData.velocity = math.lerp(moveData.velocity, float3.zero, DeltaTime * 10f);
                    return;
                }

                var transform = AllTransforms[entity];
                float3 currentPos = transform.Position;
                ref var grid = ref GridBlob.Value;

                float3 targetPos = way[^1].point;
                float3 toTarget = targetPos - currentPos;
                float distSqTotal = math.lengthsq(toTarget);

                float arrivalDist = (way.Length == 1) ? 0.05f : 0.25f;
                
                if (distSqTotal < arrivalDist)
                {
                    way.RemoveAt(way.Length - 1);
                    if (way.IsEmpty)
                    {
                        moveData.velocity = float3.zero;
                        agentState.Flags = (byte)PFAgentsStatus.Idle; 
                        return;
                    }
                    toTarget = way[^1].point - currentPos;
                }

                float3 dirToTarget = math.normalize(toTarget + 0.001f);

                float3 separationForce = float3.zero;
                int2 myGridCell = (int2)math.floor(currentPos.xz / CellSize);
                float repulseDistSq = SeparationRadius * SeparationRadius;
                int neighborsCount = 0;

                if (SpatialMap.TryGetFirstValue(GridUtils.GetSpatialHashKeyFromCell(myGridCell), out int neighborIndex,
                        out var it))
                {
                    do
                    {
                        if (neighborIndex == myIndex) continue;
                        float3 diff = currentPos - AllPositions[neighborIndex];
                        float dSq = math.lengthsq(diff);

                        if (dSq < repulseDistSq && dSq > 0.001f)
                        {
                            separationForce += diff * (1.0f / (math.sqrt(dSq) + 0.001f));
                            neighborsCount++;
                        }
                    } while (neighborsCount < 6 && SpatialMap.TryGetNextValue(out neighborIndex, ref it));
                }

                float3 wallPush = float3.zero;
                int2 myCoord = GridUtils.WorldToCellCoord(currentPos, grid.Origin);
                //TODO if (((nx | ny | (width - 1 - nx) | (height - 1 - ny)) & 0x80000000) == 0)
                for (int x = -1; x <= 1; x++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        int2 nCoord = myCoord + new int2(x, z);
                        if (nCoord.x >= 0 && nCoord.x < grid.Dimensions.x && nCoord.y >= 0 &&
                            nCoord.y < grid.Dimensions.y)
                        {
                            if (grid.CellsType[nCoord.y * grid.Dimensions.x + nCoord.x] == CellType.Wall)
                            {
                                float3 cellPos = grid.Origin +
                                                 new float3(nCoord.x * grid.CellSize, 0, nCoord.y * grid.CellSize);
                                float3 toAgent = currentPos - cellPos;
                                toAgent.y = 0;
                                float dist = math.length(toAgent);

                                if (dist < grid.CellSize * 1.2f)
                                {
                                    wallPush += (toAgent / (dist + 0.001f)) * (grid.CellSize * 1.2f - dist);
                                }
                            }
                        }
                    }
                }

                float3 steering = dirToTarget + (separationForce * 0.15f) + (wallPush * 5.0f);
                float3 targetVel = math.normalize(steering + 0.001f) * moveData.speed;

                moveData.velocity = math.lerp(moveData.velocity, targetVel, DeltaTime * 10.0f);

                float3 movement = moveData.velocity * DeltaTime;
                float3 nextPos = currentPos + movement;

                if (GridUtils.IsWallAtWorldPos(nextPos, ref grid))
                {
                    if (math.lengthsq(wallPush) > 0.01f)
                    {
                        float3 normal = math.normalize(wallPush);
                        
                        float3 slideMovement = movement - normal * math.dot(movement, normal);

                        float3 slidePos = currentPos + slideMovement;

                        if (!GridUtils.IsWallAtWorldPos(slidePos, ref grid))
                        {
                            nextPos = slidePos;
                        }
                        else
                        {
                            nextPos = currentPos;
                            moveData.velocity = float3.zero;
                        }
                    }
                    else
                    {
                        nextPos = currentPos;
                        moveData.velocity = float3.zero;
                    }
                }

                if (math.lengthsq(moveData.velocity) > 0.01f)
                {
                    transform.Rotation = math.slerp(transform.Rotation,
                        quaternion.LookRotationSafe(moveData.velocity, math.up()), DeltaTime * 8.0f);
                }

                transform.Position = nextPos;
                AllTransforms[entity] = transform;
            }
        }


        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (_spatialMap.IsCreated) _spatialMap.Dispose();
        }
    }
}