using System.Collections.Generic;
using FOW;
using Unity.Mathematics;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Per-frame proof stats from the two-pass combine. Wall-blocked rays should always preserve
    /// pass-1 distance; terrain may only shorten open rays.
    /// </summary>
    public struct CombatForestFogWallBaselineReport
    {
        public int DenseRayCount;
        public int SparseWallSegmentCount;
        public int WallBlockedRayCount;
        public int WallPreservedRayCount;
        public int WallViolationCount;
        public int TerrainClippedOpenRayCount;
        public int ForestIgnoredAtWallRayCount;
        public int FinalSparseSegmentCount;
        public float MaxWallViolationDistanceWorld;

        public bool AllWallBlockedRaysPreserved => WallViolationCount == 0;

        public bool HasData => SparseWallSegmentCount > 0 || FinalSparseSegmentCount > 0;

        public bool ForestPassApplied { get; set; }

        public string SummaryLine =>
            !HasData
                ? "No wall baseline data yet (select unit, refresh LOS)"
                : !ForestPassApplied
                    ? $"Forest OFF — {FinalSparseSegmentCount} baseline verts uploaded"
                    : AllWallBlockedRaysPreserved
                        ? $"Walls OK: {WallPreservedRayCount}/{WallBlockedRayCount} (baseline untouched, terrain in shader)"
                        : $"WALL MISMATCH: {WallViolationCount}/{WallBlockedRayCount} wall verts changed";

        public string DetailLine =>
            !ForestPassApplied
                ? "Magenta loop = current stock FindEdges upload (reference snapshot)"
                : $"Shader upload: {SparseWallSegmentCount} baseline + {TerrainClippedOpenRayCount} clipped of {FinalSparseSegmentCount - SparseWallSegmentCount} terrain rays";
    }

    /// <summary>
    /// Draws pass-1 sparse wall polygon and dense wall-hit samples for visual comparison.
    /// </summary>
    internal sealed class CombatForestFogWallBaselineProofDrawer
    {
        private readonly List<Vector3> sparseWallPointsWorld = new();
        private readonly List<Vector3> denseWallHitPointsWorld = new();
        private readonly List<Vector3> violationPointsWorld = new();

        private Vector3 eyeWorld;

        public bool HasData { get; private set; }

        public void Capture(
            IReadOnlyList<RaycastRevealer.SightSegment> wallPassSegments,
            RaycastRevealer.SightSegment[] combinedViewPoints,
            int combinedCount,
            Vector3 sourceEyeWorld,
            float maxRadius,
            bool circleIsComplete,
            FogOfWarRevealer3D.PlaneProjection projection)
        {
            sparseWallPointsWorld.Clear();
            denseWallHitPointsWorld.Clear();
            violationPointsWorld.Clear();
            eyeWorld = sourceEyeWorld;
            HasData = false;

            if (wallPassSegments == null || wallPassSegments.Count == 0)
            {
                return;
            }

            for (var i = 0; i < wallPassSegments.Count; i++)
            {
                var segment = wallPassSegments[i];
                if (!TrySegmentToWorld(segment, sourceEyeWorld, projection, out var pointWorld))
                {
                    continue;
                }

                sparseWallPointsWorld.Add(pointWorld);
                HasData = true;
            }

            if (combinedViewPoints == null || combinedCount <= 0)
            {
                return;
            }

            const float distanceEpsilon = 0.01f;
            var extraRadius = FogOfWarWorld.instance != null
                ? FogOfWarWorld.instance.SightExtraAmount
                : 0f;
            for (var i = 0; i < combinedCount; i++)
            {
                ref var segment = ref combinedViewPoints[i];
                if (!segment.DidHit)
                {
                    continue;
                }

                var dir2 = Unity.Mathematics.math.normalize(segment.Direction);
                var wallDistance = CombatFogSparsePolygonQuery.GetBoundaryDistance(
                    wallPassSegments,
                    dir2,
                    maxRadius,
                    circleIsComplete,
                    extraRadius);
                var wallBlocks = wallDistance <= maxRadius - distanceEpsilon;
                if (!wallBlocks)
                {
                    continue;
                }

                if (!TrySegmentToWorld(segment, sourceEyeWorld, projection, out var hitWorld))
                {
                    continue;
                }

                denseWallHitPointsWorld.Add(hitWorld);
                if (math.abs(segment.Radius - wallDistance) > distanceEpsilon)
                {
                    violationPointsWorld.Add(hitWorld);
                }
            }
        }

        public void DrawRuntime(
            Color sparseWallColor,
            Color denseWallHitColor,
            Color violationColor)
        {
            if (!HasData)
            {
                return;
            }

            DrawClosedLoop(sparseWallPointsWorld, sparseWallColor);
            for (var i = 0; i < denseWallHitPointsWorld.Count; i++)
            {
                Debug.DrawLine(eyeWorld, denseWallHitPointsWorld[i], denseWallHitColor, 0f, false);
            }

            for (var i = 0; i < violationPointsWorld.Count; i++)
            {
                Debug.DrawLine(eyeWorld, violationPointsWorld[i], violationColor, 0f, false);
            }
        }

        public void ApplyGameViewLines(
            LineRenderer loopLine,
            LineRenderer wallHitLine,
            float lineYBoostWorld)
        {
            if (loopLine == null)
            {
                return;
            }

            if (!HasData)
            {
                loopLine.positionCount = 0;
                if (wallHitLine != null)
                {
                    wallHitLine.positionCount = 0;
                }

                return;
            }

            var boost = Vector3.up * lineYBoostWorld;
            var loopCount = sparseWallPointsWorld.Count;
            if (loopCount > 1)
            {
                loopLine.positionCount = loopCount + 1;
                for (var i = 0; i < loopCount; i++)
                {
                    loopLine.SetPosition(i, sparseWallPointsWorld[i] + boost);
                }

                loopLine.SetPosition(loopCount, sparseWallPointsWorld[0] + boost);
            }
            else
            {
                loopLine.positionCount = 0;
            }

            if (wallHitLine == null)
            {
                return;
            }

            if (denseWallHitPointsWorld.Count == 0)
            {
                wallHitLine.positionCount = 0;
                return;
            }

            wallHitLine.positionCount = denseWallHitPointsWorld.Count * 2;
            var writeIndex = 0;
            for (var i = 0; i < denseWallHitPointsWorld.Count; i++)
            {
                wallHitLine.SetPosition(writeIndex++, eyeWorld + boost);
                wallHitLine.SetPosition(writeIndex++, denseWallHitPointsWorld[i] + boost);
            }
        }

        public static LineRenderer CreateProofLineRenderer(Transform parent, string name, Color color, float width)
        {
            var lineObject = new GameObject(name);
            lineObject.transform.SetParent(parent, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = color;
            line.endColor = color;
            line.startWidth = width;
            line.endWidth = width;
            line.positionCount = 0;
            return line;
        }

        public void DrawGizmos(
            Color sparseWallColor,
            Color denseWallHitColor,
            Color violationColor)
        {
            if (!HasData)
            {
                return;
            }

            Gizmos.color = sparseWallColor;
            for (var i = 1; i < sparseWallPointsWorld.Count; i++)
            {
                Gizmos.DrawLine(sparseWallPointsWorld[i - 1], sparseWallPointsWorld[i]);
            }

            if (sparseWallPointsWorld.Count > 1)
            {
                Gizmos.DrawLine(
                    sparseWallPointsWorld[sparseWallPointsWorld.Count - 1],
                    sparseWallPointsWorld[0]);
            }

            Gizmos.color = denseWallHitColor;
            for (var i = 0; i < denseWallHitPointsWorld.Count; i++)
            {
                Gizmos.DrawLine(eyeWorld, denseWallHitPointsWorld[i]);
                Gizmos.DrawSphere(denseWallHitPointsWorld[i], 0.03f);
            }

            Gizmos.color = violationColor;
            for (var i = 0; i < violationPointsWorld.Count; i++)
            {
                Gizmos.DrawSphere(violationPointsWorld[i], 0.08f);
            }
        }

        private static void DrawClosedLoop(IReadOnlyList<Vector3> points, Color color)
        {
            for (var i = 1; i < points.Count; i++)
            {
                Debug.DrawLine(points[i - 1], points[i], color, 0f, false);
            }

            if (points.Count > 1)
            {
                Debug.DrawLine(points[points.Count - 1], points[0], color, 0f, false);
            }
        }

        private static bool TrySegmentToWorld(
            RaycastRevealer.SightSegment segment,
            Vector3 sourceEyeWorld,
            FogOfWarRevealer3D.PlaneProjection projection,
            out Vector3 pointWorld)
        {
            pointWorld = default;
            var direction2D = segment.Direction;
            if (Unity.Mathematics.math.lengthsq(direction2D) <= 1e-8f)
            {
                return false;
            }

            direction2D = Unity.Mathematics.math.normalize(direction2D);
            pointWorld = sourceEyeWorld
                + (CombatFogProjection.Direction2DToWorld(direction2D, projection) * segment.Radius);
            return true;
        }
    }
}
