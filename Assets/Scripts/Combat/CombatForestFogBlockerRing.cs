using System.Collections.Generic;
using FOW;
using UnityEngine;
using UnityEngine.Rendering;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Per-unit forest frontier debug mesh. Lives on the same GameObject as the revealer.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatForestFogBlockerRing : MonoBehaviour
    {
        private FogOfWarRevealer3D revealer;
        private bool ignoresForestForLineOfSight;
        [SerializeField, Min(8)] private int segmentCount = 64;
        [SerializeField, Min(0.5f)] private float wallHeightWorld = 3f;
        [SerializeField, Min(0.01f)] private float wallThicknessWorld = 0.25f;
        [SerializeField, Min(0f)] private float segmentOverlapWorld = 0.15f;

        [Header("Debug")]
        [SerializeField] private bool drawGeneratedMesh = true;
        [SerializeField] private bool drawWireframeInSceneView = true;
        [SerializeField] private bool drawSamplePoints = true;
        [SerializeField] private Color drawMeshColor = new(0.15f, 0.85f, 0.25f, 0.5f);
        [SerializeField] private Color drawWireColor = new(0.1f, 1f, 0.2f, 1f);
        [SerializeField] private Color drawSampleColor = new(1f, 0.85f, 0.1f, 1f);
        [SerializeField, Min(0.01f)] private float drawSampleRadius = 0.08f;

        private readonly List<float> clipScratch = new();
        private readonly List<bool> limitedScratch = new();
        private readonly List<Vector3> lastSamplePointsWorld = new();
        private readonly List<OccluderSegmentEntry> segmentPool = new();
        private readonly List<Vector3> vertexScratch = new();
        private readonly List<int> triangleScratch = new();

        private GameObject occluderRoot;
        private Material debugMaterial;
        private Vector3 lastEyeWorld;
        private int activeSegmentCount;
        private bool hasValidDebugPose;

        public void ConfigureForUnit(Unit unit)
        {
            ignoresForestForLineOfSight = unit != null
                && CombatAbilitySolver.IgnoresForestWhenDeterminingLineOfSight(unit);
        }

        public void ConfigureForUnitDefinition(UnitTypeDefinition definition)
        {
            ignoresForestForLineOfSight = definition != null
                && CombatAbilitySolver.IgnoresForestWhenDeterminingLineOfSight(definition, null);
        }

        public void RebuildForDebug()
        {
            if (!TryBuildOccluderShapes(enablePhysics: false, out _))
            {
                SetOccludersActive(false);
            }
        }

        public void RebuildNow()
        {
            if (!ShouldMaintainBlockers())
            {
                SetOccludersActive(false);
                return;
            }

            RebuildForDebug();
        }

        private sealed class OccluderSegmentEntry
        {
            public GameObject GameObject;
            public Transform Transform;
            public MeshCollider Collider;
            public Mesh PhysicsMesh;
            public MeshFilter DebugFilter;
            public MeshRenderer DebugRenderer;
            public Vector3 LastSize;
        }

        private void Awake()
        {
            revealer = GetComponent<FogOfWarRevealer3D>();
            EnsureOccluderRoot();
        }

        private void OnDestroy()
        {
            DestroySegmentMeshes();
            if (debugMaterial != null)
            {
                Destroy(debugMaterial);
            }
        }

        private void OnDrawGizmos()
        {
            if (!drawGeneratedMesh || !Application.isPlaying || !hasValidDebugPose)
            {
                return;
            }

            if (drawSamplePoints)
            {
                Gizmos.color = drawSampleColor;
                for (var i = 0; i < lastSamplePointsWorld.Count; i++)
                {
                    Gizmos.DrawSphere(lastSamplePointsWorld[i], drawSampleRadius);
                }

                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(lastEyeWorld, drawSampleRadius * 0.75f);
            }

            if (!drawWireframeInSceneView || occluderRoot == null || !occluderRoot.activeInHierarchy)
            {
                return;
            }

            Gizmos.color = drawWireColor;
            for (var i = 0; i < activeSegmentCount; i++)
            {
                var entry = segmentPool[i];
                if (entry?.Transform == null)
                {
                    continue;
                }

                Gizmos.matrix = entry.Transform.localToWorldMatrix;
                Gizmos.DrawWireCube(Vector3.zero, entry.LastSize);
            }

            Gizmos.matrix = Matrix4x4.identity;
        }

        /// <summary>
        /// Forest FOW uses analytic ray clipping instead of these colliders.
        /// </summary>
        public void DisableForFogCalculation()
        {
            SetOccludersActive(false);
        }

        private bool ShouldMaintainBlockers()
        {
            return revealer != null && revealer.UseOcclusion && revealer.isActiveAndEnabled && !ignoresForestForLineOfSight;
        }

        private bool TryBuildOccluderShapes(bool enablePhysics, out int segmentCountBuilt)
        {
            segmentCountBuilt = 0;
            var segmentIndex = 0;
            lastSamplePointsWorld.Clear();
            hasValidDebugPose = false;

            CombatForestFogClipper.EnsureCache();
            if (!CombatForestFogClipper.HasActiveZones || revealer == null || !revealer.isActiveAndEnabled)
            {
                return false;
            }

            EnsureOccluderRoot();

            if (!TryAppendOccluderShapesForRevealer(revealer, enablePhysics, ref segmentIndex))
            {
                return false;
            }

            hasValidDebugPose = true;
            segmentCountBuilt = segmentIndex;
            activeSegmentCount = segmentIndex;
            for (var i = segmentIndex; i < segmentPool.Count; i++)
            {
                segmentPool[i].GameObject.SetActive(false);
            }

            if (enablePhysics)
            {
                SetOccludersActive(true);
                Physics.SyncTransforms();
            }
            else
            {
                EnableDebugShapesOnly(segmentIndex);
            }

            return true;
        }

        private bool TryAppendOccluderShapesForRevealer(
            FogOfWarRevealer3D unitRevealer,
            bool enablePhysics,
            ref int segmentIndex)
        {
            if (unitRevealer == null || !unitRevealer.isActiveAndEnabled)
            {
                return false;
            }

            var startSegmentIndex = segmentIndex;
            var eyeWorld = (Vector3)unitRevealer.GetEyePosition();
            var eyeLocal = occluderRoot.transform.InverseTransformPoint(eyeWorld);
            var viewRadius = Mathf.Max(unitRevealer.ViewRadius, unitRevealer.TotalRevealerRadius);
            if (viewRadius <= 0.001f)
            {
                return false;
            }

            lastEyeWorld = eyeWorld;

            var depthWorld = CombatForestFogDepth.ResolveDepthWorld();

            var groundY = eyeLocal.y;
            var angleStep = (Mathf.PI * 2f) / segmentCount;
            var anyLimitedClip = false;
            clipScratch.Clear();
            limitedScratch.Clear();
            lastSamplePointsWorld.Clear();

            for (var i = 0; i < segmentCount; i++)
            {
                var angle = angleStep * i;
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var clipDistance = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                    eyeWorld,
                    dir,
                    viewRadius,
                    depthWorld);

                clipScratch.Add(clipDistance);
                var isLimited = clipDistance < viewRadius - 0.01f;
                limitedScratch.Add(isLimited);
                anyLimitedClip |= isLimited;

                var sampleWorld = eyeWorld + (dir * clipDistance);
                if (isLimited)
                {
                    lastSamplePointsWorld.Add(sampleWorld);
                }
            }

            if (!anyLimitedClip)
            {
                return false;
            }

            var revealerSuffix = unitRevealer.GetInstanceID();
            for (var i = 0; i < segmentCount; i++)
            {
                var next = (i + 1) % segmentCount;
                if (!limitedScratch[i] || !limitedScratch[next])
                {
                    continue;
                }

                var angleA = angleStep * i;
                var angleB = angleStep * next;
                var dirA = new Vector3(Mathf.Cos(angleA), 0f, Mathf.Sin(angleA));
                var dirB = new Vector3(Mathf.Cos(angleB), 0f, Mathf.Sin(angleB));

                var baseA = eyeLocal + (dirA * clipScratch[i]);
                var baseB = eyeLocal + (dirB * clipScratch[next]);
                baseA.y = groundY;
                baseB.y = groundY;

                var outward = ((dirA + dirB) * 0.5f).normalized;
                if (outward.sqrMagnitude <= 1e-8f)
                {
                    continue;
                }

                segmentIndex = PlaceRingSegmentMesh(segmentIndex, baseA, baseB, outward, groundY, $"Ring_{revealerSuffix}_{i}", enablePhysics);
            }

            for (var i = 0; i < segmentCount; i++)
            {
                if (!IsForestBoundarySample(i))
                {
                    continue;
                }

                var angle = angleStep * i;
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var previous = (i - 1 + segmentCount) % segmentCount;
                var next = (i + 1) % segmentCount;
                var previousAngle = angleStep * previous;
                var nextAngle = angleStep * next;
                var previousDir = new Vector3(Mathf.Cos(previousAngle), 0f, Mathf.Sin(previousAngle));
                var nextDir = new Vector3(Mathf.Cos(nextAngle), 0f, Mathf.Sin(nextAngle));
                var outward = dir;
                if (!limitedScratch[previous])
                {
                    outward = ((dir + nextDir) * 0.5f).normalized;
                }
                else if (!limitedScratch[next])
                {
                    outward = ((dir + previousDir) * 0.5f).normalized;
                }

                if (outward.sqrMagnitude <= 1e-8f)
                {
                    continue;
                }

                segmentIndex = PlaceCornerCapMesh(segmentIndex, eyeLocal, dir, clipScratch[i], groundY, outward, $"Cap_{revealerSuffix}_{i}", enablePhysics);
            }

            return segmentIndex > startSegmentIndex;
        }

        private void EnableDebugShapesOnly(int segmentCount)
        {
            if (occluderRoot != null)
            {
                occluderRoot.SetActive(true);
            }

            for (var i = 0; i < segmentCount; i++)
            {
                var entry = segmentPool[i];
                if (entry?.Collider != null)
                {
                    entry.Collider.enabled = false;
                }

                if (entry?.GameObject != null)
                {
                    entry.GameObject.SetActive(true);
                }
            }
        }

        private bool IsForestBoundarySample(int index)
        {
            if (!limitedScratch[index])
            {
                return false;
            }

            var previous = (index - 1 + segmentCount) % segmentCount;
            var next = (index + 1) % segmentCount;
            return !limitedScratch[previous] || !limitedScratch[next];
        }

        private int PlaceCornerCapMesh(
            int segmentIndex,
            Vector3 eyeLocal,
            Vector3 dir,
            float clipDistance,
            float groundY,
            Vector3 outward,
            string label,
            bool enablePhysics)
        {
            var frontier = eyeLocal + (dir * clipDistance);
            frontier.y = groundY;
            var center = frontier + (outward * (wallThicknessWorld * 0.5f));
            center.y = groundY + (wallHeightWorld * 0.5f);

            var tangent = new Vector3(-dir.z, 0f, dir.x);
            if (tangent.sqrMagnitude <= 1e-8f)
            {
                return segmentIndex;
            }

            tangent.Normalize();
            var capDepth = wallThicknessWorld + segmentOverlapWorld;
            var size = new Vector3(capDepth, wallHeightWorld, capDepth);
            return PlaceSegmentMesh(segmentIndex, center, Quaternion.LookRotation(tangent, Vector3.up), size, label, enablePhysics);
        }

        private int PlaceRingSegmentMesh(
            int segmentIndex,
            Vector3 baseA,
            Vector3 baseB,
            Vector3 outward,
            float groundY,
            string label,
            bool enablePhysics)
        {
            var chord = baseB - baseA;
            var length = chord.magnitude + (segmentOverlapWorld * 2f);
            if (length <= 0.01f)
            {
                return segmentIndex;
            }

            var chordDir = chord.normalized;
            var thicknessAxis = Vector3.Cross(Vector3.up, chordDir);
            if (thicknessAxis.sqrMagnitude <= 1e-8f)
            {
                return segmentIndex;
            }

            thicknessAxis.Normalize();
            if (Vector3.Dot(thicknessAxis, outward) < 0f)
            {
                thicknessAxis = -thicknessAxis;
            }

            var center = (baseA + baseB) * 0.5f;
            center.y = groundY + (wallHeightWorld * 0.5f);
            center += thicknessAxis * (wallThicknessWorld * 0.5f);

            var size = new Vector3(wallThicknessWorld, wallHeightWorld, length);
            return PlaceSegmentMesh(segmentIndex, center, Quaternion.LookRotation(chordDir, Vector3.up), size, label, enablePhysics);
        }

        private int PlaceSegmentMesh(
            int segmentIndex,
            Vector3 localCenter,
            Quaternion localRotation,
            Vector3 size,
            string label,
            bool enablePhysics)
        {
            var entry = GetOrCreateSegment(segmentIndex, label);
            entry.Transform.localPosition = localCenter;
            entry.Transform.localRotation = localRotation;
            entry.LastSize = size;
            PopulateBoxMesh(entry.PhysicsMesh, size, doubleSided: true);
            entry.Collider.enabled = enablePhysics;
            if (enablePhysics)
            {
                entry.Collider.sharedMesh = null;
                entry.Collider.sharedMesh = entry.PhysicsMesh;
            }

            if (drawGeneratedMesh && entry.DebugFilter != null)
            {
                entry.DebugFilter.sharedMesh = entry.PhysicsMesh;
                if (entry.DebugRenderer != null && debugMaterial != null)
                {
                    entry.DebugRenderer.sharedMaterial = debugMaterial;
                    entry.DebugRenderer.enabled = true;
                }
            }
            else if (entry.DebugRenderer != null)
            {
                entry.DebugRenderer.enabled = false;
            }

            entry.GameObject.SetActive(true);
            return segmentIndex + 1;
        }

        private OccluderSegmentEntry GetOrCreateSegment(int index, string label)
        {
            while (segmentPool.Count <= index)
            {
                var go = new GameObject($"ForestFogOccluderMesh_{segmentPool.Count}");
                go.transform.SetParent(occluderRoot.transform, false);
                go.layer = GetFogOccluderLayer();
                go.AddComponent<CombatForestFogBlocker>();

                var physicsMesh = new Mesh
                {
                    name = $"ForestFogOccluderPhysics_{segmentPool.Count}"
                };

                var collider = go.AddComponent<MeshCollider>();
                collider.convex = false;

                MeshFilter debugFilter = null;
                MeshRenderer debugRenderer = null;
                if (drawGeneratedMesh)
                {
                    var debugGo = new GameObject("DebugVisual");
                    debugGo.transform.SetParent(go.transform, false);
                    debugGo.layer = LayerMask.NameToLayer("Default");
                    debugFilter = debugGo.AddComponent<MeshFilter>();
                    debugRenderer = debugGo.AddComponent<MeshRenderer>();
                    debugRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    debugRenderer.receiveShadows = false;
                    debugRenderer.lightProbeUsage = LightProbeUsage.Off;
                    debugRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    EnsureDebugMaterial();
                    if (debugMaterial != null)
                    {
                        debugRenderer.sharedMaterial = debugMaterial;
                    }
                }

                segmentPool.Add(new OccluderSegmentEntry
                {
                    GameObject = go,
                    Transform = go.transform,
                    Collider = collider,
                    PhysicsMesh = physicsMesh,
                    DebugFilter = debugFilter,
                    DebugRenderer = debugRenderer
                });
            }

            segmentPool[index].GameObject.name = label;
            return segmentPool[index];
        }

        private void PopulateBoxMesh(Mesh mesh, Vector3 size, bool doubleSided)
        {
            mesh.Clear();

            var hx = size.x * 0.5f;
            var hy = size.y * 0.5f;
            var hz = size.z * 0.5f;

            vertexScratch.Clear();
            triangleScratch.Clear();

            vertexScratch.Add(new Vector3(-hx, -hy, -hz));
            vertexScratch.Add(new Vector3(hx, -hy, -hz));
            vertexScratch.Add(new Vector3(hx, hy, -hz));
            vertexScratch.Add(new Vector3(-hx, hy, -hz));
            vertexScratch.Add(new Vector3(-hx, -hy, hz));
            vertexScratch.Add(new Vector3(hx, -hy, hz));
            vertexScratch.Add(new Vector3(hx, hy, hz));
            vertexScratch.Add(new Vector3(-hx, hy, hz));

            AddBoxFace(0, 1, 2, 3, doubleSided);
            AddBoxFace(5, 4, 7, 6, doubleSided);
            AddBoxFace(4, 0, 3, 7, doubleSided);
            AddBoxFace(1, 5, 6, 2, doubleSided);
            AddBoxFace(3, 2, 6, 7, doubleSided);
            AddBoxFace(4, 5, 1, 0, doubleSided);

            mesh.SetVertices(vertexScratch);
            mesh.SetTriangles(triangleScratch, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
        }

        private void AddBoxFace(int i0, int i1, int i2, int i3, bool doubleSided)
        {
            triangleScratch.Add(i0);
            triangleScratch.Add(i1);
            triangleScratch.Add(i2);
            triangleScratch.Add(i0);
            triangleScratch.Add(i2);
            triangleScratch.Add(i3);

            if (!doubleSided)
            {
                return;
            }

            triangleScratch.Add(i0);
            triangleScratch.Add(i2);
            triangleScratch.Add(i1);
            triangleScratch.Add(i0);
            triangleScratch.Add(i3);
            triangleScratch.Add(i2);
        }

        private void EnsureOccluderRoot()
        {
            if (occluderRoot != null)
            {
                return;
            }

            occluderRoot = new GameObject("ForestFogBlockers");
            occluderRoot.transform.SetParent(transform, false);
            occluderRoot.transform.localPosition = Vector3.zero;
            occluderRoot.transform.localRotation = Quaternion.identity;
            occluderRoot.transform.localScale = Vector3.one;
            occluderRoot.layer = GetFogOccluderLayer();
            occluderRoot.SetActive(false);
        }

        private void EnsureDebugMaterial()
        {
            if (debugMaterial != null)
            {
                return;
            }

            debugMaterial = CreateDebugMaterial();
        }

        private static Material CreateDebugMaterial()
        {
            var shader = Shader.Find("Sprites/Default")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color");
            if (shader == null)
            {
                return null;
            }

            var material = new Material(shader)
            {
                color = new Color(0.15f, 0.85f, 0.25f, 0.5f)
            };
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        private void SetOccludersActive(bool active)
        {
            if (occluderRoot != null)
            {
                occluderRoot.SetActive(active);
            }

            if (!active)
            {
                activeSegmentCount = 0;
                for (var i = 0; i < segmentPool.Count; i++)
                {
                    segmentPool[i].GameObject.SetActive(false);
                }
            }
        }

        private void DestroySegmentMeshes()
        {
            for (var i = 0; i < segmentPool.Count; i++)
            {
                if (segmentPool[i].PhysicsMesh != null)
                {
                    Destroy(segmentPool[i].PhysicsMesh);
                }
            }
        }

        private static int GetFogOccluderLayer()
        {
            var layer = LayerMask.NameToLayer(CombatLayers.FogOccluderLayerName);
            return layer >= 0 ? layer : 6;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (debugMaterial != null)
            {
                debugMaterial.color = drawMeshColor;
            }
        }
#endif
    }
}
