using Core.Gameplay;
using Map.Grid;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Transforms;
using UnityEngine;

namespace Core.PathfindingAStar
{
    public struct NodeData
    {
        public float Cost;
        public uint Version;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PathRequestUpdateStatusSystem))]
    [BurstCompile]
    public partial struct PathFindingSystem : ISystem
    {
#if UNITY_EDITOR
        private static readonly ProfilerMarker k_ProfilePlayerPathLogic = new ProfilerMarker("[PF] Player.Pathfinding.Scheduling");
#endif

        private int _vipIterationLimit;

        private EntityQuery _playerQuery;
        private EntityQuery _highPriorityPfQuery;
        private EntityQuery _pathRequestQuery;
        private EntityQuery _gridQuery;

        private ComponentTypeHandle<PFRequestMetadata> _metadataHandle;
        private ComponentTypeHandle<PFAgentState> _stateHandle;
        private EntityTypeHandle _entityHandle;

        [ReadOnly]
        private NativeArray<int2> _neighbours;
        private NativeArray<int> _indexOffsets;
        private NativeArray<float> _distMultipliers;
        private NativeArray<NodeData> _costSoFar;
        private NativeArray<int2> _cameFrom;
        
        private NativeBinaryMinHeap _openSet;
        
        private const int NeighborCount = 8;

        private int _currentBufferSize;

        private ComponentLookup<LocalTransform> _transformLookup;
        private ComponentLookup<NavigationTargetGridData> _navigationTargetLookup;
        private ComponentLookup<PFRequestAgent> _pathRequestLookup;
        private ComponentLookup<PFAgentState> _agentStateLookup;
        private ComponentLookup<PFRequestMetadata> _metaLookup;

        private BufferLookup<Waypoint> _waypointLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridBlobReference>();
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<GridTag>();

            _playerQuery = SystemAPI.QueryBuilder()
                .WithAllRW<PFRequestAgent>()
                .WithAllRW<PFRequestMetadata>()
                .WithAll<PFAgentState>()
                .WithAll<PlayerTag>()
                .Build();

            _highPriorityPfQuery = SystemAPI.QueryBuilder()
                .WithAll<PFRequestAgent>()
                .WithAll<PathfindingHighPriorityTag>()
                .Build();

            _pathRequestQuery = SystemAPI.QueryBuilder()
                .WithAllRW<PFRequestAgent>()
                .WithAllRW<PFRequestMetadata>()
                .WithAll<PFAgentState>()
                .WithNone<PlayerTag>()
                .Build();

            _gridQuery = SystemAPI.QueryBuilder()
                .WithAll<GridTag, GridBlobReference>()
                .Build();

            state.RequireForUpdate(_gridQuery);

            _metadataHandle = state.GetComponentTypeHandle<PFRequestMetadata>(true);
            _stateHandle = state.GetComponentTypeHandle<PFAgentState>(true);
            _entityHandle = state.GetEntityTypeHandle();

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

            _distMultipliers = new NativeArray<float>(8, Allocator.Persistent)
            {
                [0] = 1f,
                [1] = 1f,
                [2] = 1f,
                [3] = 1f,
                [4] = 1.414f,
                [5] = 1.414f,
                [6] = 1.414f,
                [7] = 1.414f
            };
            
            _vipIterationLimit = 10000;
            
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            _pathRequestLookup = state.GetComponentLookup<PFRequestAgent>(false);
            _navigationTargetLookup = state.GetComponentLookup<NavigationTargetGridData>(true);

            _waypointLookup = state.GetBufferLookup<Waypoint>(false);
            _agentStateLookup = state.GetComponentLookup<PFAgentState>(false);
            _metaLookup = state.GetComponentLookup<PFRequestMetadata>(false);

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<NavigationSettings>(out var navSettings)) return;

            _waypointLookup.Update(ref state);
            _transformLookup.Update(ref state);
            _pathRequestLookup.Update(ref state);
            _navigationTargetLookup.Update(ref state);
            _agentStateLookup.Update(ref state);
            _metaLookup.Update(ref state);

            var batchSize = navSettings.InnerLoopBatchSize;
            var gridBlobRef = SystemAPI.GetSingleton<GridBlobReference>().Value;
            var dimensions = gridBlobRef.Value.Dimensions;
            int dimX = dimensions.x;
            int dimY = dimensions.y;
            int maxPerFrame = navSettings.MaxPerFrame;
            int maxRequestsPerFrame = navSettings.MaxRequestsPerFrame;

            int targetJitterRange = navSettings.TargetJitterRange;
            int gridSize = dimX * dimY;

            int vipOffset = math.max(1, _highPriorityPfQuery.CalculateEntityCount());
            int currentPhysicalLimit = _costSoFar.IsCreated ? (_costSoFar.Length / _currentBufferSize) - vipOffset : -1;

            bool sizeChanged = gridSize != _currentBufferSize;
            bool limitIncreased = maxPerFrame > currentPhysicalLimit;

            if (gridSize > 0 && (!_costSoFar.IsCreated || sizeChanged || limitIncreased))
            {
                state.CompleteDependency();
                DisposePathfindingBuffers();

                _currentBufferSize = gridSize;
                int totalCapacity = (maxPerFrame + vipOffset) * _currentBufferSize;

                _costSoFar = new NativeArray<NodeData>(totalCapacity, Allocator.Persistent);
                _cameFrom = new NativeArray<int2>(totalCapacity, Allocator.Persistent);
                _openSet = new NativeBinaryMinHeap(totalCapacity, Allocator.Persistent);

                _indexOffsets = new NativeArray<int>(8, Allocator.Persistent)
                {
                    [0] = -1,
                    [1] = dimX,
                    [2] = 1,
                    [3] = -dimX,

                    [4] = -1 + dimX,
                    [5] = 1 + dimX,
                    [6] = 1 - dimX,
                    [7] = -1 - dimX
                };
            }

            uint uniqueSearchID = state.GlobalSystemVersion * (uint)(maxPerFrame + vipOffset + 1);

            if (uniqueSearchID > uint.MaxValue - 1000)
            {
                Debug.LogWarning("[PF] UniqueSearchID CLOSE to max");
            }

#if UNITY_EDITOR
            using (k_ProfilePlayerPathLogic.Auto())
            {
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
                        var playerJobHandle = new PlayerPathJob
                        {
                            VipIterationLimit = _vipIterationLimit,
                            GreedyCoef = 1,
                            PlayerEntity = playerEntity,
                            CostSoFar = _costSoFar.Slice(0, _currentBufferSize),
                            CameFrom = _cameFrom.Slice(0, _currentBufferSize),
                            OpenSet = _openSet.Slice(0, _currentBufferSize),
                            StartPos = playerRequest.StartCoord,
                            Destination = playerRequest.Destination,
                            Waypoints = _waypointLookup[playerEntity],
                            GridBlob = gridBlobRef,
                            Dimensions = dimensions,
                            Neighbours = _neighbours,
                            DistMultipliers = _distMultipliers,
                            IndexOffsets = _indexOffsets,
                            AgentStateLookup = _agentStateLookup,
                            UniqueSearchID = uniqueSearchID
                        }.Schedule(state.Dependency);

                        state.Dependency = playerJobHandle;
                    }
                }

#if UNITY_EDITOR
            }
#endif

            int totalWaiting = _pathRequestQuery.CalculateEntityCount();
            if (totalWaiting == 0) return;

            int chunkCount = _pathRequestQuery.CalculateChunkCount();
            var stream = new NativeStream(chunkCount, Allocator.TempJob);

            _metadataHandle.Update(ref state);
            _stateHandle.Update(ref state);
            _entityHandle.Update(ref state);

            var collectByBucketJobHandle = new CollectRequestsByBucketJob
            {
                StreamWriter = stream.AsWriter(),
                MetadataHandle = _metadataHandle,
                StateHandle = _stateHandle,
                EntityHandle = _entityHandle
            }.ScheduleParallel(_pathRequestQuery, state.Dependency);

            var finalSortableList = new NativeList<PathRequestCandidate>(maxRequestsPerFrame * 2, Allocator.TempJob);

            var mergeJobHandle = new MergeBucketsJob
            {
                StreamReader = stream.AsReader(),
                FinalList = finalSortableList,
                MaxToCollect = maxRequestsPerFrame * 2
            }.Schedule(collectByBucketJobHandle);

            // var sortableList = new NativeList<PathRequestCandidate>(totalWaiting, Allocator.TempJob);
            //
            // var collectJobHandle = new CollectRequestsJob
            // {
            //     SortableList = sortableList.AsParallelWriter()
            // }.ScheduleParallel(_pathRequestQuery, state.Dependency);

            int physicalLimit = (_costSoFar.Length / _currentBufferSize) - vipOffset;
            int agentsToProcess = math.min(totalWaiting, math.min(maxPerFrame, physicalLimit));
            var processingEntities = new NativeArray<Entity>(agentsToProcess, Allocator.TempJob);
            var pathArray = new NativeArray<PFRequestAgent>(agentsToProcess, Allocator.TempJob);

            // var sortHandle = sortableList.SortJob(new RequestComparer()).Schedule(collectJobHandle);

            var prepareHandle = new PrepareAndMarkJob
            {
                GridBlob = gridBlobRef,
                TargetJitterRange = targetJitterRange,

                CurrentTime = (float)state.WorldUnmanaged.Time.ElapsedTime,
                FrameCount = Time.frameCount,

                SortedList = finalSortableList,
                ProcessingEntities = processingEntities,
                PathArray = pathArray,

                PathRequestLookup = _pathRequestLookup,
                AgentStateLookup = _agentStateLookup,
                NavigationTargetLookup = _navigationTargetLookup,
                TransformLookup = _transformLookup,
                MetaLookup = _metaLookup,
            }.Schedule(agentsToProcess, batchSize, mergeJobHandle);

            var findHandle = new FindPathAStarJob
            {
                Offset = vipOffset,
                GridBlob = gridBlobRef,
                Dimensions = dimensions,
                GreedyCoef = navSettings.GreedyCoef,
                IterationLimit = navSettings.IterationLimit,
                
                ProcessingEntities = processingEntities,
                GridStride = _currentBufferSize,
                WaypointsLookup = _waypointLookup,
                ActualPathLookup = _pathRequestLookup,
                NavigationTargetLookup = _navigationTargetLookup,
                AgentStateLookup = _agentStateLookup,
                MetaLookup = _metaLookup,
                PathList = pathArray,
                UniqueSearchID = uniqueSearchID,

                CostSoFar = _costSoFar,
                CameFrom = _cameFrom,
                OpenSet = _openSet,
                Neighbours = _neighbours,
                DistMultipliers = _distMultipliers,
                IndexOffsets = _indexOffsets,
            }.Schedule(agentsToProcess, batchSize, prepareHandle);

            state.Dependency = findHandle;

            processingEntities.Dispose(state.Dependency);
            pathArray.Dispose(state.Dependency);
            // sortableList.Dispose(state.Dependency);
            finalSortableList.Dispose(state.Dependency);
            state.Dependency = stream.Dispose(state.Dependency);
        }

        [BurstCompile]
        private struct PlayerPathJob : IJob
        {
            public int VipIterationLimit;

            [ReadOnly]
            public BlobAssetReference<GridBlob> GridBlob;

            public int2 Dimensions;

            public float GreedyCoef;

            public NativeSlice<NodeData> CostSoFar;
            public NativeSlice<int2> CameFrom;
            public NativeBinaryMinHeap OpenSet;

            public int2 StartPos;
            public int2 Destination;
            public DynamicBuffer<Waypoint> Waypoints;

            public ComponentLookup<PFAgentState> AgentStateLookup;

            public Entity PlayerEntity;
            [ReadOnly]
            public NativeArray<int2> Neighbours;
            [ReadOnly]
            public NativeArray<float> DistMultipliers;
            [ReadOnly]
            public NativeArray<int> IndexOffsets;

            public uint UniqueSearchID;

            public void Execute()
            {
                Waypoints.Clear();
                OpenSet.Clear();
                uint finalSearchID = UniqueSearchID + (uint)PlayerEntity.Index;

                var box = BoxDataBuilder.Create()
                    .WithBuffers(CostSoFar, CameFrom, OpenSet)
                    .WithGrid(GridBlob, Dimensions)
                    .WithPathPoints(StartPos, Destination)
                    .WithWaypoints(Waypoints)
                    .WithSettings(finalSearchID, GreedyCoef, VipIterationLimit)
                    .Build();
                

                if (AStarCrowd.FindPath(ref box, IndexOffsets, Neighbours, DistMultipliers, GreedyCoef))
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
        private struct CollectRequestsByBucketJob : IJobChunk
        {
            public NativeStream.Writer StreamWriter;
            [ReadOnly]
            public ComponentTypeHandle<PFRequestMetadata> MetadataHandle;
            [ReadOnly]
            public ComponentTypeHandle<PFAgentState> StateHandle;
            [ReadOnly]
            public EntityTypeHandle EntityHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var metadatas = chunk.GetNativeArray(ref MetadataHandle);
                var states = chunk.GetNativeArray(ref StateHandle);
                var entities = chunk.GetNativeArray(EntityHandle);

                StreamWriter.BeginForEachIndex(unfilteredChunkIndex);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var state = states[i];
                    if ((state.Flags & (byte)PFAgentStatus.Find) == 0) continue;

                    var metadata = metadatas[i];
                    int bucketIndex = 2;
                    if (metadata.Weight > 400f)
                    {
                        bucketIndex = 0;
                    }
                    else if (metadata.Weight > 50f)
                    {
                        bucketIndex = 1;
                    }

                    StreamWriter.Write(new PathRequestCandidate
                    {
                        Entity = entities[i],
                        Weight = metadata.Weight,
                        RequestTime = metadata.RequestTime,
                        Priority = bucketIndex
                    });
                }

                StreamWriter.EndForEachIndex();
            }
        }

        [BurstCompile]
        struct MergeBucketsJob : IJob
        {
            public NativeStream.Reader StreamReader;
            public NativeList<PathRequestCandidate> FinalList;
            public int MaxToCollect;

            public void Execute()
            {
                var b0 = new NativeList<PathRequestCandidate>(MaxToCollect, Allocator.Temp);
                var b1 = new NativeList<PathRequestCandidate>(MaxToCollect, Allocator.Temp);
                var b2 = new NativeList<PathRequestCandidate>(MaxToCollect, Allocator.Temp);

                for (int i = 0; i < StreamReader.ForEachCount; i++)
                {
                    int count = StreamReader.BeginForEachIndex(i);
                    for (int j = 0; j < count; j++)
                    {
                        var item = StreamReader.Read<PathRequestCandidate>();
                        if (item.Priority == 0) b0.Add(item);
                        else if (item.Priority == 1) b1.Add(item);
                        else b2.Add(item);
                    }
                    StreamReader.EndForEachIndex();
                }

                Combine(b0);
                Combine(b1);
                Combine(b2);
            }

            private void Combine(NativeList<PathRequestCandidate> bucket)
            {
                for (int i = 0; i < bucket.Length && FinalList.Length < MaxToCollect; i++)
                {
                    FinalList.Add(bucket[i]);
                }
            }
        }

        [BurstCompile]
        public partial struct CollectRequestsJob : IJobEntity
        {
            [WriteOnly]
            [NativeDisableContainerSafetyRestriction]
            public NativeList<PathRequestCandidate>.ParallelWriter SortableList;

            void Execute(Entity entity, in PFRequestMetadata metadata, in PFAgentState state)
            {
                if ((state.Flags & (byte)PFAgentStatus.Find) != 0)
                {
                    SortableList.AddNoResize(new PathRequestCandidate
                    {
                        Entity = entity,
                        Weight = metadata.Weight,
                        RequestTime = metadata.RequestTime,
                        Priority = metadata.Priority
                    });
                }
            }
        }

        [BurstCompile]
        private struct PrepareAndMarkJob : IJobParallelFor
        {
            public float CurrentTime;
            public int TargetJitterRange;
            public int FrameCount;
            
            [ReadOnly]
            public BlobAssetReference<GridBlob> GridBlob;

            [ReadOnly]
            public NativeList<PathRequestCandidate> SortedList;

            [WriteOnly]
            public NativeArray<Entity> ProcessingEntities;
            [WriteOnly]
            public NativeArray<PFRequestAgent> PathArray;

            [ReadOnly]
            public ComponentLookup<NavigationTargetGridData> NavigationTargetLookup;
            [ReadOnly]
            public ComponentLookup<LocalTransform> TransformLookup;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<PFRequestAgent> PathRequestLookup;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<PFAgentState> AgentStateLookup;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<PFRequestMetadata> MetaLookup;

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
                    int2 rawTarget = targetData.CurrentCell;
                    var random = Unity.Mathematics.Random.CreateFromIndex((uint)(entity.Index + (uint)FrameCount));
                    int jR = TargetJitterRange;
                    int2 offset = random.NextInt2(new int2(-jR, -jR), new int2(jR, jR));

                    var jittered = math.clamp(rawTarget + offset, 0, GridBlob.Value.Dimensions - 1);
                    var jitteredIdx = GridUtils.CoordToIndex(jittered, GridBlob.Value.Dimensions.x);

                    request.Destination = GridBlob.Value.CellsType[jitteredIdx] == CellType.Wall 
                        ? rawTarget  
                        : jittered;
                    
                    request.StartCoord = math.clamp(
                        GridUtils.WorldToCellCoord(pos, GridBlob.Value.Origin, GridBlob.Value.CellSize),
                        0,
                        GridBlob.Value.Dimensions - 1
                    );
                    request.Owner = entity;

                    var state = AgentStateLookup[entity];
                    state.Flags = (byte)PFAgentStatus.Processing;

                    var meta = MetaLookup[request.Owner];

                    float jitter = (entity.Index % 32) * 0.02f;
                    meta.NextAllowedUpdateTime = CurrentTime + 0.5f + jitter;
                    meta.LastProcessedVersion = NavigationTargetLookup[request.Focus].Version;
                    meta.RequestTime = CurrentTime + 0.5f + jitter;

                    PathRequestLookup[entity] = request;
                    AgentStateLookup[entity] = state;
                    MetaLookup[request.Owner] = meta;

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
        private struct FindPathAStarJob : IJobParallelFor
        {
            private static readonly ProfilerMarker Marker = new ProfilerMarker("AStar_SingleAgent");

            public int Offset;
            public int2 Dimensions;

            public int GridStride;

            public float GreedyCoef;
            public int IterationLimit;
            
            [ReadOnly]
            public BlobAssetReference<GridBlob> GridBlob;
            [NativeDisableParallelForRestriction]
            public BufferLookup<Waypoint> WaypointsLookup;

            [ReadOnly]
            public NativeArray<Entity> ProcessingEntities;
            [ReadOnly]
            public NativeArray<PFRequestAgent> PathList;
            [ReadOnly]
            public ComponentLookup<PFRequestAgent> ActualPathLookup;
            [ReadOnly]
            public ComponentLookup<NavigationTargetGridData> NavigationTargetLookup;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<PFAgentState> AgentStateLookup;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<PFRequestMetadata> MetaLookup;

            [NativeDisableParallelForRestriction]
            public NativeArray<NodeData> CostSoFar;
            [NativeDisableParallelForRestriction]
            public NativeArray<int2> CameFrom;
            [NativeDisableParallelForRestriction]
            public NativeBinaryMinHeap OpenSet;

            [ReadOnly]
            public NativeArray<int2> Neighbours;
            [ReadOnly]
            public NativeArray<float> DistMultipliers;
            [ReadOnly]
            public NativeArray<int> IndexOffsets;
            public uint UniqueSearchID;

            public void Execute(int index)
            {
                if (ProcessingEntities[index] == Entity.Null) return;

                int actualIndex = index + Offset;

                var costSoFarSlice = CostSoFar.Slice(actualIndex * GridStride, GridStride);
                var cameFromSlice = CameFrom.Slice(actualIndex * GridStride, GridStride);
                var openSetSlice = OpenSet.Slice(actualIndex * GridStride, GridStride);

                var request = PathList[index];

                openSetSlice.Clear();

                if (request.Owner == Entity.Null) return;

                Marker.Begin();

                if (ActualPathLookup.HasComponent(request.Owner))
                {
                    request.Destination = ActualPathLookup[request.Owner].Destination;
                }

                var waypoints = WaypointsLookup[request.Owner];
                waypoints.Clear();

                uint finalSearchID = UniqueSearchID + (uint)index;

                var box = BoxDataBuilder.Create()
                    .WithBuffers(costSoFarSlice, cameFromSlice, openSetSlice)
                    .WithGrid(GridBlob, Dimensions)
                    .WithPathPoints(request.StartCoord, request.Destination)
                    .WithWaypoints(waypoints)
                    .WithSettings(finalSearchID, GreedyCoef, IterationLimit)
                    .Build();

                if (AStarCrowd.FindPath(ref box, IndexOffsets, Neighbours, DistMultipliers, GreedyCoef))
                {
                    AStarCrowd.BuildPath(ref GridBlob.Value, ref box);
                    var state = AgentStateLookup[request.Owner];
                    state.Flags = (byte)(waypoints.Length > 0 ? PFAgentStatus.Process : PFAgentStatus.Idle);
                    AgentStateLookup[request.Owner] = state;
                    
                    if (NavigationTargetLookup.HasComponent(request.Focus))
                    {
                        var meta = MetaLookup[request.Owner];
                        meta.LastProcessedVersion = NavigationTargetLookup[request.Focus].Version;
                        MetaLookup[request.Owner] = meta;
                    }
                }
                else
                {
                    var state = AgentStateLookup[request.Owner];
                    state.Flags = (byte)PFAgentStatus.Idle;
                    AgentStateLookup[request.Owner] = state;
                }
                Marker.End();
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (_neighbours.IsCreated) _neighbours.Dispose();
            if (_distMultipliers.IsCreated) _distMultipliers.Dispose();
            if (_indexOffsets.IsCreated) _indexOffsets.Dispose();
            DisposePathfindingBuffers();
        }

        private void DisposePathfindingBuffers()
        {
            if (_costSoFar.IsCreated) _costSoFar.Dispose();
            if (_cameFrom.IsCreated) _cameFrom.Dispose();
            if (_openSet.IsCreated) _openSet.Dispose();
        }
    }
}
