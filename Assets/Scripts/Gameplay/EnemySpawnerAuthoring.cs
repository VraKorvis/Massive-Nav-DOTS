using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Gameplay
{
    public struct SpawnerConfig : IComponentData
    {
        public Entity Prefab;
        public int Count;
        public float SpawnRadius;
        public float3 SpawnPosition;
        public int BatchSize;
    }
    
    public class EnemySpawnerAuthoring : MonoBehaviour
    {
        public GameObject enemyPrefab;
        public int count = 50000;
        public int batchSize = 1000;
        public float spawnRadius = 10f;

        class Baker : Baker<EnemySpawnerAuthoring>
        {
            public override void Bake(EnemySpawnerAuthoring authoring)
            {
                var worldPosition = authoring.transform.position;
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new SpawnerConfig
                {
                    Prefab = GetEntity(authoring.enemyPrefab, TransformUsageFlags.Dynamic),
                    Count = authoring.count,
                    BatchSize = authoring.batchSize,
                    SpawnRadius = authoring.spawnRadius,
                    SpawnPosition = worldPosition
                });
            }
        }
    }
}