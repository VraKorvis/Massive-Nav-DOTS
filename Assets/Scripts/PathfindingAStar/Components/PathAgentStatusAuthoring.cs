using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public struct Waypoint : IBufferElementData
{
    public float3 point;
}

public struct PathAgentStatus : IComponentData
{
    public AgentStatus Value;
}

public struct PathAgentStatusFindTag : IComponentData, IEnableableComponent {}

public struct PathAgentStatusAddPathRequestTag : IComponentData, IEnableableComponent {}

public struct PathAgentStatusSignificantMoveTag : IComponentData, IEnableableComponent {}

public struct PathAgentStatusNoneTag : IComponentData, IEnableableComponent {}

public struct PathAgentStatusReadyTag : IComponentData, IEnableableComponent {}

public struct PathAgentStatusProcessTag : IComponentData, IEnableableComponent
{
    // public int wayBufferIndex;
}

public enum AgentStatus
{
    AddPathRequest,
    Find,
    Ready,
    Wait,
    None,
    Process,
    Done
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

        AddComponent<PathAgentStatusFindTag>(entity);
        AddComponent<PathAgentStatusProcessTag>(entity);
        AddComponent<PathAgentStatusNoneTag>(entity);
        AddComponent<PathAgentStatusSignificantMoveTag>(entity);

        SetComponentEnabled<PathAgentStatusFindTag>(entity, true); 
        SetComponentEnabled<PathAgentStatusProcessTag>(entity, false); 
        SetComponentEnabled<PathAgentStatusNoneTag>(entity, false);
        SetComponentEnabled<PathAgentStatusSignificantMoveTag>(entity, true);

        AddBuffer<Waypoint>(entity);
    }
}