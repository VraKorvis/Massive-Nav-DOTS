using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PFStar.Morton
{
    public struct SpatialPartitioningData : IComponentData
    {
        public NativeArray<MortonEntry> MortonA;
        public NativeArray<MortonEntry> MortonB;
        public NativeArray<MortonEntry> RadixTempBuffer;
        public NativeArray<float3> PositionCacheA;
        public NativeArray<float3> PositionCacheB;
        public NativeArray<int> CellStartsA;
        public NativeArray<int> CellStartsB;

        public JobHandle HandleA;
        public JobHandle HandleB;
        public bool IsBufferA;
        public bool Initialized;
        public int AgentCount;
    }
}
