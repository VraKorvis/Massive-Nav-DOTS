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
    }
    
    public class EnemySpawnerAuthoring : MonoBehaviour
    {
        public GameObject enemyPrefab;
        public int count = 500;
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
                    SpawnRadius = authoring.spawnRadius,
                    SpawnPosition = worldPosition
                });
            }
        }
    }
}