using Core.Gameplay;
using Core.PathfindingAStar;
using Map.Grid;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Core.Spatial
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [BurstCompile]
    public partial struct SpatialPartitioningSystem : ISystem
    {
        private EntityQuery _agentQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridBlobReference>();
            _agentQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PFAgentState, LocalTransform, Waypoint, MoveSettings, MinionTag>()
                .Build(ref state);

            state.EntityManager.CreateEntity(typeof(SpatialPartitioningData));
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingletonRW<SpatialPartitioningData>(out var spatialData)) return;
            if (!SystemAPI.TryGetSingleton<NavigationSettings>(out var navSettings)) return;

            int count = _agentQuery.CalculateEntityCount();
            if (count == 0) return;

            var gridBlob = SystemAPI.GetSingleton<GridBlobReference>().Value;
            var gridDims = gridBlob.Value.Dimensions;
            var gridSizMorton = 1 << (math.ceillog2(math.max(gridDims.x, gridDims.y)) * 2);

            if (!spatialData.ValueRO.MortonA.IsCreated || spatialData.ValueRO.MortonA.Length < count)
            {
                state.Dependency.Complete();
                spatialData.ValueRW.HandleA.Complete();
                spatialData.ValueRW.HandleB.Complete();
                EnsureCapacity(ref spatialData.ValueRW.MortonA, count, default);
                EnsureCapacity(ref spatialData.ValueRW.MortonB, count, default);
                EnsureCapacity(ref spatialData.ValueRW.RadixTempBuffer, count, default);
                EnsureCapacity(ref spatialData.ValueRW.PositionCacheA, count, default);
                EnsureCapacity(ref spatialData.ValueRW.PositionCacheB, count, default);
                EnsureCapacity(ref spatialData.ValueRW.CellStartsA, gridSizMorton, default);
                EnsureCapacity(ref spatialData.ValueRW.CellStartsB, gridSizMorton, default);
            }

            var isBufferA = spatialData.ValueRO.IsBufferA;
            var nextPositionCache = isBufferA ? spatialData.ValueRO.PositionCacheB : spatialData.ValueRO.PositionCacheA;
            var nextMorton = isBufferA ? spatialData.ValueRO.MortonB : spatialData.ValueRO.MortonA;
            var nextCellStarts = isBufferA ? spatialData.ValueRO.CellStartsB : spatialData.ValueRO.CellStartsA;
            var radixTempBuffer = spatialData.ValueRO.RadixTempBuffer;

            var transforms = _agentQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            var copyJobHandle = new CopyPositionsParallelJob
            {
                Transforms = transforms,
                Positions = nextPositionCache
            }.Schedule(count, 64, state.Dependency);

            var nextHandle = isBufferA ? spatialData.ValueRO.HandleB : spatialData.ValueRO.HandleA;

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
                    TempBuffer = radixTempBuffer
                }.Schedule(prepareHandle);

                var buildIndexHandle = new BuildCellStartsJob
                {
                    SortedEntries = nextMorton,
                    CellStarts = nextCellStarts
                }.Schedule(sortHandle);

                if (isBufferA)
                {
                    spatialData.ValueRW.HandleB = buildIndexHandle;
                }
                else
                {
                    spatialData.ValueRW.HandleA = buildIndexHandle;
                }

                spatialData.ValueRW.IsBufferA = !isBufferA;
                spatialData.ValueRW.Initialized = true;
            }

            spatialData.ValueRW.AgentCount = count;
            state.Dependency = JobHandle.CombineDependencies(state.Dependency, copyJobHandle);
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
                unsafe
                {
                    UnsafeUtility.MemSet(CellStarts.GetUnsafePtr(), 0xFF, (long)CellStarts.Length * sizeof(int));
                }

                if (SortedEntries.Length == 0) return;

                uint firstAgentCellCode = SortedEntries[0].Key;

                if (firstAgentCellCode < (uint)CellStarts.Length)
                {
                    CellStarts[(int)firstAgentCellCode] = 0;
                }

                uint previousCellCode = firstAgentCellCode;

                for (int i = 1; i < SortedEntries.Length; i++)
                {
                    uint currentCellCode = SortedEntries[i].Key;

                    if (currentCellCode != previousCellCode)
                    {
                        if (currentCellCode < (uint)CellStarts.Length)
                        {
                            CellStarts[(int)currentCellCode] = i;
                        }

                        previousCellCode = currentCellCode;
                    }
                }
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
            if (SystemAPI.TryGetSingleton<SpatialPartitioningData>(out var data))
            {
                JobHandle.CombineDependencies(data.HandleA, data.HandleB, state.Dependency).Complete();
                if (data.MortonA.IsCreated)
                {
                    data.MortonA.Dispose();
                    data.MortonB.Dispose();
                    data.RadixTempBuffer.Dispose();
                    data.PositionCacheA.Dispose();
                    data.PositionCacheB.Dispose();
                    data.CellStartsA.Dispose();
                    data.CellStartsB.Dispose();
                }
            }
        }
    }
}
