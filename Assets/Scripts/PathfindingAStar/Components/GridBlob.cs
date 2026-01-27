using Unity.Entities;
using Unity.Mathematics;

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
}