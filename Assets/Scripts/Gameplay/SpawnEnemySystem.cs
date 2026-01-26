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
            
            for (int i = 0; i < instances.Length; i++)
            {
                state.EntityManager.SetName(instances[i], $"Agent_{i}");
            }
            
            var random = new Random(123);
            
            foreach (var instance in instances)
            {
                var offset = random.NextFloat3(
                    new float3(-config.SpawnRadius, 1, -config.SpawnRadius), 
                    new float3(config.SpawnRadius, 1, config.SpawnRadius));
        
                var finalPos = config.SpawnPosition + offset;
                
                SystemAPI.SetComponent(instance, LocalTransform.FromPosition(finalPos));
                SystemAPI.SetComponent(instance, new MoveSettings { speed = random.NextFloat(1f, 5f) });
        
                state.EntityManager.AddComponent<PathAgentStatusFindTag>(instance);
            }
            state.Enabled = false;
            
        }
    }
}