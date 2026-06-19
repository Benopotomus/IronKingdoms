using System.Collections.Generic;
using FOW;
using Unity.Mathematics;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Draws GPU upload data plus the chord segments the fog shader actually uses at walls.
    /// </summary>
    internal sealed class CombatFogShaderUploadPolygonDrawer
    {
        private const float ChordMinDistSq = 0.0225f;
        private const float DistanceEpsilonWorld = 0.01f;

        private readonly List<Vector3> baselinePointsWorld = new();
        private readonly List<Vector3> terrainClippedPointsWorld = new();
        private readonly List<(Vector3 start, Vector3 end)> baselineWallChordSegmentsWorld = new();
        private readonly List<Vector3> effectiveBoundaryPointsWorld = new();

        private Vector3 eyeWorld;

        public bool HasData { get; private set; }
        public int BaselineSegmentCount => baselinePointsWorld.Count;
        public int TerrainSegmentCount => terrainClippedPointsWorld.Count;
        public int BaselineWallChordCount => baselineWallChordSegmentsWorld.Count;

        public void Capture(
            float2[] baselineDirections,
            float[] baselineUploadLengths,
            int baselineCount,
            float2[] terrainDirections,
            float[] terrainUploadLengths,
            int terrainCount,
            Vector3 sourceEyeWorld,
            float totalRevealerRadius,
            bool circleIsComplete,
            FogOfWarRevealer3D.PlaneProjection projection)
        {
            baselinePointsWorld.Clear();
            terrainClippedPointsWorld.Clear();
            baselineWallChordSegmentsWorld.Clear();
            effectiveBoundaryPointsWorld.Clear();
            eyeWorld = sourceEyeWorld;
            HasData = false;

            if (TryBuildPolygon(
                    baselineDirections,
                    baselineUploadLengths,
                    baselineCount,
                    sourceEyeWorld,
                    totalRevealerRadius,
                    projection,
                    baselinePointsWorld))
            {
                HasData = true;
                BuildBaselineWallChords(
                    baselineDirections,
                    baselineUploadLengths,
                    baselineCount,
                    sourceEyeWorld,
                    totalRevealerRadius,
                    projection);
                BuildEffectiveBoundary(
                    baselineDirections,
                    baselineUploadLengths,
                    baselineCount,
                    sourceEyeWorld,
                    totalRevealerRadius,
                    circleIsComplete,
                    projection);
            }

            if (terrainDirections != null && terrainUploadLengths != null && terrainCount > 0)
            {
                for (var i = 0; i < terrainCount; i++)
                {
                    if (!TryUploadSegmentToWorld(
                            terrainDirections[i],
                            terrainUploadLengths[i],
                            sourceEyeWorld,
                            totalRevealerRadius,
                            projection,
                            out var pointWorld,
                            out var clipped)
                        || !clipped)
                    {
                        continue;
                    }

                    terrainClippedPointsWorld.Add(pointWorld);
                    HasData = true;
                }
            }
        }

        public void ApplyGameViewLines(
            LineRenderer baselineLoopLine,
            LineRenderer terrainLoopLine,
            LineRenderer terrainClipTicksLine,
            LineRenderer baselineChordLine,
            LineRenderer effectiveBoundaryLine,
            float lineYBoostWorld)
        {
            var boost = Vector3.up * lineYBoostWorld;
            ApplyLoop(baselineLoopLine, baselinePointsWorld, boost);
            if (terrainLoopLine != null)
            {
                terrainLoopLine.positionCount = 0;
            }
            ApplyChordSegments(baselineChordLine, baselineWallChordSegmentsWorld, boost);
            ApplyLoop(effectiveBoundaryLine, effectiveBoundaryPointsWorld, boost);

            if (terrainClipTicksLine == null)
            {
                return;
            }

            if (terrainClippedPointsWorld.Count == 0)
            {
                terrainClipTicksLine.positionCount = 0;
                return;
            }

            terrainClipTicksLine.positionCount = terrainClippedPointsWorld.Count * 2;
            var writeIndex = 0;
            for (var i = 0; i < terrainClippedPointsWorld.Count; i++)
            {
                terrainClipTicksLine.SetPosition(writeIndex++, eyeWorld + boost);
                terrainClipTicksLine.SetPosition(writeIndex++, terrainClippedPointsWorld[i] + boost);
            }
        }

        public void DrawRuntimeLines(
            Color baselineColor,
            Color terrainColor,
            Color terrainClipColor,
            Color baselineChordColor,
            Color effectiveBoundaryColor)
        {
            if (!HasData)
            {
                return;
            }

            DrawLoopRuntime(baselinePointsWorld, baselineColor);
            DrawLoopRuntime(effectiveBoundaryPointsWorld, effectiveBoundaryColor);
            DrawChordRuntime(baselineWallChordSegmentsWorld, baselineChordColor);

            for (var i = 0; i < terrainClippedPointsWorld.Count; i++)
            {
                Debug.DrawLine(eyeWorld, terrainClippedPointsWorld[i], terrainColor, 0f, false);
            }
        }

        public void DrawGizmos(
            Color baselineColor,
            Color terrainColor,
            Color terrainClipColor,
            Color baselineChordColor,
            Color effectiveBoundaryColor)
        {
            if (!HasData)
            {
                return;
            }

            DrawLoopGizmos(baselinePointsWorld, baselineColor);
            DrawLoopGizmos(effectiveBoundaryPointsWorld, effectiveBoundaryColor);
            DrawChordGizmos(baselineWallChordSegmentsWorld, baselineChordColor);

            Gizmos.color = terrainColor;
            for (var i = 0; i < terrainClippedPointsWorld.Count; i++)
            {
                Gizmos.DrawLine(eyeWorld, terrainClippedPointsWorld[i]);
            }
        }

        public static LineRenderer CreateLoopLineRenderer(
            Transform parent,
            string name,
            Color color,
            float width,
            bool loop)
        {
            var lineObject = new GameObject(name);
            lineObject.transform.SetParent(parent, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = loop;
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

        private void BuildBaselineWallChords(
            float2[] directions,
            float[] uploadLengths,
            int count,
            Vector3 sourceEyeWorld,
            float totalRevealerRadius,
            FogOfWarRevealer3D.PlaneProjection projection)
        {
            if (count < 2)
            {
                return;
            }

            for (var i = 0; i < count; i++)
            {
                var next = (i + 1) % count;
                if (!TryGetUploadSegment(
                        directions[i],
                        uploadLengths[i],
                        totalRevealerRadius,
                        out var prevDir,
                        out var prevLen,
                        out var prevCutShort)
                    || !TryGetUploadSegment(
                        directions[next],
                        uploadLengths[next],
                        totalRevealerRadius,
                        out var currDir,
                        out var currLen,
                        out var currCutShort))
                {
                    continue;
                }

                if (!prevCutShort || !currCutShort)
                {
                    continue;
                }

                var start = prevDir * prevLen;
                var end = currDir * currLen;
                if (math.dot(end - start, end - start) <= ChordMinDistSq)
                {
                    continue;
                }

                var startWorld = sourceEyeWorld
                    + (CombatFogProjection.Direction2DToWorld(prevDir, projection) * prevLen);
                var endWorld = sourceEyeWorld
                    + (CombatFogProjection.Direction2DToWorld(currDir, projection) * currLen);
                baselineWallChordSegmentsWorld.Add((startWorld, endWorld));
            }
        }

        private void BuildEffectiveBoundary(
            float2[] directions,
            float[] uploadLengths,
            int count,
            Vector3 sourceEyeWorld,
            float totalRevealerRadius,
            bool circleIsComplete,
            FogOfWarRevealer3D.PlaneProjection projection)
        {
            if (count < 2)
            {
                return;
            }

            var segments = new List<RaycastRevealer.SightSegment>(count);
            for (var i = 0; i < count; i++)
            {
                if (!TryGetUploadSegment(
                        directions[i],
                        uploadLengths[i],
                        totalRevealerRadius,
                        out var dir,
                        out var len,
                        out var cutShort))
                {
                    continue;
                }

                segments.Add(new RaycastRevealer.SightSegment
                {
                    Direction = dir,
                    Radius = cutShort ? len : totalRevealerRadius,
                    DidHit = cutShort,
                });
            }

            if (segments.Count < 2)
            {
                return;
            }

            const int sampleCount = 360;
            for (var i = 0; i < sampleCount; i++)
            {
                var angle = (i / (float)sampleCount) * math.PI * 2f;
                var queryDir = math.normalize(new float2(math.cos(angle), math.sin(angle)));
                var boundaryDistance = CombatFogSparsePolygonQuery.GetBoundaryDistance(
                    segments,
                    queryDir,
                    totalRevealerRadius,
                    circleIsComplete);
                if (boundaryDistance > totalRevealerRadius + 0.5f)
                {
                    continue;
                }

                effectiveBoundaryPointsWorld.Add(
                    sourceEyeWorld
                    + (CombatFogProjection.Direction2DToWorld(queryDir, projection) * boundaryDistance));
            }
        }

        private static bool TryBuildPolygon(
            float2[] directions,
            float[] uploadLengths,
            int count,
            Vector3 sourceEyeWorld,
            float totalRevealerRadius,
            FogOfWarRevealer3D.PlaneProjection projection,
            List<Vector3> pointsWorld,
            List<Vector3> clippedPointsWorld = null)
        {
            if (directions == null || uploadLengths == null || count < 2)
            {
                return false;
            }

            var wroteAny = false;
            for (var i = 0; i < count; i++)
            {
                if (!TryUploadSegmentToWorld(
                        directions[i],
                        uploadLengths[i],
                        sourceEyeWorld,
                        totalRevealerRadius,
                        projection,
                        out var pointWorld,
                        out var clipped))
                {
                    continue;
                }

                pointsWorld.Add(pointWorld);
                wroteAny = true;
                if (clipped && clippedPointsWorld != null)
                {
                    clippedPointsWorld.Add(pointWorld);
                }
            }

            return wroteAny;
        }

        private static bool TryGetUploadSegment(
            float2 direction2D,
            float uploadLength,
            float totalRevealerRadius,
            out float2 direction,
            out float length,
            out bool cutShort)
        {
            direction = default;
            length = 0f;
            cutShort = false;
            if (math.lengthsq(direction2D) <= 1e-8f)
            {
                return false;
            }

            direction = math.normalize(direction2D);
            cutShort = uploadLength <= totalRevealerRadius - DistanceEpsilonWorld;
            length = cutShort ? uploadLength : math.min(totalRevealerRadius, uploadLength - 1f);
            return true;
        }

        private static bool TryUploadSegmentToWorld(
            float2 direction2D,
            float uploadLength,
            Vector3 sourceEyeWorld,
            float totalRevealerRadius,
            FogOfWarRevealer3D.PlaneProjection projection,
            out Vector3 pointWorld,
            out bool clipped)
        {
            pointWorld = default;
            clipped = false;
            if (!TryGetUploadSegment(direction2D, uploadLength, totalRevealerRadius, out var direction, out var length, out clipped))
            {
                return false;
            }

            pointWorld = sourceEyeWorld
                + (CombatFogProjection.Direction2DToWorld(direction, projection) * length);
            return true;
        }

        private static void ApplyLoop(LineRenderer line, IReadOnlyList<Vector3> points, Vector3 boost)
        {
            if (line == null)
            {
                return;
            }

            if (points.Count < 2)
            {
                line.positionCount = 0;
                return;
            }

            line.positionCount = points.Count + 1;
            for (var i = 0; i < points.Count; i++)
            {
                line.SetPosition(i, points[i] + boost);
            }

            line.SetPosition(points.Count, points[0] + boost);
        }

        private static void ApplyChordSegments(
            LineRenderer line,
            IReadOnlyList<(Vector3 start, Vector3 end)> segments,
            Vector3 boost)
        {
            if (line == null)
            {
                return;
            }

            if (segments.Count == 0)
            {
                line.positionCount = 0;
                return;
            }

            line.loop = false;
            line.positionCount = segments.Count * 2;
            var writeIndex = 0;
            for (var i = 0; i < segments.Count; i++)
            {
                line.SetPosition(writeIndex++, segments[i].start + boost);
                line.SetPosition(writeIndex++, segments[i].end + boost);
            }
        }

        private static void DrawLoopGizmos(IReadOnlyList<Vector3> points, Color color)
        {
            if (points.Count < 2)
            {
                return;
            }

            Gizmos.color = color;
            for (var i = 1; i < points.Count; i++)
            {
                Gizmos.DrawLine(points[i - 1], points[i]);
            }

            Gizmos.DrawLine(points[points.Count - 1], points[0]);
        }

        private static void DrawChordGizmos(IReadOnlyList<(Vector3 start, Vector3 end)> segments, Color color)
        {
            Gizmos.color = color;
            for (var i = 0; i < segments.Count; i++)
            {
                Gizmos.DrawLine(segments[i].start, segments[i].end);
            }
        }

        private static void DrawLoopRuntime(IReadOnlyList<Vector3> points, Color color)
        {
            if (points.Count < 2)
            {
                return;
            }

            for (var i = 1; i < points.Count; i++)
            {
                Debug.DrawLine(points[i - 1], points[i], color, 0f, false);
            }

            Debug.DrawLine(points[points.Count - 1], points[0], color, 0f, false);
        }

        private static void DrawChordRuntime(IReadOnlyList<(Vector3 start, Vector3 end)> segments, Color color)
        {
            for (var i = 0; i < segments.Count; i++)
            {
                Debug.DrawLine(segments[i].start, segments[i].end, color, 0f, false);
            }
        }
    }
}
