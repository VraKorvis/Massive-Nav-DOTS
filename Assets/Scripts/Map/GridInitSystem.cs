using PFStar;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Map
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default)]
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    public partial struct GridInitSystem : ISystem
    {
        private EntityQuery _gridQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridInitTag>();
            _gridQuery = state.GetEntityQuery(ComponentType.ReadOnly<GridBlobReference>());
        }

        private BlobAssetReference<GridBlob> _blobAsset;
        private bool _isInitialized;

        public unsafe void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (initTag, entity) in SystemAPI.Query<RefRO<GridInitTag>>().WithEntityAccess())
            {

                if (!_gridQuery.IsEmptyIgnoreFilter)
                {
                    var existingRef = _gridQuery.GetSingleton<GridBlobReference>();
                    if (existingRef.Value.IsCreated)
                    {
                        existingRef.Value.Dispose();
                    }

                    ecb.RemoveComponent<GridBlobReference>(_gridQuery.GetSingletonEntity());
                }

                var asset = initTag.ValueRO.GridDataAsset;
                int total = asset.Value.Heights.Length;

                using var builder = new BlobBuilder(Allocator.Temp);
                ref GridBlob root = ref builder.ConstructRoot<GridBlob>();

                root.Dimensions = asset.Value.Dimensions;
                root.CellSize = asset.Value.CellSize;
                root.Origin = asset.Value.Origin;

                var bH = builder.Allocate(ref root.Heights, total);
                var bN = builder.Allocate(ref root.Normals, total);
                var bC = builder.Allocate(ref root.CellsType, total);
                var bW = builder.Allocate(ref root.Weights, total);

                fixed (float* src = asset.Value.Heights) UnsafeUtility.MemCpy(bH.GetUnsafePtr(), src, total * sizeof(float));
                fixed (float3* src = asset.Value.Normals) UnsafeUtility.MemCpy(bN.GetUnsafePtr(), src, total * sizeof(float3));
                fixed (CellType* src = asset.Value.CellsType) UnsafeUtility.MemCpy(bC.GetUnsafePtr(), src, total * sizeof(CellType));
                fixed (float* src = asset.Value.Weights) UnsafeUtility.MemCpy(bW.GetUnsafePtr(), src, total * sizeof(float));

                var newBlobAsset = builder.CreateBlobAssetReference<GridBlob>(Allocator.Persistent);

                ecb.AddComponent(entity, new GridTag());
                ecb.AddComponent(entity, new GridBlobReference
                {
                    Value = newBlobAsset
                });

                ecb.RemoveComponent<GridInitTag>(entity);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        public void OnDestroy(ref SystemState state)
        {
            var query = state.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<GridBlobReference>());
            if (!query.IsEmptyIgnoreFilter)
            {
                var gridRef = query.GetSingleton<GridBlobReference>();
                if (gridRef.Value.IsCreated)
                {
                    gridRef.Value.Dispose();
                }
            }
        }
    }
}
