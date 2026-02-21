using Core.Gameplay;
using Unity.Entities;

namespace Core.Input
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(MoveToCommandSystem))] 
    public partial struct InputCleanupSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (_, entity) in SystemAPI.Query<RefRO<MoveToCommand>>().WithEntityAccess())
            {
                SystemAPI.SetComponentEnabled<MoveToCommand>(entity, false);
            }
        }
    }
}