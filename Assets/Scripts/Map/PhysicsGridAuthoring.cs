using PFStar;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Map
{
    public class PhysicsGridAuthoring : MonoBehaviour
    {
        [Header("Grid Settings")] public int2 dimensions = new(199, 199);
        public float cellSize = 1f;

        [Header("Obstacle Settings")] public LayerMask obstacleLayer;
        public float inflationRadius = 1.0f;

        public class PhysicsGridBaker : Baker<PhysicsGridAuthoring>
        {
            public override void Bake(PhysicsGridAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);

                float3 position = authoring.transform.position;
                float3 offset = new float3(
                    (authoring.dimensions.x - 1) * authoring.cellSize * 0.5f,
                    0,
                    (authoring.dimensions.y - 1) * authoring.cellSize * 0.5f
                );
                float3 cornerOrigin = position - offset;

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
                    int2 coord = new int2(i % width, i / width);

                    float3 cellCenter = cornerOrigin +
                                        new float3(coord.x * authoring.cellSize, 0.5f, coord.y * authoring.cellSize);

                    float3 halfExtents = new float3(authoring.cellSize * 0.48f, 1.0f, authoring.cellSize * 0.48f);

                    bool isObstacle = Physics.CheckBox(
                        cellCenter,
                        halfExtents,
                        Quaternion.identity,
                        authoring.obstacleLayer
                    );

                    bool isEdge = (coord.x == 0 || coord.y == 0 || coord.x == width - 1 || coord.y == height - 1);

                    if (isObstacle || isEdge)
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

                if (authoring.inflationRadius > 0)
                {
                    var tempCells = new NativeArray<CellType>(totalCells, Allocator.Temp);
                    for (int i = 0; i < totalCells; i++) tempCells[i] = cells[i];

                    for (int i = 0; i < totalCells; i++)
                    {
                        if (tempCells[i] == CellType.Wall)
                        {
                            int2 wallCoord = new int2(i % width, i / width);
                            int range = (int)math.ceil(authoring.inflationRadius);

                            for (int dy = -range; dy <= range; dy++)
                            {
                                for (int dx = -range; dx <= range; dx++)
                                {
                                    int2 neighbor = wallCoord + new int2(dx, dy);
                                    if (neighbor.x >= 0 && neighbor.x < width && neighbor.y >= 0 && neighbor.y < height)
                                    {
                                        int nIndex = neighbor.y * width + neighbor.x;
                                        if (cells[nIndex] != CellType.Wall)
                                        {
                                            weights[nIndex] = math.max(weights[nIndex], 10.0f);
                                        }
                                    }
                                }
                            }
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
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            var entityManager = world.EntityManager;
            var query = entityManager.CreateEntityQuery(typeof(GridBlobReference));

            if (!query.IsEmpty)
            {
                var gridRef = query.GetSingleton<GridBlobReference>();
                if (!gridRef.Value.IsCreated) return;

                ref var grid = ref gridRef.Value.Value;

                Color wallColor = new Color(1.0f, 0.0f, 0.0f, 0.75f);
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