#if UNITY_EDITOR
using UnityEngine;
using Unity.Mathematics;

namespace Map.Generation
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public class TerrainMeshGenerator : MonoBehaviour
    {
        [Header("Grid Sync")]
        public int2 MapSize = new int2(200, 200);
        public float CellSize = 1f;

        [Header("Noise Settings")]
        public float NoiseScale = 0.05f;
        public float HeightMultiplier = 5f;
        public uint Seed = 123;

        [ContextMenu("Generate Terrain")]
        public void Generate()
        {
            MeshFilter meshFilter = GetComponent<MeshFilter>();
            MeshCollider meshCollider = GetComponent<MeshCollider>();

            Mesh mesh = new Mesh
            {
                name = "ProceduralTerrain"
            };

            int resX = MapSize.x + 1;
            int resY = MapSize.y + 1;

            Vector3[] vertices = new Vector3[resX * resY];
            int[] triangles = new int[MapSize.x * MapSize.y * 6];
            Vector2[] uvs = new Vector2[vertices.Length];

            float3 offset = new float3(
                (MapSize.x) * CellSize * 0.5f,
                0,
                (MapSize.y) * CellSize * 0.5f
            );

            for (int y = 0; y < resY; y++)
            {
                for (int x = 0; x < resX; x++)
                {
                    int index = y * resX + x;

                    float n = noise.cnoise(new float2(x, y) * NoiseScale + Seed);
                    float h = n * HeightMultiplier;

                    vertices[index] = new Vector3(x * CellSize, h, y * CellSize) - (Vector3)offset;
                    uvs[index] = new Vector2((float)x / MapSize.x, (float)y / MapSize.y);
                }
            }

            int tri = 0;
            for (int y = 0; y < MapSize.y; y++)
            {
                for (int x = 0; x < MapSize.x; x++)
                {
                    int root = y * resX + x;
                    triangles[tri + 0] = root;
                    triangles[tri + 1] = root + resX;
                    triangles[tri + 2] = root + 1;
                    triangles[tri + 3] = root + 1;
                    triangles[tri + 4] = root + resX;
                    triangles[tri + 5] = root + resX + 1;
                    tri += 6;
                }
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            meshFilter.sharedMesh = mesh;
            meshCollider.sharedMesh = mesh;

            meshFilter.mesh = mesh;

            GetComponent<MeshCollider>().sharedMesh = mesh;

            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.EditorUtility.SetDirty(meshCollider);
            Debug.Log("Terrain Generated!");
        }

        [ContextMenu("Save Mesh As Asset")]
        public void SaveMesh()
        {
            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf.sharedMesh == null) return;

            if (!System.IO.Directory.Exists("Assets/GeneratedMeshes"))
                UnityEditor.AssetDatabase.CreateFolder("Assets", "GeneratedMeshes");

            string path = "Assets/GeneratedMeshes/TerrainMesh.asset";
            UnityEditor.AssetDatabase.CreateAsset(mf.sharedMesh, path);
            UnityEditor.AssetDatabase.SaveAssets();
            Debug.Log("Mesh saved to " + path);
        }
    }
}
#endif