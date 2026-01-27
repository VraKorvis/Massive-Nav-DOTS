using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PFStar
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PathRequestUpdateSystem))]
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
        private const int IterationLimit = 5000;
        private const int InnerLoopBatchSize = 1;
        
        private const int MaxPossibleAgents = 1000;
        const int MaxPerFrame = 64; 
        
        private int _currentBufferSize;

        private ComponentLookup<PathRequestAgent> _pathRequestLookup;
        private BufferLookup<GridBuffer> _gridBufferLookup;
        private BufferLookup<Waypoint> _waypointLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<GridTag>();
            
            _pathRequestQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<PathRequestAgent>()      
                .WithAllRW<PathRequestMetadata>()   
                .WithAll<PathAgentStatusFindTag>()  
                .Build(ref state);

            _gridQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<GridBuffer, GridTag>()
                .Build(ref state);

            state.RequireForUpdate(_gridQuery);
            
            _neighbours = new NativeArray<int2>(NeighborCount, Allocator.Persistent)
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

            _pathRequestLookup = state.GetComponentLookup<PathRequestAgent>(true);
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
            
            int gridSize = dimX * dimY;
            
            if (!_costSoFar.IsCreated && gridSize > 0)
            {
                _currentBufferSize = gridSize;
                int totalCapacity = _currentBufferSize * MaxPossibleAgents;
        
                _costSoFar = new NativeArray<float>(totalCapacity, Allocator.Persistent);
                _cameFrom = new NativeArray<int2>(totalCapacity, Allocator.Persistent);
                _openSet = new NativeMinHeap(totalCapacity, Allocator.Persistent);
        
            }

            if (!_costSoFar.IsCreated) return;
            
            int totalWaiting = _pathRequestQuery.CalculateEntityCount();
            if (totalWaiting == 0) return;
            
            var entities = _pathRequestQuery.ToEntityArray(Allocator.TempJob);
            var metadata = _pathRequestQuery.ToComponentDataArray<PathRequestMetadata>(Allocator.TempJob);
            
            if (entities.Length == 0) return;
            
            var sortableList = new NativeList<SortableRequest>(entities.Length, Allocator.TempJob);
            for (int i = 0; i < entities.Length; i++)
            {
                sortableList.Add(new SortableRequest { Entity = entities[i], RequestTime = metadata[i].RequestTime });
            }
            sortableList.Sort(new RequestComparer());
            
            int agentsToProcess = math.min(totalWaiting, MaxPerFrame);
            var processingEntities = new NativeArray<Entity>(agentsToProcess, Allocator.TempJob);
            var pathArray = new NativeArray<PathRequestAgent>(agentsToProcess, Allocator.TempJob);

            for (int i = 0; i < agentsToProcess; i++)
            {
                processingEntities[i] = sortableList[i].Entity;
            }

            entities.Dispose();
            metadata.Dispose();
            sortableList.Dispose();

            var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            var parallelEcb = ecb.AsParallelWriter();
            _pathRequestLookup.Update(ref state);
            
            var collectHandle = new CollectSortedPathsJob
            {
                EntitiesToProcess = processingEntities,
                PathRequestLookup = _pathRequestLookup,
                PathArray = pathArray,
                ECB = parallelEcb
            }.Schedule(agentsToProcess, 64, state.Dependency);
            
            var findHandle = new FindPathAStarJob
            {
                dimX = dimX,
                dimY = dimY,
                gridStride = _currentBufferSize,
                grid = gridBuffer,
                WaypointsLookup = _waypointLookup,
                pathList = pathArray,
                CostSoFar = _costSoFar,
                CameFrom = _cameFrom,
                OpenSet = _openSet,
                neighbours = _neighbours,
                ECB = parallelEcb
            }.Schedule(agentsToProcess, InnerLoopBatchSize, collectHandle);

            state.Dependency = findHandle;
            
            processingEntities.Dispose(state.Dependency);
            pathArray.Dispose(state.Dependency);
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
        private struct CollectSortedPathsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> EntitiesToProcess;
            [ReadOnly] public ComponentLookup<PathRequestAgent> PathRequestLookup;
    
            [WriteOnly] public NativeArray<PathRequestAgent> PathArray;
            public EntityCommandBuffer.ParallelWriter ECB;
            
            public void Execute(int index)
            {
                Entity entity = EntitiesToProcess[index];
                PathRequestAgent path = PathRequestLookup[entity];

                PathArray[index] = path;
                
                ECB.SetComponentEnabled<PathAgentStatusFindTag>(index, entity, false);
                ECB.SetComponentEnabled<PathAgentStatusProcessTag>(index, entity, true);
                ECB.SetComponent(index, entity, new PathAgentStatus { Value = AgentStatus.Process });
                
            }
        }

        [BurstCompile]
        private unsafe struct FindPathAStarJob : IJobParallelFor
        {
            public int dimX;
            public int dimY;
            public int gridStride;
            
            public EntityCommandBuffer.ParallelWriter ECB;
            
            [ReadOnly] public DynamicBuffer<GridBuffer> grid;

            [NativeDisableParallelForRestriction] public BufferLookup<Waypoint> WaypointsLookup;

            [ReadOnly] public NativeArray<PathRequestAgent> pathList;

            [NativeDisableParallelForRestriction] public NativeArray<float> CostSoFar;

            [NativeDisableParallelForRestriction] public NativeArray<int2> CameFrom;

            [NativeDisableParallelForRestriction] public NativeMinHeap OpenSet;

            [ReadOnly] public NativeArray<int2> neighbours;

            public void Execute(int index)
            {
                var costSoFarSlice = CostSoFar.Slice(index * gridStride, gridStride);
                var cameFromSlice = CameFrom.Slice(index * gridStride, gridStride);
                
                var openSetSlice = OpenSet.Slice(index * gridStride, gridStride);
                
                var request = pathList[index];
                
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
                else 
                {
                    ECB.SetComponent(index, request.owner, new PathAgentStatus { Value = AgentStatus.None });
    
                    ECB.SetComponentEnabled<PathAgentStatusFindTag>(index, request.owner, false);
                    ECB.SetComponentEnabled<PathAgentStatusProcessTag>(index, request.owner, false);
    
                    ECB.SetComponentEnabled<PathAgentStatusNoneTag>(index, request.owner, true);
                }
                
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

                int counter = 0;
                while (box.openSet.HasNext())
                {
                    if (counter++ > IterationLimit) return false;
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