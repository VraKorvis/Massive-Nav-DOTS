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
                if (SystemAPI.HasComponent<GridBlobReference>(entity))
                {
                    var oldRef = SystemAPI.GetComponent<GridBlobReference>(entity);
                    if (oldRef.Value.IsCreated) oldRef.Value.Dispose();
                    if (SystemAPI.HasComponent<GridBlobCleanup>(entity))
                        ecb.RemoveComponent<GridBlobCleanup>(entity);
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
                var bP = builder.Allocate(ref root.WallPushField, total);

                fixed (void* srcH = asset.Value.Heights) UnsafeUtility.MemCpy(bH.GetUnsafePtr(), srcH, total * sizeof(float));
                fixed (void* srcN = asset.Value.Normals) UnsafeUtility.MemCpy(bN.GetUnsafePtr(), srcN, total * sizeof(float3));
                fixed (void* srcC = asset.Value.CellsType) UnsafeUtility.MemCpy(bC.GetUnsafePtr(), srcC, total * sizeof(CellType));
                fixed (void* srcW = asset.Value.Weights) UnsafeUtility.MemCpy(bW.GetUnsafePtr(), srcW, total * sizeof(float));
                fixed (void* srcP = asset.Value.WallPush) UnsafeUtility.MemCpy(bP.GetUnsafePtr(), srcP, total * sizeof(float3));

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
    }
}
