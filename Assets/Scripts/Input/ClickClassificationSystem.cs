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
            state.RequireForUpdate<GridTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            
            foreach (var (clickData, entity) in SystemAPI.Query<RefRO<ClickEventData>>().WithAll<IsClickTag>().WithEntityAccess())
            {
                if (!SystemAPI.TryGetSingletonEntity<ClickMarkerTag>(out var markerEntity)) continue;

                SystemAPI.SetComponent(markerEntity, new MoveToCommand { WorldPosition = clickData.ValueRO.WorldPosition });
                SystemAPI.SetComponentEnabled<MoveToCommand>(markerEntity, true);
                SystemAPI.SetComponentEnabled<IsClickTag>(entity, false);
            }     
        }
    }
}