using Core.Gameplay;
using Features.OptRenderer;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Features.Pheromones
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct PrepareCrowdPositionsBufferSystem : ISystem
    {
        private EntityQuery _agentQuery;

        public void OnCreate(ref SystemState state)
        {
            _agentQuery = state.GetEntityQuery(typeof(LocalTransform), typeof(DensityCullingData), typeof(MinionTag));
            state.EntityManager.AddComponentData(state.SystemHandle, new CrowdPositonsBufferReference());
        }

        public void OnUpdate(ref SystemState state)
        {
            var bufferRef = state.EntityManager.GetComponentData<CrowdPositonsBufferReference>(state.SystemHandle);
            int count = _agentQuery.CalculateEntityCount();
            if (count == 0) return;

            UpdateInternalBuffers(count, bufferRef);

            var fillPheromoneBufferJobHandle = new FillPheromoneBufferJob
            {
                Positions = bufferRef.CpuData
            }.ScheduleParallel(_agentQuery, state.Dependency);
            
            fillPheromoneBufferJobHandle.Complete();
            bufferRef.GpuBuffer.SetData(bufferRef.CpuData, 0, 0, count);
            bufferRef.ActualCount = count;
            state.EntityManager.SetComponentData(state.SystemHandle, bufferRef);
            state.Dependency =  fillPheromoneBufferJobHandle;
            
        }

        [BurstDiscard]
        private void UpdateInternalBuffers(int count, CrowdPositonsBufferReference bufferRef)
        {
            if (!bufferRef.CpuData.IsCreated || bufferRef.CpuData.Length < count)
            {
                bufferRef.Dispose();
                int capacity = Mathf.NextPowerOfTwo(count);
                bufferRef.CpuData = new NativeArray<float4>(capacity, Allocator.Persistent);
                bufferRef.GpuBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(float) * 4);
            }
        }
        
        [BurstCompile]
        public partial struct FillPheromoneBufferJob : IJobEntity
        {
            [WriteOnly]
            [NativeDisableParallelForRestriction]
            public NativeArray<float4> Positions;

            private void Execute([EntityIndexInQuery] int index, in LocalTransform transform, in DensityCullingData culling)
            {
                Positions[index] = new float4(transform.Position, culling.Visibility);
            }
        }

        public void OnDestroy(ref SystemState state)
        {
            if (state.EntityManager.HasComponent<CrowdPositonsBufferReference>(state.SystemHandle))
            {
                var bufferRef = state.EntityManager.GetComponentData<CrowdPositonsBufferReference>(state.SystemHandle);
                bufferRef.Dispose();
                state.EntityManager.RemoveComponent<CrowdPositonsBufferReference>(state.SystemHandle);
            }
        }
    }
}