using Unity.Entities;

namespace OptRenderer
{
    public struct DensityCullingData : IComponentData
    {
        public float Visibility;
        public bool ShouldBeVisible;
    }
}