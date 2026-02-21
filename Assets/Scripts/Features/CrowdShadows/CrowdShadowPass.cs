using Features.Pheromones;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Features.CrowdShadows
{
    public class CrowdShadowPass : ScriptableRenderPass
    {

        private CrowdShadowsFeature.CrowdShadowData _settings;

        private ComputeShader _computeShader;

        private GraphicsBuffer _shadowCountBuffer;
        private RTHandle _shadowFloatRT;
        private RTHandle _tmpBlurRT;

        private static readonly int ShadowMapPropertyId = Shader.PropertyToID("_CrowdShadowMap");

        private const int kAgentThreads = 64;
        private const int kTileThreadXY = 8;
        private float _defaultNormalizeScale = 0.0000001f; //1.0f / 8.0f;
        private int _blurRadius = 2;

        private readonly int _textureSize;

        private class ShadowPassData
        {
            public GraphicsBuffer AgentPositions;
            public int AgentCount;
            public BufferHandle ShadowCountBuffer;
            public TextureHandle FloatTexture;
            public TextureHandle TempTexture;
            public ComputeShader Compute;
            public Vector4 WorldParams;
            public int TexSize;
            public float NormalizeScale;
            public int BlurRadius;
        }

        public CrowdShadowPass(CrowdShadowsFeature.CrowdShadowData settings, GraphicsBuffer shadowCountBuffer, RTHandle shadowFloatRT, RTHandle tmpBlurRT)
        {
            _settings = settings;
            _shadowCountBuffer = shadowCountBuffer;
            _computeShader = settings.ComputeShader;
            _shadowFloatRT = shadowFloatRT;
            _tmpBlurRT = tmpBlurRT;
            _textureSize = settings.TextureSize;
            _defaultNormalizeScale = settings.DefaultNormalizeScale;
            _blurRadius = settings.BlurRadius;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_computeShader == null) return;

            var world = World.DefaultGameObjectInjectionWorld;
            GraphicsBuffer agentBuffer = null;
            int agentCount = 0;
            
            if (world != null)
            {
                var systemHandle = world.GetExistingSystem<PrepareCrowdPositionsBufferSystem>();
                if (systemHandle != SystemHandle.Null && world.EntityManager.HasComponent<CrowdPositonsBufferReference>(systemHandle))
                {
                    var bufferRef = world.EntityManager.GetComponentData<CrowdPositonsBufferReference>(systemHandle);
                    agentBuffer = bufferRef.GpuBuffer;
                    agentCount = bufferRef.ActualCount;
                }
            }

            BufferHandle countShadowBufferHandle = renderGraph.ImportBuffer(_shadowCountBuffer);
            TextureHandle floatHandle = renderGraph.ImportTexture(_shadowFloatRT);
            TextureHandle tmpHandle = renderGraph.ImportTexture(_tmpBlurRT);

            BufferHandle agentPosHandle = (agentBuffer != null && agentBuffer.IsValid())
                ? renderGraph.ImportBuffer(agentBuffer)
                : default;

            float offsetX = _settings.WorldOffset.x - (_settings.WorldSize.x * 0.5f);
            float offsetZ = _settings.WorldOffset.z - (_settings.WorldSize.y * 0.5f);
            Vector4 worldParams = new Vector4(_settings.WorldSize.x, _settings.WorldSize.y, offsetX, offsetZ);

            // PASS A: Clear
            using (var clear = renderGraph.AddComputePass("CrowdShadows_ClearCount", out ShadowPassData clearData))
            {
                clearData.TexSize = _textureSize;
                clearData.Compute = _computeShader;
                clearData.ShadowCountBuffer = countShadowBufferHandle;

                clear.UseBuffer(clearData.ShadowCountBuffer, AccessFlags.Write);

                clear.SetRenderFunc<ShadowPassData>((p, ctx) =>
                {
                    int kernel = p.Compute.FindKernel("ClearCounts");
                    ctx.cmd.SetComputeIntParam(p.Compute, "_TexSize", p.TexSize);
                    ctx.cmd.SetComputeBufferParam(p.Compute, kernel, "_CrowdShadowBuffer", p.ShadowCountBuffer);
                    int groups = Mathf.CeilToInt(p.TexSize / (float)kTileThreadXY);
                    ctx.cmd.DispatchCompute(p.Compute, kernel, groups, groups, 1);
                });
            }

            // PASS B: Accumulate
            using (var accum = renderGraph.AddComputePass("CrowdShadows_Accumulate", out ShadowPassData accumData))
            {
                accumData.AgentPositions = agentBuffer;
                accumData.AgentCount = agentCount;
                accumData.ShadowCountBuffer = countShadowBufferHandle;
                accumData.Compute = _computeShader;
                accumData.WorldParams = worldParams;
                accumData.TexSize = _textureSize;

                if (agentPosHandle.IsValid())
                {
                    accum.UseBuffer(agentPosHandle, AccessFlags.Read);
                }
                
                accum.UseBuffer(accumData.ShadowCountBuffer, AccessFlags.ReadWrite);

                accum.SetRenderFunc<ShadowPassData>((p, ctx) =>
                {
                    if (p.AgentCount <= 0 || p.AgentPositions == null || !p.AgentPositions.IsValid()) return;

                    int kernel = p.Compute.FindKernel("AccumulateCounts");
                    ctx.cmd.SetComputeIntParam(p.Compute, "_AgentCount", p.AgentCount);
                    ctx.cmd.SetComputeIntParam(p.Compute, "_TexSize", p.TexSize);
                    ctx.cmd.SetComputeVectorParam(p.Compute, "_WorldParams", p.WorldParams);
                    ctx.cmd.SetComputeBufferParam(p.Compute, kernel, "_AgentPositions", p.AgentPositions);
                    ctx.cmd.SetComputeBufferParam(p.Compute, kernel, "_CrowdShadowBuffer", p.ShadowCountBuffer);
                    int groups = Mathf.CeilToInt(p.AgentCount / (float)kAgentThreads);
                    ctx.cmd.DispatchCompute(p.Compute, kernel, groups, 1, 1);
                });
            }

            // PASS C: Normalize
            using (var norm = renderGraph.AddComputePass("CrowdShadows_Normalize", out ShadowPassData normData))
            {
                normData.ShadowCountBuffer = countShadowBufferHandle;
                normData.FloatTexture = floatHandle;
                normData.Compute = _computeShader;
                normData.NormalizeScale = _defaultNormalizeScale;
                normData.TexSize = _textureSize;

                norm.UseBuffer(normData.ShadowCountBuffer, AccessFlags.Read);
                norm.UseTexture(normData.FloatTexture, AccessFlags.Write);

                norm.SetRenderFunc<ShadowPassData>((p, ctx) =>
                {
                    int kernel = p.Compute.FindKernel("NormalizeCounts");
                    ctx.cmd.SetComputeIntParam(p.Compute, "_TexSize", p.TexSize);
                    ctx.cmd.SetComputeFloatParam(p.Compute, "_NormalizeScale", p.NormalizeScale);
                    ctx.cmd.SetComputeBufferParam(p.Compute, kernel, "_CrowdShadowBuffer", p.ShadowCountBuffer);
                    ctx.cmd.SetComputeTextureParam(p.Compute, kernel, "_CrowdShadowFloat", p.FloatTexture);
                    int groups = Mathf.CeilToInt(p.TexSize / (float)kTileThreadXY);
                    ctx.cmd.DispatchCompute(p.Compute, kernel, groups, groups, 1);
                });
            }

            // PASS D: BlurH
            using (var blurH = renderGraph.AddComputePass("CrowdShadows_BlurH", out ShadowPassData blurHData))
            {
                blurHData.FloatTexture = floatHandle;
                blurHData.TempTexture = tmpHandle;
                blurHData.Compute = _computeShader;
                blurHData.BlurRadius = _blurRadius;
                blurHData.TexSize = _textureSize;

                blurH.UseTexture(blurHData.FloatTexture, AccessFlags.Read);
                blurH.UseTexture(blurHData.TempTexture, AccessFlags.Write);

                blurH.SetRenderFunc<ShadowPassData>((p, ctx) =>
                {
                    int kernel = p.Compute.FindKernel("BlurH");
                    ctx.cmd.SetComputeIntParam(p.Compute, "_TexSize", p.TexSize);
                    ctx.cmd.SetComputeIntParam(p.Compute, "_BlurRadius", p.BlurRadius);
                    ctx.cmd.SetComputeTextureParam(p.Compute, kernel, "_InputFloat", p.FloatTexture);
                    ctx.cmd.SetComputeTextureParam(p.Compute, kernel, "_OutputFloat", p.TempTexture);
                    int groups = Mathf.CeilToInt(p.TexSize / (float)kTileThreadXY);
                    ctx.cmd.DispatchCompute(p.Compute, kernel, groups, groups, 1);
                });
            }

            // PASS E: BlurV
            using (var blurV = renderGraph.AddComputePass("CrowdShadows_BlurV", out ShadowPassData blurVData))
            {
                blurVData.TempTexture = tmpHandle;
                blurVData.FloatTexture = floatHandle;
                blurVData.Compute = _computeShader;
                blurVData.BlurRadius = _blurRadius;
                blurVData.TexSize = _textureSize;

                blurV.UseTexture(blurVData.TempTexture, AccessFlags.Read);
                blurV.UseTexture(blurVData.FloatTexture, AccessFlags.Write);
                blurV.AllowGlobalStateModification(true);

                blurV.SetRenderFunc<ShadowPassData>((p, ctx) =>
                {
                    int kernel = p.Compute.FindKernel("BlurV");
                    ctx.cmd.SetComputeIntParam(p.Compute, "_TexSize", p.TexSize);
                    ctx.cmd.SetComputeIntParam(p.Compute, "_BlurRadius", p.BlurRadius);
                    ctx.cmd.SetComputeTextureParam(p.Compute, kernel, "_InputFloat", p.TempTexture);
                    ctx.cmd.SetComputeTextureParam(p.Compute, kernel, "_OutputFloat", p.FloatTexture);
                    int groups = Mathf.CeilToInt(p.TexSize / (float)kTileThreadXY);
                    ctx.cmd.DispatchCompute(p.Compute, kernel, groups, groups, 1);

                    ctx.cmd.SetGlobalTexture(ShadowMapPropertyId, p.FloatTexture);
                });
            }
        }
    }
}
