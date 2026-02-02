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
            _transformLookup = state.GetComponentLookup<LocalTransform>(false);
            _agentQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PFAgentState>()
                .WithAll<LocalTransform>()
                .WithAll<MoveSettings>()
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

            private const float StopThresholdSq = 0.04f;
            private const int MaxNeighborsTotal = 5;

            private void Execute(
                [EntityIndexInQuery] int myIndex,
                Entity entity,
                ref DynamicBuffer<Waypoint> way,
                ref MoveSettings moveData)
            {
                if (way.IsEmpty)
                {
                    moveData.velocity = math.lerp(moveData.velocity, float3.zero, DeltaTime * 10f);
                    return;
                }

                var transform = AllTransforms[entity];
                float3 currentPos = transform.Position;
                ref var grid = ref GridBlob.Value;

                int lastIndex = way.Length - 1;
                float3 targetPos = way[lastIndex].point;
                float3 toTarget = targetPos - currentPos;
                float distSq = math.lengthsq(toTarget);

                bool isFinalTarget = way.Length == 1;

                float3 forward = math.normalize(moveData.velocity + 0.001f);
                if (distSq < 0.36f || (!isFinalTarget && math.dot(forward, toTarget) < 0))
                {
                    way.RemoveAt(lastIndex);
                    if (way.IsEmpty) return;

                    lastIndex = way.Length - 1;
                    targetPos = way[lastIndex].point;
                    toTarget = targetPos - currentPos;
                    distSq = math.lengthsq(toTarget);
                }

                if (isFinalTarget && distSq < StopThresholdSq)
                {
                    moveData.velocity = math.lerp(moveData.velocity, float3.zero, DeltaTime * 10f);
                    return;
                }

                float3 dirToTarget = toTarget * math.rsqrt(distSq + 0.0001f);

                float3 avoidanceForce = float3.zero;

                int2 centerCell = (int2)math.floor(currentPos.xz / CellSize);
                float radiusSq = SeparationRadius * SeparationRadius;
                int totalChecked = 0;

                for (int x = -1; x <= 1 && totalChecked < MaxNeighborsTotal; x++)
                {
                    for (int z = -1; z <= 1 && totalChecked < MaxNeighborsTotal; z++)
                    {
                        int cellKey = GridUtils.GetSpatialHashKeyFromCell(centerCell + new int2(x, z));
                        if (SpatialMap.TryGetFirstValue(cellKey, out int neighborIndex, out var iterator))
                        {
                            do
                            {
                                if (neighborIndex == myIndex) continue;
                                float3 neighborPos = AllPositions[neighborIndex];
                                float3 pushVec = currentPos - neighborPos;
                                float dSq = math.lengthsq(pushVec);

                                if (dSq < radiusSq && dSq > 0.001f)
                                {
                                    float d = math.sqrt(dSq);
                                    float distScale = isFinalTarget ? math.saturate(distSq) : 1.0f;
                                    avoidanceForce += (pushVec / d) * (1.0f - d / SeparationRadius) * distScale;
                                    totalChecked++;
                                }
                            } while (totalChecked < MaxNeighborsTotal &&
                                     SpatialMap.TryGetNextValue(out neighborIndex, ref iterator));
                        }
                    }
                }

                float3 sideDir = math.cross(dirToTarget, new float3(0, 1, 0));
                float3 wallAvoidance = float3.zero;

                if (GridUtils.IsWallAtWorldPos(currentPos + dirToTarget * 0.4f, ref grid))
                    wallAvoidance -= dirToTarget * 1.2f;
                if (GridUtils.IsWallAtWorldPos(currentPos - sideDir * 0.4f, ref grid)) wallAvoidance += sideDir * 0.8f;
                if (GridUtils.IsWallAtWorldPos(currentPos + sideDir * 0.4f, ref grid)) wallAvoidance -= sideDir * 0.8f;

                float3 steering = dirToTarget + (avoidanceForce * SeparationWeight) + wallAvoidance;
                float3 targetVelocity = math.normalize(steering) * moveData.speed;

                if (GridUtils.IsWallAtWorldPos(currentPos + dirToTarget * 0.3f, ref grid)) targetVelocity *= 0.1f;

                moveData.velocity = math.lerp(moveData.velocity, targetVelocity, DeltaTime * 4.0f);

                if (math.lengthsq(moveData.velocity) > 0.01f)
                {
                    var targetRot = quaternion.LookRotationSafe(math.normalize(moveData.velocity), math.up());
                    transform.Rotation = math.slerp(transform.Rotation, targetRot, DeltaTime * 6.0f);
                }

                transform.Position += moveData.velocity * DeltaTime;
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