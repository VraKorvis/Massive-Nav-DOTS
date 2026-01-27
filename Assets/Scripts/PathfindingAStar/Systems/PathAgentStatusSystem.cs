// using Unity.Burst;
// using Unity.Entities;
//
// namespace PathfindingAStar
// {
//     [UpdateInGroup(typeof(SimulationSystemGroup))]
//     [UpdateAfter(typeof(PathRequestUpdateSystem))]
//     [BurstCompile]
//     public partial struct PathAgentStatusSystem : ISystem
//     {
//         public void OnCreate(ref SystemState state)
//         {
//             state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
//         }
//
//         [BurstCompile]
//         public void OnUpdate(ref SystemState state)
//         {
//             var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
//                 .CreateCommandBuffer(state.WorldUnmanaged);
//             
//             foreach (var (status, entity) in SystemAPI.Query<RefRO<PathAgentStatus>>()
//                          .WithChangeFilter<PathAgentStatus>()
//                          .WithEntityAccess())
//             {
//                 switch (status.ValueRO.Value)
//                 {
//                     case AgentStatus.Find:
//                         ecb.AddComponent<PathAgentStatusFindTag>(entity);
//                         ecb.RemoveComponent<PathAgentStatusProcessTag>(entity);
//                         ecb.RemoveComponent<PathAgentStatusNoneTag>(entity);
//                         break;
//                     case AgentStatus.Process:
//                         ecb.AddComponent<PathAgentStatusProcessTag>(entity);
//                         ecb.RemoveComponent<PathAgentStatusFindTag>(entity);
//                         ecb.RemoveComponent<PathAgentStatusNoneTag>(entity);
//                         break;
//                     case AgentStatus.None:
//                         ecb.AddComponent<PathAgentStatusNoneTag>(entity);
//                         ecb.RemoveComponent<PathAgentStatusFindTag>(entity);
//                         ecb.RemoveComponent<PathAgentStatusProcessTag>(entity);
//                         break;
//                 }
//             }
//         }
//     }
// }