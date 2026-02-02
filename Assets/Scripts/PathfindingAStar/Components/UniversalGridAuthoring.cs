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
        public int inflationRadius = 1;

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

                    float4x4 worldToWallLocal = math.inverse(wall.transform.localToWorldMatrix);

                    float3 minP = wallPos - (wallScale * 0.5f);
                    float3 maxP = wallPos + (wallScale * 0.5f);

                    int2 minCoord = GridUtils.WorldToCellCoord(minP - new float3(authoring.cellSize), cornerOrigin);
                    int2 maxCoord = GridUtils.WorldToCellCoord(maxP + new float3(authoring.cellSize), cornerOrigin);
    
                    for (int y = math.max(0, minCoord.y); y <= math.min(authoring.dimensions.y - 1, maxCoord.y); y++)
                    {
                        for (int x = math.max(0, minCoord.x); x <= math.min(authoring.dimensions.x - 1, maxCoord.x); x++)
                        {
                            float3 cellWorldPos = cornerOrigin + new float3(x * authoring.cellSize, 0, y * authoring.cellSize);

                            float3 localPos = math.transform(worldToWallLocal, cellWorldPos);
                            
                            float margin = 0.5f + (authoring.cellSize * 0.5f / wallScale.x); 
                            float marginZ = 0.5f + (authoring.cellSize * 0.5f / wallScale.z);            

                            if (math.abs(localPos.x) <= margin && math.abs(localPos.z) <= marginZ) 
                            {
                                int index = GridUtils.CoordToIndex(new int2(x, y), authoring.dimensions.x);
                                cells[index] = CellType.Wall;
                            }
                        }
                    }
                }

                for (int i = 0; i < totalCells; i++)
                {
                    if (cells[i] == CellType.Wall)
                    {
                        int2 wallCoord = GridUtils.IndexToCoord(i, authoring.dimensions.x);
                        for (int dy = -authoring.inflationRadius; dy <= authoring.inflationRadius; dy++)
                        {
                            for (int dx = -authoring.inflationRadius; dx <= authoring.inflationRadius; dx++)
                            {
                                int2 neighborCoord = wallCoord + new int2(dx, dy);

                                if (neighborCoord.x >= 0 && neighborCoord.x < authoring.dimensions.x &&
                                    neighborCoord.y >= 0 && neighborCoord.y < authoring.dimensions.y)
                                {
                                    int neighborIndex = GridUtils.CoordToIndex(neighborCoord, authoring.dimensions.x);

                                    if (cells[neighborIndex] != CellType.Wall)
                                    {
                                        weights[neighborIndex] = math.max(weights[neighborIndex], 5.0f);
                                    }
                                }
                            }
                        }
                    }
                }

                AddComponent(entity, new GridTag());
                AddComponent(entity,
                    new GridSettings
                        { Dimensions = authoring.dimensions, Origin = cornerOrigin, CellSize = authoring.cellSize });

                var blobRef = builder.CreateBlobAssetReference<GridBlob>(Allocator.Persistent);
                AddBlobAsset(ref blobRef, out var hash);
                AddComponent(entity, new GridBlobReference { Value = blobRef });
            }
        }

        private void OnDrawGizmos()
        {
            if (dimensions.x <= 0 || dimensions.y <= 0) return;

            Color wallColor = new Color(1.0f, 0.0f, 0.0f, 0.2f); 
            Color groundColor = new Color(0.0f, 1.0f, 1.0f, 0.1f); 
            
            Vector3 offset = new Vector3(
                (dimensions.x - 1) * cellSize * 0.5f,
                0,
                (dimensions.y - 1) * cellSize * 0.5f
            );
            Vector3 cornerOrigin = transform.position - offset + new Vector3(0, 0.3f, 0);

            var walls = FindObjectsByType<WallAuthoring>(FindObjectsSortMode.None);

            for (int y = 0; y < dimensions.y; y++)
            {
                for (int x = 0; x < dimensions.x; x++)
                {
                    Vector3 pos = cornerOrigin + new Vector3(x * cellSize, 0, y * cellSize);
                    bool isWall = false;

                    foreach (var wall in walls)
                    {
                        Matrix4x4 worldToWallLocal = wall.transform.worldToLocalMatrix;
                        Vector3 localPos = worldToWallLocal.MultiplyPoint3x4(pos);

                        Vector3 wallScale = wall.transform.localScale;
                        
                        float marginX = 0.5f + (cellSize * 0.5f / wallScale.x);
                        float marginZ = 0.5f + (cellSize * 0.5f / wallScale.z);
                        
                        if (Mathf.Abs(localPos.x) <= marginX && Mathf.Abs(localPos.z) <= marginZ)
                        {
                            isWall = true;
                            break;
                        }
                    }

                    Gizmos.color = isWall ? wallColor : groundColor;
            
                    if (isWall)
                    {
                        Gizmos.DrawCube(pos, new Vector3(cellSize, 0.2f, cellSize));
                    }
                    else
                    {
                        Gizmos.DrawWireCube(pos, new Vector3(cellSize * 0.9f, 0.1f, cellSize * 0.9f));
                    }
                }
            }
        }
    }
}