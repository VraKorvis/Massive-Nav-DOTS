using Unity.Entities;

namespace Core.Input
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(InputReaderSystem))]
    public partial struct ClickDispatchSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<RawInputFrame>();
            state.RequireForUpdate<ClickEventQueue>();
            state.RequireForUpdate<ClickMarkerTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingletonEntity<ClickMarkerTag>(out var marker)) return;

            var frame = SystemAPI.GetSingleton<RawInputFrame>();

            if (!frame.ClickThisFrame || !frame.ClickWorldValid) return;

            var q = SystemAPI.GetSingleton<ClickEventQueue>();
            q.Queue.Enqueue(frame.ClickWorldPos);
            
            Unity.Mathematics.float3 latest = default;
            bool hasAny = false;
            while (q.Queue.TryDequeue(out var pos))
            {
                latest = pos;
                hasAny = true;
            }

            if (!hasAny) return;

            state.EntityManager.SetComponentData(marker, new MoveToCommand { WorldPosition = latest });
            state.EntityManager.SetComponentEnabled<MoveToCommand>(marker, true);
        }
    }
}