using System.Runtime.CompilerServices;
using PathfindingAStar;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public static class GridUtils {
    private const int QuadrantCellSize = 4;
    private const int QuadrantMultiplier = 100;

    public static readonly int2[] Offsets = new int2[] {
        new int2(1, 0), new int2(-1, 0), new int2(0, 1), new int2(0, -1)
    };
    
    // public static readonly NativeArray<int2> Directions = new NativeArray<int2>(4, Allocator.Persistent) {
    //     [0] = new int2(0, 1),
    //     [1] = new int2(1, 0),
    //     [2] = new int2(0, -1),
    //     [3] = new int2(-1, 0),
    // };

    /// <summary>
    /// Get neighbors cell coordinates
    /// </summary>
    /// <param name="i">current cell coordinate</param>
    /// <returns></returns>
    public static int2[] GetCoordNeighbours(int2 coord) {
        int2[] neighbours = new int2[4];
        neighbours[0] = coord + new int2(1, 0);
        neighbours[1] = coord + new int2(-1, 0);
        neighbours[2] = coord + new int2(0, 1);
        neighbours[3] = coord + new int2(0, -1);
        return neighbours;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int2 GetNeighbourCoord(int2 coord, int neighborIndex) {
        switch (neighborIndex) {
            case 0: return coord + new int2(1, 0);
            case 1: return coord + new int2(-1, 0);
            case 2: return coord + new int2(0, 1);
            case 3: return coord + new int2(0, -1);
            default: return coord;
        }
    }

    /// <summary>
    /// Get neighbors cell indexes
    /// </summary>
    /// <param name="cellIndex"> current cell index</param>
    /// <param name="dimX"> width of maze</param>
    /// <returns></returns>
    public static int[] GetNeighboursIndex(int cellIndex, int dimX) {
        int[] neighbours = new int[4];
        neighbours[0] = cellIndex + 1;
        neighbours[1] = cellIndex + -1;
        neighbours[2] = cellIndex + 1 * dimX;
        neighbours[3] = cellIndex + -1 * dimX;
        return neighbours;
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int2 WorldToCellCoord(float2 p0, float3 p1) {
        var dx = math.round(math.abs(p0.x - p1.x));
        var dy = math.round(math.abs(p0.y - p1.y));
        return new int2((int) dx, (int) dy);
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

    /// <summary>
    /// Get the index of the cell in ahead by the direction vector
    /// </summary>
    /// <param name="currentCellIndex">cell index current position</param>
    /// <param name="direction">Direction for move</param>
    /// <param name="dimX">Width on the maze</param>
    /// <returns></returns>
    public static int ForwardCellIndex(int currentCellIndex, float2 direction, int dimX) {
        int offsetIndex = 0;
        if (direction.x != 0) {
            offsetIndex = (int) direction.x;
        }
        else if (direction.y != 0) {
            offsetIndex = (int) direction.y * dimX;
        }

        return currentCellIndex + offsetIndex;
    }

    public static int LeftCellIndex(int currentCellIndex, float2 direction, int dimX) {
        int offsetIndex = 0;
        if (direction.x != 0) {
            offsetIndex = (int) direction.x;
        }
        else if (direction.y != 0) {
            offsetIndex = (int) direction.y * dimX;
        }

        return currentCellIndex + offsetIndex;
    }

    /// <summary>
    /// Get vector direction x,y (int2)
    /// </summary>
    /// <param name="direct">enum direction</param>
    /// <returns></returns>
    public static int2 GetDir(MoveDirection direct) {
        switch (direct) {
            case MoveDirection.UP: return new int2(0, 1);
            case MoveDirection.DOWN: return new int2(0, -1);
            case MoveDirection.RIGHT: return new int2(1, 0);
            case MoveDirection.LEFT: return new int2(-1, 0);
            default: return new int2(0, 1);
        }
    }

    /// <summary>
    /// Get array offset direction all neighbors (int2[])
    /// </summary>
    /// <param name="canDiagonal"></param>
    /// <returns></returns>
    public static int2[] NeighborsOffset(bool canDiagonal) {
        var count = canDiagonal ? 8 : 4;
        var neighbors = new int2[count];
        neighbors[1] = new int2(0, 1);
        neighbors[2] = new int2(1, 0);
        neighbors[3] = new int2(0, -1);
        neighbors[4] = new int2(-1, 0);
        if (canDiagonal) {
            neighbors[5] = new int2(1, 1);
            neighbors[6] = new int2(1, -1);
            neighbors[7] = new int2(-1, -1);
            neighbors[8] = new int2(-1, 1);
        }

        return neighbors;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int2 IndexToCoord(int index, int dimX) {
        var x = index % dimX;
        var y = index / dimX;
        return new int2(x, y);
    }

    /// <summary>
    /// HashKey for MultiHashMap QuadrantSystem, by default quadrantCellSize = 4, quadrantMultiplier 100
    /// </summary>
    /// <param name="pos">Current position</param>
    /// <param name="quadCellSize">by default quadrantCellSize = 4</param>
    /// <param name="quadMultiplier">quadrantMultiplier 100</param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetPositionHashMapKey(float3 pos, int quadCellSize = QuadrantCellSize,
        int quadMultiplier = QuadrantMultiplier) {
        return (int) (math.floor(pos.x / quadCellSize) + (quadMultiplier * math.floor(pos.y / quadCellSize)));
    }

    /// <summary>
    /// Debug Gizmos quadrant
    /// </summary>
    /// <param name="pos">Current position</param>
    /// <param name="quadrantCellSize">by default quadrantCellSize = 4</param>
    public static void DebugDrawQuadrant(float3 pos, int quadrantCellSize = 4) {
        var leftCorner = new float3(math.floor(pos.x / quadrantCellSize) * quadrantCellSize,
            math.floor(pos.y / quadrantCellSize) * quadrantCellSize,
            0);
        var rightCorner = leftCorner + new float3(1, 0, 0) * quadrantCellSize;
        var upCorner = leftCorner + new float3(0, 1, 0) * quadrantCellSize;

        Debug.DrawLine(leftCorner, rightCorner);
        Debug.DrawLine(leftCorner, upCorner);
        Debug.DrawLine(rightCorner, rightCorner + new float3(0, 1, 0) * quadrantCellSize);
        Debug.DrawLine(upCorner, upCorner + new float3(1, 0, 0) * quadrantCellSize);
    }

    // public static CellType GetTileType(string nameTilemap) {
    //     switch (nameTilemap) {
    //         case "Ground":
    //             return CellType.Ground;
    //         case "Wall":
    //             return CellType.Wall;
    //         case "Trap":
    //             return CellType.Trap;
    //         case "Mask":
    //             return CellType.Mask;
    //         default:
    //             return CellType.Empty;
    //     }
    // }
}

public enum MoveDirection {
    UP,
    RIGHT,
    DOWN,
    LEFT
}