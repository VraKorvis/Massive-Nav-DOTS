using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PFStar
{
    public class GridAuthoring : MonoBehaviour {
        public int2 dimensions = new int2(100, 100);
        public float cellSize = 1f;
        public GameObject wallPrefab; 

        public class Baker : Baker<GridAuthoring> {
            public override void Bake(GridAuthoring authoring) {
                var entity = GetEntity(TransformUsageFlags.Renderable);
                AddComponent(entity, new GridTag());
                AddComponent(entity, new GridSettings {
                    Dimensions = authoring.dimensions,
                    CellSize = authoring.cellSize,
                    Origin = authoring.transform.position
                });

                var buffer = AddBuffer<GridBuffer>(entity);
                int totalCells = authoring.dimensions.x * authoring.dimensions.y;
            
                for (int i = 0; i < totalCells; i++) {
                    buffer.Add(new GridBuffer { Type = CellType.Ground });
                }
            }
        }
    }
}