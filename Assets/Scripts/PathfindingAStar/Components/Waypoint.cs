using System;
using Unity.Entities;
using Unity.Mathematics;

namespace PFStar
{
    [Serializable]
    public struct Waypoint : IBufferElementData
    {
        public float3 point;
    }
}