using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Features.CrowdShadows
{
    public class CrowdShadowsFeature : ScriptableRendererFeature
    {
        private CrowdShadowPass _crowdShadowPass;
        public CrowdShadowData Settings;
        
        private GraphicsBuffer _shadowCountBuffer;
        private RTHandle _shadowFloatRT;
        private RTHandle _tmpBlurRT;
        
        [Serializable]
        public class CrowdShadowData
        {
            public int TextureSize = 512;
            public ComputeShader ComputeShader;
            public RenderPassEvent RenderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
            public Vector2 WorldSize = new Vector2(200, 200);
            public Vector3 WorldOffset = Vector3.zero;
            public float DefaultNormalizeScale = 0.3f;
            public int BlurRadius = 5;
        }
        
        public override void Create()
        {
            DisposeResources();

            if (Settings.ComputeShader == null) return;

            int bufferSize = Settings.TextureSize * Settings.TextureSize;
        
            _shadowCountBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, bufferSize, sizeof(uint));
        
            _shadowFloatRT = RTHandles.Alloc(
                Settings.TextureSize, Settings.TextureSize,
                colorFormat: GraphicsFormat.R16_SFloat,
                enableRandomWrite: true,
                filterMode: FilterMode.Bilinear,
                name: "_CrowdShadowFloat");

            _tmpBlurRT = RTHandles.Alloc(
                Settings.TextureSize, Settings.TextureSize,
                colorFormat: GraphicsFormat.R16_SFloat,
                enableRandomWrite: true,
                filterMode: FilterMode.Bilinear,
                name: "_CrowdShadowBlurTmp");

            _crowdShadowPass = new CrowdShadowPass(Settings, _shadowCountBuffer, _shadowFloatRT, _tmpBlurRT)
            {
                renderPassEvent = Settings.RenderPassEvent
            };
        }
        
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(_crowdShadowPass);
        }

        protected override void Dispose(bool disposing)
        {
            DisposeResources();
            base.Dispose(disposing);
        }
        
        private void DisposeResources()
        {
            _shadowCountBuffer?.Release();
            _shadowCountBuffer = null;
            _shadowFloatRT?.Release();
            _shadowFloatRT = null;
            _tmpBlurRT?.Release();
            _tmpBlurRT = null;
        }
    }
}
