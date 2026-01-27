using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace PFStar
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [BurstCompile]
    public partial struct GridBlobBootstrapSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var query = SystemAPI.QueryBuilder()
                .WithAll<GridBuffer, GridTag>()
                .WithNone<GridBlobReference>()
                .Build();

            if (query.IsEmpty) return;

            var gridEntity = SystemAPI.GetSingletonEntity<GridTag>();
            var settings = SystemAPI.GetComponent<GridSettings>(gridEntity);
            var buffer = SystemAPI.GetBuffer<GridBuffer>(gridEntity);

            using var builder = new BlobBuilder(Allocator.Temp);
            ref GridBlob root = ref builder.ConstructRoot<GridBlob>();
        
            root.Dimensions = settings.Dimensions;
            root.Origin = settings.Origin; 
            root.CellSize = settings.CellSize;
            int totalCells = settings.Dimensions.x * settings.Dimensions.y;
        
            var cells = builder.Allocate(ref root.CellsType, totalCells);
            var weights = builder.Allocate(ref root.Weights, totalCells);

            for (int i = 0; i < totalCells; i++)
            {
                cells[i] = buffer[i].Type;
                weights[i] = (buffer[i].Type == CellType.Wall) ? float.PositiveInfinity : 1.0f;
            }

            var blobRef = builder.CreateBlobAssetReference<GridBlob>(Allocator.Persistent);
        
            state.EntityManager.AddComponentData(gridEntity, new GridBlobReference { Value = blobRef });
        
            UnityEngine.Debug.Log($"[Bootstrap] BlobAsset Grid {settings.Dimensions} created successfully.");
        }
    }
}