using System;
using UnityEngine;
using Unity.Mathematics;
using UnityEditor;
using Object = UnityEngine.Object;

namespace Map
{
    public class MapEditorGenerator : MonoBehaviour
    {
        [Header("Assets")] public GameObject[] rockPrefabs;
        public Transform parentFolder;

        [Header("Settings")] public int2 mapSize = new int2(99, 99);
        public float cellSize = 1.5f;
        public float noiseScale = 0.1f;
        [Range(0, 1)] public float threshold = 0.5f;
        public uint seed = 123;

        [ContextMenu("Generate Map")]
        public void Generate()
        {
            Clear();

            if (parentFolder == null)
            {
                Debug.LogError($"parentFolder not assigned");
                return;
            }
            
            int obstacleLayer = LayerMask.NameToLayer("Env_static");
            
            var rand = new Unity.Mathematics.Random(seed);
            Vector3 startPos = transform.position -
                               new Vector3(mapSize.x * 0.5f * cellSize, 0, mapSize.y * 0.5f * cellSize);

            for (int x = 0; x < mapSize.x; x++)
            {
                for (int y = 0; y < mapSize.y; y++)
                {
                    float n = noise.cnoise(new float2(x, y) * noiseScale);

                    if (n > threshold)
                    {
                        GameObject prefab = rockPrefabs[rand.NextInt(0, rockPrefabs.Length)];

                        Vector3 pos = startPos + new Vector3(x * cellSize, rand.NextFloat(0.5f, 1f), y * cellSize);

                        GameObject instance = Instantiate(prefab, pos, Quaternion.Euler(0, rand.NextFloat(0, 360), 0));

                        instance.transform.SetParent(parentFolder);

                        instance.transform.localScale = Vector3.one * rand.NextFloat(0.7f, 1.3f);
                        
                        instance.layer = obstacleLayer;
                        foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
                        {
                            child.gameObject.layer = obstacleLayer;
                        }
                    }
                }
            }
            Debug.Log($"Generated {parentFolder.childCount} rocks.");
        }
        
        [ContextMenu("Show All Rocks")]
        public void ShowAll()
        {
            foreach (Transform child in parentFolder)
            {
                child.gameObject.hideFlags = HideFlags.None;
            }
            EditorApplication.DirtyHierarchyWindowSorting();
        }
        
        [ContextMenu("Hide All Rocks")]
        public void HideAll()
        {
            foreach (Transform child in parentFolder)
            {
                child.gameObject.hideFlags = HideFlags.HideInHierarchy;
            }
            EditorApplication.DirtyHierarchyWindowSorting();
        }

        [ContextMenu("Clear Map")]
        public void Clear()
        {
            if (parentFolder == null) return;

            for (int i = parentFolder.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(parentFolder.GetChild(i).gameObject);
            }
        }
        
        [ContextMenu("Force Clear Selection")]
        public void ForceClear()
        {
            Selection.activeGameObject = null;
            Selection.objects = Array.Empty<Object>();
        }
    }
}