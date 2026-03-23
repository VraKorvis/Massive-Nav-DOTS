using Core;
using Core.PathfindingAStar;
using Map.Grid;
using UnityEngine;
using Unity.Mathematics;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace Map.Generation
{
    public class MapEditorGenerator : MonoBehaviour
    {
        public GridDataAsset DataAsset;

        [Header("Assets")]
        public GameObject[] RockPrefabs;
        public Transform ParentFolder;

        [Header("Generate Settings")]
        public int2 MapSize = new int2(200, 200);
        public float CellSize = 1f;
        public float NoiseScale = 0.1f;
        [Range(0, 1)]
        public float Threshold = 0.5f;
        [Range(0, 2)]
        public float VerticalOffset = 0.5f;
        public bool HasInflation = false;
        public float InflationMultiplier = 1f;
        public float InflationRadius = 1.0f;
        public float WallAvoidanceRange = 1.2f;

        public uint Seed = 123;

        [Header("Grass Settings")]
        public GameObject[] GrassPrefabs;
        public float GrassNoiseScale = 0.2f;
        [Range(0, 1)]
        public float GrassThreshold = 0.3f;
        public bool AlignGrassToNormal = false;

        [Header("Physics & Baking")]
        public LayerMask GroundLayer;
        public LayerMask ObstacleLayer;

        [Range(0f, 1f)]
        public float WalkableSlopeThreshold = 0.91f;
        public bool ShowGrid;

        [ContextMenu("Generate World")]
        private void GenerateWorld()
        {
            Clear();
            GenerateRock();
            GenerateGrass();
            AnalyzeGrid();
        }

        private void GenerateRock()
        {
            if (ParentFolder == null || RockPrefabs.Length == 0) return;

            int layerIndex = 0;
            int layerMaskValue = ObstacleLayer.value;
            for (int i = 0; i < 32; i++)
            {
                if ((layerMaskValue >> i & 1) == 1)
                {
                    layerIndex = i;
                    break;
                }
            }

            var rand = new Unity.Mathematics.Random(Seed);

            float3 offset = new float3((MapSize.x - 1) * CellSize * 0.5f, 0, (MapSize.y - 1) * CellSize * 0.5f);
            Vector3 startPos = transform.position - (Vector3)offset;

            GameObject parentRock = new GameObject("Rocks");
            parentRock.transform.SetParent(ParentFolder);

            Collider[] results = new Collider[1];

            for (int x = 0; x < MapSize.x; x++)
            {
                for (int y = 0; y < MapSize.y; y++)
                {
                    float n = noise.cnoise(new float2(x, y) * NoiseScale);
                    if (n > Threshold)
                    {
                        GameObject prefab = RockPrefabs[rand.NextInt(0, RockPrefabs.Length)];

                        Vector3 pos = startPos + new Vector3(x * CellSize, 0, y * CellSize);
                        float3 rayStart = new float3(pos.x, 100f, pos.z);

                        Quaternion randomRot = Quaternion.Euler(0, rand.NextFloat(0, 360), 0);

                        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 200f, GroundLayer))
                        {
                            float rockRadius = 1.0f;
                            MeshFilter mf = prefab.GetComponentInChildren<MeshFilter>();
                            if (mf != null)
                            {
                                Vector3 extents = mf.sharedMesh.bounds.extents;
                                rockRadius = math.max(extents.x, extents.z);
                            }
                            float checkRadius = rockRadius * 0.6f;

                            Vector3 finalPos = hit.point + (hit.normal * VerticalOffset);

                            int overlapCount = Physics.OverlapSphereNonAlloc(finalPos, checkRadius, results, ObstacleLayer);

                            if (overlapCount > 0) continue;

                            Quaternion finalRot = Quaternion.FromToRotation(Vector3.up, hit.normal) * randomRot;

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
            Debug.Log($"Generated {parentRock.transform.childCount} rocks.");
        }

        private void GenerateGrass()
        {
            if (ParentFolder == null || GrassPrefabs.Length == 0) return;

            var rand = new Unity.Mathematics.Random(Seed + 1);

            float3 offset = new float3((MapSize.x - 1) * CellSize * 0.5f, 0, (MapSize.y - 1) * CellSize * 0.5f);
            Vector3 startPos = transform.position - (Vector3)offset;

            GameObject parentGrass = new GameObject("Grass");
            parentGrass.transform.SetParent(ParentFolder);

            for (int x = 0; x < MapSize.x; x++)
            {
                for (int y = 0; y < MapSize.y; y++)
                {
                    float n = noise.cnoise(new float2(x, y) * GrassNoiseScale);

                    if (n > GrassThreshold)
                    {
                        GameObject prefab = GrassPrefabs[rand.NextInt(0, GrassPrefabs.Length)];
                        Vector3 posXZ = startPos + new Vector3(x * CellSize, 0, y * CellSize);
                        float3 rayStart = new float3(posXZ.x, 100f, posXZ.z);

                        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 200f, GroundLayer))
                        {
                            Vector3 finalPos = hit.point;

                            Quaternion randomRot = Quaternion.Euler(0, rand.NextFloat(0, 360), 0);
                            Quaternion finalRot;

                            if (AlignGrassToNormal)
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
            Debug.Log($"Generated {parentGrass.transform.childCount} grass/objects.");
        }

        [ContextMenu("Analyze Grid")]
        public void AnalyzeGrid()
        {
            Physics.SyncTransforms();

            if (DataAsset == null) return;

            if (!GridValidator.Validate(DataAsset)) return;

            int width = MapSize.x;
            int height = MapSize.y;
            int total = width * height;

            float3 offset = new float3((width - 1) * CellSize * 0.5f, 0, (height - 1) * CellSize * 0.5f);
            float3 cornerOrigin = (float3)transform.position - offset;

            DataAsset.Dimensions = MapSize;
            DataAsset.CellSize = CellSize;
            DataAsset.Origin = cornerOrigin;
            DataAsset.Heights = new float[total];
            DataAsset.Normals = new float3[total];
            DataAsset.CellsType = new CellType[total];
            DataAsset.Weights = new float[total];
            DataAsset.WallPush = new float3[total];

            BakeTerrainParams(cornerOrigin, width, height, total);

            // BakeWallPushField(width, height);
            BakeWallPushGradientField(width, height);
            ApplyWallInflation(total, width, height);

            DataAsset.hasData = true;
#if UNITY_EDITOR

            EditorUtility.SetDirty(DataAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
#endif
            Debug.Log("<color=green>Grid analyzed successfully using SO.</color>");
        }

        private void BakeTerrainParams(float3 cornerOrigin, int width, int height, int total)
        {
            for (int i = 0; i < total; i++)
            {
                int2 coord = new int2(i % width, i / width);
                float3 cellCenterGround = cornerOrigin + new float3(coord.x * CellSize, 0, coord.y * CellSize);

                float3 planeCenter = cornerOrigin + new float3(coord.x * CellSize, 0, coord.y * CellSize);

                float3 rayStart = cellCenterGround + new float3(0, 50f, 0);
                bool hitSurface = Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 100f, GroundLayer);
                DataAsset.Heights[i] = hitSurface ? hit.point.y : cornerOrigin.y;
                DataAsset.Normals[i] = hitSurface ? hit.normal : new float3(0, 1, 0);

                float3 obstacleCheckCenter = new float3(planeCenter.x, DataAsset.Heights[i] + 1.0f, planeCenter.z);

                float3 halfExtents = new float3(CellSize * 0.48f, 1.5f, CellSize * 0.48f);

                bool isObstacle = Physics.CheckBox(obstacleCheckCenter, halfExtents, Quaternion.identity, ObstacleLayer);

                bool isEdge = (coord.x == 0 || coord.y == 0 || coord.x == width - 1 || coord.y == height - 1);
                bool isTooSteep = hitSurface && (math.dot(DataAsset.Normals[i], new float3(0, 1, 0)) < WalkableSlopeThreshold);

                if (isObstacle || isEdge || isTooSteep)
                {
                    DataAsset.CellsType[i] = CellType.Wall;
                    DataAsset.Weights[i] = float.PositiveInfinity;
                }
                else
                {
                    DataAsset.CellsType[i] = CellType.Ground;
                    DataAsset.Weights[i] = 1.0f;
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

                    if (DataAsset.CellsType[index] == CellType.Wall) continue;

                    float3 currentCellPos = DataAsset.Origin + new float3(x * DataAsset.CellSize, 0, y * DataAsset.CellSize);
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
                                if (DataAsset.CellsType[nIndex] == CellType.Wall)
                                {
                                    float3 wallPos = DataAsset.Origin + new float3(neighborX * DataAsset.CellSize, 0, neighborZ * DataAsset.CellSize);
                                    float3 toCell = currentCellPos - wallPos;
                                    toCell.y = 0;
                                    float dist = math.length(toCell);

                                    float wallPushRadius = DataAsset.CellSize * WallAvoidanceRange;
                                    if (dist < wallPushRadius)
                                    {
                                        totalPush += (toCell / (dist + 0.001f)) * (wallPushRadius - dist);
                                    }
                                }
                            }
                        }
                    }
                    DataAsset.WallPush[index] = totalPush;
                }
            }
        }

        private void BakeWallPushGradientField(int width, int height)
        {
            float[] distField = new float[width * height];

            for (int i = 0; i < distField.Length; i++)
                distField[i] = (DataAsset.CellsType[i] == CellType.Wall) ? 0f : 1000f;

            for (int y = 1; y < height; y++)
            {
                for (int x = 1; x < width; x++)
                {
                    int i = y * width + x;
                    if (distField[i] == 0) continue;
                    float d1 = distField[i - 1] + 1f;
                    float d2 = distField[i - width] + 1f;
                    distField[i] = math.min(distField[i], math.min(d1, d2));
                }
            }

            for (int y = height - 2; y >= 0; y--)
            {
                for (int x = width - 2; x >= 0; x--)
                {
                    int i = y * width + x;
                    if (distField[i] == 0) continue;
                    float d1 = distField[i + 1] + 1f;
                    float d2 = distField[i + width] + 1f;
                    distField[i] = math.min(distField[i], math.min(d1, d2));
                }
            }

            float maxDistance = 1.0f;

            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    int i = y * width + x;
                    if (DataAsset.CellsType[i] == CellType.Wall) continue;

                    if (distField[i] < maxDistance)
                    {
                        float dx = distField[i + 1] - distField[i - 1];
                        float dz = distField[i + width] - distField[i - width];

                        float3 grad = new float3(dx, 0, dz);
                        if (math.lengthsq(grad) > 0.0001f)
                        {
                            DataAsset.WallPush[i] = math.normalize(grad);
                        }
                    }
                    else
                    {
                        DataAsset.WallPush[i] = float3.zero;
                    }
                }
            }
        }

        private void ApplyWallInflation(int total, int width, int height)
        {
            if (InflationRadius > 0 && HasInflation)
            {
                int range = (int)math.ceil(InflationRadius);
                CellType[] tempTypes = (CellType[])DataAsset.CellsType.Clone();
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
                                    if (DataAsset.CellsType[nIndex] != CellType.Wall)
                                        DataAsset.Weights[nIndex] = math.max(DataAsset.Weights[nIndex], InflationMultiplier);
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
            if (ParentFolder == null) return;
#if UNITY_EDITOR
            Undo.RegisterCompleteObjectUndo(ParentFolder, "Clear Map");
#endif
            for (int i = ParentFolder.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(ParentFolder.GetChild(i).gameObject);
            }
            if (DataAsset != null)
            {
                DataAsset.hasData = false;
#if UNITY_EDITOR
                EditorUtility.SetDirty(DataAsset);
#endif
            }

#if UNITY_EDITOR
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
            Debug.Log("Map cleared.");
        }

        private void OnDrawGizmos()
        {
            if (DataAsset == null || !DataAsset.hasData) return;

            Color wallColor = new Color(1.0f, 0.0f, 0.0f, 0.70f);
            Color groundColor = new Color(0.0f, 1.0f, 1.0f, 0.25f);
            Color groundWeights = new Color(1f, 0.8f, 0.1f, 0.25f);

            for (int i = 0; i < DataAsset.CellsType.Length; i++)
            {
                int2 coord = new int2(i % DataAsset.Dimensions.x, i / DataAsset.Dimensions.x);
                Vector3 pos = (Vector3)DataAsset.Origin + new Vector3(coord.x * DataAsset.CellSize, DataAsset.Heights[i] + 0.1f, coord.y * DataAsset.CellSize);

                Vector3 normal = DataAsset.Normals[i];
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal);

                pos.y = DataAsset.Heights[i] + 0.2f;
                Matrix4x4 cubeMatrix = Matrix4x4.TRS(pos, rotation, Vector3.one);
                Gizmos.matrix = cubeMatrix;

                if (DataAsset.CellsType[i] == CellType.Wall)
                {
                    Gizmos.color = wallColor;
                    Gizmos.DrawCube(Vector3.zero, new Vector3(DataAsset.CellSize, 0.2f, DataAsset.CellSize));
                    Gizmos.DrawWireCube(Vector3.zero, new Vector3(DataAsset.CellSize, 0.05f, DataAsset.CellSize));
                }
                else if (ShowGrid)
                {
                    Gizmos.color = DataAsset.Weights[i] > 1.0f ? groundWeights : groundColor;
                    Gizmos.DrawWireCube(Vector3.zero, new Vector3(DataAsset.CellSize * 0.9f, 0.1f, DataAsset.CellSize * 0.9f));
                }
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
