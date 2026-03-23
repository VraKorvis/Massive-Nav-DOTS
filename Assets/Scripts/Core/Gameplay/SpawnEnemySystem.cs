using Core.PathfindingAStar;
using Map.Grid;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Core.Gameplay
{
    [BurstCompile]
    public partial struct SpawnEnemySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
        }
        private int _spawnedCount;

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<SpawnerConfig>(out var config)) return;
            if (_spawnedCount >= config.Count) { state.Enabled = false; return; }

            var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            int toSpawn = math.min(config.BatchSize, config.Count - _spawnedCount);

            for (int i = 0; i < toSpawn; i++)
            {
                Entity e = ecb.Instantiate(config.Prefab);
                ecb.AddComponent<NewAgentTag>(e);
            }

            _spawnedCount += toSpawn;
        }
    }
    
}
