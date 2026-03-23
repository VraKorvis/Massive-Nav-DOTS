using Core.PathfindingAStar;
using Map.Grid;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Core.Gameplay
{
    [BurstCompile]
    [UpdateAfter(typeof(SpawnEnemySystem))]
    public partial struct InitializeEnemySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SpawnerConfig>();
            state.RequireForUpdate<PlayerTag>();
            state.RequireForUpdate<GridBlobReference>();
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

            var gridRef = SystemAPI.GetSingleton<GridBlobReference>();
            var targetEntity = SystemAPI.GetSingletonEntity<PlayerTag>();
            var config = SystemAPI.GetSingleton<SpawnerConfig>();
            var time = (float)SystemAPI.Time.ElapsedTime;

            state.Dependency = new InitJob
            {
                Ecb = ecb,
                GridBlobRef = gridRef.Value,
                TargetEntity = targetEntity,
                Config = config,
                Time = time,
                Seed = (uint)(time * 1000) + 1
            }.ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    public partial struct InitJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter Ecb;
        [ReadOnly]
        public BlobAssetReference<GridBlob> GridBlobRef;
        public Entity TargetEntity;
        public SpawnerConfig Config;
        public float Time;
        public uint Seed;
        
        private void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndex, ref LocalTransform transform, ref MoveSettings move, ref PFRequestAgent request, ref PFRequestMetadata meta, in NewAgentTag tag)
        {
            var random = Random.CreateFromIndex(Seed + (uint)entity.Index);
            ref var gridBlob = ref GridBlobRef.Value;

            float3 pos;
            int safety = 0;
            do
            {
                float3 offset = random.NextFloat3(new float3(-Config.SpawnRadius, 0, -Config.SpawnRadius), new float3(Config.SpawnRadius, 0, Config.SpawnRadius));
                float3 p = Config.SpawnPosition + offset;
                pos = new float3(p.x, GridUtils.GetHeightBilinear(ref gridBlob, p), p.z);
                safety++;
            } while (CheckWall(pos, ref gridBlob) && safety < 1000);

            transform.Position = pos;

            move.Speed = random.NextFloat(3f, 10f);
            move.LookDir = new float3(0, 0, 1);

            request.Owner = entity;
            request.Focus = TargetEntity;

            meta.RequestTime = Time;

            Ecb.RemoveComponent<NewAgentTag>(chunkIndex, entity);
        }
        
        private bool CheckWall(float3 pos, ref GridBlob gridBlob)
        {
            return GridUtils.IsWallAtWorldPos(pos, ref gridBlob);
        }
    }
}