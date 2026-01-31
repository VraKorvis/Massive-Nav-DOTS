using Unity.Entities;
using Unity.Rendering;

namespace OptRenderer
{
    /// <summary>
    /// Provides a link between the ECS data and the GPU shader properties.
    /// This component is used by the BatchRendererGroup to send per-instance data to the GPU.
    /// </summary>
    /// <remarks>
    /// Implementation details:
    /// - Reference name in Shader Graph: "_Visibility"
    /// - Property type: Float
    /// - "Hybrid Instancing" must be enabled in the shader property settings.
    /// </remarks>
    [MaterialProperty("_Visibility")]
    public struct VisibilityProperty : IComponentData
    {
        /// <summary>
        /// Represents the alpha/opacity value of the agent.
        /// 0.0 = completely transparent, 1.0 = fully opaque.
        /// Updated by DensityCullingSystem to provide smooth fade-in/fade-out effects.
        /// </summary>
        public float Value;
    }
}