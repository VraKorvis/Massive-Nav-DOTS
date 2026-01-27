using Unity.Entities;
using Unity.Mathematics;

public struct PathRequestAgent : IComponentData {
    public Entity focus;
    public Entity owner;
    public int2 startCoord;
    public int2 destination;
    public float NextAllowedUpdateTime;
}
