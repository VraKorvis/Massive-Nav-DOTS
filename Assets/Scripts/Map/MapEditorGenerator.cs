using Core;
using UnityEngine;
using Unity.Mathematics;
using UnityEditor;
using PFStar;
using Unity.Entities;
using UnityEditor.SceneManagement;

namespace Map
{
    public struct GridBlob
    {
        public int2 Dimensions;
        public float3 Origin;
        public float CellSize;

        public BlobArray<CellType> CellsType;
        public BlobArray<float> Weights;

        public BlobArray<float> Heights;
        public BlobArray<float3> Normals;
        public BlobArray<float3> WallPushField;
    }

    public struct GridBlobReference : IComponentData
    {
        public BlobAssetReference<GridBlob> Value;
    }

    public class MapEditorGenerator : MonoBehaviour
    {
        public GridDataAsset dataAsset;

        [Header("Assets")]
        public GameObject[] rockPrefabs;
        public Transform parentFolder;

        [Header("Generate Settings")]
        public int2 mapSize = new int2(199, 199);
        public float cellSize = 1f;
        public float noiseScale = 0.1f;
        [Range(0, 1)]
        public float threshold = 0.5f;
        [Range(0, 2)]
        public float verticalOffset = 0.5f;
        public bool hasInflation = false;
        public float inflationMultiplier = 1f;
        public float inflationRadius = 1.0f;
        public float wallAvoidanceRange = 1.2f;
        
        public uint seed = 123;

        [Header("Grass Settings")]
        public GameObject[] grassPrefabs;
        public float grassNoiseScale = 0.2f;
        [Range(0, 1)]
        public float grassThreshold = 0.3f;
        public bool alignGrassToNormal = false;

        [Header("Physics & Baking")]
        public LayerMask groundLayer;
        public LayerMask obstacleLayer;

        [Range(0f, 1f)]
        public float walkableSlopeThreshold = 0.91f;
        public bool showGrid;

        [ContextMenu("Generate Rocks")]
        public void GenerateRock()
        {
            Clear();
            if (parentFolder == null || rockPrefabs.Length == 0) return;

            int layerIndex = 0;
            int layerMaskValue = obstacleLayer.value;
            for (int i = 0; i < 32; i++)
            {
                if ((layerMaskValue >> i & 1) == 1)
                {
                    layerIndex = i;
                    break;
                }
            }

            var rand = new Unity.Mathematics.Random(seed);

            float3 offset = new float3((mapSize.x - 1) * cellSize * 0.5f, 0, (mapSize.y - 1) * cellSize * 0.5f);
            Vector3 startPos = transform.position - (Vector3)offset;

            GameObject parentRock = Instantiate(new GameObject("Rocks"), parentFolder);
            for (int x = 0; x < mapSize.x; x++)
            {
                for (int y = 0; y < mapSize.y; y++)
                {
                    float n = noise.cnoise(new float2(x, y) * noiseScale);
                    if (n > threshold)
                    {
                        GameObject prefab = rockPrefabs[rand.NextInt(0, rockPrefabs.Length)];

                        Vector3 pos = startPos + new Vector3(x * cellSize, 0, y * cellSize);
                        float3 rayStart = new float3(pos.x, 100f, pos.z);

                        Quaternion randomRot = Quaternion.Euler(0, rand.NextFloat(0, 360), 0);

                        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 200f, groundLayer))
                        {
                            Quaternion finalRot = Quaternion.FromToRotation(Vector3.up, hit.normal) * randomRot;

                            Vector3 finalPos = hit.point + (hit.normal * verticalOffset);

                            GameObject instance = Instantiate(prefab, finalPos, finalRot);

                            // instance.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;
                            instance.transform.SetParent(parentRock.transform);
                            instance.layer = layerIndex;

                            foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
                                child.gameObject.layer = layerIndex;

                            instance.isStatic = true;

#if UNITY_EDITOR
                            var flags = GameObjectUtility.GetStaticEditorFlags(instance);

                            flags &= ~StaticEditorFlags.BatchingStatic;

                            GameObjectUtility.SetStaticEditorFlags(instance, flags);

                            foreach (var child in instance.GetComponentsInChildren<Transform>(true))
                            {
                                child.gameObject.isStatic = true;
                                var childFlags = GameObjectUtility.GetStaticEditorFlags(child.gameObject);
                                childFlags &= ~StaticEditorFlags.BatchingStatic;
                                GameObjectUtility.SetStaticEditorFlags(child.gameObject, childFlags);
                            }
#endif
                        }
                    }
                }
            }
            Physics.SyncTransforms();
            Debug.Log($"Generated {parentFolder.childCount} rocks.");
        }

        [ContextMenu("Generate Grass")]
        public void GenerateGrass()
        {
            if (parentFolder == null || grassPrefabs.Length == 0) return;

            var rand = new Unity.Mathematics.Random(seed + 1);

            float3 offset = new float3((mapSize.x - 1) * cellSize * 0.5f, 0, (mapSize.y - 1) * cellSize * 0.5f);
            Vector3 startPos = transform.position - (Vector3)offset;

            GameObject parentGrass = Instantiate(new GameObject("Grass"), parentFolder);

            for (int x = 0; x < mapSize.x; x++)
            {
                for (int y = 0; y < mapSize.y; y++)
                {
                    float n = noise.cnoise(new float2(x, y) * grassNoiseScale);

                    if (n > grassThreshold)
                    {
                        GameObject prefab = grassPrefabs[rand.NextInt(0, grassPrefabs.Length)];
                        Vector3 posXZ = startPos + new Vector3(x * cellSize, 0, y * cellSize);
                        float3 rayStart = new float3(posXZ.x, 100f, posXZ.z);

                        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 200f, groundLayer))
                        {
                            Vector3 finalPos = hit.point;

                            Quaternion randomRot = Quaternion.Euler(0, rand.NextFloat(0, 360), 0);
                            Quaternion finalRot;

                            if (alignGrassToNormal)
                            {
                                finalRot = Quaternion.FromToRotation(Vector3.up, hit.normal) * randomRot;
                            }
                            else
                            {
                                finalRot = randomRot;
                            }

                            GameObject instance = Instantiate(prefab, finalPos, finalRot);

                            // instance.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;
                            instance.transform.SetParent(parentGrass.transform);
                            instance.transform.localScale = Vector3.one * rand.NextFloat(0.8f, 1.2f);

                            instance.isStatic = true;

#if UNITY_EDITOR
                            StaticEditorFlags targetFlags = StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI;

                            GameObjectUtility.SetStaticEditorFlags(instance, targetFlags);

                            foreach (var child in instance.GetComponentsInChildren<Transform>(true))
                            {
                                GameObjectUtility.SetStaticEditorFlags(child.gameObject, targetFlags);
                            }
#endif

                        }
                    }
                }
            }
            Debug.Log($"Generated {parentFolder.childCount} grass/objects.");
        }

        [ContextMenu("Analyze Grid")]
        public void AnalyzeGrid()
        {
            Physics.SyncTransforms();

            if (dataAsset == null) return;

            if (!GridValidator.Validate(dataAsset)) return;
            
            int width = mapSize.x;
            int height = mapSize.y;
            int total = width * height;

            float3 offset = new float3((width - 1) * cellSize * 0.5f, 0, (height - 1) * cellSize * 0.5f);
            float3 cornerOrigin = (float3)transform.position - offset;

            dataAsset.Dimensions = mapSize;
            dataAsset.CellSize = cellSize;
            dataAsset.Origin = cornerOrigin;
            dataAsset.Heights = new float[total];
            dataAsset.Normals = new float3[total];
            dataAsset.CellsType = new CellType[total];
            dataAsset.Weights = new float[total];
            dataAsset.WallPush = new float3[total];

            BakeTerrainParams(cornerOrigin, width, height, total);
            
            BakeWallPushField(width, height);
            ApplyWallInflation(total, width, height);

            dataAsset.hasData = true;
            EditorUtility.SetDirty(dataAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("<color=green>Grid analyzed successfully using SO.</color>");
        }
        
        private void BakeTerrainParams(float3 cornerOrigin, int width, int height, int total)
        {
            for (int i = 0; i < total; i++)
            {
                int2 coord = new int2(i % width, i / width);
                float3 cellCenterGround = cornerOrigin + new float3(coord.x * cellSize, 0, coord.y * cellSize);

                float3 planeCenter = cornerOrigin + new float3(coord.x * cellSize, 0, coord.y * cellSize);

                float3 rayStart = cellCenterGround + new float3(0, 50f, 0);
                bool hitSurface = Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 100f, groundLayer);
                dataAsset.Heights[i] = hitSurface ? hit.point.y : cornerOrigin.y;
                dataAsset.Normals[i] = hitSurface ? hit.normal : new float3(0, 1, 0);

                float3 obstacleCheckCenter = new float3(planeCenter.x, dataAsset.Heights[i] + 1.0f, planeCenter.z);

                float3 halfExtents = new float3(cellSize * 0.48f, 1.5f, cellSize * 0.48f);

                bool isObstacle = Physics.CheckBox(obstacleCheckCenter, halfExtents, Quaternion.identity, obstacleLayer);

                bool isEdge = (coord.x == 0 || coord.y == 0 || coord.x == width - 1 || coord.y == height - 1);
                bool isTooSteep = hitSurface && (math.dot(dataAsset.Normals[i], new float3(0, 1, 0)) < walkableSlopeThreshold);

                if (isObstacle || isEdge || isTooSteep)
                {
                    dataAsset.CellsType[i] = CellType.Wall;
                    dataAsset.Weights[i] = float.PositiveInfinity;
                }
                else
                {
                    dataAsset.CellsType[i] = CellType.Ground;
                    dataAsset.Weights[i] = 1.0f;
                }
            }
        }

        private void BakeWallPushField(int width, int height)
        {
       
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    
                    if (dataAsset.CellsType[index] == CellType.Wall) continue;

                    float3 currentCellPos = dataAsset.Origin + new float3(x * dataAsset.CellSize, 0, y * dataAsset.CellSize);
                    float3 totalPush = float3.zero;

                    for (int nx = -1; nx <= 1; nx++)
                    {
                        for (int nz = -1; nz <= 1; nz++)
                        {
                            int neighborX = x + nx;
                            int neighborZ = y + nz;

                            if (neighborX >= 0 && neighborX < width && neighborZ >= 0 && neighborZ < height)
                            {
                                int nIndex = neighborZ * width + neighborX;
                                if (dataAsset.CellsType[nIndex] == CellType.Wall)
                                {
                                    float3 wallPos = dataAsset.Origin + new float3(neighborX * dataAsset.CellSize, 0, neighborZ * dataAsset.CellSize);
                                    float3 toCell = currentCellPos - wallPos;
                                    toCell.y = 0;
                                    float dist = math.length(toCell);

                                    float wallPushRadius = dataAsset.CellSize * wallAvoidanceRange;
                                    if (dist < wallPushRadius)
                                    {
                                        totalPush += (toCell / (dist + 0.001f)) * (wallPushRadius - dist);
                                    }
                                }
                            }
                        }
                    }
                    dataAsset.WallPush[index] = totalPush;
                }
            }
        }

        private void ApplyWallInflation(int total, int width, int height)
        {
            if (inflationRadius > 0 && hasInflation)
            {
                int range = (int)math.ceil(inflationRadius);
                CellType[] tempTypes = (CellType[])dataAsset.CellsType.Clone();
                for (int i = 0; i < total; i++)
                {
                    if (tempTypes[i] == CellType.Wall)
                    {
                        int2 wallCoord = new int2(i % width, i / width);
                        for (int dy = -range; dy <= range; dy++)
                        {
                            for (int dx = -range; dx <= range; dx++)
                            {
                                int2 neighbor = wallCoord + new int2(dx, dy);
                                if (neighbor.x >= 0 && neighbor.x < width && neighbor.y >= 0 && neighbor.y < height)
                                {
                                    int nIndex = neighbor.y * width + neighbor.x;
                                    if (dataAsset.CellsType[nIndex] != CellType.Wall)
                                        dataAsset.Weights[nIndex] = math.max(dataAsset.Weights[nIndex], inflationMultiplier);
                                }
                            }
                        }
                    }
                }
            }
        }

        [ContextMenu("Clear Map")]
        public void Clear()
        {
            if (parentFolder == null) return;
            Undo.RegisterCompleteObjectUndo(parentFolder, "Clear Map");
            for (int i = parentFolder.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(parentFolder.GetChild(i).gameObject);
            }

            if (dataAsset != null)
            {
                dataAsset.hasData = false;
                EditorUtility.SetDirty(dataAsset);
            }
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
            Debug.Log("Map cleared.");
        }

        private void OnDrawGizmos()
        {
            if (dataAsset == null || !dataAsset.hasData) return;

            Color wallColor = new Color(1.0f, 0.0f, 0.0f, 0.70f);
            Color groundColor = new Color(0.0f, 1.0f, 1.0f, 0.25f);
            Color groundWeights = new Color(1f, 0.8f, 0.1f, 0.25f);

            for (int i = 0; i < dataAsset.CellsType.Length; i++)
            {
                int2 coord = new int2(i % dataAsset.Dimensions.x, i / dataAsset.Dimensions.x);
                Vector3 pos = (Vector3)dataAsset.Origin + new Vector3(coord.x * dataAsset.CellSize, dataAsset.Heights[i] + 0.1f, coord.y * dataAsset.CellSize);

                Vector3 normal = dataAsset.Normals[i];
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal);

                pos.y = dataAsset.Heights[i] + 0.2f;
                Matrix4x4 cubeMatrix = Matrix4x4.TRS(pos, rotation, Vector3.one);
                Gizmos.matrix = cubeMatrix;

                if (dataAsset.CellsType[i] == CellType.Wall)
                {
                    Gizmos.color = wallColor;
                    Gizmos.DrawCube(Vector3.zero, new Vector3(dataAsset.CellSize, 0.2f, dataAsset.CellSize));
                    Gizmos.DrawWireCube(Vector3.zero, new Vector3(dataAsset.CellSize, 0.05f, dataAsset.CellSize));
                }
                else if (showGrid)
                {
                    Gizmos.color = dataAsset.Weights[i] > 1.0f ? groundWeights : groundColor;
                    Gizmos.DrawWireCube(Vector3.zero, new Vector3(dataAsset.CellSize * 0.9f, 0.1f, dataAsset.CellSize * 0.9f));
                }
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
