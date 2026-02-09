using Gameplay.Player;
using Map;
using PFStar;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.VisualScripting;

namespace Gameplay
{
    [BurstCompile]
    public partial struct SpawnEnemySystem : ISystem
    {
        private NativeList<Entity> _spawnedEntities;
        
        private ComponentLookup<PFRequestAgent> _requestLookup;
        private ComponentLookup<LocalTransform> _transformLookup;
        private ComponentLookup<MoveSettings> _moveSettingsLookup;
        private ComponentLookup<PFRequestMetadata> _metaLookup;
        
        private int _spawnedCount;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerTag>();
            state.RequireForUpdate<SpawnerConfig>();
            
            _spawnedEntities = new NativeList<Entity>(Allocator.Persistent);

            _requestLookup = state.GetComponentLookup<PFRequestAgent>(false);
            _transformLookup = state.GetComponentLookup<LocalTransform>(false);
            _moveSettingsLookup = state.GetComponentLookup<MoveSettings>(false);
            _metaLookup = state.GetComponentLookup<PFRequestMetadata>(false);
            state.RequireForUpdate<GridBlobReference>();

            _spawnedCount = 0;
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (_spawnedEntities.IsCreated)
            {
                _spawnedEntities.Dispose();
            }
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingletonEntity<PlayerTag>(out var targetEntity)) return;

            if (!SystemAPI.TryGetSingleton<GridBlobReference>(out var gridRef)) return;

            if (!SystemAPI.TryGetSingleton<SpawnerConfig>(out var config)) return;

            if (_spawnedCount >= config.Count)
            {
                if (_spawnedEntities.IsCreated)
                {
                    _spawnedEntities.Dispose(state.Dependency);
                }
                state.Enabled = false;
                return;
            }
            
            var gridBlobRef = gridRef.Value;
            
            _requestLookup.Update(ref state);
            _transformLookup.Update(ref state);
            _moveSettingsLookup.Update(ref state);
            _metaLookup.Update(ref state);
            
            int toSpawn = math.min(config.BatchSize, config.Count - _spawnedCount);
            _spawnedEntities.Clear();
            _spawnedEntities.ResizeUninitialized(toSpawn);
            
            state.EntityManager.Instantiate(config.Prefab, _spawnedEntities.AsArray());
            
            var setupJobHandle = new SetupSpawnedAgentsJob
            {
                GridBlobRef = gridBlobRef,
                Entities = _spawnedEntities.AsDeferredJobArray(),
                TargetEntity = targetEntity,
                Config = config,
                StartIndex = _spawnedCount,
                CurrentTime = (float)SystemAPI.Time.ElapsedTime,
                PfRequestLookup = _requestLookup,
                TransformLookup = _transformLookup,
                MoveSettingsLookup = _moveSettingsLookup,
                MetaLookup = _metaLookup
            }.Schedule(toSpawn, 64, state.Dependency);

            _spawnedCount += toSpawn;

            state.Dependency = setupJobHandle;
        }
    }

    [BurstCompile]
    public struct SetupSpawnedAgentsJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<Entity> Entities;
        public Entity TargetEntity;
        public SpawnerConfig Config;
        public float CurrentTime;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<PFRequestAgent> PfRequestLookup;
        [NativeDisableParallelForRestriction]
        public ComponentLookup<LocalTransform> TransformLookup;
        [NativeDisableParallelForRestriction]
        public ComponentLookup<MoveSettings> MoveSettingsLookup;
        [NativeDisableParallelForRestriction]
        public ComponentLookup<PFRequestMetadata> MetaLookup;
        
        [ReadOnly] public BlobAssetReference<GridBlob> GridBlobRef;
        
        public int StartIndex;
        
        public void Execute(int index)
        {
            ref var gridBlob = ref GridBlobRef.Value;
            var entity = Entities[index];
            var random = Random.CreateFromIndex((uint)(StartIndex + index));
            
            var offset = random.NextFloat3(
                new float3(-Config.SpawnRadius, 0, -Config.SpawnRadius),
                new float3(Config.SpawnRadius, 0, Config.SpawnRadius));

            var spawnPos = Config.SpawnPosition + offset;
            float spawnHeight = GridUtils.GetHeightBilinear(ref gridBlob, spawnPos);
            float3 finalSpawnPos = new float3(spawnPos.x, spawnHeight, spawnPos.z);

            TransformLookup[entity] = LocalTransform.FromPosition(finalSpawnPos);

            PfRequestLookup[entity] = new PFRequestAgent
            {
                Owner = entity,
                Focus = TargetEntity,
                StartCoord = int2.zero,
                Destination = int2.zero
            };

            MoveSettingsLookup[entity] = new MoveSettings
            {
                Speed = random.NextFloat(2f, 7f)
            };
            
            MetaLookup[entity] = new PFRequestMetadata
            {
                RequestTime = CurrentTime,
                Priority = 0
            };
        }
    }
    
    
}
