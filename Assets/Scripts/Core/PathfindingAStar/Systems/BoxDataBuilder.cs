using Map.Grid;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Core.PathfindingAStar
{
    public struct BoxData
    {
        [ReadOnly]
        public BlobAssetReference<GridBlob> GridBlob;
        public int DimX;
        public int DimY;
        
        public int2 StartPos;
        public int2 Destination;
        
        public DynamicBuffer<Waypoint> Waypoints;
        
        public NativeSlice<NodeData> CostSoFar;
        public NativeSlice<int2> CameFrom;
        public NativeBinaryMinHeap OpenSet;

        public uint SearchID;

        public float GreedyCoef;
        public int IterationLimit;
    }
    
    public struct BoxDataBuilder
    {
        private BoxData _box;

        public static BoxDataBuilder Create() => new BoxDataBuilder { _box = new BoxData() };

        public BoxDataBuilder WithGrid(BlobAssetReference<GridBlob> grid, int2 dim)
        {
            _box.GridBlob = grid;
            _box.DimX = dim.x;
            _box.DimY = dim.y;
            return this;
        }

        public BoxDataBuilder WithWaypoints(DynamicBuffer<Waypoint> waypoints)
        {
            _box.Waypoints = waypoints;
            return this;
        }

        public BoxDataBuilder WithPathPoints(int2 start, int2 dest)
        {
            _box.StartPos = start;
            _box.Destination = dest;
            return this;
        }

        public BoxDataBuilder WithBuffers(NativeSlice<NodeData> costSoFar, NativeSlice<int2> cameFrom, NativeBinaryMinHeap openSet)
        {
            _box.CostSoFar = costSoFar;
            _box.CameFrom = cameFrom;
            _box.OpenSet = openSet;
            return this;
        }

        public BoxDataBuilder WithSettings(uint searchID, float greedy, int limit)
        {
            _box.SearchID = searchID;
            _box.GreedyCoef = greedy;
            _box.IterationLimit = limit;
            return this;
        }

        public BoxData Build() => _box;
    }
}
