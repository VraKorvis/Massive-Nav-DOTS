using PFStar;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Gameplay
{
    [BurstCompile]
    public partial struct SpawnEnemySystem : ISystem
    {
        private ComponentLookup<PFRequestAgent> _requestLookup;
        private ComponentLookup<LocalTransform> _transformLookup;
        private ComponentLookup<MoveSettings> _moveSettingsLookup;
        private ComponentLookup<PFRequestMetadata> _metaLookup;
        
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NavigationTargetGridData>();
            state.RequireForUpdate<SpawnerConfig>();
            
            _requestLookup = state.GetComponentLookup<PFRequestAgent>(false);
            _transformLookup = state.GetComponentLookup<LocalTransform>(false);
            _moveSettingsLookup = state.GetComponentLookup<MoveSettings>(false);
            _metaLookup = state.GetComponentLookup<PFRequestMetadata>(false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<SpawnerConfig>();
            var targetEntity = SystemAPI.GetSingletonEntity<NavigationTargetGridData>();
    
            var instances = state.EntityManager.Instantiate(config.Prefab, config.Count, Allocator.TempJob);
            
            _requestLookup.Update(ref state);
            _transformLookup.Update(ref state);
            _moveSettingsLookup.Update(ref state);
            _metaLookup.Update(ref state);

            var setupJob = new SetupSpawnedAgentsJob
            {
                Entities = instances,
                TargetEntity = targetEntity,
                Config = config,
                CurrentTime = (float)SystemAPI.Time.ElapsedTime,
                PfRequestLookup = _requestLookup,
                TransformLookup = _transformLookup,
                MoveSettingsLookup = _moveSettingsLookup,
                MetaLookup = _metaLookup
            };

            state.Dependency = setupJob.Schedule(config.Count, 64, state.Dependency);
    
            instances.Dispose(state.Dependency);
    
            state.Enabled = false;
        }
    }

    [BurstCompile]
    public struct SetupSpawnedAgentsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Entity> Entities;
        public Entity TargetEntity;
        public SpawnerConfig Config;
        public float CurrentTime;

        [NativeDisableParallelForRestriction] public ComponentLookup<PFRequestAgent> PfRequestLookup;
        [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> TransformLookup;
        [NativeDisableParallelForRestriction] public ComponentLookup<MoveSettings> MoveSettingsLookup;
        [NativeDisableParallelForRestriction] public ComponentLookup<PFRequestMetadata> MetaLookup;

        public void Execute(int index)
        {
            var entity = Entities[index];
            var random = new Random((uint)(index + 1) * 0x9E3779B9); 

            var offset = random.NextFloat3(
                new float3(-Config.SpawnRadius, 0, -Config.SpawnRadius),
                new float3(Config.SpawnRadius, 0, Config.SpawnRadius));

            TransformLookup[entity] = LocalTransform.FromPosition(Config.SpawnPosition + offset);

            PfRequestLookup[entity] = new PFRequestAgent
            {
                Owner = entity,
                Focus = TargetEntity,
                StartCoord = int2.zero,
                Destination = int2.zero
            };

            MoveSettingsLookup[entity] = new MoveSettings { speed = random.NextFloat(3f, 8f) };
            MetaLookup[entity] = new PFRequestMetadata { RequestTime = CurrentTime };
        }
    }
}