using PFStar;
using Unity.Mathematics;
using UnityEngine;

namespace Map
{
    [CreateAssetMenu(fileName = "GridData", menuName = "Map/GridData")]
    public class GridDataAsset : ScriptableObject
    {
        public int2 Dimensions;
        public float CellSize;
        public float3 Origin;
        
        public float[] Heights;
        public float3[] Normals;
        public float3[] WallPush;
        public CellType[] CellsType;
        public float[] Weights;
        public bool hasData;
    }
}
