using Unity.Entities;

namespace Features.OptRenderer
{
    public struct DensityCullingData : IComponentData
    {
        public float Visibility;
        public bool ShouldBeVisible;
    }
}