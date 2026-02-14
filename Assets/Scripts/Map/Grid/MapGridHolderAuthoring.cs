using Core.PathfindingAStar;
using Map.Generation;
using Unity.Entities;
using UnityEngine;

namespace Map.Grid
{
    public struct GridInitTag : IComponentData
    {
        public UnityObjectRef<GridDataAsset> GridDataAsset;
    }
    
    public class MapGridHolderAuthoring : MonoBehaviour
    {
        public GridDataAsset dataAsset;

        public class MapGridBaker : Baker<MapGridHolderAuthoring>
        {
            public override void Bake(MapGridHolderAuthoring authoring)
            {
                if (authoring.dataAsset == null || !authoring.dataAsset.hasData) return;

                var entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new GridSettings
                {
                    Dimensions = authoring.dataAsset.Dimensions,
                    Origin = authoring.dataAsset.Origin,
                    CellSize = authoring.dataAsset.CellSize
                });

                AddComponent(entity, new GridInitTag { GridDataAsset = authoring.dataAsset });
            }
        }
    }
}