using System.Runtime.CompilerServices;
using PFStar;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
public static class GridUtils {
    private const int QuadrantCellSize = 4;
    private const int QuadrantMultiplier = 100;

    /// <summary>
    /// Get Cell index
    /// Example, for 3х3 maze:
    /// index   coord
    /// 0 -     [0,0]
    /// 1 -     [1,0]
    /// 3 -     [0,1]
    /// 4 -     [1,1]
    /// index = y* width + x
    /// </summary>
    /// <param name="coord"></param>
    /// <param name="dimX"></param>
    /// <returns> </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CoordToIndex(int2 coord, int dimX) {
        int index = coord.y * dimX + coord.x;
        return index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CoordToIndex(int x, int y, int dimX) {
        int index = y * dimX + x;
        return index;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int2 WorldToCellCoord(float3 worldPos, float3 origin) {
        float2 diff = worldPos.xz - origin.xz; 
        return (int2)math.round(diff);
    }

    /// <summary>
    /// Get world pos of cell
    /// </summary>
    /// <param name="grid">buffer of cells grid</param>
    /// <param name="coord">Coord of cell</param>
    /// <param name="dimX">dimension x</param>
    /// <param name="index"> index of cell</param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float3 CoordToWorld(DynamicBuffer<GridBuffer> grid, int index) {
        var cell = grid[index];
        var worldPos = cell.WorldPos;
        return worldPos;
    }

    /// <summary>
    /// Heuristic distance (Manhatten)
    /// </summary>
    /// <param name="current"></param>
    /// <param name="destination"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int H(int2 current, int2 destination) {
        var dX = math.abs(destination.x - current.x);
        var dY = math.abs(destination.y - current.y);
        return dX + dY;
    }

    /// <summary>
    /// Heuristic distance (Euclid)
    /// </summary>
    /// <param name="current"></param>
    /// <param name="destination"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float H_Euclid(int2 current, int2 destination) {
        return math.csum(math.abs(destination - current));
    }
    
}
