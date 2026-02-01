using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[assembly: RegisterGenericJobType(typeof(SortJob<PFStar.SortableRequest, PFStar.RequestComparer>))]

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
        private NativeArray<int> _searchVersions;
        private NativeArray<int2> _cameFrom;
        private NativeMinHeap _openSet;

        private const int NeighborCount = 8;

        private int _currentBufferSize;

        private ComponentLookup<PFRequestAgent> _pathRequestLookup;
        private ComponentLookup<PFAgentState> _agentStateLookup;
        private BufferLookup<Waypoint> _waypointLookup;
        private ComponentLookup<GridBlobReference> _gridBlobLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<GridTag>();

            _pathRequestQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<PFRequestAgent>()
                .WithAllRW<PFRequestMetadata>()
                .WithAll<PFAgentState>()
                .Build(ref state);

            _gridQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<GridTag, GridBlobReference>()
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

            _gridBlobLookup = state.GetComponentLookup<GridBlobReference>(true);
            _pathRequestLookup = state.GetComponentLookup<PFRequestAgent>(true);
            _waypointLookup = state.GetBufferLookup<Waypoint>(false);
            _agentStateLookup = state.GetComponentLookup<PFAgentState>(false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<PathfindingSettings>(out var pfSettings)) return;

            _waypointLookup.Update(ref state);
            _gridBlobLookup.Update(ref state);
            _pathRequestLookup.Update(ref state);
            _agentStateLookup.Update(ref state);

            var gridEntity = SystemAPI.GetSingletonEntity<GridTag>();
            var gridSettings = SystemAPI.GetComponent<GridSettings>(gridEntity);

            var gridBlobRef = _gridBlobLookup[gridEntity].Value;
            int dimX = gridSettings.Dimensions.x;
            int dimY = gridSettings.Dimensions.y;

            int gridSize = dimX * dimY;

            if (!_costSoFar.IsCreated && gridSize > 0)
            {
                _currentBufferSize = gridSize;
                int totalCapacity = _currentBufferSize * pfSettings.MaxPossibleAgents;

                _searchVersions = new NativeArray<int>(totalCapacity, Allocator.Persistent);
                _costSoFar = new NativeArray<float>(totalCapacity, Allocator.Persistent);
                _cameFrom = new NativeArray<int2>(totalCapacity, Allocator.Persistent);
                _openSet = new NativeMinHeap(totalCapacity, Allocator.Persistent);
            }

            int totalWaiting = _pathRequestQuery.CalculateEntityCount();
            if (totalWaiting == 0) return;

            var sortableList = new NativeList<SortableRequest>(totalWaiting, Allocator.TempJob);

            var collectJob = new CollectRequestsJob
            {
                SortableList = sortableList.AsParallelWriter()
            }.ScheduleParallel(_pathRequestQuery, state.Dependency);

            int agentsToProcess = math.min(totalWaiting, pfSettings.MaxPerFrame);
            var processingEntities = new NativeArray<Entity>(agentsToProcess, Allocator.TempJob);
            var pathArray = new NativeArray<PFRequestAgent>(agentsToProcess, Allocator.TempJob);

            var sortHandle = sortableList.SortJob(new RequestComparer()).Schedule(collectJob);

            var prepareHandle = new PrepareAndMarkJob
            {
                SortedList = sortableList,
                MaxToProcess = pfSettings.MaxPerFrame,
                ProcessingEntities = processingEntities,
                PathArray = pathArray,
                PathRequestLookup = _pathRequestLookup,
                AgentStateLookup = _agentStateLookup,
            }.Schedule(sortHandle);

            var findHandle = new FindPathAStarJob
            {
                GreedyCoef = pfSettings.GreedyCoef,
                IterationLimit = pfSettings.IterationLimit,
                GridBlob = gridBlobRef,
                DimX = dimX,
                DimY = dimY,
                ProcessingEntities = processingEntities,
                GridStride = _currentBufferSize,
                WaypointsLookup = _waypointLookup,
                ActualPathLookup = _pathRequestLookup,
                PathList = pathArray,
                SearchVersions = _searchVersions,
                CurrentFrame = Time.frameCount,
                CostSoFar = _costSoFar,
                CameFrom = _cameFrom,
                OpenSet = _openSet,
                Neighbours = _neighbours,
                AgentStateLookup = _agentStateLookup,
            }.Schedule(agentsToProcess, pfSettings.InnerLoopBatchSize, prepareHandle);

            state.Dependency = findHandle;

            processingEntities.Dispose(state.Dependency);
            pathArray.Dispose(state.Dependency);
            sortableList.Dispose(state.Dependency);
        }

        [BurstCompile]
        public partial struct CollectRequestsJob : IJobEntity
        {
            [WriteOnly] [NativeDisableContainerSafetyRestriction]
            public NativeList<SortableRequest>.ParallelWriter SortableList;

            void Execute(Entity entity, in PFRequestMetadata metadata, in PFAgentState state)
            {
                if ((state.Flags & (byte)PFAgentsStatus.Find) != 0)
                {
                    SortableList.AddNoResize(new SortableRequest
                    {
                        Entity = entity,
                        RequestTime = metadata.RequestTime
                    });
                }
            }
        }

        [BurstCompile]
        private struct PrepareAndMarkJob : IJob
        {
            public int MaxToProcess;

            [ReadOnly] [NativeDisableContainerSafetyRestriction]
            public NativeList<SortableRequest> SortedList;

            [WriteOnly] public NativeArray<Entity> ProcessingEntities;
            [WriteOnly] public NativeArray<PFRequestAgent> PathArray;

            [ReadOnly] public ComponentLookup<PFRequestAgent> PathRequestLookup;
            public ComponentLookup<PFAgentState> AgentStateLookup;

            public void Execute()
            {
                int count = math.min(SortedList.Length, MaxToProcess);

                for (int i = 0; i < MaxToProcess; i++)
                {
                    if (i < count)
                    {
                        Entity entity = SortedList[i].Entity;
                        ProcessingEntities[i] = entity;
                        PathArray[i] = PathRequestLookup[entity];

                        var state = AgentStateLookup[entity];
                        state.Flags = (byte)PFAgentsStatus.Process;
                        AgentStateLookup[entity] = state;
                    }
                    else
                    {
                        ProcessingEntities[i] = Entity.Null;
                    }
                }
            }
        }

        [BurstCompile]
        private unsafe struct FindPathAStarJob : IJobParallelFor
        {
            public int DimX;
            public int DimY;
            public int GridStride;

            public float GreedyCoef;
            public int IterationLimit;

            public int CurrentFrame;

            [ReadOnly] public BlobAssetReference<GridBlob> GridBlob;
            [NativeDisableParallelForRestriction] public BufferLookup<Waypoint> WaypointsLookup;

            [ReadOnly] public NativeArray<Entity> ProcessingEntities;
            [ReadOnly] public NativeArray<PFRequestAgent> PathList;
            [ReadOnly] public ComponentLookup<PFRequestAgent> ActualPathLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<PFAgentState> AgentStateLookup;

            [NativeDisableParallelForRestriction] public NativeArray<int> SearchVersions;
            [NativeDisableParallelForRestriction] public NativeArray<float> CostSoFar;
            [NativeDisableParallelForRestriction] public NativeArray<int2> CameFrom;
            [NativeDisableParallelForRestriction] public NativeMinHeap OpenSet;

            [ReadOnly] public NativeArray<int2> Neighbours;

            public void Execute(int index)
            {
                if (ProcessingEntities[index] == Entity.Null) return;

                var searchVersionsSlice = SearchVersions.Slice(index * GridStride, GridStride);
                var costSoFarSlice = CostSoFar.Slice(index * GridStride, GridStride);
                var cameFromSlice = CameFrom.Slice(index * GridStride, GridStride);

                var openSetSlice = OpenSet.Slice(index * GridStride, GridStride);

                var request = PathList[index];

                UnsafeUtility.MemClear(costSoFarSlice.GetUnsafePtr(), costSoFarSlice.Length * sizeof(float));
                UnsafeUtility.MemClear(cameFromSlice.GetUnsafePtr(), cameFromSlice.Length * sizeof(int2));
                openSetSlice.Clear();

                if (request.Owner == Entity.Null) return;

                if (ActualPathLookup.HasComponent(request.Owner))
                {
                    request.Destination = ActualPathLookup[request.Owner].Destination;
                }

                var waypoints = WaypointsLookup[request.Owner];
                waypoints.Clear();

                int uniqueSearchID = math.max(1, (CurrentFrame * 100000) + index);

                var box = new BoxData
                {
                    GreedyCoef = GreedyCoef,
                    IterationLimit = IterationLimit,
                    GridBlob = GridBlob,
                    Waypoints = waypoints,
                    DimX = DimX, DimY = DimY,
                    StartPos = request.StartCoord,
                    Destination = request.Destination,
                    CostSoFar = costSoFarSlice,
                    CameFrom = cameFromSlice,
                    OpenSet = openSetSlice,
                    SearchVersions = searchVersionsSlice,
                    SearchID = uniqueSearchID,
                };

                if (FindPath(ref box))
                {
                    BuildPath(ref GridBlob.Value, ref box);
                    var state = AgentStateLookup[request.Owner];
                    state.Flags = (byte)PFAgentsStatus.Process;
                    AgentStateLookup[request.Owner] = state;
                }
                else
                {
                    var state = AgentStateLookup[request.Owner];
                    state.Flags = (byte)PFAgentsStatus.None;
                    AgentStateLookup[request.Owner] = state;
                }
            }

            private struct BoxData
            {
                [ReadOnly] public BlobAssetReference<GridBlob> GridBlob;
                public DynamicBuffer<Waypoint> Waypoints;
                public int DimX;
                public int DimY;
                public int2 StartPos;
                public int2 Destination;
                public NativeSlice<float> CostSoFar;
                public NativeSlice<int2> CameFrom;
                public NativeMinHeap OpenSet;

                public NativeSlice<int> SearchVersions;
                public int SearchID;

                public float GreedyCoef;
                public int IterationLimit;
            }

            private bool FindPath(ref BoxData box)
            {
                ref var grid = ref box.GridBlob.Value;

                if (box.StartPos.Equals(box.Destination))
                {
                    var indexCell = GridUtils.CoordToIndex(box.Destination, box.DimX);
                    box.Waypoints.Add(new Waypoint
                    {
                        point = GridUtils.CoordToWorld(ref grid, indexCell)
                    });
                    return false;
                }

                var startIdx = GridUtils.CoordToIndex(box.StartPos, box.DimX);
                box.CostSoFar[startIdx] = 0;
                box.SearchVersions[startIdx] = box.SearchID;

                var H = GridUtils.H_Octile(box.StartPos, box.Destination);
                float weightedH = H * box.GreedyCoef;
                box.OpenSet.Push(new MinHeapNode(box.StartPos, weightedH, weightedH));
                float minH = float.MaxValue;

                int2 bestPointSoFar = box.StartPos;

                int counter = 0;
                while (box.OpenSet.HasNext())
                {
                    if (counter++ > box.IterationLimit)
                    {
                        box.Destination = bestPointSoFar;
                        return true;
                    }

                    var current = box.OpenSet.Pop();

                    if (current.DistanceToGoal < minH)
                    {
                        minH = current.DistanceToGoal;
                        bestPointSoFar = current.Position;
                    }

                    if (current.Position.Equals(box.Destination)) return true;

                    var fromIndex = GridUtils.CoordToIndex(current.Position, box.DimX);
                    var initialCost = box.CostSoFar[fromIndex];

                    for (int i = 0; i < Neighbours.Length; i++)
                    {
                        var nextPosition = current.Position + Neighbours[i];
                        if (nextPosition.x < 0 || nextPosition.x >= box.DimX ||
                            nextPosition.y < 0 || nextPosition.y >= box.DimY) continue;

                        var toIndex = GridUtils.CoordToIndex(nextPosition, box.DimX);
                        var cellCost = GetCost(toIndex, i, ref grid);

                        if (float.IsInfinity(cellCost)) continue;

                        var newCost = initialCost + cellCost;

                        bool isVisited = box.SearchVersions[toIndex] == box.SearchID;
                        float oldCost = isVisited ? box.CostSoFar[toIndex] : float.MaxValue;

                        if (oldCost > 0 && oldCost <= newCost) continue;

                        box.CostSoFar[toIndex] = newCost;
                        box.SearchVersions[toIndex] = box.SearchID;
                        box.CameFrom[toIndex] = current.Position;
                        var h = GridUtils.H_Octile(nextPosition, box.Destination);
                        weightedH = h * GreedyCoef;

                        float f = newCost + weightedH;
                        box.OpenSet.Push(new MinHeapNode(nextPosition, f, weightedH));
                    }
                }

                return false;
            }

            private void BuildPath(ref GridBlob grid, ref BoxData box)
            {
                var ind = GridUtils.CoordToIndex(box.Destination, box.DimX);
                box.Waypoints.Add(new Waypoint { point = GridUtils.CoordToWorld(ref grid, ind) });

                var currCoord = box.CameFrom[ind];
                while (!currCoord.Equals(box.StartPos))
                {
                    int cInd = GridUtils.CoordToIndex(currCoord, box.DimX);
                    box.Waypoints.Add(new Waypoint { point = GridUtils.CoordToWorld(ref grid, cInd) });
                    currCoord = box.CameFrom[cInd];
                }
            }

            private float GetCost(int gridIndex, int neighborIndex, ref GridBlob grid)
            {
                if (grid.CellsType[gridIndex] == CellType.Wall)
                {
                    return float.PositiveInfinity;
                }

                //TODO add Weights
                // float baseWeight = grid.Weights[gridIndex];
                float baseWeight = 1;
                float distanceMultiplier = (neighborIndex < 4) ? 1f : 1.414f;
                return baseWeight * distanceMultiplier;
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            DisposeAll();
        }

        private void DisposeAll()
        {
            if (_neighbours.IsCreated) _neighbours.Dispose();
            if (_costSoFar.IsCreated) _costSoFar.Dispose();
            if (_cameFrom.IsCreated) _cameFrom.Dispose();
            if (_searchVersions.IsCreated) _searchVersions.Dispose();
            if (_openSet.IsCreated) _openSet.Dispose();
        }
    }
}