using System.Collections.Generic;
using System;
using Pathfinding;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Authoritative XZ polygon footprint for irregular combat zones (forests, etc.).
    /// Generates a tabletop visual mesh from local vertices. Footprint queries use analytic
    /// polygon math — no physics collider (forests must not block navmesh baking).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CombatZone))]
    [ExecuteAlways]
    public class CombatZonePolygonFootprint : MonoBehaviour
    {
        private const string VisualChildName = "Visual";

        [SerializeField] private List<Vector2> localVertices = new();
        [SerializeField] private float colliderCenterLocalY = 0f;
        [SerializeField] private float colliderHeight = 2.54f;
        [SerializeField] private float visualThickness = 0.05f;
        [SerializeField] private Material visualMaterial;
#if UNITY_EDITOR
        [SerializeField, HideInInspector] private bool footprintPlaneMigrated;
#endif
        private readonly List<int> triangleScratch = new();
        private readonly List<int> visualTriangleScratch = new();
        private readonly List<Vector2> cachedWorldPolygon = new();
        private Mesh visualMesh;
        private bool isRegeneratingGeometry;
        private int cachedWorldPolygonKey = int.MinValue;
        private int cachedGeometryHash;
#if UNITY_EDITOR
        private int cachedEditorGeometryHash;
#endif

        public IReadOnlyList<Vector2> LocalVertices => localVertices;
        public bool HasFootprint => CombatPolygonFootprintGeometry.IsValidFootprint(localVertices);
        /// <summary>World Y of the XZ footprint plane (local origin).</summary>
        public float TabletopWorldY => transform.TransformPoint(Vector3.zero).y;

        public void SetLocalVertices(IReadOnlyList<Vector2> vertices)
        {
            localVertices.Clear();
            if (vertices != null)
            {
                localVertices.AddRange(vertices);
            }

            InvalidateWorldPolygonCache();
        }

        /// <summary>
        /// Replaces the footprint with a regular polygon cylinder outline (local XZ plane).
        /// </summary>
        public void SetRegularPolygonFootprint(float diameterInches, int segmentCount, float startAngleDegrees = 0f)
        {
            SetLocalVertices(CombatPolygonFootprintGeometry.BuildRegularPolygonLocalVertices(
                diameterInches,
                segmentCount,
                startAngleDegrees));
        }

        public void SetLocalVerticesFromWorld(IReadOnlyList<Vector3> worldVertices)
        {
            localVertices.Clear();
            if (worldVertices == null)
            {
                InvalidateWorldPolygonCache();
                return;
            }

            for (var i = 0; i < worldVertices.Count; i++)
            {
                var local = transform.InverseTransformPoint(worldVertices[i]);
                localVertices.Add(new Vector2(local.x, local.z));
            }

            InvalidateWorldPolygonCache();
        }

        public void CollectWorldFootprintCorners(List<Vector3> corners)
        {
            if (!HasFootprint)
            {
                return;
            }

            var tabletopY = TabletopWorldY;
            for (var i = 0; i < localVertices.Count; i++)
            {
                var world = LocalVertexToWorld(i);
                world.y = tabletopY;
                corners.Add(world);
            }
        }

        public bool ContainsPointWorld(Vector3 worldPoint)
        {
            if (!HasFootprint)
            {
                return false;
            }

            EnsureWorldPolygonCache();
            return CombatPolygonFootprintGeometry.ContainsPointLocal(
                new Vector2(worldPoint.x, worldPoint.z),
                cachedWorldPolygon);
        }

        public bool TryGetRayFootprintIntervalWorld(
            Vector3 worldOrigin,
            Vector3 worldDirection,
            out float enterWorld,
            out float exitWorld)
        {
            enterWorld = -1f;
            exitWorld = -1f;
            if (!HasFootprint)
            {
                return false;
            }

            worldOrigin.y = 0f;
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude <= 1e-8f)
            {
                return false;
            }

            worldDirection.Normalize();
            EnsureWorldPolygonCache();
            return CombatPolygonFootprintGeometry.TryRayPolygonIntervalLocal(
                new Vector2(worldOrigin.x, worldOrigin.z),
                new Vector2(worldDirection.x, worldDirection.z),
                cachedWorldPolygon,
                out enterWorld,
                out exitWorld);
        }

        private void InvalidateWorldPolygonCache()
        {
            cachedWorldPolygonKey = int.MinValue;
            cachedWorldPolygon.Clear();
        }

        private void EnsureWorldPolygonCache()
        {
            var key = ComputeWorldPolygonKey();
            if (key == cachedWorldPolygonKey && cachedWorldPolygon.Count == localVertices.Count)
            {
                return;
            }

            cachedWorldPolygonKey = key;
            cachedWorldPolygon.Clear();
            if (!HasFootprint)
            {
                return;
            }

            if (cachedWorldPolygon.Capacity < localVertices.Count)
            {
                cachedWorldPolygon.Capacity = localVertices.Count;
            }

            for (var i = 0; i < localVertices.Count; i++)
            {
                var world = LocalVertexToWorld(i);
                cachedWorldPolygon.Add(new Vector2(world.x, world.z));
            }
        }

        private Vector3 LocalVertexToWorld(int index)
        {
            var vertex = localVertices[index];
            return transform.TransformPoint(new Vector3(vertex.x, 0f, vertex.y));
        }

        private void MigrateLegacyFootprintPlaneIfNeeded()
        {
            if (colliderCenterLocalY <= 0.001f)
            {
                return;
            }

#if UNITY_EDITOR
            if (footprintPlaneMigrated)
            {
                return;
            }

            footprintPlaneMigrated = true;
#endif
            transform.position += transform.TransformVector(new Vector3(0f, colliderCenterLocalY, 0f));
            colliderCenterLocalY = 0f;
        }

        private int ComputeWorldPolygonKey()
        {
            unchecked
            {
                var hash = ComputeGeometryHash();
                hash = (hash * 397) ^ transform.position.GetHashCode();
                hash = (hash * 397) ^ transform.rotation.GetHashCode();
                hash = (hash * 397) ^ transform.lossyScale.GetHashCode();
                hash = (hash * 397) ^ Time.frameCount;
                return hash;
            }
        }

        private void OnEnable()
        {
            MigrateLegacyFootprintPlaneIfNeeded();
            RemoveInvalidPolygonCollider();
            EnsureVisualMeshIfNeeded();
        }

        private void EnsureVisualMeshIfNeeded()
        {
            if (!HasFootprint)
            {
                return;
            }

            var visual = transform.Find(VisualChildName);
            if (visual == null)
            {
                RegenerateGeometry();
                return;
            }

            var meshFilter = visual.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                RegenerateGeometry();
            }
        }

        [ContextMenu("Regenerate Mesh")]
        public void RegenerateGeometry()
        {
            if (!HasFootprint || isRegeneratingGeometry)
            {
                return;
            }

            isRegeneratingGeometry = true;
            try
            {
                MigrateLegacyFootprintPlaneIfNeeded();
                RemoveInvalidPolygonCollider();
                InvalidateWorldPolygonCache();
                var visualMeshInstance = BuildVisualMesh();

                var visual = GetOrCreateVisualChild();
                visual.localScale = Vector3.one;
                var meshFilter = EnsureMeshFilter(visual);
                var meshRenderer = EnsureMeshRenderer(visual);
                meshFilter.sharedMesh = visualMeshInstance;
                if (visualMaterial != null)
                {
                    meshRenderer.sharedMaterial = visualMaterial;
                }

                ExcludeVisualFromNavmeshScan(visual.gameObject);

#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    EditorPersistVisualMeshHandler?.Invoke(gameObject, visual, visualMeshInstance);
                }
#endif
            }
            catch (Exception ex)
            {
                CombatStartupLog.LogException($"CombatZonePolygonFootprint.RegenerateGeometry '{name}'", ex);
            }
            finally
            {
                isRegeneratingGeometry = false;
            }
        }

        /// <summary>
        /// XZ bounds for fog cache and debug. Polygon zones do not use a physics collider.
        /// </summary>
        public bool TryGetFootprintBounds(out Bounds bounds)
        {
            bounds = default;
            if (!HasFootprint)
            {
                return false;
            }

            var minX = float.MaxValue;
            var maxX = float.MinValue;
            var minZ = float.MaxValue;
            var maxZ = float.MinValue;
            var tabletopY = TabletopWorldY;
            for (var i = 0; i < localVertices.Count; i++)
            {
                var world = LocalVertexToWorld(i);
                minX = Mathf.Min(minX, world.x);
                maxX = Mathf.Max(maxX, world.x);
                minZ = Mathf.Min(minZ, world.z);
                maxZ = Mathf.Max(maxZ, world.z);
            }

            if (minX > maxX || minZ > maxZ)
            {
                return false;
            }

            var center = new Vector3(
                (minX + maxX) * 0.5f,
                tabletopY,
                (minZ + maxZ) * 0.5f);
            var size = new Vector3(
                Mathf.Max(0.01f, maxX - minX),
                Mathf.Max(0.01f, Mathf.Max(visualThickness, 0.01f)),
                Mathf.Max(0.01f, maxZ - minZ));
            bounds = new Bounds(center, size);
            return true;
        }

        private static void ExcludeVisualFromNavmeshScan(GameObject visualObject)
        {
            var modifier = visualObject.GetComponent<RecastNavmeshModifier>();
            if (modifier == null)
            {
                modifier = visualObject.AddComponent<RecastNavmeshModifier>();
            }

            modifier.includeInScan = RecastNavmeshModifier.ScanInclusion.AlwaysExclude;
            modifier.dynamic = false;
        }

        private void RemoveInvalidPolygonCollider()
        {
            var meshCollider = GetComponent<MeshCollider>();
            if (meshCollider == null)
            {
                return;
            }

#if UNITY_EDITOR
            DestroyImmediate(meshCollider);
#else
            Destroy(meshCollider);
#endif
        }

        private Transform GetOrCreateVisualChild()
        {
            var existing = transform.Find(VisualChildName);
            if (existing != null)
            {
                EnsureMeshFilter(existing);
                EnsureMeshRenderer(existing);
                return existing;
            }

            var visualObject = new GameObject(VisualChildName);
            visualObject.transform.SetParent(transform, false);
            visualObject.AddComponent<MeshFilter>();
            visualObject.AddComponent<MeshRenderer>();
            return visualObject.transform;
        }

        private static MeshFilter EnsureMeshFilter(Transform visual)
        {
            var meshFilter = visual.GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = visual.gameObject.AddComponent<MeshFilter>();
            }

            return meshFilter;
        }

        private static MeshRenderer EnsureMeshRenderer(Transform visual)
        {
            var meshRenderer = visual.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                meshRenderer = visual.gameObject.AddComponent<MeshRenderer>();
            }

            return meshRenderer;
        }

        private Mesh BuildVisualMesh()
        {
            triangleScratch.Clear();
            if (CombatPolygonFootprintGeometry.IsConvexFootprintLocal(localVertices))
            {
                for (var i = 1; i < localVertices.Count - 1; i++)
                {
                    triangleScratch.Add(0);
                    triangleScratch.Add(i);
                    triangleScratch.Add(i + 1);
                }
            }
            else if (!CombatPolygonFootprintGeometry.TryTriangulateSimplePolygonLocal(localVertices, triangleScratch))
            {
                for (var i = 1; i < localVertices.Count - 1; i++)
                {
                    triangleScratch.Add(0);
                    triangleScratch.Add(i);
                    triangleScratch.Add(i + 1);
                }
            }

            var vertexCount = localVertices.Count;
            const float surfaceY = 0f;

            if (visualMesh == null)
            {
                visualMesh = new Mesh { name = $"{name}_VisualMesh" };
            }
            else
            {
                visualMesh.Clear(false);
            }

            var vertices = new Vector3[vertexCount];
            visualTriangleScratch.Clear();
            if (visualTriangleScratch.Capacity < triangleScratch.Count)
            {
                visualTriangleScratch.Capacity = triangleScratch.Count;
            }

            for (var i = 0; i < vertexCount; i++)
            {
                var xz = localVertices[i];
                vertices[i] = new Vector3(xz.x, surfaceY, xz.y);
            }

            // Footprint vertices are CCW in XZ; Unity front faces need CW when viewed from +Y.
            var footprintCounterClockwise = CombatPolygonFootprintGeometry.SignedAreaLocal(localVertices) > 0f;
            for (var t = 0; t < triangleScratch.Count; t += 3)
            {
                var a = triangleScratch[t];
                var b = triangleScratch[t + 1];
                var c = triangleScratch[t + 2];
                if (footprintCounterClockwise)
                {
                    visualTriangleScratch.Add(a);
                    visualTriangleScratch.Add(c);
                    visualTriangleScratch.Add(b);
                }
                else
                {
                    visualTriangleScratch.Add(a);
                    visualTriangleScratch.Add(b);
                    visualTriangleScratch.Add(c);
                }
            }

            var normals = new Vector3[vertexCount];
            for (var i = 0; i < vertexCount; i++)
            {
                normals[i] = Vector3.up;
            }

            visualMesh.SetVertices(vertices);
            visualMesh.SetTriangles(visualTriangleScratch, 0);
            visualMesh.SetNormals(normals);
            visualMesh.RecalculateBounds();
            cachedGeometryHash = ComputeGeometryHash();
            return visualMesh;
        }

        private int ComputeGeometryHash()
        {
            unchecked
            {
                var hash = localVertices.Count;
                for (var i = 0; i < localVertices.Count; i++)
                {
                    hash = (hash * 397) ^ localVertices[i].GetHashCode();
                }

                hash = (hash * 397) ^ colliderCenterLocalY.GetHashCode();
                hash = (hash * 397) ^ colliderHeight.GetHashCode();
                hash = (hash * 397) ^ visualThickness.GetHashCode();
                return hash;
            }
        }


#if UNITY_EDITOR
        public static Action<GameObject, Transform, Mesh> EditorPersistVisualMeshHandler;
#endif

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Ensures prefab-stage previews refresh when the asset is first opened.
            if (Application.isPlaying || !HasFootprint)
            {
                return;
            }

            var visual = transform.Find(VisualChildName);
            if (visual == null)
            {
                return;
            }

            var meshFilter = visual.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                return;
            }

            EnsureVisualMeshIfNeeded();
        }

        private void OnValidate()
        {
            if (!HasFootprint || !isActiveAndEnabled)
            {
                return;
            }

            var geometryHash = ComputeGeometryHash();
            if (geometryHash == cachedEditorGeometryHash)
            {
                return;
            }

            UnityEditor.EditorApplication.delayCall -= ScheduleDeferredRegenerateGeometry;
            UnityEditor.EditorApplication.delayCall += ScheduleDeferredRegenerateGeometry;
        }

        private void ScheduleDeferredRegenerateGeometry()
        {
            UnityEditor.EditorApplication.delayCall -= ScheduleDeferredRegenerateGeometry;
            if (this == null || !HasFootprint)
            {
                return;
            }

            var geometryHash = ComputeGeometryHash();
            if (geometryHash == cachedEditorGeometryHash && visualMesh != null)
            {
                return;
            }

            cachedEditorGeometryHash = geometryHash;
            RegenerateGeometry();
        }
#endif
    }
}
