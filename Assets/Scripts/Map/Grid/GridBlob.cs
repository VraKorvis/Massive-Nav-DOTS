using Core.PathfindingAStar;
using Unity.Entities;
using Unity.Mathematics;

namespace Map.Grid
{
    public struct GridBlob
    {
        public int2 Dimensions;
        public float3 Origin;
        public float CellSize;

        public BlobArray<CellType> CellsType;
        public BlobArray<float> Weights;

        public BlobArray<float> Heights;
        public BlobArray<float3> Normals;
        public BlobArray<float3> WallPushField;
    }

    public struct GridBlobReference : IComponentData
    {
        public BlobAssetReference<GridBlob> Value;
    }
}
