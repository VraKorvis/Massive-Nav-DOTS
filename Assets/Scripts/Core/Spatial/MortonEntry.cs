using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Entities;
using Unity.Mathematics;

namespace Core.Spatial
{
    public struct MortonEntry
    {
        public uint Key;
        public int Index;
        public Entity AgentEntity;
        public float3 Position;
    }

    public struct MortonEntryComparer : IComparer<MortonEntry>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare(MortonEntry x, MortonEntry y)
        {
            if (x.Key < y.Key) return -1;
            if (x.Key > y.Key) return 1;
            return 0;
        }
    }
}
