using System.Runtime.CompilerServices;
using PFStar;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
public static class GridUtils
{
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
    public static int CoordToIndex(int2 coord, int dimX)
    {
        int index = coord.y * dimX + coord.x;
        return index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CoordToIndex(int x, int y, int dimX)
    {
        int index = y * dimX + x;
        return index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float3 CoordToWorld(int2 coord, float3 origin, float cellSize)
    {
        return new float3(
            (coord.x * cellSize) + origin.x,
            origin.y,
            (coord.y * cellSize) + origin.z
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int2 IndexToCoord(int index, int dimX)
    {
        return new int2(index % dimX, index / dimX);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int2 WorldToCellCoord(float3 worldPos, float3 origin)
    {
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
    public static float3 CoordToWorld(DynamicBuffer<GridBuffer> grid, int index)
    {
        var cell = grid[index];
        var worldPos = cell.WorldPos;
        return worldPos;
    }

    /// <summary>
    /// Converts a 1D grid index to a world position using GridBlob data.
    /// </summary>
    /// <param name="grid">The GridBlob containing dimensions, origin, and cell size.</param>
    /// <param name="index">The 1D index of the cell.</param>
    /// <returns>A float3 world position at the center or origin of the cell.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float3 CoordToWorld(ref GridBlob grid, int index)
    {
        int x = index % grid.Dimensions.x;
        int y = index / grid.Dimensions.x;

        return new float3(
            (x * grid.CellSize) + grid.Origin.x,
            grid.Origin.y,
            (y * grid.CellSize) + grid.Origin.z
        );
    }

    /// <summary>
    /// Generates a unique hash key from a world position. 
    /// Internally converts world coordinates to grid coordinates using floor to handle negative values correctly.
    /// </summary>
    /// <param name="worldPos">The world position of the entity.</param>
    /// <param name="cellSize">The size of the spatial hash cell (e.g., SeparationRadius * 2).</param>
    /// <returns>A hash key calculated from the cell coordinates.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetSpatialHashKey(float3 worldPos, float cellSize)
    {
        // Floor ensures that -0.1 becomes -1, maintaining grid consistency across the world origin
        int2 cell = (int2)math.floor(worldPos.xz / cellSize);

        return GetSpatialHashKeyFromCell(cell);
    }

    /// <summary>
    /// Calculates a hash key from integer cell coordinates using large prime numbers.
    /// This is the core hashing function used for both adding entities and looking up neighbor cells.
    /// </summary>
    /// <param name="cellCoord">The integer coordinates (x, y) of the cell.</param>
    /// <returns>A deterministic hash key with low collision probability.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetSpatialHashKeyFromCell(int2 cellCoord)
    {
        // Prime number XOR hashing is a standard high-performance technique for spatial grids
        return cellCoord.x * 73856093 ^ cellCoord.y * 19349663;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsWallAtWorldPos(float3 worldPos, ref GridBlob grid)
    {
        int2 coord = WorldToCellCoord(worldPos, grid.Origin);
    
        // Проверка границ
        if (coord.x < 0 || coord.x >= grid.Dimensions.x || coord.y < 0 || coord.y >= grid.Dimensions.y)
            return true;

        int index = CoordToIndex(coord, grid.Dimensions.x);
        return grid.CellsType[index] == CellType.Wall;
    }

    /// <summary>
    /// Calculates the Manhattan distance (L1 norm) between two grid coordinates.
    /// Suitable for grids where movement is restricted to 4 directions (up, down, left, right).
    /// </summary>
    /// <param name="current">Current cell coordinates.</param>
    /// <param name="destination">Target cell coordinates.</param>
    /// <returns>The total number of steps in a cardinal grid.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int H(int2 current, int2 destination)
    {
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
    public static float H_Euclid(int2 current, int2 destination)
    {
        return math.csum(math.abs(destination - current));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float H_Octile(int2 current, int2 destination)
    {
        int dx = math.abs(current.x - destination.x);
        int dy = math.abs(current.y - destination.y);

        return (dx + dy) + (1.4142135f - 2f) * math.min(dx, dy);
    }
}