using Core.Camera;
using Core.PathfindingAStar;
using Core.Spatial;
using Map.Grid;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace Features.OptRenderer
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [BurstCompile]
    public partial struct DensityCullingSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MainCameraData>();
            state.RequireForUpdate<CullingSettings>();
            state.RequireForUpdate<GridTag>();
            state.RequireForUpdate<GridBlobReference>();
            state.RequireForUpdate<GridSettings>();
            state.Enabled = false;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<NavigationSettings>(out var navSettings)) return;
            if (!SystemAPI.TryGetSingleton<CullingSettings>(out var cullingSettings)) return;
            if (!SystemAPI.TryGetSingleton<SpatialPartitioningData>(out var spatialData)) return;

            if (!spatialData.Initialized || spatialData.AgentCount == 0) return;
            var cameraData = SystemAPI.GetSingleton<MainCameraData>();

            if (!cullingSettings.EnableCulling)
            {
                state.Dependency = new ResetVisibilityJob().ScheduleParallel(state.Dependency);
                return;
            }
            
            var readyMorton = spatialData.IsBufferA ? spatialData.MortonA : spatialData.MortonB;
            var readyCellStarts = spatialData.IsBufferA ? spatialData.CellStartsA : spatialData.CellStartsB;
            var readyCellCounts = spatialData.IsBufferA ? spatialData.CellCountsA : spatialData.CellCountsB;

            var readyHandle = spatialData.IsBufferA ? spatialData.HandleA : spatialData.HandleB;

            var jobDeps = JobHandle.CombineDependencies(state.Dependency, readyHandle);

            var agentSpareCullingJob = new CombinedCullingJob
            {
                CameraPos = cameraData.Position,
                CellSize = navSettings.SpatialCellSize,
                SortedEntries = readyMorton,
                CellStarts = readyCellStarts,
                CellCounts = readyCellCounts,
                TotalAntsCount = spatialData.AgentCount,
                GlobalThreshold = cullingSettings.GlobalThreshold,
                MaxAntsPerCell = cullingSettings.MaxAntsPerCell,
                SafeDistanceSq = cullingSettings.SafeDistance * cullingSettings.SafeDistance,
                DeltaTime = state.WorldUnmanaged.Time.DeltaTime,
                FadeSpeed = cullingSettings.FadeSpeed,
            }.ScheduleParallel(jobDeps);

            state.Dependency = agentSpareCullingJob;

        }

        [BurstCompile]
        [WithAll(typeof(DensityCullingData), typeof(GpuVisibilityProperty))]
        // [WithPresent(typeof(MaterialMeshInfo))]
        public partial struct ResetVisibilityJob : IJobEntity
        {
            private void Execute(EnabledRefRW<MaterialMeshInfo> mmiEnabled, ref DensityCullingData cullingData, ref GpuVisibilityProperty shaderProp)
            {
                mmiEnabled.ValueRW = true;
                cullingData.Visibility = 1.0f;
                cullingData.ShouldBeVisible = true;
                shaderProp.Value = 1.0f;
            }
        }

        [BurstCompile]
        [WithAll(typeof(DensityCullingData), typeof(GpuVisibilityProperty))]
        // [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
        public partial struct CombinedCullingJob : IJobEntity
        {
            [ReadOnly]
            public NativeArray<MortonEntry> SortedEntries;
            [ReadOnly]
            public NativeArray<int> CellStarts;
            [ReadOnly]
            public NativeArray<int> CellCounts;

            public float3 CameraPos;
            public float CellSize;
            public int TotalAntsCount;
            public int GlobalThreshold;
            public int MaxAntsPerCell;
            public float SafeDistanceSq;
            public float DeltaTime;
            public float FadeSpeed;

            private void Execute(Entity entity, EnabledRefRW<MaterialMeshInfo> mmiEnabled, ref DensityCullingData cullingData, ref GpuVisibilityProperty shaderProp, in LocalTransform transform)
            {
                float distSq = math.distancesq(transform.Position, CameraPos);

                bool shouldBeVisible = true;
                
                uint code = MortonUtils.GetMorton2D(transform.Position, CellSize);
                int density = 0;

                if (code < CellStarts.Length)
                {
                    int startIdx = CellStarts[(int)code];
                    if (startIdx != -1)
                        density = CellCounts[(int)code];
                }
                
                if (distSq >= SafeDistanceSq && density > MaxAntsPerCell)
                {
                    float survivalChance = (float)MaxAntsPerCell / density;

                    uint hash = math.hash(new uint2((uint)entity.Index, code));
                    float entityHash = (hash & 0xFFFFFF) / (float)0xFFFFFF;

                    shouldBeVisible &= entityHash <= survivalChance;
                }
                
                if (TotalAntsCount > GlobalThreshold)
                {
                    float globalChance = (float)GlobalThreshold / TotalAntsCount;

                    uint hash = math.hash(new uint2((uint)entity.Index, 12345));
                    float entityHash = (hash & 0xFFFFFF) / (float)0xFFFFFF;

                    shouldBeVisible &= entityHash <= globalChance;
                }

                cullingData.Visibility = shouldBeVisible
                    ? math.min(1.0f, cullingData.Visibility + DeltaTime * FadeSpeed)
                    : math.max(0.0f, cullingData.Visibility - DeltaTime * FadeSpeed);
                shaderProp.Value = cullingData.Visibility;

                bool isActuallyVisible = cullingData.Visibility > 0.02f;
                if (mmiEnabled.ValueRW != isActuallyVisible)
                {
                    mmiEnabled.ValueRW = isActuallyVisible;
                }

                cullingData.ShouldBeVisible = shouldBeVisible;
            }
        }
    }
}
