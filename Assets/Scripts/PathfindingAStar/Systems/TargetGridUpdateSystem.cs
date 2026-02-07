using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace PFStar
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [BurstCompile]
    public partial struct TargetGridUpdateSystem : ISystem
    {
        private const int Threshold = 10;

        private EntityQuery _allAgentsQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<GridSettings>();
            _allAgentsQuery = state.GetEntityQuery(
                ComponentType.ReadWrite<PFAgentState>()
            );
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var grid = SystemAPI.GetSingleton<GridSettings>();
            bool anyTargetMoved = false;

            foreach (var (transform, targetData, entity) in
                     SystemAPI.Query<RefRO<LocalTransform>, RefRW<NavigationTargetGridData>>()
                         .WithEntityAccess())
            {
                int2 rawCoord = GridUtils.WorldToCellCoord(transform.ValueRO.Position, grid.Origin, grid.CellSize);

                int2 clampedCoord = math.clamp(rawCoord, 0, grid.Dimensions - 1);
                
                if (!clampedCoord.Equals(targetData.ValueRO.CurrentCell))
                {
                    targetData.ValueRW.CurrentCell = clampedCoord;

                    int distance = math.abs(clampedCoord.x - targetData.ValueRO.LastSignificantCell.x) +
                                   math.abs(clampedCoord.y - targetData.ValueRO.LastSignificantCell.y);

                    if (distance >= Threshold)
                    {
                        targetData.ValueRW.LastSignificantCell = clampedCoord;
                        SystemAPI.SetComponentEnabled<TargetChangedTag>(entity, true);
                        anyTargetMoved = true;
                    }
                    else
                    {
                        SystemAPI.SetComponentEnabled<TargetChangedTag>(entity, false);
                    }
                }
                else
                {
                    SystemAPI.SetComponentEnabled<TargetChangedTag>(entity, false);
                }
            }

            //todo temp duck tape, need logic
            if (anyTargetMoved)
            {
                state.Dependency = new SetSignificantMassiveJob().ScheduleParallel(_allAgentsQuery, state.Dependency);
            }
        }

        [BurstCompile]
        public partial struct SetSignificantMassiveJob : IJobEntity
        {
            void Execute(RefRW<PFAgentState> state)
            {
                var flags = state.ValueRO.Flags;
        
                flags &= (byte)~PFAgentStatus.Find;
                flags &= (byte)~PFAgentStatus.Process;
                flags |= (byte)PFAgentStatus.Significant;
        
                state.ValueRW.Flags = flags;
            }
        }
    }
}