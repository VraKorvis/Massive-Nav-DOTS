using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace PathfindingAStar
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public partial struct PathRequestUpdateSystem : ISystem
    {
        private EntityQuery _playerQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<PathTargetData>();
            _playerQuery = state.GetEntityQuery(ComponentType.ReadOnly<PathTargetData>());
            _playerQuery.SetChangedVersionFilter(ComponentType.ReadOnly<PathTargetData>());
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (_playerQuery.IsEmpty) return;

            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            var targetEntity = _playerQuery.GetSingletonEntity();
            var targetData = SystemAPI.GetSingleton<PathTargetData>();

            double currentTime = state.WorldUnmanaged.Time.ElapsedTime;
            
            int requestsCount = 0;
            const int maxRequestsPerFrame = 32;
            
            foreach (var (transform, request, status, entity) in 
                     SystemAPI.Query<RefRO<LocalTransform>, RefRW<PathRequestAgent>, RefRW<PathAgentStatus>>()
                         .WithNone<PathAgentStatusFindTag>()
                         .WithEntityAccess())
            {
                if (requestsCount >= maxRequestsPerFrame) break; 

                if (currentTime < request.ValueRO.NextAllowedUpdateTime) continue;

                bool significantMove = !request.ValueRO.destination.Equals(targetData.LastSignificantCell);
        
                if (significantMove)
                {
                    request.ValueRW.focus = targetEntity;
                    request.ValueRW.destination = targetData.CurrentCell;
                    request.ValueRW.startCoord = GridUtils.WorldToCellCoord(transform.ValueRO.Position, gridSettings.Origin);
            
                    var seed = (uint)(entity.Index + (uint)(currentTime * 1000));
                    var random = new Random(seed == 0 ? 1 : seed);
                    request.ValueRW.NextAllowedUpdateTime = (float)currentTime + 0.5f + random.NextFloat(0.0f, 1.0f);
            
                    status.ValueRW.Value = AgentStatus.Find;
                    requestsCount++; 
                }
            }
        }
    }
}