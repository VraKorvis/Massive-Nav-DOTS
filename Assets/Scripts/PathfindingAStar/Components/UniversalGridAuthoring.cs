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

                int width = authoring.dimensions.x;
                int height = authoring.dimensions.y;
                
                int totalCells = width * height;
                var cells = builder.Allocate(ref root.CellsType, totalCells);
                var weights = builder.Allocate(ref root.Weights, totalCells);

                
                for (int i = 0; i < totalCells; i++)
                {
                    int x = i % width;
                    int y = i / width;

                    if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                    {
                        cells[i] = CellType.Wall;
                        weights[i] = float.PositiveInfinity;
                    }
                    else
                    {
                        cells[i] = CellType.Ground;
                        weights[i] = 1.0f;
                    }
                }

                var walls = FindObjectsByType<WallAuthoring>(FindObjectsSortMode.None);
                foreach (var wall in walls)
                {
                    float3 wallScale = wall.transform.localScale;
                    float4x4 worldToWallLocal = math.inverse(wall.transform.localToWorldMatrix);

                    var renderer = wall.GetComponent<Renderer>();
                    Bounds b = renderer != null ? renderer.bounds : new Bounds(wall.transform.position, wallScale);

                    int2 minCoord = GridUtils.WorldToCellCoord(b.min, cornerOrigin);
                    int2 maxCoord = GridUtils.WorldToCellCoord(b.max, cornerOrigin);

                    for (int y = math.max(0, minCoord.y); y <= math.min(authoring.dimensions.y - 1, maxCoord.y); y++)
                    {
                        for (int x = math.max(0, minCoord.x);
                             x <= math.min(authoring.dimensions.x - 1, maxCoord.x);
                             x++)
                        {
                            float3 cellCenter = cornerOrigin +
                                                new float3(x * authoring.cellSize, 0, y * authoring.cellSize);
                            float h = authoring.cellSize * 0.5f;

                            bool isWall =
                                CheckPoint(cellCenter, worldToWallLocal) ||
                                CheckPoint(cellCenter + new float3(h, 0, h), worldToWallLocal) ||
                                CheckPoint(cellCenter + new float3(-h, 0, h), worldToWallLocal) ||
                                CheckPoint(cellCenter + new float3(h, 0, -h), worldToWallLocal) ||
                                CheckPoint(cellCenter + new float3(-h, 0, -h), worldToWallLocal) ||
                                CheckPoint(cellCenter + new float3(h, 0, 0), worldToWallLocal) ||
                                CheckPoint(cellCenter + new float3(-h, 0, 0), worldToWallLocal) ||
                                CheckPoint(cellCenter + new float3(0, 0, h), worldToWallLocal) ||
                                CheckPoint(cellCenter + new float3(0, 0, -h), worldToWallLocal);

                            if (isWall)
                            {
                                int index = GridUtils.CoordToIndex(new int2(x, y), authoring.dimensions.x);
                                cells[index] = CellType.Wall;
                                weights[index] = float.PositiveInfinity;

                            }
                        }
                    }
                }

                bool CheckPoint(float3 worldPoint, float4x4 worldToWallLocal)
                {
                    float3 localPos = math.transform(worldToWallLocal, worldPoint);
                    return math.abs(localPos.x) <= 0.505f && math.abs(localPos.z) <= 0.505f;
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
                Color groundColor = new Color(0.0f, 1.0f, 1.0f, 0.4f);

                for (int i = 0; i < grid.CellsType.Length; i++)
                {
                    bool isWall = grid.CellsType[i] == CellType.Wall;

                    int2 coord = GridUtils.IndexToCoord(i, grid.Dimensions.x);
                    Vector3 pos = grid.Origin + new float3(coord.x * grid.CellSize, 0, coord.y * grid.CellSize);

                    if (isWall)
                    {
                        Gizmos.color = wallColor;
                        Gizmos.DrawCube(pos, new Vector3(grid.CellSize, 0.25f, grid.CellSize));

                        Gizmos.color = Color.red;
                        Gizmos.DrawWireCube(pos, new Vector3(grid.CellSize, 0.25f, grid.CellSize));
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