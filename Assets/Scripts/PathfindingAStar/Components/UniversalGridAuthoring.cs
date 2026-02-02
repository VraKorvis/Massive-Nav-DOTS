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

                float3 cornerOrigin = position - offset + new float3(0, 0.3f, 0);

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
                    
                    var renderer = wall.GetComponent<Renderer>();
                    Bounds b = renderer != null ? renderer.bounds : new Bounds(wallPos, wallScale); 
    
                    float3 minP = b.min;
                    float3 maxP = b.max;

                    int2 minCoord = GridUtils.WorldToCellCoord(minP - new float3(authoring.cellSize), cornerOrigin);
                    int2 maxCoord = GridUtils.WorldToCellCoord(maxP + new float3(authoring.cellSize), cornerOrigin);
    
                    for (int y = math.max(0, minCoord.y); y <= math.min(authoring.dimensions.y - 1, maxCoord.y); y++)
                    {
                        for (int x = math.max(0, minCoord.x); x <= math.min(authoring.dimensions.x - 1, maxCoord.x); x++)
                        {
                            float3 cellWorldPos = cornerOrigin + new float3(x * authoring.cellSize, 0, y * authoring.cellSize);

                            float3 localPos = math.transform(worldToWallLocal, cellWorldPos);
                            
                            float margin = 0.59f + (authoring.cellSize * 0.5f / wallScale.x); 
                            float marginZ = 0.59f + (authoring.cellSize * 0.5f / wallScale.z);           

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
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
    
            var entityManager = world.EntityManager;
            var query = entityManager.CreateEntityQuery(typeof(GridBlobReference));
    
            if (!query.IsEmpty)
            {
                
                var gridRef = query.GetSingleton<GridBlobReference>();
                if (!gridRef.Value.IsCreated) return;

                ref var grid = ref gridRef.Value.Value;
        
                Color wallColor = new Color(1.0f, 0.0f, 0.0f, 0.4f); 
                Color groundColor = new Color(0.0f, 1.0f, 1.0f, 0.1f); 

                for (int i = 0; i < grid.CellsType.Length; i++)
                {
                    bool isWall = grid.CellsType[i] == CellType.Wall;
            
                    int2 coord = GridUtils.IndexToCoord(i, grid.Dimensions.x);
                    Vector3 pos = grid.Origin + new float3(coord.x * grid.CellSize, 0, coord.y * grid.CellSize);

                    if (isWall)
                    {
                        Gizmos.color = wallColor;
                        Gizmos.DrawCube(pos, new Vector3(grid.CellSize, 0.2f, grid.CellSize));
                
                        Gizmos.color = Color.red;
                        Gizmos.DrawWireCube(pos, new Vector3(grid.CellSize, 0.2f, grid.CellSize));
                    }
                    else
                    {
                        Gizmos.color = groundColor;
                        Gizmos.DrawWireCube(pos, new Vector3(grid.CellSize * 0.9f, 0.1f, grid.CellSize * 0.9f));
                    }
                }
            }
        }
        
    }
}