using Core.Spatial;
using Unity.Collections;

namespace Core.PathfindingAStar
{
    public struct SeparationContext
    {
        public NativeArray<MortonEntry> SortedEntries;
        public NativeArray<int> CellStarts;
        public float SpatialCellSize;
        public float SeparationRadius;
        public float SeparationWeight;
        public int AgentCount;
    }
}
