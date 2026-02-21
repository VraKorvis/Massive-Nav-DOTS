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
            _agentQuery = SystemAPI.QueryBuilder()
                .WithAll<PFAgentState, LocalTransform, Waypoint, MoveSettings, MinionTag>()
                .Build();

            state.EntityManager.CreateEntity(typeof(SpatialPartitioningData));
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingletonRW<SpatialPartitioningData>(out var spatialData)) return;
            if (!SystemAPI.TryGetSingleton<NavigationSettings>(out var navSettings)) return;

            int count = _agentQuery.CalculateEntityCount();
            if (count == 0) return;
            
            if (!spatialData.ValueRO.MortonA.IsCreated || spatialData.ValueRO.MortonA.Length < count)
            {
                var gridBlob = SystemAPI.GetSingleton<GridBlobReference>().Value;
                var gridDims = gridBlob.Value.Dimensions;
                var gridSizMorton = 1 << (math.ceillog2(math.max(gridDims.x, gridDims.y)) * 2);

                state.Dependency.Complete();
                spatialData.ValueRW.HandleA.Complete();
                spatialData.ValueRW.HandleB.Complete();
                EnsureCapacity(ref spatialData.ValueRW.MortonA, count, default);
                EnsureCapacity(ref spatialData.ValueRW.MortonB, count, default);
                EnsureCapacity(ref spatialData.ValueRW.RadixTempBuffer, count, default);
                EnsureCapacity(ref spatialData.ValueRW.PositionCacheA, count, default);
                EnsureCapacity(ref spatialData.ValueRW.PositionCacheB, count, default);
                
                EnsureCapacity(ref spatialData.ValueRW.EntitiesCacheA, count, default);
                EnsureCapacity(ref spatialData.ValueRW.EntitiesCacheB, count, default);
                
                EnsureCapacity(ref spatialData.ValueRW.CellStartsA, gridSizMorton, default);
                EnsureCapacity(ref spatialData.ValueRW.CellStartsB, gridSizMorton, default);
                
                EnsureCapacity(ref spatialData.ValueRW.CellCountsA, gridSizMorton, default);
                EnsureCapacity(ref spatialData.ValueRW.CellCountsB, gridSizMorton, default);
            }
            
            var isBufferA = spatialData.ValueRO.IsBufferA;
            var nextPositionCache = isBufferA ? spatialData.ValueRO.PositionCacheB : spatialData.ValueRO.PositionCacheA;
            var nextEntitiesCache = isBufferA ? spatialData.ValueRO.EntitiesCacheB : spatialData.ValueRO.EntitiesCacheA;
            var nextMorton = isBufferA ? spatialData.ValueRO.MortonB : spatialData.ValueRO.MortonA;
            var nextCellStarts = isBufferA ? spatialData.ValueRO.CellStartsB : spatialData.ValueRO.CellStartsA;
            var nextCellCount = isBufferA ? spatialData.ValueRO.CellCountsB : spatialData.ValueRO.CellCountsA;
            var radixTempBuffer = spatialData.ValueRO.RadixTempBuffer;

            var copyJobHandle = new CopyPositionsParallelJob
            {
                Positions = nextPositionCache,
                AgentQueryEntities = nextEntitiesCache
            }.ScheduleParallel(_agentQuery, state.Dependency);
            
            var nextHandle = isBufferA ? spatialData.ValueRO.HandleB : spatialData.ValueRO.HandleA;
   
            if (nextHandle.IsCompleted)
            {
                var prepareDeps = JobHandle.CombineDependencies(copyJobHandle, state.Dependency);

                var prepareHandle = new PrepareMortonJobForArray
                {
                    Positions = nextPositionCache,
                    MortonEntries = nextMorton,
                    CellSize = navSettings.SpatialCellSize,
                    AgentQueryEntities = nextEntitiesCache
                }.Schedule(count, 64, prepareDeps);

                var sortHandle = new MortonRadixSortJob
                {
                    Data = nextMorton,
                    TempBuffer = radixTempBuffer
                }.Schedule(prepareHandle);

                var buildIndexHandle = new BuildCellStartsJob
                {
                    SortedEntries = nextMorton,
                    CellStarts = nextCellStarts,
                    CellCounts = nextCellCount
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
        private partial struct CopyPositionsParallelJob : IJobEntity
        {
            [WriteOnly] [NativeDisableContainerSafetyRestriction]
            public NativeArray<float3> Positions;
            [WriteOnly] [NativeDisableContainerSafetyRestriction]
            public NativeArray<Entity> AgentQueryEntities;

            private void Execute(Entity entity, [EntityIndexInQuery] int index, in LocalTransform transform)
            {
                Positions[index] = transform.Position;
                AgentQueryEntities[index] = entity;
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
            public NativeArray<int> CellCounts;
        
            public void Execute()
            {
                unsafe
                {
                    UnsafeUtility.MemSet(CellStarts.GetUnsafePtr(), 0xFF, (long)CellStarts.Length * sizeof(int));
                    UnsafeUtility.MemClear(CellCounts.GetUnsafePtr(), CellCounts.Length * sizeof(int));
                }
        
                if (SortedEntries.Length == 0)
                    return;
        
                uint previousCellCode = SortedEntries[0].Key;
                int startIndex = 0;
        
                for (int i = 0; i <= SortedEntries.Length; i++)
                {
                    bool endOfCell =
                        i == SortedEntries.Length ||
                        SortedEntries[i].Key != previousCellCode;
        
                    if (endOfCell)
                    {
                        if (previousCellCode < (uint)CellStarts.Length)
                        {
                            int keyIndex = (int)previousCellCode;
                            CellStarts[keyIndex] = startIndex;
                            CellCounts[keyIndex] = i - startIndex;
                        }
        
                        if (i < SortedEntries.Length)
                        {
                            previousCellCode = SortedEntries[i].Key;
                            startIndex = i;
                        }
                    }
                }
            }
        }

        [BurstCompile]
        private struct PrepareMortonJobForArray : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float3> Positions;
            [ReadOnly]
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
            if (SystemAPI.TryGetSingletonRW<SpatialPartitioningData>(out var data))
            {
                JobHandle.CombineDependencies(data.ValueRO.HandleA, data.ValueRO.HandleB, state.Dependency).Complete();
        
                DisposeIfCreated(ref data.ValueRW.MortonA);
                DisposeIfCreated(ref data.ValueRW.MortonB);
                DisposeIfCreated(ref data.ValueRW.RadixTempBuffer);
                DisposeIfCreated(ref data.ValueRW.PositionCacheA);
                DisposeIfCreated(ref data.ValueRW.PositionCacheB);
                DisposeIfCreated(ref data.ValueRW.EntitiesCacheA);
                DisposeIfCreated(ref data.ValueRW.EntitiesCacheB);
                DisposeIfCreated(ref data.ValueRW.CellStartsA);
                DisposeIfCreated(ref data.ValueRW.CellStartsB);
                DisposeIfCreated(ref data.ValueRW.CellCountsA);
                DisposeIfCreated(ref data.ValueRW.CellCountsB);
            }
        }
        
        private void DisposeIfCreated<T>(ref NativeArray<T> array) where T : struct
        {
            if (array.IsCreated) array.Dispose();
        }
    }
}
