using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Features.Pheromones
{
    public class PheromoneFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class PheromoneSettings
        {
            public ComputeShader ComputeShader;

            public int TextureSize = 1024;
            public RenderPassEvent RenderEvent = RenderPassEvent.BeforeRenderingOpaques;
            
            //TODO SO data
            public Vector2 WorldSize = new Vector2(200, 200);
            public Vector3 WorldOffset = Vector3.zero;
        }

        public PheromoneSettings Settings = new PheromoneSettings();
        private PheromonePass _pheromonePass;

        public override void Create()
        {
            _pheromonePass = new PheromonePass(Settings, Settings.ComputeShader, Settings.TextureSize)
            {
                renderPassEvent = Settings.RenderEvent
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(_pheromonePass);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _pheromonePass?.Cleanup();
            }
            base.Dispose(disposing);
        }
    }
    
}