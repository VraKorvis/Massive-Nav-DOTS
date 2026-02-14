using Unity.Entities;
using Unity.Mathematics;

namespace Core.PathfindingAStar
{
    public struct GridSettings : IComponentData {
        public int2 Dimensions;
        public float CellSize;
        public float3 Origin; 
    }

    public enum CellType { Walkable, Wall, Ground }

    public struct GridBuffer : IBufferElementData {
        public float3 WorldPos;
        public CellType Type;
    }

    public struct GridTag : IComponentData { }
    
    
}