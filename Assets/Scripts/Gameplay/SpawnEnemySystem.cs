using PFStar;
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
            state.RequireForUpdate<NavigationTargetGridData>();
            state.RequireForUpdate<SpawnerConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var spawnerEntity = SystemAPI.GetSingletonEntity<SpawnerConfig>();
            var targetEntity = SystemAPI.GetSingletonEntity<NavigationTargetGridData>();
            var config = SystemAPI.GetComponent<SpawnerConfig>(spawnerEntity);
            
            var instances = state.EntityManager.Instantiate(config.Prefab, config.Count, Allocator.Temp);
            
            var random = new Random(123);
            var currentTime = SystemAPI.Time.ElapsedTime;
            
            foreach (var instance in instances)
            {
                var offset = random.NextFloat3(
                    new float3(-config.SpawnRadius, 0, -config.SpawnRadius), 
                    new float3(config.SpawnRadius, 0, config.SpawnRadius));
        
                var finalPos = config.SpawnPosition + offset;
                
                state.EntityManager.SetComponentData(instance, new PFRequestAgent
                {
                    Owner = instance,
                    Focus = targetEntity, 
                    StartCoord = int2.zero,
                    Destination = int2.zero
                });
                
                state.EntityManager.SetComponentData(instance, LocalTransform.FromPosition(finalPos));
                state.EntityManager.SetComponentData(instance, new MoveSettings { speed = random.NextFloat(2f, 7f) });
                state.EntityManager.SetComponentData(instance, new PFRequestMetadata { RequestTime = currentTime });
                
            
            }
            state.Enabled = false;
            
        }
    }
}