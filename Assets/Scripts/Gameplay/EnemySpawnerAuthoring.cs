using Unity.Entities;
using UnityEngine;

namespace Gameplay
{
    public struct SpawnerConfig : IComponentData
    {
        public Entity Prefab;
        public int Count;
        public float SpawnRadius;
    }
    
    public class EnemySpawnerAuthoring : MonoBehaviour
    {
        public GameObject enemyPrefab;
        public int count = 1000;
        public float spawnRadius = 20f;

        class Baker : Baker<EnemySpawnerAuthoring>
        {
            public override void Bake(EnemySpawnerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new SpawnerConfig
                {
                    Prefab = GetEntity(authoring.enemyPrefab, TransformUsageFlags.Dynamic),
                    Count = authoring.count,
                    SpawnRadius = authoring.spawnRadius
                });
            }
        }
    }
}