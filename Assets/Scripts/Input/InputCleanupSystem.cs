using Gameplay.Player;
using Unity.Entities;

namespace Input
{
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    public partial struct InputCleanupSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (_, entity) in SystemAPI.Query<RefRO<IsClickTag>>().WithEntityAccess())
            {
                SystemAPI.SetComponentEnabled<IsClickTag>(entity, false);
            }

            foreach (var (_, entity) in SystemAPI.Query<RefRO<MoveToCommand>>().WithEntityAccess())
            {
                SystemAPI.SetComponentEnabled<MoveToCommand>(entity, false);
            }
        }
    }
}