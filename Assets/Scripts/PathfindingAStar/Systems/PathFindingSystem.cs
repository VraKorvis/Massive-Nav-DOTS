using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PathfindingAStar
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PathAgentStatusSystem))]
    [BurstCompile]
    public partial struct PathFindingSystem : ISystem
    {
        private EntityQuery _pathRequestQuery;
        private EntityQuery _gridQuery;

        private NativeArray<int2> _neighbours;
        private NativeArray<float> _costSoFar;
        private NativeArray<int2> _cameFrom;
        private NativeMinHeap _openSet;

        private const int NeighborCount = 8;
        private const int WorkerCount = 5;
        private const int IterationLimit = 100;
        private const int InnerLoopBatchSize = 1;

        private int _currentBufferSize;

        private BufferLookup<GridBuffer> _gridBufferLookup;
        private BufferLookup<Waypoint> _waypointLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridTag>();
            _pathRequestQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PathRequestAgent>()
                .WithAll<PathAgentStatusFindTag>()
                .Build(ref state);

            _gridQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<GridBuffer, GridTag>()
                .Build(ref state);

            state.RequireForUpdate(_gridQuery);

            // _neighbours = new NativeArray<int2>(4, Allocator.Persistent)
            // {
            //     [0] = new int2(-1, 0), [1] = new int2(0, 1),
            //     [2] = new int2(1, 0), [3] = new int2(0, -1)
            // };
            
            _neighbours = new NativeArray<int2>(8, Allocator.Persistent)
            {
                [0] = new int2(-1, 0), 
                [1] = new int2(0, 1),
                [2] = new int2(1, 0), 
                [3] = new int2(0, -1),

                [4] = new int2(-1, 1), 
                [5] = new int2(1, 1),
                [6] = new int2(1, -1), 
                [7] = new int2(-1, -1)
            };

            _gridBufferLookup = state.GetBufferLookup<GridBuffer>(true);
            _waypointLookup = state.GetBufferLookup<Waypoint>(false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _gridBufferLookup.Update(ref state);
            _waypointLookup.Update(ref state);

            var gridEntity = SystemAPI.GetSingletonEntity<GridTag>();
            var gridBuffer = _gridBufferLookup[gridEntity];
            var settings = SystemAPI.GetComponent<GridSettings>(gridEntity);

            int dimX = settings.Dimensions.x;
            int dimY = settings.Dimensions.y;
            int count = _pathRequestQuery.CalculateEntityCount();

            CheckAndResizeBuffers(dimX * dimY, count);

            if (count == 0) return;

            var pathArray = new NativeArray<PathRequestAgent>(count, Allocator.TempJob);

            var collectHandle = new CollectPathsJob
            {
                PathArray = pathArray
            }.ScheduleParallel(_pathRequestQuery, state.Dependency);

            var query = SystemAPI.QueryBuilder().WithAll<PathAgentStatusFindTag>().Build();
            int searchingNow = query.CalculateEntityCount();
            // UnityEngine.Debug.Log($"[PERF] Агентов ищут путь в этом кадре: {searchingNow}");   
            
            var findHandle = new FindPathAStarJob
            {
                dimX = dimX,
                dimY = dimY,
                grid = gridBuffer,
                WaypointsLookup = _waypointLookup,
                pathList = pathArray,
                CostSoFar = _costSoFar,
                CameFrom = _cameFrom,
                OpenSet = _openSet,
                neighbours = _neighbours
            }.Schedule(pathArray.Length, 1, collectHandle);

            state.Dependency = findHandle;

            pathArray.Dispose(findHandle);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (_neighbours.IsCreated) _neighbours.Dispose();
            if (_costSoFar.IsCreated) _costSoFar.Dispose();
            if (_cameFrom.IsCreated) _cameFrom.Dispose();
            if (_openSet.IsCreated) _openSet.Dispose();
        }

        [BurstCompile]
        private void CheckAndResizeBuffers(int newSize, int agentCount)
        {
            int requiredSize = newSize * agentCount;
            if (_currentBufferSize == requiredSize && _costSoFar.IsCreated) return;
            
            if (_costSoFar.IsCreated) _costSoFar.Dispose();
            if (_cameFrom.IsCreated) _cameFrom.Dispose();
            if (_openSet.IsCreated) _openSet.Dispose();

            _costSoFar = new NativeArray<float>(requiredSize, Allocator.Persistent);
            _cameFrom = new NativeArray<int2>(requiredSize, Allocator.Persistent);

            // int openSetSize = (IterationLimit + 1) * NeighborCount * WorkerCount;
            _openSet = new NativeMinHeap(requiredSize, Allocator.Persistent);

            _currentBufferSize = newSize;
        }

        [BurstCompile]
        public partial struct CollectPathsJob : IJobEntity
        {
            [WriteOnly] public NativeArray<PathRequestAgent> PathArray;

            public void Execute([EntityIndexInQuery] int entityInQueryIndex, in PathRequestAgent path)
            {
                PathArray[entityInQueryIndex] = path;
            }
        }

        [BurstCompile]
        private unsafe struct FindPathAStarJob : IJobParallelFor
        {
            public int dimX;
            public int dimY;

            [ReadOnly] public DynamicBuffer<GridBuffer> grid;

            [NativeDisableParallelForRestriction] public BufferLookup<Waypoint> WaypointsLookup;

            [ReadOnly] public NativeArray<PathRequestAgent> pathList;

            [NativeDisableParallelForRestriction] public NativeArray<float> CostSoFar;

            [NativeDisableParallelForRestriction] public NativeArray<int2> CameFrom;

            [NativeDisableParallelForRestriction] public NativeMinHeap OpenSet;

            [ReadOnly] public NativeArray<int2> neighbours;

            public void Execute(int index)
            {
                var size = dimX * dimY;
                
                var costSoFarSlice = CostSoFar.Slice(index * size, size);
                var cameFromSlice = CameFrom.Slice(index * size, size);
                var openSetSize = (IterationLimit + 1) * NeighborCount;
                // var openSetSlice = OpenSet.Slice(index * openSetSize, openSetSize);
                var openSetSlice = OpenSet.Slice(index * size, size);

                // var pathReqInd = index;

                // while (pathList.Length > pathReqInd)
                // {
                    var request = pathList[index];
                    // pathReqInd += WorkerCount;

                    // Очищаем буферы для нового поиска пути
                    // for (int i = 0; i < costSoFarSlice.Length; i++)
                    // {
                    //     costSoFarSlice[i] = 0f;
                    // }

                    UnsafeUtility.MemClear(costSoFarSlice.GetUnsafePtr(), costSoFarSlice.Length * sizeof(float));
                    UnsafeUtility.MemClear(cameFromSlice.GetUnsafePtr(), cameFromSlice.Length * sizeof(int2));
                    openSetSlice.Clear();
                    
                    if (request.owner == Entity.Null) return;

                    var waypoints = WaypointsLookup[request.owner];
                    waypoints.Clear();

                    var box = new BoxData
                    {
                        waypoints = waypoints,
                        dimX = dimX, dimY = dimY,
                        startPos = request.startCoord,
                        destination = request.destination,
                        costSoFar = costSoFarSlice,
                        cameFrom = cameFromSlice,
                        openSet = openSetSlice
                    };

                    if (FindPath(ref box))
                    {
                        BuildPath(ref box);
                        
                    }
                // }
            }

            private struct BoxData
            {
                public DynamicBuffer<Waypoint> waypoints;
                public int dimX;
                public int dimY;
                public int2 startPos;
                public int2 destination;
                public NativeSlice<float> costSoFar;
                public NativeSlice<int2> cameFrom;
                public NativeMinHeap openSet;
            }

            private bool FindPath(ref BoxData box)
            {
                if (box.startPos.Equals(box.destination))
                {
                    var indexCell = GridUtils.CoordToIndex(box.destination, box.dimX);
                    box.waypoints.Add(new Waypoint { point = GridUtils.CoordToWorld(grid, indexCell) });
                    return false;
                }

                var H = GridUtils.H_Euclid(box.startPos, box.destination);
                box.openSet.Push(new MinHeapNode(box.startPos, H, H));

                while (box.openSet.HasNext())
                {
                    var current = box.openSet.Pop();
                    if (current.Position.Equals(box.destination)) return true;

                    var fromIndex = GridUtils.CoordToIndex(current.Position, box.dimX);
                    var initialCost = box.costSoFar[fromIndex];

                    for (int i = 0; i < neighbours.Length; i++)
                    {
                        var nextPosition = current.Position + neighbours[i];
                        if (nextPosition.x < 0 || nextPosition.x >= box.dimX ||
                            nextPosition.y < 0 || nextPosition.y >= box.dimY) continue;

                        var toIndex = GridUtils.CoordToIndex(nextPosition, box.dimX);
                        var cellCost = GetCost(toIndex, i);
                        if (float.IsInfinity(cellCost)) continue;

                        var newCost = initialCost + cellCost;
                        var oldCost = box.costSoFar[toIndex];

                        if (oldCost > 0 && oldCost <= newCost) continue;

                        box.costSoFar[toIndex] = newCost;
                        box.cameFrom[toIndex] = current.Position;
                        var h = GridUtils.H_Euclid(nextPosition, box.destination);
                        box.openSet.Push(new MinHeapNode(nextPosition, newCost + h, h));
                    }
                }

                return false;
            }

            private void BuildPath(ref BoxData box)
            {
                var ind = GridUtils.CoordToIndex(box.destination, box.dimX);
                box.waypoints.Add(new Waypoint { point = GridUtils.CoordToWorld(grid, ind) });

                var currCoord = box.cameFrom[ind];
                while (!currCoord.Equals(box.startPos))
                {
                    int cInd = GridUtils.CoordToIndex(currCoord, box.dimX);
                    box.waypoints.Add(new Waypoint { point = GridUtils.CoordToWorld(grid, cInd) });
                    currCoord = box.cameFrom[cInd];
                }
            }

            // private float GetCost(int index)
            // {
            //     return grid[index].Type == CellType.Wall ? float.PositiveInfinity : 1f;
            // }
            
            private float GetCost(int gridIndex, int neighborIndex) 
            {
                if (grid[gridIndex].Type == CellType.Wall)
                {
                    return float.PositiveInfinity;
                }
                
                return (neighborIndex < 4) ? 1f : 1.414f;
            }
        }
    }
}