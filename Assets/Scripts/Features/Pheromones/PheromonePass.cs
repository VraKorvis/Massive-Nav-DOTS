using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Features.Pheromones
{
    public class PheromonePass : ScriptableRenderPass
    {
        private readonly PheromoneFeature.PheromoneSettings _settings;

        private readonly ComputeShader _computeShader;
        private static readonly int PheromoneMapPropertyId = Shader.PropertyToID("_PheromoneMap");
        
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
                filterMode : FilterMode.Bilinear,
                name: "_PheromoneMap"
            );
            
            TextureHandle pheromoneTextureHandle = renderGraph.ImportTexture(_pheromoneMap);

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            var systemHandle = world.GetExistingSystem<PreparePheromoneBufferSystem>();
            if (systemHandle == SystemHandle.Null) return;

            var bufferRef = world.EntityManager.GetComponentData<PheromoneBufferReference>(systemHandle);
            if (bufferRef.GpuBuffer == null || bufferRef.ActualCount <= 0) return;

            bufferRef.GpuBuffer.SetData(bufferRef.CpuData, 0, 0, bufferRef.ActualCount);
            
            using var builder = renderGraph.AddComputePass("PheromoneUpdatePass", out PheromonePassData passData);
            
            passData.AgentPositions = bufferRef.GpuBuffer;
            passData.AgentCount = bufferRef.ActualCount;
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

                cmd.SetComputeFloatParam(pData.Compute, "_EvaporationSpeed", 0.1f); 
                cmd.SetComputeFloatParam(pData.Compute, "_DeltaTime", Time.deltaTime); 
                cmd.SetComputeIntParam(pData.Compute, "_AgentCount", pData.AgentCount);
                cmd.SetComputeVectorParam(pData.Compute, "_WorldParams", pData.WorldParams);
                
                int evaporateKernel = pData.Compute.FindKernel("Evaporate");
                cmd.SetComputeTextureParam(pData.Compute, evaporateKernel, "_PheromoneMap", pData.OutputTexture);
    
                int groups = Mathf.CeilToInt(pData.TexSize / 8.0f);
                cmd.DispatchCompute(pData.Compute, evaporateKernel, groups, groups, 1);
                
                int drawKernel = pData.Compute.FindKernel("DrawPheromones");

                cmd.SetComputeBufferParam(pData.Compute, drawKernel, "_AgentPositions", pData.AgentPositions);
                cmd.SetComputeTextureParam(pData.Compute, drawKernel, "_PheromoneMap", pData.OutputTexture);
                int threadGroups = Mathf.Max(1, Mathf.CeilToInt(pData.AgentCount / 64.0f));
                cmd.DispatchCompute(pData.Compute, drawKernel, threadGroups, 1, 1);

                cmd.SetGlobalTexture(PheromoneMapPropertyId, pData.OutputTexture);
            });
        }
        
        public void Cleanup()
        {
            _pheromoneMap?.Release();
            _pheromoneMap = null;
        }
    }
}