using Gameplay.Player;
using Map;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Transforms;
using UnityEngine;

[assembly: RegisterGenericJobType(typeof(SortJob<PFStar.SortableRequest, PFStar.RequestComparer>))]

namespace PFStar
{
    public struct AStarCrowd
    {
        public static bool FindPath(ref BoxData box, NativeArray<int2> neighbours, float greedyCoef)
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

                for (int i = 0; i < neighbours.Length; i++)
                {
                    var nextPosition = current.Position + neighbours[i];
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
                    weightedH = h * greedyCoef;

                    float f = newCost + weightedH;
                    box.OpenSet.Push(new MinHeapNode(nextPosition, f, weightedH));
                }
            }

            return false;
        }

        public static void BuildPath(ref GridBlob grid, ref BoxData box)
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

        private static float GetCost(int gridIndex, int neighborIndex, ref GridBlob grid)
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

    public struct BoxData
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

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PathRequestUpdateStatusSystem))]
    [BurstCompile]
    public partial struct PathFindingSystem : ISystem
    {
#if UNITY_EDITOR
        private static readonly ProfilerMarker k_ProfilePlayerPathLogic = new("[PF] Player.Pathfinding.Scheduling");
#endif


        private const int VipOffset = 1;
        private const int VipIterationLimit = 10000;

        private EntityQuery _playerQuery;
        private EntityQuery _pathRequestQuery;
        private EntityQuery _gridQuery;

        private NativeArray<int2> _neighbours;
        private NativeArray<float> _costSoFar;
        private NativeArray<int> _searchVersions;
        private NativeArray<int2> _cameFrom;
        private NativeMinHeap _openSet;

        private const int NeighborCount = 8;

        private int _currentBufferSize;

        private ComponentLookup<LocalTransform> _transformLookup;
        private ComponentLookup<NavigationTargetGridData> _navigationTargetLookup;
        private ComponentLookup<PFRequestAgent> _pathRequestLookup;
        private ComponentLookup<PFAgentState> _agentStateLookup;

        private ComponentLookup<GridBlobReference> _gridBlobLookup;
        private BufferLookup<Waypoint> _waypointLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<GridTag>();

            _playerQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<PFRequestAgent>()
                .WithAllRW<PFRequestMetadata>()
                .WithAll<PFAgentState>()
                .WithAll<PlayerTag>()
                .Build(ref state);

            _pathRequestQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<PFRequestAgent>()
                .WithAllRW<PFRequestMetadata>()
                .WithAll<PFAgentState>()
                .WithNone<PlayerTag>()
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
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            _pathRequestLookup = state.GetComponentLookup<PFRequestAgent>(false);
            _navigationTargetLookup = state.GetComponentLookup<NavigationTargetGridData>(true);

            _waypointLookup = state.GetBufferLookup<Waypoint>(false);
            _agentStateLookup = state.GetComponentLookup<PFAgentState>(false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<NavigationSettings>(out var navSettings)) return;

            _waypointLookup.Update(ref state);
            _gridBlobLookup.Update(ref state);
            _transformLookup.Update(ref state);
            _pathRequestLookup.Update(ref state);
            _navigationTargetLookup.Update(ref state);
            _agentStateLookup.Update(ref state);

            var batchSize = navSettings.InnerLoopBatchSize;
            var gridEntity = SystemAPI.GetSingletonEntity<GridTag>();
            var gridBlobRef = _gridBlobLookup[gridEntity].Value;
            int dimX = gridBlobRef.Value.Dimensions.x;
            int dimY = gridBlobRef.Value.Dimensions.y;
            int maxPerFrame = navSettings.MaxPerFrame;

            int gridSize = dimX * dimY;

            int currentPhysicalLimit = _costSoFar.IsCreated ? (_costSoFar.Length / _currentBufferSize) - VipOffset : -1;

            bool sizeChanged = gridSize != _currentBufferSize;
            bool limitIncreased = maxPerFrame > currentPhysicalLimit;

            state.Dependency.Complete();
            if (gridSize > 0 && (!_costSoFar.IsCreated || sizeChanged || limitIncreased))
            {
                if (_costSoFar.IsCreated) DisposeAll();

                _currentBufferSize = gridSize;
                int totalCapacity = (maxPerFrame + VipOffset) * _currentBufferSize;

                _searchVersions = new NativeArray<int>(totalCapacity, Allocator.Persistent);
                _costSoFar = new NativeArray<float>(totalCapacity, Allocator.Persistent);
                _cameFrom = new NativeArray<int2>(totalCapacity, Allocator.Persistent);
                _openSet = new NativeMinHeap(totalCapacity, Allocator.Persistent);
            }


#if UNITY_EDITOR
            var markerScope = k_ProfilePlayerPathLogic.Auto();
#endif
            
            if (!_playerQuery.IsEmpty)
            {
                var playerEntity = _playerQuery.GetSingletonEntity();
                var playerState = SystemAPI.GetComponent<PFAgentState>(playerEntity);
                if (SystemAPI.HasComponent<PFRequestAgent>(playerEntity) &&
                    (playerState.Flags & (byte)PFAgentStatus.Find) != 0)
                {
                    var pState = _agentStateLookup[playerEntity];
                    pState.Flags &= (byte)~PFAgentStatus.Find;
                    _agentStateLookup[playerEntity] = pState;

                    var playerRequest = SystemAPI.GetComponent<PFRequestAgent>(playerEntity);
                    var playerJob = new PlayerPathJob
                    {
                        VipIterationLimit = VipIterationLimit,
                        GreedyCoef = 1,
                        PlayerEntity = playerEntity,
                        CostSoFar = _costSoFar,
                        CameFrom = _cameFrom,
                        SearchVersions = _searchVersions,
                        OpenSet = _openSet,
                        StartPos = playerRequest.StartCoord,
                        Destination = playerRequest.Destination,
                        Waypoints = _waypointLookup[playerEntity],
                        GridBlob = gridBlobRef,
                        DimX = dimX,
                        DimY = dimY,
                        GridSize = gridSize,
                        Neighbours = _neighbours,
                        AgentStateLookup = _agentStateLookup,
                    };

                    state.Dependency = playerJob.Schedule(state.Dependency);
                }
            }
            
#if UNITY_EDITOR
            markerScope.Dispose();
#endif
            
            int totalWaiting = _pathRequestQuery.CalculateEntityCount();
            if (totalWaiting == 0) return;

            var sortableList = new NativeList<SortableRequest>(totalWaiting, Allocator.TempJob);

            var collectJobHandle = new CollectRequestsJob
            {
                SortableList = sortableList.AsParallelWriter()
            }.ScheduleParallel(_pathRequestQuery, state.Dependency);

            int physicalLimit = (_costSoFar.Length / _currentBufferSize) - VipOffset;
            int agentsToProcess = math.min(totalWaiting, math.min(maxPerFrame, physicalLimit));
            var processingEntities = new NativeArray<Entity>(agentsToProcess, Allocator.TempJob);
            var pathArray = new NativeArray<PFRequestAgent>(agentsToProcess, Allocator.TempJob);

            var sortHandle = sortableList.SortJob(new RequestComparer()).Schedule(collectJobHandle);

            var prepareHandle = new PrepareAndMarkJob
            {
                ActualCount = sortableList.Length,
                GridOrigin = gridBlobRef.Value.Origin,
                Cellsize = gridBlobRef.Value.CellSize,
                CurrentTime = (float)state.WorldUnmanaged.Time.ElapsedTime,

                SortedList = sortableList,
                ProcessingEntities = processingEntities,
                PathArray = pathArray,

                PathRequestLookup = _pathRequestLookup,
                AgentStateLookup = _agentStateLookup,
                NavigationTargetLookup = _navigationTargetLookup,
                TransformLookup = _transformLookup,
            }.Schedule(agentsToProcess, batchSize, sortHandle);

            var findHandle = new FindPathAStarJob
            {
                Offset = VipOffset,
                GridBlob = gridBlobRef,
                DimX = dimX,
                DimY = dimY,
                CurrentFrame = Time.frameCount,
                GreedyCoef = navSettings.GreedyCoef,
                IterationLimit = navSettings.IterationLimit,

                ProcessingEntities = processingEntities,
                GridStride = _currentBufferSize,
                WaypointsLookup = _waypointLookup,
                ActualPathLookup = _pathRequestLookup,
                AgentStateLookup = _agentStateLookup,
                PathList = pathArray,
                SearchVersions = _searchVersions,

                CostSoFar = _costSoFar,
                CameFrom = _cameFrom,
                OpenSet = _openSet,
                Neighbours = _neighbours,
            }.Schedule(agentsToProcess, batchSize, prepareHandle);

            state.Dependency = findHandle;

            processingEntities.Dispose(state.Dependency);
            pathArray.Dispose(state.Dependency);
            sortableList.Dispose(state.Dependency);
        }

        [BurstCompile]
        private unsafe struct PlayerPathJob : IJob
        {
            public int VipIterationLimit;

            public BlobAssetReference<GridBlob> GridBlob;

            public int DimX;
            public int DimY;
            public float GreedyCoef;

            public NativeArray<float> CostSoFar;
            public NativeArray<int2> CameFrom;
            public NativeArray<int> SearchVersions;
            public NativeMinHeap OpenSet;

            public int2 StartPos;
            public int2 Destination;
            public DynamicBuffer<Waypoint> Waypoints;

            public ComponentLookup<PFAgentState> AgentStateLookup;

            public Entity PlayerEntity;
            [ReadOnly] public NativeArray<int2> Neighbours;
            public int GridSize;

            public void Execute()
            {
                OpenSet.Clear();
                UnsafeUtility.MemClear(CostSoFar.GetUnsafePtr(), GridSize * sizeof(float));
                UnsafeUtility.MemClear(SearchVersions.GetUnsafePtr(), GridSize * sizeof(int));

                Waypoints.Clear();

                var box = new BoxData
                {
                    GridBlob = GridBlob,
                    DimX = DimX,
                    DimY = DimY,
                    GreedyCoef = GreedyCoef,
                    IterationLimit = VipIterationLimit,
                    StartPos = StartPos,
                    Destination = Destination,
                    Waypoints = Waypoints,

                    CostSoFar = CostSoFar,
                    CameFrom = CameFrom,
                    SearchVersions = SearchVersions,
                    OpenSet = OpenSet,
                    SearchID = 1
                };

                if (AStarCrowd.FindPath(ref box, Neighbours, GreedyCoef))
                {
                    AStarCrowd.BuildPath(ref GridBlob.Value, ref box);

                    var state = AgentStateLookup[PlayerEntity];
                    state.Flags = (byte)(Waypoints.Length > 0 ? PFAgentStatus.Process : PFAgentStatus.Idle);
                    AgentStateLookup[PlayerEntity] = state;
                }
                else
                {
                    var state = AgentStateLookup[PlayerEntity];
                    state.Flags = (byte)PFAgentStatus.Idle;
                    AgentStateLookup[PlayerEntity] = state;
                }
            }
        }

        [BurstCompile]
        public partial struct CollectRequestsJob : IJobEntity
        {
            [WriteOnly] [NativeDisableContainerSafetyRestriction]
            public NativeList<SortableRequest>.ParallelWriter SortableList;

            void Execute(Entity entity, in PFRequestMetadata metadata, in PFAgentState state)
            {
                if ((state.Flags & (byte)PFAgentStatus.Find) != 0)
                {
                    SortableList.AddNoResize(new SortableRequest
                    {
                        Entity = entity,
                        RequestTime = metadata.RequestTime,
                        Priority = metadata.Priority,
                    });
                }
            }
        }

        [BurstCompile]
        private struct PrepareAndMarkJob : IJobParallelFor
        {
            public int ActualCount;
            public float CurrentTime;
            public float3 GridOrigin;
            public float Cellsize;

            [ReadOnly] public NativeList<SortableRequest> SortedList;

            [WriteOnly] public NativeArray<Entity> ProcessingEntities;
            [WriteOnly] public NativeArray<PFRequestAgent> PathArray;

            [ReadOnly] public ComponentLookup<NavigationTargetGridData> NavigationTargetLookup;
            [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;

            [NativeDisableParallelForRestriction] public ComponentLookup<PFRequestAgent> PathRequestLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<PFAgentState> AgentStateLookup;

            public void Execute(int i)
            {
                if (i >= SortedList.Length)
                {
                    ProcessingEntities[i] = Entity.Null;
                    return;
                }

                Entity entity = SortedList[i].Entity;

                if (!TransformLookup.HasComponent(entity))
                {
                    ProcessingEntities[i] = Entity.Null;
                    return;
                }

                var pos = TransformLookup[entity].Position;
                var request = PathRequestLookup[entity];

                if (NavigationTargetLookup.TryGetComponent(request.Focus, out var targetData))
                {
                    request.Destination = targetData.CurrentCell;
                    request.StartCoord = GridUtils.WorldToCellCoord(pos, GridOrigin, Cellsize);

                    var state = AgentStateLookup[entity];
                    state.Flags |= (byte)PFAgentStatus.Process;
                    state.Flags &= (byte)~(PFAgentStatus.Find | PFAgentStatus.Idle |
                                           PFAgentStatus.Significant | PFAgentStatus.ForceUpdate);

                    float jitter = (entity.Index % 32) * 0.02f;
                    request.NextAllowedUpdateTime = CurrentTime + 0.5f + jitter;

                    PathRequestLookup[entity] = request;
                    AgentStateLookup[entity] = state;

                    PathArray[i] = request;
                    ProcessingEntities[i] = entity;
                }
                else
                {
                    var state = AgentStateLookup[entity];
                    state.Flags = (byte)PFAgentStatus.Idle;
                    AgentStateLookup[entity] = state;
                    ProcessingEntities[i] = Entity.Null;
                }
            }
        }

        [BurstCompile]
        private unsafe struct FindPathAStarJob : IJobParallelFor
        {
            public int Offset;
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

                int actualIndex = index + Offset;

                var searchVersionsSlice = SearchVersions.Slice(actualIndex * GridStride, GridStride);
                var costSoFarSlice = CostSoFar.Slice(actualIndex * GridStride, GridStride);
                var cameFromSlice = CameFrom.Slice(actualIndex * GridStride, GridStride);
                var openSetSlice = OpenSet.Slice(actualIndex * GridStride, GridStride);

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

                if (AStarCrowd.FindPath(ref box, Neighbours, GreedyCoef))
                {
                    AStarCrowd.BuildPath(ref GridBlob.Value, ref box);
                    var state = AgentStateLookup[request.Owner];
                    state.Flags = (byte)(waypoints.Length > 0 ? PFAgentStatus.Process : PFAgentStatus.Idle);
                    AgentStateLookup[request.Owner] = state;
                }
                else
                {
                    var state = AgentStateLookup[request.Owner];
                    state.Flags = (byte)PFAgentStatus.Idle;
                    AgentStateLookup[request.Owner] = state;
                }
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