using Unity.Entities;
using Unity.Rendering;

namespace PFStar
{
    /// <summary>
    /// Provides a link between the ECS data and the GPU shader properties.
    /// This component is used by the BatchRendererGroup to send per-instance data to the GPU.
    /// Property: _AgentEffect (float)
    /// </summary>
    /// /// <remarks>
    /// Implementation details:
    /// - Reference name in Shader Graph: "_AgentEffect"
    /// - Property type: Float
    /// - "Hybrid Instancing" must be enabled in the shader property settings.
    /// </remarks>
    [MaterialProperty("_AgentEffect")] 
    public struct AgentVisualParams : IComponentData, IEnableableComponent
    {
        public float EffectValue;
        public int OrderInCell;
    }
}