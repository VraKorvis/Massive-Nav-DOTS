using Unity.Entities;
using Unity.Collections;
using UnityEngine;

namespace Map
{
    public class GridDataHolder : MonoBehaviour
    {
        public GridDataAsset dataAsset;

        public class MapGridBaker : Baker<GridDataHolder>
        {
            public override void Bake(GridDataHolder authoring)
            {
                if (authoring.dataAsset == null || !authoring.dataAsset.hasData) return;

                var entity = GetEntity(TransformUsageFlags.None);

                using var builder = new BlobBuilder(Allocator.Temp);
                ref GridBlob root = ref builder.ConstructRoot<GridBlob>();

                root.Dimensions = authoring.dataAsset.Dimensions;
                root.CellSize = authoring.dataAsset.CellSize;
                root.Origin = authoring.dataAsset.Origin;

                int total = authoring.dataAsset.Heights.Length;
                
                // var bH = builder.Allocate(ref root.Heights, total);
                // var bN = builder.Allocate(ref root.Normals, total);
                // var bC = builder.Allocate(ref root.CellsType, total);
                // var bW = builder.Allocate(ref root.Weights, total);
                //
                //
                // unsafe 
                // {
                //     fixed (float* src = authoring.dataAsset.Heights)
                //         UnsafeUtility.MemCpy(bH.GetUnsafePtr(), src, total * sizeof(float));
                //
                //     fixed (float3* src = authoring.dataAsset.Normals)
                //         UnsafeUtility.MemCpy(bN.GetUnsafePtr(), src, total * sizeof(float3));
                //
                //     fixed (CellType* src = authoring.dataAsset.CellsType)
                //         UnsafeUtility.MemCpy(bC.GetUnsafePtr(), src, total * sizeof(CellType));
                //
                //     fixed (float* src = authoring.dataAsset.Weights)
                //         UnsafeUtility.MemCpy(bW.GetUnsafePtr(), src, total * sizeof(float));
                // }
                //
                // var blobRef = builder.CreateBlobAssetReference<GridBlob>(Allocator.Persistent);
                // AddBlobAsset(ref blobRef, out var hash);
                //
                // AddComponent(entity, new GridTag());
                // AddComponent(entity, new GridSettings
                // {
                //     Dimensions = authoring.dataAsset.Dimensions,
                //     Origin = authoring.dataAsset.Origin,
                //     CellSize = authoring.dataAsset.CellSize
                // });
                //
                // AddComponent(entity, new GridBlobReference { Value = blobRef });
            }
        }
    }
}