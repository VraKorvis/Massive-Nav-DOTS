using System;
using System.Collections.Generic;
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
        None        = 1 << 0,  // 0000 0001
        Significant = 1 << 1,  // 0000 0010
        Find        = 1 << 2,  // 0000 0100
        Process     = 1 << 3,  // 0000 1000
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

    public struct PathRequestMetadata : IComponentData
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
        public int Compare(SortableRequest x, SortableRequest y) => x.RequestTime.CompareTo(y.RequestTime);
    }

    public class PathAgentStatusAuthoring : MonoBehaviour
    {
        public AgentStatus status;
    }

    public class PathAgentStatusBaker : Baker<PathAgentStatusAuthoring>
    {
        public override void Bake(PathAgentStatusAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new PathRequestAgent
            {
                owner = entity,
                startCoord = int2.zero,
                destination = int2.zero
            });

            AddComponent(entity, new PathAgentStatus { Value = authoring.status });
            AddComponent(entity, new PathRequestMetadata { RequestTime = 0 });

            AddComponent<PathAgentStatusAddPathRequestTag>(entity);

            AddComponent(entity, new PFAgentState {
                Flags = (byte)PFAgentsStatus.Significant 
            });

            AddBuffer<Waypoint>(entity);
        }
    }
}