using Gameplay;
using Map;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Transforms;
using UnityEngine;

namespace PFStar
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PathFindingSystem))]
    [BurstCompile]
    public partial struct PathMovementSystemWithSpatialHash : ISystem
    {
#if UNITY_EDITOR
        private static readonly ProfilerMarker k_ProfilePlayerPathLogic = new("[PF] Player.Pathfinding.PathMovementSystemWithSpatialHash");
#endif

        private EntityQuery _agentQuery;
        private NativeParallelMultiHashMap<int, int> _spatialMap;
        private ComponentLookup<GridBlobReference> _gridBlobLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridTag>();
            _agentQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PFAgentState>()
                .WithAll<LocalTransform>()
                .WithAll<MoveSettings>()
                .WithAll<MinionTag>()
                .Build(ref state);

            _gridBlobLookup = state.GetComponentLookup<GridBlobReference>(true);
            state.Enabled = false;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<NavigationSettings>(out var navSettings)) return;

            _gridBlobLookup.Update(ref state);

            var gridEntity = SystemAPI.GetSingletonEntity<GridTag>();
            var gridBlobRef = _gridBlobLookup[gridEntity].Value;

            int count = _agentQuery.CalculateEntityCount();

            if (!_spatialMap.IsCreated || _spatialMap.Capacity < count)
            {
                if (_spatialMap.IsCreated) _spatialMap.Dispose();
                _spatialMap = new NativeParallelMultiHashMap<int, int>((int)(count * 2f), Allocator.Persistent);
            }

            _spatialMap.Clear();
            
            var allPositions = new NativeArray<float3>(count, Allocator.TempJob);

            var copyJobHandle = new CopyPositionsJob
            {
                Positions = allPositions
            }.ScheduleParallel(_agentQuery, state.Dependency);

            int framePhase = Time.frameCount % 2;            
            var hashJobHandle = new HashToMultiMapJob
            {
                SpatialMap = _spatialMap.AsParallelWriter(),
                CellSize = navSettings.SpatialCellSize
            }.ScheduleParallel(_agentQuery, copyJobHandle);

            var moveJobHandle = new PathMoveJob
            {
                GridBlob = gridBlobRef,
                SpatialMap = _spatialMap,
                AllPositions = allPositions,
                DeltaTime = SystemAPI.Time.DeltaTime,
                CellSize = navSettings.SpatialCellSize,
                SeparationRadius = navSettings.SeparationRadius,
                SeparationWeight = navSettings.SeparationWeight,
                FramePhase = framePhase,
            }.ScheduleParallel(hashJobHandle);

            state.Dependency = moveJobHandle;

            allPositions.Dispose(state.Dependency);
        }

        [BurstCompile]
        public partial struct CopyPositionsJob : IJobEntity
        {
            [WriteOnly]
            public NativeArray<float3> Positions;

            private void Execute([EntityIndexInQuery] int index, in LocalTransform transform)
            {
                Positions[index] = transform.Position;
            }
        }

        [BurstCompile]
        public partial struct HashToMultiMapJob : IJobEntity
        {
            public float CellSize;
            [WriteOnly]
            public NativeParallelMultiHashMap<int, int>.ParallelWriter SpatialMap;

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
            [ReadOnly]
            public NativeParallelMultiHashMap<int, int> SpatialMap;
            [ReadOnly]
            public NativeArray<float3> AllPositions;
            [ReadOnly]
            public BlobAssetReference<GridBlob> GridBlob;

            public float CellSize;
            public float SeparationRadius;
            public float SeparationWeight;
            public int FramePhase;

            private void Execute(
                [EntityIndexInQuery] int myIndex,
                Entity entity,
                ref LocalTransform transform, 
                ref DynamicBuffer<Waypoint> way,
                ref MoveSettings moveData,
                ref PFAgentState agentState)
            {
                if (way.IsEmpty)
                {
                    moveData.Velocity = math.lerp(moveData.Velocity, float3.zero, DeltaTime * 10f);
                    agentState.Flags = (byte)PFAgentStatus.Idle;
                    return;
                }

                float3 currentPos = transform.Position;
                ref var grid = ref GridBlob.Value;
                
                
                float3 targetPos = way[^1].point;
                float3 toTarget = targetPos - currentPos;
                float distSqTotal = math.lengthsq(toTarget);
                int2 cellCoord = GridUtils.WorldToCellCoord(currentPos, grid.Origin, grid.CellSize);
                cellCoord = math.clamp(cellCoord, 0, grid.Dimensions - 1);
                var cellIndex = GridUtils.CoordToIndex(cellCoord, grid.Dimensions.x);
                float3 wallPush = grid.WallPushField[cellIndex];
                
                float arrivalDist = (way.Length == 1) ? 0.05f : 0.25f;

                if (distSqTotal < arrivalDist)
                {
                    way.RemoveAt(way.Length - 1);
                    if (way.IsEmpty)
                    {
                        moveData.Velocity = float3.zero;
                        agentState.Flags = (byte)PFAgentStatus.Idle;
                        return;
                    }
                    toTarget = way[^1].point - currentPos;
                }

                float3 dirToTarget = math.normalize(toTarget + 0.001f);
                float3 separationForce = float3.zero;

                if (myIndex % 2 == FramePhase)
                {
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
                    float3 steering = dirToTarget + (separationForce * 0.15f) + (wallPush * 5.0f);
                    float3 targetVel = math.normalize(steering + 0.001f) * moveData.Speed;

                    moveData.Velocity = math.lerp(moveData.Velocity, targetVel, DeltaTime * 10.0f);
                }
                
                float3 movement = moveData.Velocity * DeltaTime;
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
                            moveData.Velocity = float3.zero;
                        }
                    }
                    else
                    {
                        nextPos = currentPos;
                        moveData.Velocity = float3.zero;
                    }
                }

                if (math.lengthsq(moveData.Velocity) > 0.01f)
                {
                    transform.Rotation = math.slerp(transform.Rotation,
                        quaternion.LookRotationSafe(moveData.Velocity, math.up()), DeltaTime * 8.0f);
                }

                transform.Position = nextPos;
            }
        }


        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (_spatialMap.IsCreated) _spatialMap.Dispose();
        }
    }
}
