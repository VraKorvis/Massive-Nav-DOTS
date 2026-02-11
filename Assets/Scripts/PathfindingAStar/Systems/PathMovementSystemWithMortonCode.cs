using System.Runtime.CompilerServices;
using Core.Mathematics;
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

        private NativeArray<MortonEntry> _mortonA, _mortonB;
        private NativeArray<MortonEntry> _radixTempBuffer;

        private NativeArray<int> _cellStartsA, _cellStartsB;
        private JobHandle _lastSortHandle;
        private JobHandle _handleA;
        private JobHandle _handleB;
        private bool _isBufferA;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridBlobReference>();
            state.RequireForUpdate<GridTag>();
            _agentQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PFAgentState>()
                .WithAll<LocalTransform>()
                .WithAll<Waypoint>()
                .WithAll<MoveSettings>()
                .WithAll<MinionTag>()
                .Build(ref state);
            _isBufferA = true;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<NavigationSettings>(out var navSettings)) return;
            int count = _agentQuery.CalculateEntityCount();
            if (count == 0) return;

            var gridBlob = SystemAPI.GetSingleton<GridBlobReference>().Value;
            var gridDims = gridBlob.Value.Dimensions;
            var gridSizMorton = 1 << (math.ceillog2(math.max(gridDims.x, gridDims.y)) * 2);

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
                EnsureCapacity(ref _cellStartsA, gridSizMorton, default);
                EnsureCapacity(ref _cellStartsB, gridSizMorton, default);
            }

            var readyMorton = _isBufferA ? _mortonA : _mortonB;
            var readyHandle = _isBufferA ? _handleA : _handleB;
            var nextPositionCache = _isBufferA ? _positionCacheB : _positionCacheA;
            var nextMorton = _isBufferA ? _mortonB : _mortonA;

            var readyCellStarts = _isBufferA ? _cellStartsA : _cellStartsB;
            var nextCellStarts = _isBufferA ? _cellStartsB : _cellStartsA;

            var transforms = _agentQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            var copyJobHandle = new CopyPositionsParallelJob
            {
                Transforms = transforms,
                Positions = nextPositionCache
            }.Schedule(count, 64, state.Dependency);

            if (_initialized)
            {
                state.Dependency = new PathMovePBDJob
                {
                    GridBlob = gridBlob,
                    SortedEntries = readyMorton,
                    DeltaTime = SystemAPI.Time.DeltaTime,
                    CellSize = navSettings.SpatialCellSize,
                    SeparationRadius = navSettings.SeparationRadius,
                    SeparationWeight = navSettings.SeparationWeight,
                    CellStarts = readyCellStarts,
                    FramePhase = Time.frameCount % 2,
                    AgentCount = count
                }.ScheduleParallel(_agentQuery, JobHandle.CombineDependencies(state.Dependency, readyHandle));
            }

            var nextHandle = _isBufferA ? _handleB : _handleA;

            if (nextHandle.IsCompleted)
            {
                var prepareDeps = JobHandle.CombineDependencies(copyJobHandle, state.Dependency);
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

                var buildIndexHandle = new BuildCellStartsJob
                {
                    SortedEntries = nextMorton,
                    CellStarts = nextCellStarts
                }.Schedule(sortHandle);

                if (_isBufferA)
                {
                    _handleB = buildIndexHandle;
                }
                else
                {
                    _handleA = buildIndexHandle;
                }

                _isBufferA = !_isBufferA;
            }

            state.Dependency = JobHandle.CombineDependencies(state.Dependency, copyJobHandle);
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
        struct BuildCellStartsJob : IJob
        {
            [ReadOnly]
            public NativeArray<MortonEntry> SortedEntries;
            public NativeArray<int> CellStarts;

            public void Execute()
            {
                for (int i = 0; i < CellStarts.Length; i++)
                    CellStarts[i] = -1;

                if (SortedEntries.Length > 0)
                {
                    uint lastCell = SortedEntries[0].Key;

                    if (lastCell < CellStarts.Length)
                    {
                        CellStarts[(int)lastCell] = 0;
                    }

                    for (int i = 1; i < SortedEntries.Length; i++)
                    {
                        uint currentCell = SortedEntries[i].Key;
                        if (currentCell != lastCell)
                        {
                            if (currentCell < CellStarts.Length)
                                CellStarts[(int)currentCell] = i;
                            lastCell = currentCell;
                        }
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
            
            [ReadOnly]
            public NativeArray<int> CellStarts;

            private void Execute(
                [EntityIndexInQuery] int myIndex,
                ref LocalTransform transform,
                ref DynamicBuffer<Waypoint> way,
                ref MoveSettings moveData,
                ref PFAgentState agentState,
                in MinionTag minionTag)
            {

                ref var grid = ref GridBlob.Value;

                float3 pos = transform.Position;
                float baseRadius = math.max(0.05f, grid.CellSize * moveData.ArrivalRadiusFactor);
                float arrivalRadius = (way.Length == 1) ? 0.05f : baseRadius;

                if (!TrimReachedWaypoints(way, pos, arrivalRadius))
                {
                    StopAgent(ref agentState, ref moveData);
                    return;
                }

                pos.y = GridUtils.GetHeightBilinear(ref grid, pos);
                transform.Position.y = pos.y;

                float3 targetPos = way[^1].point;
                targetPos.y = GridUtils.GetHeightBilinear(ref grid, targetPos);


                // PBD + steering
                float3 pbdDisplacement = float3.zero;

                if (myIndex % 2 == FramePhase)
                {
                    uint agentCode = MortonUtils.GetMorton2D(pos, CellSize);
                    float checkRadiusSq = SeparationRadius * SeparationRadius;

                    if (agentCode < CellStarts.Length)
                    {
                        int startIdx = CellStarts[(int)agentCode];
                        if (startIdx != -1)
                        {
                            pbdDisplacement += CheckNeighbors(startIdx, 1, pos, checkRadiusSq);
                            pbdDisplacement += CheckNeighbors(startIdx, -1, pos, checkRadiusSq);
                        }
                    }

                }

                float3 toTarget = targetPos - pos;
                int2 cellCoord = math.clamp(GridUtils.WorldToCellCoord(pos, grid.Origin, grid.CellSize), 0, grid.Dimensions - 1);
                float3 wallPush = grid.WallPushField[GridUtils.CoordToIndex(cellCoord, grid.Dimensions.x)];
                float3 dirToTarget = math.normalize(toTarget + 0.001f);

                float3 desiredDir = math.normalize(dirToTarget + pbdDisplacement * SeparationWeight);
                if (math.lengthsq(wallPush) > 0.01f)
                {
                    float3 wallNormal = math.normalize(wallPush);
                    float dot = math.dot(desiredDir, -wallNormal);
                    if (dot > 0)
                    {
                        desiredDir = math.normalize(desiredDir + wallNormal * dot);
                    }
                    desiredDir = math.normalize(desiredDir + wallNormal * 0.5f);
                }
                
                float3 steering = desiredDir;
                
                float distXZ = math.distance(pos.xz, targetPos.xz);
                float desiredSpeed = CalculateDesiredSpeed(in moveData, ref grid, pos, moveData.Speed, targetPos.y, distXZ, arrivalRadius);

                float3 targetVelocity = math.normalize(steering + 0.001f) * desiredSpeed;

                moveData.Velocity = math.lerp(moveData.Velocity, targetVelocity, DeltaTime * moveData.Acceleration);

                float3 movement = moveData.Velocity * DeltaTime;
                float3 nextPos = pos + movement;

                if (GridUtils.IsWallAtWorldPos(nextPos, ref grid))
                {
                    if (math.lengthsq(wallPush) > 0.01f)
                    {
                        float3 normal = math.normalize(wallPush);

                        float3 slideMovement = movement - normal * math.dot(movement, normal);

                        float3 slidePos = pos + slideMovement;

                        if (!GridUtils.IsWallAtWorldPos(slidePos, ref grid))
                        {
                            nextPos = slidePos;
                        }
                        else
                        {
                            nextPos = pos;
                            moveData.Velocity = float3.zero;
                        }
                    }
                    else
                    {
                        nextPos = pos;
                        if (math.lengthsq(wallPush) > 0.001f)
                        {
                            moveData.Velocity = math.normalize(wallPush) * 0.1f;
                        }
                        else
                        {
                            moveData.Velocity *= 0.5f;
                        }
                    }
                }

                ApplyMovement(ref moveData, ref transform, pos, nextPos);
                float3 actualDir = math.normalize(moveData.Velocity + 0.001f);
                ApplyRotation(in moveData, ref transform, ref grid, pos, targetPos, actualDir);
                moveData.TargetCellPos = targetPos;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool TrimReachedWaypoints(DynamicBuffer<Waypoint> way, float3 pos, float arrivalRadius)
            {
                while (!way.IsEmpty)
                {
                    int last = way.Length - 1;
                    float3 p = way[last].point;
                    if (math.distance(pos.xz, p.xz) <= arrivalRadius)
                    {
                        way.RemoveAt(last);
                        continue;
                    }
                    return true;
                }
                return false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void StopAgent(ref PFAgentState state, ref MoveSettings move)
            {
                state.Flags = (byte)PFAgentStatus.Idle;
                move.Velocity = float3.zero;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private float CalculateDesiredSpeed(in MoveSettings moveData, ref GridBlob grid, float3 pos, float baseSpeed, float targetY, float distXZ, float arrivalRadius)
            {
                float smoothWeight = GridUtils.GetWeightBilinear(ref grid, pos);
                if (!math.isfinite(smoothWeight) || smoothWeight <= 0f) smoothWeight = 1f;

                float deltaY = targetY - pos.y;
                float slope = deltaY / math.max(distXZ, 0.5f);

                float slopeMul = 1.0f;
                if (slope > 0)
                {
                    slopeMul = math.lerp(1.0f, 0.2f, math.saturate(slope / moveData.MaxClimbRateFactor));
                }
                else
                {
                    slopeMul = math.lerp(1.0f, 1.2f, math.saturate(-slope));
                }

                float weightMul = math.clamp(1.0f / math.max(PhysConst.EPSILON_WEIGHT, smoothWeight), moveData.MinSpeedMul, moveData.MaxSpeedMul);
                float speed = moveData.Speed * weightMul * slopeMul;

                float slowRadius = arrivalRadius * 2f;
                if (distXZ < slowRadius)
                {
                    float t = math.clamp((distXZ - arrivalRadius) / (slowRadius - arrivalRadius), 0f, 1f);
                    speed = math.max(speed * 0.3f, speed * t);
                }
                return math.max(speed, baseSpeed * moveData.MinSpeedMul);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private float3 CalculateNextPosition(in MoveSettings moveData, ref GridBlob grid, float3 pos, float3 dirXZ, float speed, float cellSize)
            {
                float step = math.clamp(speed * DeltaTime, math.max(0.01f, cellSize * 0.02f), 100f);
                float3 nextXZ = pos + dirXZ * step;

                float sampledH = GridUtils.GetHeightBilinear(ref grid, nextXZ);
                float maxDy = (moveData.MaxClimbRateFactor * cellSize) * DeltaTime;
                float allowedDy = math.clamp(sampledH - pos.y, -maxDy * 2f, maxDy);

                float alphaY = math.clamp(DeltaTime * moveData.VerticalSmoothSpeed, 0f, 1f);
                float newY = math.lerp(pos.y + allowedDy, sampledH, alphaY);

                return new float3(nextXZ.x, newY, nextXZ.z);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void ApplyMovement(ref MoveSettings moveData, ref LocalTransform transform, float3 pos, float3 nextPos)
            {
                float3 finalPos = nextPos;
                float finalHeight = GridUtils.GetHeightBilinear(ref GridBlob.Value, finalPos);
                float alphaY = math.clamp(DeltaTime * moveData.VerticalSmoothSpeed, 0f, 1f);
                finalPos.y = math.lerp(pos.y, finalHeight, alphaY);
                transform.Position = finalPos;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void ApplyRotation(in MoveSettings moveData, ref LocalTransform transform, ref GridBlob grid, float3 pos, float3 target, float3 dirXZ)
            {
                float3 nCur = GridUtils.GetNormalBilinear(ref grid, pos);
                float3 nTgt = GridUtils.GetNormalBilinear(ref grid, target);
                float3 surfaceNormal = math.normalize(math.lerp(nCur, nTgt, 0.5f));

                float3 tangent = dirXZ - surfaceNormal * math.dot(dirXZ, surfaceNormal);
                if (math.lengthsq(tangent) < PhysConst.EPSILON_STABLE)
                {
                    tangent = transform.Forward();
                }
                tangent = math.normalize(tangent);

                quaternion targetRot = quaternion.LookRotationSafe(tangent, surfaceNormal);
                transform.Rotation = math.slerp(transform.Rotation, targetRot, math.min(1f, DeltaTime * moveData.RotSpeed));
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
            if (_cellStartsA.IsCreated) _cellStartsA.Dispose();
            if (_cellStartsB.IsCreated) _cellStartsB.Dispose();
        }
    }
}
