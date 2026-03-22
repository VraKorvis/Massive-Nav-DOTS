using System.Runtime.CompilerServices;
using Core.PathfindingAStar;
using Map.Grid;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
public static class GridUtils
{
    private const int QuadrantCellSize = 4;
    private const int QuadrantMultiplier = 100;
    
    public struct SurfaceData
    {
        public float Height;
        public float Weight;
        public float3 Normal;
    }

    /// <summary>
    /// Get Cell index
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
    public static int2 WorldToCellCoord(float3 worldPos, float3 origin, float cellSize)
    {
        float2 localPos = (worldPos.xz - origin.xz) / cellSize;
        return (int2)math.floor(localPos);

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float3 CellToWorldCoord(int2 cellCoord, float3 origin, float cellSize)
    {
        return new float3(
            (cellCoord.x * cellSize) + origin.x + (cellSize * 0.5f),
            origin.y,
            (cellCoord.y * cellSize) + origin.z + (cellSize * 0.5f)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInBounds(int2 coord, int2 dims)
    {
        return (uint)coord.x < (uint)dims.x && (uint)coord.y < (uint)dims.y;
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
        int2 coord = WorldToCellCoord(worldPos, grid.Origin, grid.CellSize);

        if (!IsInBounds(coord, grid.Dimensions))
            return true;

        int index = CoordToIndex(coord, grid.Dimensions.x);
        return grid.CellsType[index] == CellType.Wall;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SurfaceData GetSurfaceData(ref GridBlob grid, float3 worldPos)
    {
        float2 localPos = (worldPos.xz - grid.Origin.xz) / grid.CellSize - 0.5f;
        int x0 = math.clamp((int)math.floor(localPos.x), 0, grid.Dimensions.x - 2);
        int y0 = math.clamp((int)math.floor(localPos.y), 0, grid.Dimensions.y - 2);
        float2 t = math.frac(localPos);
        int width = grid.Dimensions.x;

        int i00 = y0 * width + x0;
        int i10 = y0 * width + (x0 + 1);
        int i01 = (y0 + 1) * width + x0;
        int i11 = (y0 + 1) * width + (x0 + 1);

        float w00 = grid.Weights[i00];
        float w10 = grid.Weights[i10];
        float w01 = grid.Weights[i01];
        float w11 = grid.Weights[i11];

        float h00 = grid.Heights[i00], h10 = grid.Heights[i10], h01 = grid.Heights[i01], h11 = grid.Heights[i11];
        float3 n00 = grid.Normals[i00], n10 = grid.Normals[i10], n01 = grid.Normals[i01], n11 = grid.Normals[i11];

        if (float.IsInfinity(w10))
        {
            h10 = h00;
            n10 = n00;
        }
        if (float.IsInfinity(w01))
        {
            h01 = h00;
            n01 = n00;
        }
        if (float.IsInfinity(w11))
        {
            h11 = h00;
            n11 = n00;
        }

        float rw00 = float.IsInfinity(w00) ? 1.0f : w00;
        float rw10 = float.IsInfinity(w10) ? 1.0f : w10;
        float rw01 = float.IsInfinity(w01) ? 1.0f : w01;
        float rw11 = float.IsInfinity(w11) ? 1.0f : w11;

        return new SurfaceData
        {
            Height = math.lerp(math.lerp(h00, h10, t.x), math.lerp(h01, h11, t.x), t.y),
            Weight = math.lerp(math.lerp(rw00, rw10, t.x), math.lerp(rw01, rw11, t.x), t.y),
            Normal = math.normalize(math.lerp(math.lerp(n00, n10, t.x), math.lerp(n01, n11, t.x), t.y))
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float GetHeightBilinear(ref GridBlob grid, float3 worldPos)
    {
        float2 localPos = (worldPos.xz - grid.Origin.xz) / grid.CellSize - 0.5f;

        int x0 = math.clamp((int)math.floor(localPos.x), 0, grid.Dimensions.x - 2);
        int y0 = math.clamp((int)math.floor(localPos.y), 0, grid.Dimensions.y - 2);

        float2 t = math.frac(localPos);
        int width = grid.Dimensions.x;

        int i00 = y0 * width + x0;
        int i10 = y0 * width + (x0 + 1);
        int i01 = (y0 + 1) * width + x0;
        int i11 = (y0 + 1) * width + (x0 + 1);

        float h00 = grid.Heights[i00];
        float h10 = grid.Heights[i10];
        float h01 = grid.Heights[i01];
        float h11 = grid.Heights[i11];

        if (float.IsInfinity(grid.Weights[i10])) h10 = h00;
        if (float.IsInfinity(grid.Weights[i01])) h01 = h00;
        if (float.IsInfinity(grid.Weights[i11])) h11 = h00;

        return math.lerp(math.lerp(h00, h10, t.x), math.lerp(h01, h11, t.x), t.y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float3 GetWallPushBilinear(ref GridBlob grid, float3 worldPos)
    {
        float2 localPos = (worldPos.xz - grid.Origin.xz) / grid.CellSize - 0.5f;

        int x = (int)math.floor(localPos.x);
        int y = (int)math.floor(localPos.y);

        int x0 = math.clamp(x, 0, grid.Dimensions.x - 1);
        int x1 = math.clamp(x + 1, 0, grid.Dimensions.x - 1);
        int y0 = math.clamp(y, 0, grid.Dimensions.y - 1);
        int y1 = math.clamp(y + 1, 0, grid.Dimensions.y - 1);

        float2 t = localPos - math.floor(localPos);
        int width = grid.Dimensions.x;

        float3 v00 = grid.WallPushField[y0 * width + x0];
        float3 v10 = grid.WallPushField[y0 * width + x1];
        float3 v01 = grid.WallPushField[y1 * width + x0];
        float3 v11 = grid.WallPushField[y1 * width + x1];

        return math.lerp(
            math.lerp(v00, v10, t.x),
            math.lerp(v01, v11, t.x),
            t.y
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float3 GetNormalBilinear(ref GridBlob grid, float3 worldPos)
    {
        float2 localPos = (worldPos.xz - grid.Origin.xz) / grid.CellSize - 0.5f;

        int x0 = math.clamp((int)math.floor(localPos.x), 0, grid.Dimensions.x - 2);
        int y0 = math.clamp((int)math.floor(localPos.y), 0, grid.Dimensions.y - 2);

        float2 t = math.frac(localPos);
        int width = grid.Dimensions.x;

        int i00 = y0 * width + x0;
        int i10 = y0 * width + (x0 + 1);
        int i01 = (y0 + 1) * width + x0;
        int i11 = (y0 + 1) * width + (x0 + 1);

        float3 n00 = grid.Normals[i00];
        float3 n10 = grid.Normals[i10];
        float3 n01 = grid.Normals[i01];
        float3 n11 = grid.Normals[i11];

        if (float.IsInfinity(grid.Weights[i10])) n10 = n00;
        if (float.IsInfinity(grid.Weights[i01])) n01 = n00;
        if (float.IsInfinity(grid.Weights[i11])) n11 = n00;

        return math.normalize(math.lerp(math.lerp(n00, n10, t.x), math.lerp(n01, n11, t.x), t.y));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float GetWeightBilinear(ref GridBlob grid, float3 worldPos)
    {
        float2 localPos = (worldPos.xz - grid.Origin.xz) / grid.CellSize - 0.5f;

        int x0 = math.clamp((int)math.floor(localPos.x), 0, grid.Dimensions.x - 2);
        int y0 = math.clamp((int)math.floor(localPos.y), 0, grid.Dimensions.y - 2);

        float2 t = math.frac(localPos);
        int width = grid.Dimensions.x;

        int i00 = y0 * width + x0;
        int i10 = y0 * width + (x0 + 1);
        int i01 = (y0 + 1) * width + x0;
        int i11 = (y0 + 1) * width + (x0 + 1);

        float w00 = grid.Weights[i00];
        float w10 = grid.Weights[i10];
        float w01 = grid.Weights[i01];
        float w11 = grid.Weights[i11];

        if (float.IsInfinity(w00)) w00 = 1.0f;
        if (float.IsInfinity(w10)) w10 = 1.0f;
        if (float.IsInfinity(w01)) w01 = 1.0f;
        if (float.IsInfinity(w11)) w11 = 1.0f;

        return math.lerp(math.lerp(w00, w10, t.x), math.lerp(w01, w11, t.x), t.y);
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
