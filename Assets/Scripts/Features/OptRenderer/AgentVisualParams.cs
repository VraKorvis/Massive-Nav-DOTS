using System.Runtime.InteropServices;
using Unity.Entities;
using Unity.Rendering;

namespace Features.OptRenderer
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
    [StructLayout(LayoutKind.Sequential, Size = 4)]
    [MaterialProperty("_AgentEffect")] 
    public struct GpuAgentVisualParams : IComponentData
    {
        public float Value;
    }
}