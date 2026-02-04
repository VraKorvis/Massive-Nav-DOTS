using Gameplay.Player;
using PFStar;
using Unity.Entities;
using Unity.Transforms;

namespace Input
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct ClickerInitSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            state.Enabled = false;
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            
            var clicker = ecb.CreateEntity();
            ecb.AddComponent(clicker, new ClickEntityTag());
            ecb.AddComponent(clicker, new ClickEventData());
            ecb.AddComponent(clicker, new IsClickTag());
            ecb.SetComponentEnabled<IsClickTag>(clicker, false);


            var marker = ecb.CreateEntity();
            ecb.AddComponent(marker, new ClickMarkerTag());
            ecb.AddComponent(marker, new LocalTransform { Scale = 1f });
            ecb.AddComponent(marker, new NavigationTargetGridData());
            ecb.AddComponent(marker, new MoveToCommand());
            ecb.SetComponentEnabled<MoveToCommand>(marker, false);

            ecb.AddComponent(marker, new TargetChangedTag());
            ecb.SetComponentEnabled<TargetChangedTag>(marker, false);

            ecb.Playback(state.EntityManager);
            ecb.Dispose();

        }
    }
}