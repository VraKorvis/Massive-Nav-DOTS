using Core.PathfindingAStar;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Map.Grid
{
    public struct GridBlobCleanup : ICleanupComponentData
    {
        public BlobAssetReference<GridBlob> Value;
    }
    
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(GridInitSystem))]
    public partial struct GridBlobLifecycleSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            
            foreach (var (cleanup, entity) in SystemAPI.Query<RefRW<GridBlobCleanup>>()
                         .WithNone<GridTag>() 
                         .WithEntityAccess())
            {
                if (cleanup.ValueRW.Value.IsCreated)
                {
                    cleanup.ValueRW.Value.Dispose();
                }
                ecb.RemoveComponent<GridBlobCleanup>(entity);
            }

            foreach (var (blobRef, entity) in SystemAPI.Query<RefRO<GridBlobReference>>()
                         .WithAll<GridTag>()
                         .WithNone<GridBlobCleanup>()
                         .WithEntityAccess())
            {
                ecb.AddComponent(entity, new GridBlobCleanup { Value = blobRef.ValueRO.Value });
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            foreach (var cleanup in SystemAPI.Query<RefRW<GridBlobCleanup>>())
            {
                if (cleanup.ValueRO.Value.IsCreated)
                {
                    cleanup.ValueRW.Value.Dispose();
                }
            }
        }
    }
}
