using Unity.Entities;
using Unity.Mathematics;

namespace Core.Input
{
    public struct RawInputFrame : IComponentData
    {
        public bool   ClickThisFrame;   
        public float3 ClickWorldPos;    
        public bool   ClickWorldValid; 
 
        public bool   RightHeld;        
        public float2 OrbitDelta;       
 
        public float  ScrollDelta;
    }
}