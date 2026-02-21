using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Core.Spatial
{
    public struct SpatialPartitioningData : IComponentData, IDisposable
    {
        public NativeArray<MortonEntry> MortonA;
        public NativeArray<MortonEntry> MortonB;
        public NativeArray<MortonEntry> RadixTempBuffer;
        public NativeArray<float3> PositionCacheA;
        public NativeArray<float3> PositionCacheB;
        public NativeArray<Entity> EntitiesCacheA;
        public NativeArray<Entity> EntitiesCacheB;
        public NativeArray<int> CellStartsA;
        public NativeArray<int> CellStartsB;
        public NativeArray<int> CellCountsA;
        public NativeArray<int> CellCountsB;

        public JobHandle HandleA;
        public JobHandle HandleB;
        public bool IsBufferA;
        public bool Initialized;
        public int AgentCount;
        
        public void Dispose()
        {
            HandleA.Complete();
            HandleB.Complete();
            if (MortonA.IsCreated) MortonA.Dispose();
            if (MortonB.IsCreated) MortonB.Dispose();
            if (RadixTempBuffer.IsCreated) RadixTempBuffer.Dispose();
            if (PositionCacheA.IsCreated) PositionCacheA.Dispose();
            if (PositionCacheB.IsCreated) PositionCacheB.Dispose();
            if (EntitiesCacheA.IsCreated) EntitiesCacheA.Dispose();
            if (EntitiesCacheB.IsCreated) EntitiesCacheB.Dispose();
            if (CellStartsA.IsCreated) CellStartsA.Dispose();
            if (CellStartsB.IsCreated) CellStartsB.Dispose();
            if (CellCountsA.IsCreated) CellCountsA.Dispose();
            if (CellCountsB.IsCreated) CellCountsB.Dispose();
        }
        
    }
}
