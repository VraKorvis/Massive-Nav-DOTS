using Unity.Entities;

namespace Core.Input
{
    public struct InputBlockStatus : IComponentData {
        public bool IsBlocked;
    }
}