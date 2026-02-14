using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Features.OptRenderer;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Core.PathfindingAStar
{
    
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
    public enum PFAgentStatus : byte
    {
        // 0000 0000 | No state assigned
        Default = 0,
        
        // 0000 0001 | Agent is stationary, no active tasks
        Idle = 1 << 0,
        
        // 0000 0010 | Significant movement detected (> 10 cells)
        Significant = 1 << 1,
        
        // 0000 0100 | REQUEST: Start pathfinding calculation
        Find = 1 << 2,
        
        // 0000 1000 | ACTIVE: Job is scheduled and running
        Processing = 1 << 3,
        
        // 0001 0000 | READY: Path found, agent can move
        Process = 1 << 4,
        
        // 0100 0000 | SYSTEM: Bypass logic, force data refresh
        ForceUpdate = 1 << 6
    }

    public struct PFAgentState : IComponentData
    {
        public byte Flags;
    }
    
    public struct PFRequestMetadata : IComponentData
    {
        public float RequestTime;
        public int Priority;
        public uint LastProcessedVersion;
        public float Weight;
    }

    public struct PathRequestCandidate
    {
        public Entity Entity;
        public float RequestTime;
        public int Priority;
        public float Weight;

    }

    public struct RequestComparer : IComparer<PathRequestCandidate>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare(PathRequestCandidate x, PathRequestCandidate y)
        {
            int priorityDiff = y.Priority - x.Priority;
            if (priorityDiff != 0) return priorityDiff;

            if (x.Weight > y.Weight) return -1;
            if (x.Weight < y.Weight) return 1;

            if (x.RequestTime < y.RequestTime) return -1;
            if (x.RequestTime > y.RequestTime) return 1;
        
            return 0;
        }
    }

    public struct PathfindingHighPriorityTag : IComponentData { }
    
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

                AddComponent(entity, new PFRequestMetadata { RequestTime = 0 });
                
                AddComponent(entity, new PFAgentState
                {
                    Flags = (byte)PFAgentStatus.Significant
                });

                AddComponent(entity, new AgentVisualParams { EffectValue = 1f });
                AddComponent(entity, new DensityCullingData());
                AddComponent(entity, new VisibilityProperty());

                AddBuffer<Waypoint>(entity);
            }
        }
    }
}