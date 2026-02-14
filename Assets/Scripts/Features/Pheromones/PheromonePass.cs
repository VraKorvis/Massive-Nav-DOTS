using Core.Gameplay;
using Features.OptRenderer;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Features.Pheromones
{
    public class PheromonePass : ScriptableRenderPass
    {
        private readonly PheromoneFeature.PheromoneSettings _settings;
        private EntityQuery _agentQuery;

        private readonly ComputeShader _computeShader;
        private static readonly int PheromoneMapPropertyId = Shader.PropertyToID("_PheromoneMap");
        
        private GraphicsBuffer _buffer;
        private RTHandle _pheromoneMap;
        
        private readonly int _textureSize;
        
        public PheromonePass(PheromoneFeature.PheromoneSettings settings, ComputeShader compute, int textureSize)
        {
            _settings = settings;
            _computeShader = compute;
            _textureSize =  textureSize;
        }

        private class PheromonePassData
        {
            public GraphicsBuffer AgentPositions;
            public int AgentCount;
            public TextureHandle OutputTexture;
            public ComputeShader Compute;
            public Vector4 WorldParams;
            public int TexSize;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_computeShader == null) return;
            
            
            _pheromoneMap ??= RTHandles.Alloc(
                _textureSize, _textureSize,
                colorFormat: UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat,
                enableRandomWrite: true,
                name: "_PheromoneMap"
            );
            
            TextureHandle pheromoneTextureHandle = renderGraph.ImportTexture(_pheromoneMap);

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            if (_agentQuery == default) _agentQuery = world.EntityManager.CreateEntityQuery(typeof(DensityCullingData), typeof(MinionTag), typeof(LocalTransform));
            
            _agentQuery.CompleteDependency();

            int count = _agentQuery.CalculateEntityCount();

            int bufferCount = Mathf.Max(1, count); 
            if (_buffer == null || _buffer.count != bufferCount)
            {
                _buffer?.Release();
                _buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, bufferCount, sizeof(float) * 4);
            }
            
            if (count > 0)
            {
                var transforms = _agentQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                var cullingDatas = _agentQuery.ToComponentDataArray<DensityCullingData>(Allocator.Temp);
            
                NativeArray<float4> positions = new NativeArray<float4>(count, Allocator.Temp);

                for (int i = 0; i < count; i++)
                {
                    positions[i] = new float4(transforms[i].Position, cullingDatas[i].Visibility);
                }

                _buffer.SetData(positions);
            }
            
            using var builder = renderGraph.AddComputePass("PheromoneUpdatePass", out PheromonePassData passData);
            
            passData.AgentPositions = _buffer;
            passData.AgentCount = count;
            passData.OutputTexture = pheromoneTextureHandle;
            passData.Compute = _computeShader;
           
            float offsetX = _settings.WorldOffset.x - (_settings.WorldSize.x * 0.5f);
            float offsetZ = _settings.WorldOffset.z - (_settings.WorldSize.y * 0.5f);
            
            passData.WorldParams = new Vector4(_settings.WorldSize.x, _settings.WorldSize.y, offsetX, offsetZ);
            passData.TexSize = _settings.TextureSize;
            
            builder.UseTexture(pheromoneTextureHandle, AccessFlags.Write);

            builder.AllowGlobalStateModification(true);
            
            builder.SetRenderFunc<PheromonePassData>((pData, context) =>
            {
                var cmd = context.cmd;
                
                cmd.SetComputeIntParam(pData.Compute, "_TexSize", pData.TexSize);

                int evaporateKernel = pData.Compute.FindKernel("Evaporate");
                cmd.SetComputeFloatParam(pData.Compute, "_EvaporationSpeed", 0.001f); 
                
                cmd.SetComputeTextureParam(pData.Compute, evaporateKernel, "_PheromoneMap", pData.OutputTexture);
    
                int groups = Mathf.CeilToInt(pData.TexSize / 8.0f);
                cmd.DispatchCompute(pData.Compute, evaporateKernel, groups, groups, 1);
                
                int kernel = pData.Compute.FindKernel("DrawPheromones");

                cmd.SetComputeBufferParam(pData.Compute, kernel, "_AgentPositions", pData.AgentPositions);
                cmd.SetComputeIntParam(pData.Compute, "_AgentCount", pData.AgentCount);
                cmd.SetComputeVectorParam(pData.Compute, "_WorldParams", pData.WorldParams);
                
                cmd.SetComputeTextureParam(pData.Compute, kernel, "_PheromoneMap", pData.OutputTexture);
                
                int threadGroups = Mathf.Max(1, Mathf.CeilToInt(pData.AgentCount / 64.0f));
                cmd.DispatchCompute(pData.Compute, kernel, threadGroups, 1, 1);

                cmd.SetGlobalTexture(PheromoneMapPropertyId, pData.OutputTexture);
            });
            
        }
        
        public void Cleanup()
        {
            _buffer?.Release();
            _buffer = null;
            _pheromoneMap?.Release();
            _pheromoneMap = null;
        }
    }
}