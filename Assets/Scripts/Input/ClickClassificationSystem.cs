using Gameplay.Player;
using PFStar;
using Unity.Entities;

namespace Input
{
    [UpdateBefore(typeof(MoveToCommandSystem))]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct ClickClassificationSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ClickEventQueue>();
            state.RequireForUpdate<GridTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var queue = SystemAPI.GetSingleton<ClickEventQueue>().Queue;
            if (queue.IsEmpty()) return;

            if (!SystemAPI.TryGetSingletonEntity<ClickMarkerTag>(out var markerEntity)) return;

            ClickEntry lastEntry = default;
            bool hasClick = false;

            while (queue.TryDequeue(out var entry))
            {
                lastEntry = entry;
                hasClick = true;
            }

            if (hasClick && lastEntry.MouseButton == 0)
            {
                state.EntityManager.SetComponentData(markerEntity, new MoveToCommand { WorldPosition = lastEntry.WorldPosition });
                state.EntityManager.SetComponentEnabled<MoveToCommand>(markerEntity, true);
            }
        }
    }
}