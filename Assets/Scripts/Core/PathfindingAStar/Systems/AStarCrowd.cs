using Map.Grid;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Core.PathfindingAStar
{
    public static class AStarCrowd
    {
        public static bool FindPath(ref BoxData box, [ReadOnly] NativeArray<int> indexOffsets, [ReadOnly] NativeArray<int2> neighbours, NativeArray<float> distMultipliers, float greedyCoef)
        {
            ref var grid = ref box.GridBlob.Value;
            int dimX = box.DimX;
            int dimY = box.DimY;
            uint sID = box.SearchID;

            if (box.StartPos.Equals(box.Destination))
            {
                var indexCell = GridUtils.CoordToIndex(box.Destination, dimX);
                box.Waypoints.Add(new Waypoint
                {
                    point = GridUtils.CoordToWorld(ref grid, indexCell)
                });
                return false;
            }

            var startIdx = GridUtils.CoordToIndex(box.StartPos, dimX);
            int destIdx = GridUtils.CoordToIndex(box.Destination, dimX);
            var nodeSlice = box.CostSoFar;
            nodeSlice[startIdx] = new NodeData
            {
                Cost = 0,
                Version = sID
            };
            box.CameFrom[startIdx] = box.StartPos;

            var hOctile = GridUtils.H_Octile(box.StartPos, box.Destination);
            float weightedH = hOctile * box.GreedyCoef;
            box.OpenSet.Push(new BinaryHeapNode(box.StartPos, weightedH, weightedH));
            float minH = float.MaxValue;

            int2 bestPointSoFar = box.StartPos;

            int counter = 0;
            while (box.OpenSet.HasNext())
            {
                if (counter++ > box.IterationLimit)
                {
                    box.Destination = bestPointSoFar;
                    return true;
                }

                var current = box.OpenSet.Pop();

                if (current.DistanceToGoal < minH)
                {
                    minH = current.DistanceToGoal;
                    bestPointSoFar = current.Position;
                }

                var fromIndex = GridUtils.CoordToIndex(current.Position, dimX);
                if (fromIndex == destIdx) return true;

                var initialCost = box.CostSoFar[fromIndex].Cost;

                for (int i = 0; i < neighbours.Length; i++)
                {
                    var step = neighbours[i];

                    var nextPos = current.Position + step;

                    if ((uint)nextPos.x >= (uint)dimX || (uint)nextPos.y >= (uint)dimY) continue;

                    if (step.x != 0 && step.y != 0)
                    {
                        int2 side1 = new int2(current.Position.x + step.x, current.Position.y);
                        int2 side2 = new int2(current.Position.x, current.Position.y + step.y);

                        int idx1 = GridUtils.CoordToIndex(side1, dimX);
                        int idx2 = GridUtils.CoordToIndex(side2, dimX);

                        bool wall1 = grid.CellsType[idx1] == CellType.Wall;
                        bool wall2 = grid.CellsType[idx2] == CellType.Wall;

                        bool inf1 = float.IsInfinity(grid.Weights[idx1]);
                        bool inf2 = float.IsInfinity(grid.Weights[idx2]);

                        if (wall1 || wall2 || inf1 || inf2)
                        {
                            continue;
                        }
                    }

                    int toIndex = fromIndex + indexOffsets[i];
                    var cellCost = GetCost(toIndex, i, distMultipliers, ref grid);

                    if (float.IsInfinity(cellCost)) continue;

                    var newCost = initialCost + cellCost;

                    bool isUpToDate = nodeSlice[toIndex].Version == sID;
                    float oldCost = isUpToDate ? box.CostSoFar[toIndex].Cost : float.MaxValue;

                    if (oldCost > 0 && oldCost <= newCost) continue;

                    nodeSlice[toIndex] = new NodeData
                    {
                        Cost = newCost,
                        Version = sID
                    };

                    box.CameFrom[toIndex] = current.Position;

                    var h = GridUtils.H_Octile(nextPos, box.Destination);
                    weightedH = h * greedyCoef;

                    float f = newCost + weightedH;
                    box.OpenSet.Push(new BinaryHeapNode(nextPos, f, weightedH));
                }
            }

            return false;
        }

        public static void BuildPath(ref GridBlob grid, ref BoxData box)
        {
            var ind = GridUtils.CoordToIndex(box.Destination, box.DimX);
            box.Waypoints.Add(new Waypoint
            {
                point = GridUtils.CoordToWorld(ref grid, ind)
            });

            var currCoord = box.CameFrom[ind];
            int safety = box.DimX * box.DimY;
            int iter = 0;

            while (!currCoord.Equals(box.StartPos))
            {
                int cInd = GridUtils.CoordToIndex(currCoord, box.DimX);
                if (iter++ > safety)
                {
                    // TODO: replace with NativeQueue<DebugEvent> for production diagnostics, Safety counter exceeded - path reconstruction aborted
                    break;
                }
                box.Waypoints.Add(new Waypoint
                {
                    point = GridUtils.CoordToWorld(ref grid, cInd)
                });
                currCoord = box.CameFrom[cInd];
            }
        }

        private static float GetCost(int gridIndex, int neighborIndex, NativeArray<float> distMultipliers, ref GridBlob grid)
        {
            //TODO add Weights
            // float baseWeight = grid.Weights[gridIndex];
            float baseWeight = grid.Weights[gridIndex];
            float distanceMultiplier = distMultipliers[neighborIndex];
            return baseWeight * distanceMultiplier;
        }
    }
}
