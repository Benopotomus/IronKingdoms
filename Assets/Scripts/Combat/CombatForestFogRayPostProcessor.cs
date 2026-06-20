using System.Collections.Generic;
using FOW;
using Unity.Mathematics;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Pass 1 (baseline): stock FindEdges walls, uploaded unchanged.
    /// Pass 2 (terrain): dense phase-1 ray grid with forest/cloud clip only (no wall tests).
    /// Shader combines baseline walls with this dense terrain mesh.
    /// </summary>
    internal sealed class CombatForestFogRayPostProcessor
    {
        private const float DistanceEpsilonWorld = 0.01f;

        private readonly HashSet<int> bridgedRayIndices = new();
        private readonly List<float> terrainClipDistances = new();
        private readonly List<RaycastRevealer.SightSegment> wallPassSegments = new();
        private readonly List<float2> terrainClipDirections = new();
        private readonly List<float> terrainClipUploadDistances = new();
        private float2[] terrainClipDirectionsArray = System.Array.Empty<float2>();
        private float[] terrainClipUploadDistancesArray = System.Array.Empty<float>();

        public HashSet<int> BridgedRayIndices => bridgedRayIndices;
        public IReadOnlyList<RaycastRevealer.SightSegment> WallPassSegments => wallPassSegments;
        public CombatForestFogWallBaselineReport LastWallBaselineReport { get; private set; }
        public int TerrainClipSegmentCount => terrainClipDirections.Count;
        public float2[] TerrainClipDirections => terrainClipDirectionsArray;
        public float[] TerrainClipUploadDistances => terrainClipUploadDistancesArray;

        public void SetLastWallBaselineReport(CombatForestFogWallBaselineReport report)
        {
            LastWallBaselineReport = report;
        }

        public void ClearDebugState()
        {
            bridgedRayIndices.Clear();
        }

        public void ClearTerrainClipUpload()
        {
            terrainClipDirections.Clear();
            terrainClipUploadDistances.Clear();
            terrainClipDirectionsArray = System.Array.Empty<float2>();
            terrainClipUploadDistancesArray = System.Array.Empty<float>();
        }

        public void BuildTerrainClipSegmentsForShader(
            RaycastRevealer.SightSegment[] viewPoints,
            int baselineSegmentCount,
            RaycastRevealer.SightIteration firstIteration,
            int stepCount,
            Vector3 eyeWorld,
            float maxRadius,
            float originRadiusWorld,
            FogOfWarRevealer3D.PlaneProjection projection,
            bool baseIntersectsForest,
            bool baseIntersectsCloud,
            bool circleIsComplete,
            int maxUploadSegments,
            bool collectDebugState,
            bool applyForestClip,
            bool applyBlockingClip,
            int desiredLutSampleCount = -1,
            bool skipTerrainPostFilters = false)
        {
            bridgedRayIndices.Clear();
            terrainClipDirections.Clear();
            terrainClipUploadDistances.Clear();
            terrainClipDistances.Clear();

            if (stepCount <= 0 || viewPoints == null || baselineSegmentCount <= 0)
            {
                ClearTerrainClipUpload();
                LastWallBaselineReport = default;
                return;
            }

            SnapshotWallPass(viewPoints, baselineSegmentCount);

            var depthWorld = CombatForestFogDepth.ResolveDepthWorld();
            if (collectDebugState)
            {
                for (var i = 0; i < stepCount; i++)
                {
                    terrainClipDistances.Add(maxRadius);
                }

                ComputeForestClipDistances(
                    firstIteration,
                    stepCount,
                    eyeWorld,
                    maxRadius,
                    originRadiusWorld,
                    depthWorld,
                    baseIntersectsForest,
                    baseIntersectsCloud,
                    projection);
            }

            var lutSampleCount = CombatFogEffectiveBoundarySampler.ResolveTerrainLutUploadCount(
                baselineSegmentCount,
                maxUploadSegments,
                desiredLutSampleCount);
            UploadAngularClipperLutForShader(
                eyeWorld,
                maxRadius,
                originRadiusWorld,
                depthWorld,
                projection,
                lutSampleCount,
                applyForestClip,
                applyBlockingClip,
                skipTerrainPostFilters);

            var report = new CombatForestFogWallBaselineReport
            {
                DenseRayCount = terrainClipDirections.Count,
                SparseWallSegmentCount = baselineSegmentCount,
                FinalSparseSegmentCount = baselineSegmentCount + terrainClipDirections.Count,
                ForestPassApplied = true,
            };

            for (var i = 0; i < baselineSegmentCount; i++)
            {
                if (!IsWallHit(viewPoints[i], maxRadius))
                {
                    continue;
                }

                report.WallBlockedRayCount++;
                report.WallPreservedRayCount++;
            }

            for (var i = 0; i < terrainClipDistances.Count; i++)
            {
                if (terrainClipDistances[i] < maxRadius - DistanceEpsilonWorld)
                {
                    report.TerrainClippedOpenRayCount++;
                }
            }
            report.WallViolationCount = 0;
            report.MaxWallViolationDistanceWorld = 0f;
            LastWallBaselineReport = report;

            CopyTerrainClipUploadArrays();
        }

        private void CopyTerrainClipUploadArrays()
        {
            var count = terrainClipDirections.Count;
            if (terrainClipDirectionsArray.Length != count)
            {
                terrainClipDirectionsArray = new float2[count];
            }

            if (terrainClipUploadDistancesArray.Length != count)
            {
                terrainClipUploadDistancesArray = new float[count];
            }

            for (var i = 0; i < count; i++)
            {
                terrainClipDirectionsArray[i] = terrainClipDirections[i];
                terrainClipUploadDistancesArray[i] = terrainClipUploadDistances[i];
            }
        }

        public CombatForestFogWallBaselineReport BuildBaselineOnlyReport(
            RaycastRevealer.SightSegment[] viewPoints,
            int numberOfPoints,
            float maxRadius)
        {
            var report = new CombatForestFogWallBaselineReport
            {
                SparseWallSegmentCount = numberOfPoints,
                FinalSparseSegmentCount = numberOfPoints,
                ForestPassApplied = false,
            };

            for (var i = 0; i < numberOfPoints; i++)
            {
                if (!IsWallHit(viewPoints[i], maxRadius))
                {
                    continue;
                }

                report.WallBlockedRayCount++;
                report.WallPreservedRayCount++;
            }

            return report;
        }

        public void SnapshotWallPassForProof(RaycastRevealer.SightSegment[] viewPoints, int wallPassCount)
        {
            SnapshotWallPass(viewPoints, wallPassCount);
        }

        private void ComputeForestClipDistances(
            RaycastRevealer.SightIteration firstIteration,
            int stepCount,
            Vector3 eyeWorld,
            float maxRadius,
            float originRadiusWorld,
            float depthWorld,
            bool baseIntersectsForest,
            bool baseIntersectsCloud,
            FogOfWarRevealer3D.PlaneProjection projection)
        {
            ComputeForestClipDistances(
                firstIteration,
                stepCount,
                eyeWorld,
                maxRadius,
                originRadiusWorld,
                depthWorld,
                baseIntersectsForest,
                baseIntersectsCloud,
                projection,
                terrainClipDistances);
        }

        /// <summary>
        /// Forest/cloud clip per dense ray — same clipper calls as the pre-shader forest pass.
        /// Does not read or write wall physics hits.
        /// </summary>
        private static void ComputeForestClipDistances(
            RaycastRevealer.SightIteration firstIteration,
            int stepCount,
            Vector3 eyeWorld,
            float maxRadius,
            float originRadiusWorld,
            float depthWorld,
            bool baseIntersectsForest,
            bool baseIntersectsCloud,
            FogOfWarRevealer3D.PlaneProjection projection,
            List<float> clipDistances)
        {
            for (var i = 0; i < stepCount; i++)
            {
                if (!TryGetRayDirections(firstIteration, i, projection, out _, out var dir3))
                {
                    continue;
                }

                var forestClip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                    eyeWorld,
                    dir3,
                    maxRadius,
                    depthWorld,
                    originRadiusWorld);
                var blockingClip = CombatBlockingTerrainClipper.GetFogClipDistanceWorld(
                    eyeWorld,
                    dir3,
                    maxRadius,
                    originRadiusWorld);
                clipDistances[i] = Mathf.Min(forestClip, blockingClip);
            }
        }

        private static readonly float[] AngularClipperLutScratch = new float[CombatForestFogAngularClipperLut.SampleCount];

        /// <summary>
        /// Builds a uniform LUT then reduces it to sparse terrain segments with edge/corner refinement.
        /// </summary>
        private void UploadAngularClipperLutForShader(
            Vector3 eyeWorld,
            float maxRadius,
            float originRadiusWorld,
            float depthWorld,
            FogOfWarRevealer3D.PlaneProjection projection,
            int sampleCount,
            bool applyForestClip = true,
            bool applyBlockingClip = true,
            bool skipTerrainPostFilters = false)
        {
            if (sampleCount < 2)
            {
                return;
            }

            var scratchLength = math.min(CombatForestFogAngularClipperLut.SampleCount, AngularClipperLutScratch.Length);
            var lutCount = math.min(sampleCount, scratchLength);
            CombatForestFogAngularClipperLut.BuildSmoothedClipDistances(
                eyeWorld,
                maxRadius,
                originRadiusWorld,
                projection,
                AngularClipperLutScratch,
                lutCount,
                applyForestClip,
                applyBlockingClip,
                skipTerrainPostFilters);

            var flatEye = eyeWorld;
            flatEye.y = 0f;
            var sampleDir = CombatForestFogAngularTables.GetDirectionWorldXZ(0, lutCount);
            var buildContext = new CombatForestFogLutBuildContext(
                flatEye,
                maxRadius,
                originRadiusWorld,
                depthWorld,
                CombatForestFogClipper.ComputeRayStartedInsideForest(flatEye, sampleDir, originRadiusWorld),
                applyForestClip,
                applyBlockingClip);

            var sparseCount = 0;
            if (CombatForestFogPassSettings.UseSparseTerrainUpload)
            {
                sparseCount = CombatForestFogTerrainSparseUploadBuilder.BuildUploadSegments(
                    AngularClipperLutScratch,
                    lutCount,
                    maxRadius,
                    eyeWorld,
                    buildContext,
                    projection,
                    applyForestClip,
                    applyBlockingClip,
                    terrainClipDirections,
                    terrainClipUploadDistances,
                    sampleCount);
            }

            if (sparseCount >= 2)
            {
                return;
            }

            terrainClipDirections.Clear();
            terrainClipUploadDistances.Clear();
            for (var i = 0; i < lutCount; i++)
            {
                var dir2 = CombatForestFogAngularTables.GetDirection2D(i, lutCount);
                var clipped = AngularClipperLutScratch[i] < maxRadius - DistanceEpsilonWorld;
                terrainClipDirections.Add(dir2);
                terrainClipUploadDistances.Add(clipped ? AngularClipperLutScratch[i] : maxRadius + 1f);
            }
        }

        private static bool IsWallHit(RaycastRevealer.SightSegment segment, float maxRadius)
        {
            return segment.DidHit && segment.Radius <= maxRadius - DistanceEpsilonWorld;
        }

        private void SnapshotWallPass(RaycastRevealer.SightSegment[] viewPoints, int wallPassCount)
        {
            wallPassSegments.Clear();
            for (var i = 0; i < wallPassCount; i++)
            {
                wallPassSegments.Add(viewPoints[i]);
            }
        }

        private static bool TryGetRayDirections(
            RaycastRevealer.SightIteration firstIteration,
            int index,
            FogOfWarRevealer3D.PlaneProjection projection,
            out float2 direction2D,
            out Vector3 directionWorld)
        {
            direction2D = firstIteration.Directions[index];
            directionWorld = Vector3.zero;
            if (math.lengthsq(direction2D) <= 1e-8f)
            {
                return false;
            }

            direction2D = math.normalize(direction2D);
            directionWorld = CombatFogProjection.Direction2DToWorld(direction2D, projection);
            return directionWorld.sqrMagnitude > 1e-8f;
        }

        private static bool TryGetRayDirections(
            RaycastRevealer.SightIteration firstIteration,
            int index,
            out float2 direction2D)
        {
            direction2D = firstIteration.Directions[index];
            if (math.lengthsq(direction2D) <= 1e-8f)
            {
                return false;
            }

            direction2D = math.normalize(direction2D);
            return true;
        }
    }

    /// <summary>
    /// Samples pass-1 wall visibility at a direction using the same cone/chord rules as the fog shader.
    /// </summary>
    internal static class CombatFogSparsePolygonQuery
    {
        private const float ChordIntersectionMinDistSq = 0.0225f;

        public static float GetBoundaryDistance(
            IReadOnlyList<RaycastRevealer.SightSegment> segments,
            float2 queryDirection,
            float totalRevealerRadius,
            bool circleIsComplete,
            float extraRadius = 0f)
        {
            if (segments == null || segments.Count < 2)
            {
                return totalRevealerRadius + 1f;
            }

            var count = segments.Count;
            var crossPrev = Cross(queryDirection, NormalizeDirection(segments[0].Direction));

            for (var c = 1; c < count; c++)
            {
                if (TryGetDistanceInCone(
                        segments[c - 1],
                        segments[c],
                        queryDirection,
                        crossPrev,
                        totalRevealerRadius,
                        extraRadius,
                        out var distance))
                {
                    return distance;
                }

                crossPrev = Cross(queryDirection, NormalizeDirection(segments[c].Direction));
            }

            if (circleIsComplete)
            {
                if (TryGetDistanceInCone(
                        segments[count - 1],
                        segments[0],
                        queryDirection,
                        Cross(queryDirection, NormalizeDirection(segments[count - 1].Direction)),
                        totalRevealerRadius,
                        extraRadius,
                        out var wrapDistance))
                {
                    return wrapDistance;
                }
            }

            return totalRevealerRadius + 1f;
        }

        private static bool TryGetDistanceInCone(
            RaycastRevealer.SightSegment previous,
            RaycastRevealer.SightSegment current,
            float2 queryDirection,
            float crossPrev,
            float totalRevealerRadius,
            float extraRadius,
            out float distance)
        {
            distance = GetUploadedLength(current, totalRevealerRadius);
            var crossCurr = Cross(queryDirection, NormalizeDirection(current.Direction));
            var inCone = crossPrev <= 0f && crossCurr >= 0f;
            if (!inCone)
            {
                return false;
            }

            var cutShortPrev = IsCutShort(previous, totalRevealerRadius);
            var cutShortCurr = IsCutShort(current, totalRevealerRadius);
            if (cutShortPrev && cutShortCurr)
            {
                var prevLen = GetUploadedLength(previous, totalRevealerRadius);
                var currLen = GetUploadedLength(current, totalRevealerRadius);
                var start = NormalizeDirection(previous.Direction) * prevLen;
                var end = NormalizeDirection(current.Direction) * currLen;
                var delta = end - start;
                if (math.dot(delta, delta) > ChordIntersectionMinDistSq)
                {
                    var intersection = CalculateIntersectionCramersRule(start, end, queryDirection);
                    distance = math.length(intersection) + extraRadius;
                }
            }

            return true;
        }

        private static float GetUploadedLength(
            RaycastRevealer.SightSegment segment,
            float totalRevealerRadius)
        {
            return segment.Radius + (segment.DidHit ? 0f : 1f);
        }

        private static bool IsCutShort(
            RaycastRevealer.SightSegment segment,
            float totalRevealerRadius)
        {
            return GetUploadedLength(segment, totalRevealerRadius) <= totalRevealerRadius;
        }

        private static float2 NormalizeDirection(float2 direction)
        {
            return math.normalizesafe(direction, new float2(1f, 0f));
        }

        private static float Cross(float2 a, float2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static float2 CalculateIntersectionCramersRule(float2 start, float2 end, float2 queryDirection)
        {
            var a1 = end.y - start.y;
            var b1 = start.x - end.x;
            var c1 = a1 * start.x + b1 * start.y;
            var a2 = queryDirection.y;
            var b2 = -queryDirection.x;
            var determinant = a1 * b2 - a2 * b1;
            if (math.abs(determinant) <= 1e-8f)
            {
                return queryDirection * math.max(math.length(start), math.length(end));
            }

            var x = b2 * c1 / determinant;
            var y = -a2 * c1 / determinant;
            return new float2(x, y);
        }
    }

    /// <summary>
    /// Queries terrain upload segments with wedge/chord rules tuned for forest clip (mixed open/clipped wedges).
    /// </summary>
    internal static class CombatFogTerrainUploadQuery
    {
        private const float MaxClippedWrapGapRadians = math.PI * 0.75f;
        private static readonly List<RaycastRevealer.SightSegment> SegmentScratch = new();

        public static float GetBoundaryDistance(
            float2[] directions,
            float[] uploadLengths,
            int count,
            float2 queryDirection,
            float totalRevealerRadius,
            bool circleIsComplete,
            float extraRadius = 0f)
        {
            if (!TryBuildUploadSegments(
                    directions,
                    uploadLengths,
                    count,
                    totalRevealerRadius,
                    SegmentScratch))
            {
                return totalRevealerRadius + 1f;
            }

            return GetTerrainBoundaryDistance(
                SegmentScratch,
                queryDirection,
                totalRevealerRadius,
                circleIsComplete,
                extraRadius);
        }

        internal static float GetTerrainBoundaryDistance(
            IReadOnlyList<RaycastRevealer.SightSegment> segments,
            float2 queryDirection,
            float totalRevealerRadius,
            bool circleIsComplete,
            float extraRadius = 0f)
        {
            if (segments == null || segments.Count < 2)
            {
                return totalRevealerRadius + 1f;
            }

            var count = segments.Count;
            var crossPrev = Cross(queryDirection, NormalizeDirection(segments[0].Direction));

            for (var c = 1; c < count; c++)
            {
                if (TryGetTerrainDistanceInCone(
                        segments[c - 1],
                        segments[c],
                        queryDirection,
                        crossPrev,
                        totalRevealerRadius,
                        extraRadius,
                        out var distance))
                {
                    return distance;
                }

                crossPrev = Cross(queryDirection, NormalizeDirection(segments[c].Direction));
            }

            if ((circleIsComplete
                    || ShouldAllowTerrainWrapWedge(segments[count - 1], segments[0], totalRevealerRadius))
                && TryGetTerrainDistanceInCone(
                    segments[count - 1],
                    segments[0],
                    queryDirection,
                    Cross(queryDirection, NormalizeDirection(segments[count - 1].Direction)),
                    totalRevealerRadius,
                    extraRadius,
                    out var wrapDistance))
            {
                return wrapDistance;
            }

            return totalRevealerRadius + 1f;
        }

        private static bool ShouldAllowTerrainWrapWedge(
            RaycastRevealer.SightSegment last,
            RaycastRevealer.SightSegment first,
            float totalRevealerRadius)
        {
            var lastOpen = !IsCutShort(last, totalRevealerRadius);
            var firstOpen = !IsCutShort(first, totalRevealerRadius);
            if (lastOpen || firstOpen)
            {
                return true;
            }

            return ComputeSortedWrapGapRadians(
                NormalizeDirection(last.Direction),
                NormalizeDirection(first.Direction)) <= MaxClippedWrapGapRadians;
        }

        private static float ComputeSortedWrapGapRadians(float2 lastDir, float2 firstDir)
        {
            var lastAngle = math.atan2(lastDir.y, lastDir.x);
            var firstAngle = math.atan2(firstDir.y, firstDir.x);
            if (firstAngle >= lastAngle)
            {
                return firstAngle - lastAngle;
            }

            return math.PI * 2f - (lastAngle - firstAngle);
        }

        private static bool TryGetTerrainDistanceInCone(
            RaycastRevealer.SightSegment previous,
            RaycastRevealer.SightSegment current,
            float2 queryDirection,
            float crossPrev,
            float totalRevealerRadius,
            float extraRadius,
            out float distance)
        {
            distance = GetUploadedLength(current, totalRevealerRadius);
            var crossCurr = Cross(queryDirection, NormalizeDirection(current.Direction));
            var inCone = crossPrev <= 0f && crossCurr >= 0f;
            if (!inCone)
            {
                return false;
            }

            var cutShortPrev = IsCutShort(previous, totalRevealerRadius);
            var cutShortCurr = IsCutShort(current, totalRevealerRadius);
            var wedgeT = ComputeAngularWedgeLerpT(
                NormalizeDirection(previous.Direction),
                NormalizeDirection(current.Direction),
                queryDirection);

            if (cutShortPrev && cutShortCurr)
            {
                distance = math.lerp(previous.Radius, current.Radius, wedgeT);
            }
            else if (cutShortPrev != cutShortCurr)
            {
                const float openBandT = 0.04f;
                if (cutShortPrev && !cutShortCurr)
                {
                    distance = wedgeT >= openBandT
                        ? totalRevealerRadius + 1f
                        : math.lerp(
                            previous.Radius + extraRadius,
                            totalRevealerRadius + 1f,
                            wedgeT / openBandT);
                }
                else
                {
                    distance = wedgeT <= 1f - openBandT
                        ? totalRevealerRadius + 1f
                        : math.lerp(
                            totalRevealerRadius + 1f,
                            current.Radius + extraRadius,
                            (wedgeT - (1f - openBandT)) / openBandT);
                }
            }

            if (distance <= totalRevealerRadius - 0.01f)
            {
                distance += extraRadius;
            }

            return true;
        }

        private static float ComputeAngularWedgeLerpT(float2 dirPrev, float2 dirCurr, float2 queryDir)
        {
            var anglePrev = math.atan2(dirPrev.y, dirPrev.x);
            var angleCurr = math.atan2(dirCurr.y, dirCurr.x);
            var angleQuery = math.atan2(queryDir.y, queryDir.x);
            if (angleCurr <= anglePrev)
            {
                angleCurr += math.PI * 2f;
            }

            if (angleQuery < anglePrev)
            {
                angleQuery += math.PI * 2f;
            }

            var span = angleCurr - anglePrev;
            return span > 1e-6f ? math.saturate((angleQuery - anglePrev) / span) : 0f;
        }

        public static bool TryBuildUploadSegments(
            float2[] directions,
            float[] uploadLengths,
            int count,
            float totalRevealerRadius,
            List<RaycastRevealer.SightSegment> segments)
        {
            segments.Clear();
            if (directions == null || uploadLengths == null || count < 2)
            {
                return false;
            }

            for (var i = 0; i < count; i++)
            {
                if (math.lengthsq(directions[i]) <= 1e-8f)
                {
                    continue;
                }

                var didHit = uploadLengths[i] <= totalRevealerRadius - 0.01f;
                var radius = didHit ? uploadLengths[i] : totalRevealerRadius;
                segments.Add(new RaycastRevealer.SightSegment
                {
                    Direction = math.normalize(directions[i]),
                    Radius = radius,
                    DidHit = didHit,
                });
            }

            return segments.Count >= 2;
        }

        private static float GetUploadedLength(
            RaycastRevealer.SightSegment segment,
            float totalRevealerRadius)
        {
            return segment.Radius + (segment.DidHit ? 0f : 1f);
        }

        private static bool IsCutShort(
            RaycastRevealer.SightSegment segment,
            float totalRevealerRadius)
        {
            return GetUploadedLength(segment, totalRevealerRadius) <= totalRevealerRadius;
        }

        private static float2 NormalizeDirection(float2 direction)
        {
            return math.normalizesafe(direction, new float2(1f, 0f));
        }

        private static float Cross(float2 a, float2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static float2 CalculateIntersectionCramersRule(float2 start, float2 end, float2 queryDirection)
        {
            var a1 = end.y - start.y;
            var b1 = start.x - end.x;
            var c1 = a1 * start.x + b1 * start.y;
            var a2 = queryDirection.y;
            var b2 = -queryDirection.x;
            var determinant = a1 * b2 - a2 * b1;
            if (math.abs(determinant) <= 1e-8f)
            {
                return queryDirection * math.max(math.length(start), math.length(end));
            }

            var x = b2 * c1 / determinant;
            var y = -a2 * c1 / determinant;
            return new float2(x, y);
        }
    }
}
