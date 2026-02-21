using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using System.Threading;
using Core.Camera;
using Core.PathfindingAStar;
using Map.Grid;

namespace Features.OptRenderer
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [BurstCompile]
    public partial struct MassiveMobCullingSystem : ISystem
    {
        private EntityQuery _crowdQuery;
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridBlobReference>();
            state.RequireForUpdate<CullingSettings>();
            state.RequireForUpdate<NavigationSettings>();
            state.RequireForUpdate<MainCameraData>();

            _crowdQuery = SystemAPI.QueryBuilder()
                .WithAll<DensityCullingData, GpuVisibilityProperty, MaterialMeshInfo, LocalTransform>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build();
            state.Enabled = true;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<CullingSettings>(out var cullingSettings)) return;

            var nav = SystemAPI.GetSingleton<NavigationSettings>();
            var cameraData = SystemAPI.GetSingleton<MainCameraData>();
            ref var gridBlob = ref SystemAPI.GetSingleton<GridBlobReference>().Value.Value;

            float3 origin = gridBlob.Origin;
            int2 dims = gridBlob.Dimensions;
            float worldSizeX = dims.x * gridBlob.CellSize;
            float worldSizeZ = dims.y * gridBlob.CellSize;

            int cellCountX = math.max(1, (int)math.ceil(worldSizeX / nav.SpatialCellSize));
            int cellCountZ = math.max(1, (int)math.ceil(worldSizeZ / nav.SpatialCellSize));
            int totalCells = cellCountX * cellCountZ;

            if (!cullingSettings.EnableCulling)
            {
                state.Dependency = new ResetVisibilityJob().ScheduleParallel(state.Dependency);
                return;
            }

            var cellCounter = new NativeArray<int>(totalCells, Allocator.TempJob);

            var entityCount = _crowdQuery.CalculateEntityCount();
            if (entityCount == 0) return;
            var results = new NativeArray<float>(entityCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var cullJobHandle = new MassiveMobCullingJob
            {
                CameraPos = cameraData.Position,
                DeltaTime = state.WorldUnmanaged.Time.DeltaTime,
                FadeSpeed = cullingSettings.FadeSpeed,
                MaxPerCell = cullingSettings.MaxAntsPerCell,
                SafeDistanceSq = cullingSettings.SafeDistance * cullingSettings.SafeDistance,
                CellSize = nav.SpatialCellSize,
                Origin = origin,
                CellCountX = cellCountX,
                CellCountZ = cellCountZ,
                CellCounter = cellCounter,
                CullingResults = results,
            }.ScheduleParallel(_crowdQuery, state.Dependency);

            state.Dependency = new ApplyVisibilityBatchJob
            {
                CullingResults = results,
            }.ScheduleParallel(_crowdQuery, cullJobHandle);

            cellCounter.Dispose(state.Dependency);
            state.Dependency = results.Dispose(state.Dependency);
        }

        [BurstCompile]
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
        [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
        public partial struct MassiveMobCullingJob : IJobEntity
        {
            [ReadOnly]
            public float3 CameraPos;
            public float DeltaTime;
            public float FadeSpeed;
            public int MaxPerCell;
            public float SafeDistanceSq;
            public float CellSize;
            public float3 Origin;
            public int CellCountX;
            public int CellCountZ;

            [NativeDisableUnsafePtrRestriction]
            public NativeArray<int> CellCounter;

            [WriteOnly]
            [NativeDisableUnsafePtrRestriction]
            public NativeArray<float> CullingResults;

            private void Execute([EntityIndexInQuery] int entityInQueryIndex,
                ref DensityCullingData cullingData,
                ref GpuVisibilityProperty shaderProp,
                in LocalTransform transform)
            {
                bool shouldBeVisible = true;
                
                float3 pos = transform.Position;
                float distSq = math.distancesq(pos, CameraPos);

                if (distSq >= SafeDistanceSq)
                {
                    int cx = (int)((pos.x - Origin.x) / CellSize);
                    int cz = (int)((pos.z - Origin.z) / CellSize);

                    if (cx >= 0 && cx < CellCountX && cz >= 0 && cz < CellCountZ)
                    {
                        int cellIdx = cz * CellCountX + cx;
                        unsafe
                        {
                            int* ptr = (int*)CellCounter.GetUnsafePtr();
                            int myRank = Interlocked.Increment(ref ptr[cellIdx]) - 1;
                            if (myRank >= MaxPerCell) shouldBeVisible = false;
                        }
                    }
                }
                
                float targetVis = shouldBeVisible ? 1f : 0f;
                cullingData.Visibility = math.lerp(cullingData.Visibility, targetVis, math.saturate(DeltaTime * FadeSpeed));

                CullingResults[entityInQueryIndex] = cullingData.Visibility;
                cullingData.ShouldBeVisible = shouldBeVisible;
            }
        }

        [BurstCompile]
        [WithAll(typeof(DensityCullingData), typeof(GpuVisibilityProperty), typeof(MaterialMeshInfo))]
        [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
        public partial struct ApplyVisibilityBatchJob : IJobEntity
        {
            [ReadOnly]
            public NativeArray<float> CullingResults;

            private void Execute([EntityIndexInQuery] int entityInQueryIndex,
                EnabledRefRW<MaterialMeshInfo> mmiEnabled,
                ref GpuVisibilityProperty shaderProp)
            {
                float vis = CullingResults[entityInQueryIndex];
                bool targetEnabled = vis > 0.2f; 
                if (mmiEnabled.ValueRW != targetEnabled) 
                {
                    mmiEnabled.ValueRW = targetEnabled;
                }
                shaderProp.Value = vis;
            }
        }
    }
}
