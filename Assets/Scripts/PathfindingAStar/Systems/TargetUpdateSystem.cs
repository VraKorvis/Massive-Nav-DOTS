// using Unity.Burst;
// using Unity.Entities;
// using Unity.Transforms;
// using Unity.Mathematics;
//
// namespace PathfindingAStar
// {
//     [UpdateInGroup(typeof(SimulationSystemGroup))]
//     [BurstCompile]
//     public partial struct TargetUpdateSystem : ISystem 
//     {
//         public void OnCreate(ref SystemState state)
//         {
//             state.RequireForUpdate<GridSettings>();
//             state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
//         }
//
//         public void OnUpdate(ref SystemState state) 
//         {
//             if (!SystemAPI.TryGetSingletonEntity<PathTargetTag>(out Entity targetEntity)) return;
//
//             // Используем системный ECB, чтобы не вызывать Playback вручную!
//             var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
//                 .CreateCommandBuffer(state.WorldUnmanaged);
//
//             float3 targetPos = SystemAPI.GetComponent<LocalTransform>(targetEntity).Position;
//             var gridSettings = SystemAPI.GetSingleton<GridSettings>();
//             int2 targetCoord = GridUtils.WorldToCellCoord(targetPos, gridSettings.Origin);
//
//             foreach (var (transform, request, status, entity) in 
//                      SystemAPI.Query<RefRO<LocalTransform>, RefRW<PathRequestAgent>, RefRW<PathAgentStatus>>()
//                          .WithEntityAccess()) 
//             {
//                 float3 currentPos = transform.ValueRO.Position;
//                 int2 currentCoord = GridUtils.WorldToCellCoord(currentPos, gridSettings.Origin);
//             
//                 // ПРОВЕРКА 1: Изменилась ли клетка агента?
//                 bool agentMoved = !currentCoord.Equals(request.ValueRO.startCoord);
//                 // ПРОВЕРКА 2: Изменилась ли клетка цели?
//                 bool targetMoved = !request.ValueRO.destination.Equals(targetCoord);
//
//                 if (agentMoved || targetMoved) 
//                 {
//                     // Обновляем данные только при реальном движении
//                     request.ValueRW.startCoord = currentCoord;
//                     request.ValueRW.destination = targetCoord;
//                 
//                     status.ValueRW.Value = AgentStatus.Find;
//         
//                     if (!state.EntityManager.HasComponent<PathAgentStatusFindTag>(entity))
//                     {
//                         ecb.AddComponent<PathAgentStatusFindTag>(entity);
//                     }
//                 }
//             }
//         }
//     }
// }