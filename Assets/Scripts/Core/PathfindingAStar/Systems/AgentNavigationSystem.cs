using System.Runtime.CompilerServices;
using Core.Gameplay;
using Core.Spatial;
using Map.Grid;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Transforms;
using UnityEngine;

namespace Core.PathfindingAStar
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PathRequestUpdateStatusSystem))]
    public partial struct AgentNavigationSystem : ISystem
    {

#if UNITY_EDITOR
        private static readonly ProfilerMarker k_ProfilePlayerPathLogic = new ProfilerMarker("[PF] Player.Pathfinding.CrowdMovementSystem");
#endif

        private ComponentTypeHandle<LocalTransform> _transformHandle;
        private ComponentTypeHandle<MoveSettings> _moveHandle;
        private ComponentTypeHandle<PFAgentState> _stateHandle;
        private BufferTypeHandle<Waypoint> _waypointHandle;

        private EntityQuery _agentQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridBlobReference>();
            state.RequireForUpdate<GridTag>();

            _agentQuery = SystemAPI.QueryBuilder()
                .WithAll<PFAgentState>()
                .WithAll<LocalTransform>()
                .WithAll<Waypoint>()
                .WithAll<MoveSettings>()
                .WithAll<MinionTag>()
                .Build();

            _transformHandle = state.GetComponentTypeHandle<LocalTransform>();
            _moveHandle = state.GetComponentTypeHandle<MoveSettings>();
            _stateHandle = state.GetComponentTypeHandle<PFAgentState>();
            _waypointHandle = state.GetBufferTypeHandle<Waypoint>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<NavigationSettings>(out var navSettings)) return;
            if (!SystemAPI.TryGetSingleton<SpatialPartitioningData>(out var spatialData)) return;

            if (spatialData.AgentCount == 0 || !spatialData.Initialized) return;

            _transformHandle.Update(ref state);
            _moveHandle.Update(ref state);
            _stateHandle.Update(ref state);
            _waypointHandle.Update(ref state);

            var gridBlob = SystemAPI.GetSingleton<GridBlobReference>().Value;

            var readyMortonSorted = spatialData.IsBufferA ? spatialData.MortonA : spatialData.MortonB;
            var readyCellStarts = spatialData.IsBufferA ? spatialData.CellStartsA : spatialData.CellStartsB;
            var readyHandle = spatialData.IsBufferA ? spatialData.HandleA : spatialData.HandleB;

            var pathMoveJobHandle = new PathMovePBDJob
            {
                TransformHandle = _transformHandle,
                MoveHandle = _moveHandle,
                StateHandle = _stateHandle,
                WaypointHandle = _waypointHandle,
                GridBlob = gridBlob,
                DeltaTime = state.WorldUnmanaged.Time.DeltaTime,
                SpatialCellSize = navSettings.SpatialCellSize,
                SeparationRadius = navSettings.SeparationRadius,
                SeparationWeight = navSettings.SeparationWeight,
                SortedEntries = readyMortonSorted,
                CellStarts = readyCellStarts,
                FramePhase = Time.frameCount % 2,
                AgentCount = spatialData.AgentCount,
            }.ScheduleParallel(_agentQuery, JobHandle.CombineDependencies(state.Dependency, readyHandle));
            state.Dependency = pathMoveJobHandle;
        }

        [BurstCompile(CompileSynchronously = true, OptimizeFor = OptimizeFor.Performance)]
        private struct PathMovePBDJob : IJobChunk
        {
            public ComponentTypeHandle<LocalTransform> TransformHandle;
            public ComponentTypeHandle<MoveSettings> MoveHandle;
            public ComponentTypeHandle<PFAgentState> StateHandle;
            public BufferTypeHandle<Waypoint> WaypointHandle;

            [ReadOnly]
            public NativeArray<MortonEntry> SortedEntries;

            [ReadOnly]
            public BlobAssetReference<GridBlob> GridBlob;

            public float DeltaTime;
            public float SpatialCellSize;
            public float SeparationRadius;
            public float SeparationWeight;
            public int FramePhase;
            public int AgentCount;

            [ReadOnly]
            public NativeArray<int> CellStarts;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var transforms = chunk.GetNativeArray(ref TransformHandle);
                var moves = chunk.GetNativeArray(ref MoveHandle);
                var agentStates = chunk.GetNativeArray(ref StateHandle);
                var waypoints = chunk.GetBufferAccessor(ref WaypointHandle);
                
                var ctx = new SeparationContext()
                {
                    SortedEntries = SortedEntries,
                    CellStarts = CellStarts,
                    SpatialCellSize = SpatialCellSize,
                    SeparationRadius = SeparationRadius,
                    SeparationWeight = SeparationWeight,
                    AgentCount = AgentCount,
                };
                
                for (int i = 0; i < chunk.Count; i++)
                {
                    int globalIndex = unfilteredChunkIndex * chunk.Capacity + i;
                    var transform = transforms[i];
                    var moveData = moves[i];
                    var agentState =  agentStates[i];
                    var way = waypoints[i];

                    ref var grid = ref GridBlob.Value;
                    float3 pos = transform.Position;
                    float baseRadius = math.max(0.05f, grid.CellSize * moveData.ArrivalRadiusFactor);
                    float arrivalRadius = (way.Length == 1) ? 0.05f : baseRadius;

                    if (!TrimReachedWaypoints(way, pos, arrivalRadius))
                    {
                        StopAgent(ref agentState, ref moveData);
                        moves[i] = moveData;
                        agentStates[i] =  agentState;
                        continue;
                    }

                    var groundHeight = GridUtils.GetHeightBilinear(ref grid, pos);
                    pos.y = groundHeight + moveData.PivotOffset;
                    transform.Position.y = pos.y;

                    float3 targetPos = way[^1].point;
                    targetPos.y = GridUtils.GetHeightBilinear(ref grid, targetPos);

                    // PBD + steering 
                    float3 pbdDisplacement = float3.zero;
                    
                    if (globalIndex % 2 == FramePhase)
                    {
                        pbdDisplacement = AgentMovement.ComputePbdDisplacement(ref grid, pos, ctx, DeltaTime);
                    }
                    float desiredSpeed = AgentMovement.ComputeDesiredSpeed(ref grid, in moveData, pos, moveData.Speed, targetPos, arrivalRadius);
                    float3 nextPos = AgentMovement.ComputeNextPos(ref moveData, pos, targetPos, pbdDisplacement, desiredSpeed, DeltaTime);
                    nextPos = AgentMovement.ComputeWallSliding(ref grid, ref moveData,pos, nextPos, DeltaTime);
                    transform.Position = AgentMovement.ComputeMovement(ref grid, ref moveData, pos, nextPos, DeltaTime);
                    transform.Rotation = AgentMovement.ComputeRotation(ref grid, ref moveData, ref transform, pos, targetPos, moveData.LookDir, DeltaTime);
                    
                    moveData.TargetCellPos = targetPos;
                    transforms[i] = transform;
                    moves[i] = moveData;
                    agentStates[i] =  agentState;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool TrimReachedWaypoints(DynamicBuffer<Waypoint> way, float3 pos, float arrivalRadius)
            {
                while (!way.IsEmpty)
                {
                    int last = way.Length - 1;
                    float3 p = way[last].point;
                    if (math.distance(pos.xz, p.xz) <= arrivalRadius)
                    {
                        way.RemoveAt(last);
                        continue;
                    }
                    return true;
                }
                return false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void StopAgent(ref PFAgentState state, ref MoveSettings move)
            {
                state.Flags = (byte)PFAgentStatus.Idle;
                move.Velocity = float3.zero;
            }
        }
    }
}
