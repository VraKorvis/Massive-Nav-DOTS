using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PFStar
{
    public struct GridBlob
    {
        public int2 Dimensions;
        public float3 Origin;
        public float CellSize;
        public BlobArray<CellType> CellsType;
        public BlobArray<float> Weights;
    }

    public struct GridBlobReference : IComponentData
    {
        public BlobAssetReference<GridBlob> Value;
    }
    
    public class UniversalGridAuthoring : MonoBehaviour
    {
        public int2 dimensions;
        public float cellSize = 1f;

        public class UniversalGridBaker : Baker<UniversalGridAuthoring>
        {
            public override void Bake(UniversalGridAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);

                float3 position = authoring.transform.position;
                float3 offset = new float3(
                    (authoring.dimensions.x - 1) * authoring.cellSize * 0.5f, 
                    0, 
                    (authoring.dimensions.y - 1) * authoring.cellSize * 0.5f
                );
                
                float3 cornerOrigin = position - offset + new float3(0, 1.0f, 0);
                
                using var builder = new BlobBuilder(Allocator.Temp);
                ref GridBlob root = ref builder.ConstructRoot<GridBlob>();  
                
                root.Dimensions = authoring.dimensions;
                root.Origin = cornerOrigin;
                root.CellSize = authoring.cellSize;
                
                int totalCells = authoring.dimensions.x * authoring.dimensions.y;
                var cells = builder.Allocate(ref root.CellsType, totalCells);
                var weights = builder.Allocate(ref root.Weights, totalCells);
                
                for (int i = 0; i < totalCells; i++)
                {
                    cells[i] = CellType.Ground;
                    weights[i] = 1.0f;
                }
                
                var walls = FindObjectsByType<WallAuthoring>(FindObjectsSortMode.None);
                foreach (var wall in walls)
                {
                    float3 wallPos = wall.transform.position;
                    float3 wallScale = wall.transform.localScale;

                    float3 minP = wallPos - (wallScale * 0.5f);
                    float3 maxP = wallPos + (wallScale * 0.5f);

                    int2 minCoord = GridUtils.WorldToCellCoord(minP, cornerOrigin);
                    int2 maxCoord = GridUtils.WorldToCellCoord(maxP, cornerOrigin);

                    int startX = math.max(0, minCoord.x);
                    int endX = math.min(authoring.dimensions.x - 1, maxCoord.x);
                    int startY = math.max(0, minCoord.y);
                    int endY = math.min(authoring.dimensions.y - 1, maxCoord.y);

                    for (int y = startY; y <= endY; y++)
                    {
                        for (int x = startX; x <= endX; x++)
                        {
                            int index = GridUtils.CoordToIndex(new int2(x, y), authoring.dimensions.x);
                            cells[index] = CellType.Wall;
                            weights[index] = float.PositiveInfinity;
                        }
                    }
                }
                               
                AddComponent(entity, new GridTag());
                AddComponent(entity, new GridSettings
                {
                    Dimensions = authoring.dimensions,
                    Origin = cornerOrigin,
                    CellSize = authoring.cellSize 
                });
                
                var blobRef = builder.CreateBlobAssetReference<GridBlob>(Allocator.Persistent);
                AddBlobAsset(ref blobRef, out var hash); 
                AddComponent(entity, new GridBlobReference { Value = blobRef });
                
            }
        }

        private void OnDrawGizmos()
        {
            if (dimensions.x <= 0 || dimensions.y <= 0) return;

            Gizmos.color = Color.cyan;

            Vector3 offset = new Vector3(
                (dimensions.x - 1) * cellSize * 0.5f, 
                0, 
                (dimensions.y - 1) * cellSize * 0.5f
            );
            Vector3 cornerOrigin = transform.position - offset;

            for (int y = 0; y < dimensions.y; y++)
            {
                for (int x = 0; x < dimensions.x; x++)
                {
                    Vector3 pos = cornerOrigin + new Vector3(x * cellSize, 0, y * cellSize);
                    Gizmos.DrawWireCube(pos, new Vector3(cellSize * 0.9f, 0.1f, cellSize * 0.9f));
                }
            }
        }
    }
}