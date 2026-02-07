using Map;
using PFStar;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Rendering;
using Unity.Transforms;

namespace OptRenderer
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [BurstCompile]
    public partial struct DensityCullingSystem : ISystem
    {
        private EntityQuery _agentsQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CullingSettings>();
            state.RequireForUpdate<GridTag>();
            state.RequireForUpdate<GridBlobReference>();
            state.RequireForUpdate<GridSettings>();

            _agentsQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<DensityCullingData, VisibilityProperty>()
                .Build(ref state);
        }

        public void OnUpdate(ref SystemState state)
        {
            var camera = UnityEngine.Camera.main;
            if (camera == null) return;

            if (!SystemAPI.TryGetSingleton<CullingSettings>(out var cullingSettings)) return;

            if (!cullingSettings.EnableCulling)
            {
                state.Dependency = new ResetVisibilityJob().ScheduleParallel(state.Dependency);
                return;
            }

            int totalAntsCount = _agentsQuery.CalculateEntityCount();

            if (totalAntsCount == 0) return;

            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            int gridSize = gridSettings.Dimensions.x * gridSettings.Dimensions.y;

            var gridCount = new NativeArray<int>(gridSize, Allocator.TempJob);

            var countJob = new CountAntsJob
            {
                GridSettings = gridSettings,
                GridCount = gridCount
            }.ScheduleParallel(state.Dependency);

            var agentSpareCullingJob = new CombinedCullingJob
            {
                CameraPos = camera.transform.position,
                GridSettings = gridSettings,
                GridCount = gridCount,
                TotalAntsCount = totalAntsCount,
                GlobalThreshold = cullingSettings.GlobalThreshold,
                MaxAntsPerCell = cullingSettings.MaxAntsPerCell,
                SafeDistanceSq = cullingSettings.SafeDistance * cullingSettings.SafeDistance,
                DeltaTime = SystemAPI.Time.DeltaTime,
                FadeSpeed = cullingSettings.FadeSpeed,
            }.ScheduleParallel(countJob);

            state.Dependency = agentSpareCullingJob;

            gridCount.Dispose(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(DensityCullingData))]
        public unsafe partial struct CountAntsJob : IJobEntity
        {
            public GridSettings GridSettings;

            [NativeDisableParallelForRestriction] public NativeArray<int> GridCount;

            void Execute(in LocalTransform transform)
            {
                int2 coord = GridUtils.WorldToCellCoord(transform.Position, GridSettings.Origin, GridSettings.CellSize);

                if (GridUtils.IsInBounds(coord, GridSettings.Dimensions))

                {
                    int index = GridUtils.CoordToIndex(coord, GridSettings.Dimensions.x);

                    System.Threading.Interlocked.Increment(ref ((int*)GridCount.GetUnsafePtr())[index]);
                }
            }
        }

        [BurstCompile]
        [WithAll(typeof(DensityCullingData))]
        [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
        // [WithPresent(typeof(MaterialMeshInfo))]
        public partial struct ResetVisibilityJob : IJobEntity
        {
            private void Execute(EnabledRefRW<MaterialMeshInfo> mmiEnabled)
            {
                mmiEnabled.ValueRW = true;
            }
        }

        [BurstCompile]
        [WithAll(typeof(DensityCullingData), typeof(VisibilityProperty))]
        [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
        public partial struct CombinedCullingJob : IJobEntity
        {
            public float3 CameraPos;
            public GridSettings GridSettings;
            [ReadOnly] public NativeArray<int> GridCount;
            public int TotalAntsCount;
            public int GlobalThreshold;
            public int MaxAntsPerCell;
            public float SafeDistanceSq;
            public float DeltaTime;
            public float FadeSpeed; 
            
            private void Execute(Entity entity, EnabledRefRW<MaterialMeshInfo> mmiEnabled, ref DensityCullingData cullingData, ref VisibilityProperty shaderProp, in LocalTransform transform)
            {
                float distSq = math.distancesq(transform.Position, CameraPos);

                bool shouldBeVisible;

                if (distSq < SafeDistanceSq)
                {
                    shouldBeVisible = true;
                }
                else if (TotalAntsCount < GlobalThreshold)
                {
                    shouldBeVisible = true;
                }
                else
                {
                    int2 coord = GridUtils.WorldToCellCoord(transform.Position, GridSettings.Origin, GridSettings.CellSize);
                    int density = 0;
                    if (GridUtils.IsInBounds(coord, GridSettings.Dimensions))
                    {
                        density = GridCount[GridUtils.CoordToIndex(coord, GridSettings.Dimensions.x)];
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