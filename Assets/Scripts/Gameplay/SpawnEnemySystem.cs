using PathfindingAStar;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Gameplay
{
    [BurstCompile]
    public partial struct SpawnEnemySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SpawnerConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var spawnerEntity = SystemAPI.GetSingletonEntity<SpawnerConfig>();
            var config = SystemAPI.GetComponent<SpawnerConfig>(spawnerEntity);
            
            var instances = state.EntityManager.Instantiate(config.Prefab, config.Count, Allocator.Temp);
            var random = new Random(123);
            
            foreach (var instance in instances)
            {
                var pos = random.NextFloat3(
                    new float3(-config.SpawnRadius, 0, -config.SpawnRadius), 
                    new float3(config.SpawnRadius, 0, config.SpawnRadius));
        
                SystemAPI.SetComponent(instance, LocalTransform.FromPosition(pos));
                SystemAPI.SetComponent(instance, new MoveSettings { speed = random.NextFloat(0.1f, 5f) });
        
                state.EntityManager.AddComponent<PathAgentStatusFindTag>(instance);
            }
            
        }
    }
}