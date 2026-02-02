using System;
using System.Collections.Generic;
using OptRenderer;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PFStar
{
    [Serializable]
    public struct Waypoint : IBufferElementData
    {
        public float3 point;
    }

    public struct PathAgentStatus : IComponentData
    {
        public AgentStatus Value;
    }

/*
Binary      Decimal   Flags set
0000 0000   0         not used
0000 0001   1         None
0000 0010   2         Significant
0000 0100   4         Find
0000 1000   8         Process
0000 0011   3         None + Significant
0000 0101   5         None + Find
0000 1001   9         None + Process
0000 0110   6         Significant + Find
0000 1010   10        Significant + Process
0000 1100   12        Find + Process
0000 0111   7         None + Significant + Find
0000 1011   11        None + Significant + Process
0000 1101   13        None + Find + Process
0000 1110   14        Significant + Find + Process
0000 1111   15        None + Significant + Find + Process
*/

    [Flags]
    public enum PFAgentsStatus : byte
    {
        None = 1 << 0, // 0000 0001
        Significant = 1 << 1, // 0000 0010
        Find = 1 << 2, // 0000 0100
        Process = 1 << 3, // 0000 1000
    }

    public struct PFAgentState : IComponentData
    {
        public byte Flags;
    }


    public struct PathAgentStatusAddPathRequestTag : IComponentData, IEnableableComponent
    {
    }

    public enum AgentStatus
    {
        Find,
        None,
        Process,
    }

    public struct PFRequestMetadata : IComponentData
    {
        public double RequestTime;
    }

    public struct SortableRequest
    {
        public Entity Entity;
        public double RequestTime;
    }

    public struct RequestComparer : IComparer<SortableRequest>
    {
        public int Compare(SortableRequest x, SortableRequest y)
        {
            if (x.Entity == Entity.Null) return y.Entity == Entity.Null ? 0 : 1;
            if (y.Entity == Entity.Null) return -1;
            
            if (x.RequestTime < y.RequestTime) return -1;
            return x.RequestTime > y.RequestTime ? 1 : 0;
        }
    }

    public struct PFRequestAgent : IComponentData
    {
        public Entity Focus;
        public Entity Owner;
        public int2 StartCoord;
        public int2 Destination;
        public float NextAllowedUpdateTime;
    }

    public class PFAgentAuthoring : MonoBehaviour
    {
        public AgentStatus status;

        public class PathAgentStatusBaker : Baker<PFAgentAuthoring>
        {
            public override void Bake(PFAgentAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new PFRequestAgent
                {
                    Owner = entity,
                    StartCoord = int2.zero,
                    Destination = int2.zero
                });

                AddComponent(entity, new PathAgentStatus { Value = authoring.status });
                AddComponent(entity, new PFRequestMetadata { RequestTime = 0 });

                AddComponent<PathAgentStatusAddPathRequestTag>(entity);

                AddComponent(entity, new PFAgentState
                {
                    Flags = (byte)PFAgentsStatus.Significant
                });

                AddComponent(entity, new AgentVisualParams { EffectValue = 1f });
                AddComponent(entity, new DensityCullingData());
                AddComponent(entity, new VisibilityProperty());

                AddBuffer<Waypoint>(entity);
            }
        }
    }
}