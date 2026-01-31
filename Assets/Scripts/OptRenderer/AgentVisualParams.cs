using Unity.Entities;
using Unity.Rendering;

namespace PFStar
{
    /// <summary>
    /// The MaterialProperty attribute allows Unity to automatically pass the value of this field 
    /// to each agent's shader via GPU Instancing (DOTS Instancing).
    /// Provides per-instance data for shader: "SG_Ant_Animated".
    /// Property: _AgentEffect (float)
    /// </summary>
    [MaterialProperty("_AgentEffect")] 
    public struct AgentVisualParams : IComponentData, IEnableableComponent
    {
        public float EffectValue;
        public int OrderInCell;
    }
}