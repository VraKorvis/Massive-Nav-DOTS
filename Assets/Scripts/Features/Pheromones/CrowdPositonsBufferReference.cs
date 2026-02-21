using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Features.Pheromones
{
    public class CrowdPositonsBufferReference : IComponentData, IDisposable
    {
        public GraphicsBuffer GpuBuffer;
        public NativeArray<float4> CpuData;
        public int ActualCount;

        public void Dispose()
        {
            GpuBuffer?.Release();
            if (CpuData.IsCreated) CpuData.Dispose();
        }
    }
}
