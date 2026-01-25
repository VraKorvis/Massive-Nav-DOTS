using System;
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

public struct PathAgentStatusFindTag : IComponentData
{
}

public struct PathAgentStatusAddPathRequestTag : IComponentData
{
}

public struct PathAgentStatusNoneTag : IComponentData
{
}

public struct PathAgentStatusReadyTag : IComponentData
{
}

public struct PathAgentStatusProcessTag : IComponentData
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

public class PathAgentStatusAuthoring : MonoBehaviour
{
    public AgentStatus status;
}

public class PathAgentStatusBaker : Baker<PathAgentStatusAuthoring>
{
    public override void Bake(PathAgentStatusAuthoring authoring)
    {
        var entity = GetEntity(TransformUsageFlags.Dynamic);

        AddComponent(entity, new PathAgentStatus
        {
            Value = authoring.status
        });

        AddComponent(entity, new PathRequestAgent
        {
            owner = entity,
            startCoord = int2.zero,
            destination = int2.zero
        });

        if (authoring.status == AgentStatus.Find)
        {
            AddComponent(entity, new PathAgentStatusFindTag());
        }
        else if (authoring.status == AgentStatus.None)
        {
            AddComponent(entity, new PathAgentStatusNoneTag());
        }

        AddBuffer<Waypoint>(entity);
    }
}