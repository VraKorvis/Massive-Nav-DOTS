using Unity.Burst;
using Unity.Mathematics;

namespace Core.Spatial
{
    [BurstCompile]
    public struct MortonUtils
    {
        private static uint Part1By1(uint x)
        {
            x &= 0x0000ffff;
            x = (x ^ (x << 8)) & 0x00ff00ff;
            x = (x ^ (x << 4)) & 0x0f0f0f0f;
            x = (x ^ (x << 2)) & 0x33333333;
            x = (x ^ (x << 1)) & 0x55555555;
            return x;
        }

        public static uint GetMorton2D(float3 position, float cellSize)
        {
            uint x = (uint)math.max(0, math.floor(position.x / cellSize));
            uint z = (uint)math.max(0, math.floor(position.z / cellSize));
            return Part1By1(x) | (Part1By1(z) << 1);
        }
    }
}
