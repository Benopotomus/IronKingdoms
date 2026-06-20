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
    public class CombatZonePolygonFootprint : MonoBehaviour
    {
        private const string VisualChildName = "Visual";

        [SerializeField] private List<Vector2> localVertices = new();
        [SerializeField] private float colliderCenterLocalY = 1.27f;
        [SerializeField] private float colliderHeight = 2.54f;
        [SerializeField] private float visualThickness = 0.05f;
        [SerializeField] private Material visualMaterial;

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
        public float TabletopWorldY => transform.TransformPoint(new Vector3(0f, colliderCenterLocalY, 0f)).y;

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
                var local = new Vector3(localVertices[i].x, colliderCenterLocalY, localVertices[i].y);
                var world = transform.TransformPoint(local);
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
                var local = new Vector3(localVertices[i].x, colliderCenterLocalY, localVertices[i].y);
                var world = transform.TransformPoint(local);
                cachedWorldPolygon.Add(new Vector2(world.x, world.z));
            }
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

        public void RegenerateGeometry()
        {
            if (!HasFootprint || isRegeneratingGeometry)
            {
                return;
            }

            isRegeneratingGeometry = true;
            try
            {
                RemoveInvalidPolygonCollider();
                InvalidateWorldPolygonCache();
                var visualMeshInstance = BuildVisualMesh();

                var visual = GetOrCreateVisualChild();
                var meshFilter = EnsureMeshFilter(visual);
                var meshRenderer = EnsureMeshRenderer(visual);
                meshFilter.sharedMesh = visualMeshInstance;
                if (visualMaterial != null)
                {
                    meshRenderer.sharedMaterial = visualMaterial;
                }

                ExcludeVisualFromNavmeshScan(visual.gameObject);
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
                var local = new Vector3(localVertices[i].x, colliderCenterLocalY, localVertices[i].y);
                var world = transform.TransformPoint(local);
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
                Mathf.Max(0.01f, colliderHeight),
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
            if (!CombatPolygonFootprintGeometry.TryTriangulateSimplePolygonLocal(localVertices, triangleScratch))
            {
                triangleScratch.Clear();
                for (var i = 1; i < localVertices.Count - 1; i++)
                {
                    triangleScratch.Add(0);
                    triangleScratch.Add(i);
                    triangleScratch.Add(i + 1);
                }
            }

            var halfHeight = colliderHeight * 0.5f;
            var bottomY = colliderCenterLocalY - halfHeight;
            var topY = colliderCenterLocalY + halfHeight;
            var vertexCount = localVertices.Count;

            if (visualMesh == null)
            {
                visualMesh = new Mesh { name = $"{name}_VisualMesh" };
            }
            else
            {
                visualMesh.Clear(false);
            }

            var vertices = new Vector3[vertexCount * 2];
            visualTriangleScratch.Clear();
            if (visualTriangleScratch.Capacity < triangleScratch.Count * 2 + vertexCount * 6)
            {
                visualTriangleScratch.Capacity = triangleScratch.Count * 2 + vertexCount * 6;
            }

            for (var i = 0; i < vertexCount; i++)
            {
                var xz = localVertices[i];
                vertices[i] = new Vector3(xz.x, bottomY, xz.y);
                vertices[i + vertexCount] = new Vector3(xz.x, topY, xz.y);
            }

            for (var t = 0; t < triangleScratch.Count; t += 3)
            {
                var a = triangleScratch[t];
                var b = triangleScratch[t + 1];
                var c = triangleScratch[t + 2];
                visualTriangleScratch.Add(a + vertexCount);
                visualTriangleScratch.Add(b + vertexCount);
                visualTriangleScratch.Add(c + vertexCount);
                visualTriangleScratch.Add(c);
                visualTriangleScratch.Add(b);
                visualTriangleScratch.Add(a);
            }

            for (var i = 0; i < vertexCount; i++)
            {
                var next = (i + 1) % vertexCount;
                AddQuad(visualTriangleScratch, i, next, next + vertexCount, i + vertexCount);
            }

            visualMesh.SetVertices(vertices);
            visualMesh.SetTriangles(visualTriangleScratch, 0);
            visualMesh.RecalculateNormals();
            visualMesh.RecalculateBounds();
            cachedGeometryHash = ComputeGeometryHash();
            return visualMesh;
        }

        private static void AddQuad(List<int> triangles, int a, int b, int c, int d)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(d);
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
                return hash;
            }
        }


#if UNITY_EDITOR
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
