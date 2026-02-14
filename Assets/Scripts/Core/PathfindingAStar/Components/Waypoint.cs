using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Core.PathfindingAStar
{
    [Serializable]
    public struct Waypoint : IBufferElementData
    {
        public float3 point;
    }
}