using Map;
using PFStar;
using PFStar.Morton;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Rendering;
using Unity.Transforms;

namespace OptRenderer
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [BurstCompile]
    public partial struct DensityCullingSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CullingSettings>();
            state.RequireForUpdate<GridTag>();
            state.RequireForUpdate<GridBlobReference>();
            state.RequireForUpdate<GridSettings>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var camera = UnityEngine.Camera.main;
            if (camera == null) return;

            if (!SystemAPI.TryGetSingleton<NavigationSettings>(out var navSettings)) return;
            if (!SystemAPI.TryGetSingleton<CullingSettings>(out var cullingSettings)) return;
            if (!SystemAPI.TryGetSingleton<SpatialPartitioningData>(out var spatialData)) return;

            if (!spatialData.Initialized || spatialData.AgentCount == 0) return;

            if (!cullingSettings.EnableCulling)
            {
                state.Dependency = new ResetVisibilityJob().ScheduleParallel(state.Dependency);
                return;
            }
            
            var readyMorton = spatialData.IsBufferA ? spatialData.MortonA : spatialData.MortonB;
            var readyCellStarts = spatialData.IsBufferA ? spatialData.CellStartsA : spatialData.CellStartsB;
            var readyHandle = spatialData.IsBufferA ? spatialData.HandleA : spatialData.HandleB;
            
            var jobDeps = JobHandle.CombineDependencies(state.Dependency, readyHandle);

            var agentSpareCullingJob = new CombinedCullingJob
            {
                CameraPos = camera.transform.position,
                CellSize = navSettings.SpatialCellSize,
                SortedEntries = readyMorton,
                CellStarts = readyCellStarts,
                TotalAntsCount = spatialData.AgentCount,
                GlobalThreshold = cullingSettings.GlobalThreshold,
                MaxAntsPerCell = cullingSettings.MaxAntsPerCell,
                SafeDistanceSq = cullingSettings.SafeDistance * cullingSettings.SafeDistance,
                DeltaTime = SystemAPI.Time.DeltaTime,
                FadeSpeed = cullingSettings.FadeSpeed,
            }.ScheduleParallel(jobDeps);

            state.Dependency = agentSpareCullingJob;

        }

        [BurstCompile]
        [WithAll(typeof(DensityCullingData))]
        [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
        // [WithPresent(typeof(MaterialMeshInfo))]
        public partial struct ResetVisibilityJob : IJobEntity
        {
            private void Execute(EnabledRefRW<MaterialMeshInfo> mmiEnabled, ref DensityCullingData cullingData, ref VisibilityProperty shaderProp)            {
                mmiEnabled.ValueRW = true;
                cullingData.Visibility = 1.0f;
                shaderProp.Value = 1.0f;            }
        }

        [BurstCompile]
        [WithAll(typeof(DensityCullingData), typeof(VisibilityProperty))]
        [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
        public partial struct CombinedCullingJob : IJobEntity
        {
            [ReadOnly]
            public NativeArray<MortonEntry> SortedEntries;
            [ReadOnly]
            public NativeArray<int> CellStarts;

            public float3 CameraPos;
            public float CellSize;
            public int TotalAntsCount;
            public int GlobalThreshold;
            public int MaxAntsPerCell;
            public float SafeDistanceSq;
            public float DeltaTime;
            public float FadeSpeed;

            private void Execute(Entity entity, EnabledRefRW<MaterialMeshInfo> mmiEnabled, ref DensityCullingData cullingData, ref VisibilityProperty shaderProp, in LocalTransform transform)
            {
                float distSq = math.distancesq(transform.Position, CameraPos);
                bool shouldBeVisible = true;

                if (distSq >= SafeDistanceSq && TotalAntsCount >= GlobalThreshold)
                {
                    uint code = MortonUtils.GetMorton2D(transform.Position, CellSize);
                    int density = 0;

                    if (code < CellStarts.Length)
                    {
                        int startIdx = CellStarts[(int)code];
                        if (startIdx != -1)
                        {
                            int endIdx = startIdx + 1;
                            while (endIdx < TotalAntsCount && SortedEntries[endIdx].Key == code)
                            {
                                endIdx++;

                                if (endIdx - startIdx > MaxAntsPerCell * 2) break;
                            }
                            density = endIdx - startIdx;
                        }
                    }

                    float survivalChance = math.saturate((float)MaxAntsPerCell / math.max(density, 1));
                    float entityHash = (float)((entity.Index * 0.61803398875f) % 1.0);
                    shouldBeVisible = entityHash <= survivalChance;
                }

                cullingData.Visibility = shouldBeVisible
                    ? math.min(1.0f, cullingData.Visibility + DeltaTime * FadeSpeed)
                    : math.max(0.0f, cullingData.Visibility - DeltaTime * FadeSpeed);
                shaderProp.Value = cullingData.Visibility;

                mmiEnabled.ValueRW = cullingData.Visibility > 0.0f;
            }
        }
    }
}