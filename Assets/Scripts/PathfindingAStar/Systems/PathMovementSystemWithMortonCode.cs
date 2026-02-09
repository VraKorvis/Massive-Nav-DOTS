using Gameplay;
using Map;
using PFStar.Morton;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Transforms;
using UnityEngine;

namespace PFStar
{
    public partial struct PathMovementSystemWithMortonCode : ISystem
    {
        
#if UNITY_EDITOR
        private static readonly ProfilerMarker k_ProfilePlayerPathLogic = new("[PF] Player.Pathfinding.PathMovementSystemWithMortonCode");
#endif

        private bool _initialized;

        private EntityQuery _agentQuery;

        private NativeArray<float3> _positionCacheA, _positionCacheB;
        private NativeArray<MortonEntry> _mortonEntries;

        private ComponentLookup<GridBlobReference> _gridBlobLookup;

        private NativeArray<MortonEntry> _mortonA, _mortonB;
        private NativeArray<MortonEntry> _radixTempBuffer;

        private JobHandle _lastSortHandle;
        private JobHandle _handleA;
        private JobHandle _handleB;
        private bool _isBufferA;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridTag>();
            _agentQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PFAgentState>()
                .WithAll<LocalTransform>()
                .WithAll<Waypoint>()
                .WithAll<MoveSettings>()
                .WithAll<MinionTag>()
                .Build(ref state);
            _gridBlobLookup = state.GetComponentLookup<GridBlobReference>(true);
            _isBufferA = true;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<NavigationSettings>(out var navSettings)) return;
            int count = _agentQuery.CalculateEntityCount();
            if (count == 0) return;

            _gridBlobLookup.Update(ref state);

            if (!_mortonA.IsCreated || _mortonA.Length < count)
            {
                state.Dependency.Complete();
                _lastSortHandle.Complete();
                _handleA.Complete();
                _handleB.Complete();

                EnsureCapacity(ref _mortonA, count, default);
                EnsureCapacity(ref _mortonB, count, default);
                EnsureCapacity(ref _radixTempBuffer, count, default);
                EnsureCapacity(ref _positionCacheA, count, default);
                EnsureCapacity(ref _positionCacheB, count, default);
            }

            var readyMorton = _isBufferA ? _mortonA : _mortonB;
            var readyHandle = _isBufferA ? _handleA : _handleB;
            var nextPositionCache = _isBufferA ? _positionCacheB : _positionCacheA;
            var nextMorton = _isBufferA ? _mortonB : _mortonA;
            
            var transforms = _agentQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            var copyJob = new CopyPositionsParallelJob
            {
                Transforms = transforms,
                Positions = nextPositionCache
            }.Schedule(count, 64, state.Dependency);
            
            if (_initialized)
            {
                state.Dependency = new PathMovePBDJob
                {
                    GridBlob = _gridBlobLookup[SystemAPI.GetSingletonEntity<GridTag>()].Value,
                    SortedEntries = readyMorton,
                    DeltaTime = SystemAPI.Time.DeltaTime,
                    CellSize = navSettings.SpatialCellSize,
                    SeparationRadius = navSettings.SeparationRadius,
                    SeparationWeight = navSettings.SeparationWeight,
                    FramePhase = Time.frameCount % 2,
                    AgentCount = count
                }.ScheduleParallel(_agentQuery, JobHandle.CombineDependencies(state.Dependency, readyHandle));
            }
            
            var nextHandle = _isBufferA ? _handleB : _handleA;

            if (nextHandle.IsCompleted)
            {
                var prepareDeps = JobHandle.CombineDependencies(copyJob, state.Dependency);
                var entities = _agentQuery.ToEntityArray(Allocator.TempJob);

                var prepareHandle = new PrepareMortonJobForArray
                {
                    Positions = nextPositionCache,
                    MortonEntries = nextMorton,
                    CellSize = navSettings.SpatialCellSize,
                    AgentQueryEntities = entities
                }.Schedule(count, 64, prepareDeps);

                var sortHandle = new MortonRadixSortJob
                {
                    Data = nextMorton,
                    TempBuffer = _radixTempBuffer
                }.Schedule(prepareHandle);

                if (_isBufferA)
                {
                    _handleB = sortHandle;
                }
                else
                {
                    _handleA = sortHandle;
                }

                _isBufferA = !_isBufferA;
            }
            
            state.Dependency = JobHandle.CombineDependencies(state.Dependency, copyJob);
            _initialized = true;
        }


        [BurstCompile]
        private struct CopyPositionsParallelJob : IJobParallelFor
        {
            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<LocalTransform> Transforms;
            [WriteOnly]
            public NativeArray<float3> Positions;

            public void Execute(int index)
            {
                Positions[index] = Transforms[index].Position;
            }
        }

        [BurstCompile]
        private struct PrepareMortonJobForArray : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float3> Positions;
            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<Entity> AgentQueryEntities;
            public float CellSize;
            [WriteOnly]
            public NativeArray<MortonEntry> MortonEntries;

            public void Execute(int index)
            {
                float3 pos = Positions[index];
                uint code = MortonUtils.GetMorton2D(pos, CellSize);
                MortonEntries[index] = new MortonEntry
                {
                    Key = code,
                    Index = index,
                    AgentEntity = AgentQueryEntities[index],
                    Position = pos,
                };
            }
        }

        [BurstCompile]
        private struct MortonRadixSortJob : IJob
        {
            public NativeArray<MortonEntry> Data;
            public NativeArray<MortonEntry> TempBuffer;

            public void Execute()
            {
                
                var src = Data;
                var dst = TempBuffer;
                
                for (int shift = 0; shift < 32; shift += 8)
                {
                    SortPass(shift, src, dst);
                    (src, dst) = (dst, src);
                }
            }

            private void SortPass(int shift, NativeArray<MortonEntry> source, NativeArray<MortonEntry> destination)
            {
                unsafe
                {
                    int* buckets = stackalloc int[256];
                    for (int i = 0; i < 256; i++) buckets[i] = 0;

                    for (int i = 0; i < source.Length; i++)
                    {
                        buckets[(source[i].Key >> shift) & 0xFF]++;
                    }

                    int offset = 0;
                    for (int i = 0; i < 256; i++)
                    {
                        int count = buckets[i];
                        buckets[i] = offset;
                        offset += count;
                    }
                    
                    for (int i = 0; i < source.Length; i++)
                    {
                        int b = (int)((source[i].Key >> shift) & 0xFF);
                        destination[buckets[b]++] = source[i];
                    }
                }
            }
        }

        [BurstCompile]
        public partial struct PathMovePBDJob : IJobEntity
        {
            [ReadOnly]
            public NativeArray<MortonEntry> SortedEntries;

            [ReadOnly]
            public BlobAssetReference<GridBlob> GridBlob;

            public float DeltaTime;
            public float CellSize;
            public float SeparationRadius;
            public float SeparationWeight;
            public int FramePhase;
            public int AgentCount;

            private void Execute(
                [EntityIndexInQuery] int myIndex,
                ref LocalTransform transform,
                ref DynamicBuffer<Waypoint> way,
                ref MoveSettings moveData,
                ref PFAgentState agentState,
                in MinionTag minionTag)
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
                float arrivalDist = (way.Length == 1) ? 0.05f : 0.25f;

                if (distSqTotal < arrivalDist)
                {
                    way.RemoveAt(way.Length - 1);
                    if (way.IsEmpty)
                    {
                        moveData.Velocity = float3.zero;
                        return;
                    }
                    toTarget = way[^1].point - currentPos;
                }

                float3 pbdDisplacement = float3.zero;

                if (myIndex % 2 == FramePhase)
                {
                    uint myCode = MortonUtils.GetMorton2D(currentPos, CellSize);
                    float checkRadiusSq = SeparationRadius * SeparationRadius;

                    int sortedIdx = BinarySearchMorton(SortedEntries, myCode);

                    pbdDisplacement += CheckNeighbors(sortedIdx, 1, currentPos, checkRadiusSq);
                    pbdDisplacement += CheckNeighbors(sortedIdx, -1, currentPos, checkRadiusSq);
                }

                int2 cellCoord = math.clamp(GridUtils.WorldToCellCoord(currentPos, grid.Origin, grid.CellSize), 0, grid.Dimensions - 1);
                float3 wallPush = grid.WallPushField[GridUtils.CoordToIndex(cellCoord, grid.Dimensions.x)];

                float3 dirToTarget = math.normalize(toTarget + 0.001f);

                float3 steering = dirToTarget + (pbdDisplacement * SeparationWeight) + (wallPush * 5.0f);
                float3 targetVel = math.normalize(steering + 0.001f) * moveData.Speed;

                moveData.Velocity = math.lerp(moveData.Velocity, targetVel, DeltaTime * 10.0f);

                float3 nextPos = currentPos + (moveData.Velocity * DeltaTime);

                float3 movement = moveData.Velocity * DeltaTime;

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

                transform.Position = nextPos;
                if (math.lengthsq(moveData.Velocity) > 0.01f)
                {
                    transform.Rotation = math.slerp(transform.Rotation, quaternion.LookRotationSafe(moveData.Velocity, math.up()), DeltaTime * 8.0f);
                }
            }

            private float3 CheckNeighbors(int startIdx, int direction, float3 myPos, float radiusSq)
            {
                float3 totalPush = float3.zero;
                int count = 0;
                for (int i = 1; i < 15; i++)
                {
                    int curr = startIdx + (i * direction);
                    if (curr < 0 || curr >= AgentCount) break;

                    Entity neighborEntity = SortedEntries[curr].AgentEntity;
                    float3 neighborPos = SortedEntries[curr].Position;

                    float3 diff = myPos - neighborPos;
                    float dSq = math.lengthsq(diff);

                    if (dSq < radiusSq && dSq > 0.0001f)
                    {
                        float r = SeparationRadius;
                        float invD = math.rsqrt(dSq);
                        totalPush += diff * (invD * r - 1f) * 0.5f;
                        count++;
                    }
                    if (count > 6) break;
                }
                return totalPush;
            }

            private int BinarySearchMorton(NativeArray<MortonEntry> array, uint key)
            {
                int low = 0, high = array.Length - 1;
                while (low <= high)
                {
                    int mid = low + ((high - low) >> 1);
                    if (array[mid].Key < key)
                    {
                        low = mid + 1;
                    }
                    else if (array[mid].Key > key)
                    {
                        high = mid - 1;
                    }
                    else return mid;
                }
                return low;
            }
        }


        private void EnsureCapacity<T>(ref NativeArray<T> array, int count, JobHandle dependency) where T : struct
        {
            if (!array.IsCreated || array.Length < count)
            {
                if (array.IsCreated)
                {
                    dependency.Complete();
                    array.Dispose();
                }
                array = new NativeArray<T>(count, Allocator.Persistent);
            }
        }

        public void OnDestroy(ref SystemState state)
        {
            JobHandle.CombineDependencies(_handleA, _handleB, state.Dependency).Complete();

            if (_positionCacheA.IsCreated) _positionCacheA.Dispose();
            if (_positionCacheB.IsCreated) _positionCacheB.Dispose();
            if (_mortonA.IsCreated) _mortonA.Dispose();
            if (_mortonB.IsCreated) _mortonB.Dispose();
            if (_radixTempBuffer.IsCreated) _radixTempBuffer.Dispose();
        }

    }

}
