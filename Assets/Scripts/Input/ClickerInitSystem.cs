using Gameplay.Player;
using PFStar;
using Unity.Collections;
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
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            
            var clicker = ecb.CreateEntity();
            ecb.AddComponent(clicker, new ClickEntityTag());
            ecb.AddComponent(clicker, new ClickEventQueue()
            {
                    Queue = new NativeQueue<ClickEntry>(Allocator.Persistent)
            });

            var marker = ecb.CreateEntity();
            ecb.AddComponent(marker, new ClickMarkerTag());
            ecb.AddComponent(marker, new LocalTransform { Scale = 1f });
            ecb.AddComponent(marker, new NavigationTargetGridData());
            ecb.AddComponent(marker, new MoveToCommand());
            ecb.SetComponentEnabled<MoveToCommand>(marker, false);

            ecb.Playback(state.EntityManager);
            ecb.Dispose();

        }

        public void OnDestroy(ref SystemState state)
        {
            SystemAPI.GetSingleton<ClickEventQueue>().Queue.Dispose();
        }
    }
}