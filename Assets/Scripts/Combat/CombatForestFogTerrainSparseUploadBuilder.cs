using System.Collections.Generic;
using FOW;
using Unity.Mathematics;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Terrain upload: LUT depth-clip distances for fill, subdivided polygon edges for straight boundaries.
    /// Distances always come from the analytic clipper — edge samples only anchor angular shape.
    /// </summary>
    internal static class CombatForestFogTerrainSparseUploadBuilder
    {
        private const float DistanceEpsilonWorld = 0.01f;
        private const float NearEntrySnapWorld = 0.08f;
        private const float MinAngleSeparationRadians = math.PI / 720f;
        /// <summary>
        /// Wedge/chord span above this must contain an open upload sample or the shader lerps
        /// between separate forest edges (giant arc), especially while the eye moves.
        /// </summary>
        private const float MaxClippedWedgeWithoutOpenRadians = math.PI / 18f;
        private const int EdgeRefineIterations = 8;
        private const float EdgeSubdivisionWorld = 0.1f;

        private static readonly List<AngularSample> OpenBreakInsertScratch = new();

        private struct AngularSample
        {
            public float Angle;
            public float2 Direction;
            public float ClipDistance;
            public bool Clipped;
            public bool Essential;
        }

        private static readonly List<AngularSample> SampleScratch = new();
        private static readonly List<Vector3> ZoneCornerScratch = new();

        public static int BuildUploadSegments(
            float[] clipDistances,
            int lutSampleCount,
            float maxRadius,
            Vector3 eyeWorld,
            in CombatForestFogLutBuildContext buildContext,
            FogOfWarRevealer3D.PlaneProjection projection,
            bool applyForestClip,
            bool applyBlockingClip,
            List<float2> outDirections,
            List<float> outUploadLengths,
            int maxSegments)
        {
            outDirections.Clear();
            outUploadLengths.Clear();
            if (clipDistances == null
                || lutSampleCount < 2
                || maxSegments < 2
                || maxRadius <= 0.001f)
            {
                return 0;
            }

            var openThreshold = maxRadius - DistanceEpsilonWorld;
            SampleScratch.Clear();

            var flatEye = eyeWorld;
            flatEye.y = 0f;
            var activeClipZoneCount = CombatForestFogClipper.GetActiveClipZoneCount(
                applyForestClip,
                applyBlockingClip);
            var injectPolygonShape = ShouldInjectPolygonShapeSamples(buildContext, activeClipZoneCount);

            // Depth fill from LUT; polygon edge anchors only when the eye is outside forest.
            InjectDepthClippedLutBins(
                clipDistances,
                lutSampleCount,
                flatEye,
                maxRadius,
                buildContext,
                openThreshold,
                activeClipZoneCount);
            RefineOpenClippedTransitions(
                clipDistances,
                lutSampleCount,
                maxRadius,
                eyeWorld,
                buildContext,
                openThreshold);
            if (injectPolygonShape)
            {
                InjectPolygonEdgeSamples(
                    eyeWorld,
                    maxRadius,
                    buildContext,
                    openThreshold,
                    applyForestClip,
                    applyBlockingClip,
                    clipDistances,
                    lutSampleCount);
                InjectFootprintCorners(
                    eyeWorld,
                    maxRadius,
                    buildContext,
                    openThreshold,
                    applyForestClip,
                    applyBlockingClip,
                    clipDistances,
                    lutSampleCount);
            }

            if (CountClippedSamples(openThreshold) < 2 && (applyForestClip || applyBlockingClip))
            {
                if (injectPolygonShape)
                {
                    BuildExitSilhouetteFallbackUpload(
                        flatEye,
                        maxRadius,
                        buildContext,
                        openThreshold,
                        applyForestClip,
                        applyBlockingClip,
                        clipDistances,
                        lutSampleCount);
                }
            }

            if (CountClippedSamples(openThreshold) < 2 && (applyForestClip || applyBlockingClip))
            {
                BuildDenseClippedLutFallbackUpload(
                    clipDistances,
                    lutSampleCount,
                    maxRadius,
                    openThreshold);
            }

            if (CountClippedSamples(openThreshold) > 0)
            {
                InjectSparseOpenLutBins(
                    clipDistances,
                    lutSampleCount,
                    maxRadius,
                    openThreshold,
                    activeClipZoneCount);
            }

            if (SampleScratch.Count < 2 && (applyForestClip || applyBlockingClip))
            {
                BuildDenseClippedLutFallbackUpload(
                    clipDistances,
                    lutSampleCount,
                    maxRadius,
                    openThreshold);
            }

            SortAndDedupeSamples(openThreshold);
            InsertEssentialOpenBreaksBetweenClippedIslands(
                flatEye,
                maxRadius,
                openThreshold,
                buildContext,
                activeClipZoneCount);
            SortAndDedupeSamples(openThreshold);

            if (SampleScratch.Count > maxSegments)
            {
                DecimateSamplesToBudget(maxSegments);
            }

            TrimToUploadBudget(maxSegments);

            if (SampleScratch.Count < 2)
            {
                return 0;
            }

            WriteSamplesToOutput(flatEye, maxRadius, buildContext, outDirections, outUploadLengths);
            return outDirections.Count;
        }

        public static bool UploadHasClippedSegments(
            IReadOnlyList<float> uploadLengths,
            float maxRadius)
        {
            if (uploadLengths == null || uploadLengths.Count < 2 || maxRadius <= 0.001f)
            {
                return false;
            }

            var openThreshold = maxRadius - DistanceEpsilonWorld;
            for (var i = 0; i < uploadLengths.Count; i++)
            {
                if (uploadLengths[i] < openThreshold)
                {
                    return true;
                }
            }

            return false;
        }

        public static int BuildDenseLutFallbackUploadSegments(
            float[] clipDistances,
            int lutSampleCount,
            float maxRadius,
            Vector3 eyeWorld,
            in CombatForestFogLutBuildContext buildContext,
            List<float2> outDirections,
            List<float> outUploadLengths,
            int maxSegments)
        {
            outDirections.Clear();
            outUploadLengths.Clear();
            if (clipDistances == null
                || lutSampleCount < 2
                || maxSegments < 2
                || maxRadius <= 0.001f)
            {
                return 0;
            }

            var openThreshold = maxRadius - DistanceEpsilonWorld;
            SampleScratch.Clear();
            var flatEye = eyeWorld;
            flatEye.y = 0f;
            BuildDenseClippedLutFallbackUpload(clipDistances, lutSampleCount, maxRadius, openThreshold);
            SortAndDedupeSamples(openThreshold);
            InsertEssentialOpenBreaksBetweenClippedIslands(
                flatEye,
                maxRadius,
                openThreshold,
                buildContext,
                CombatForestFogClipper.GetActiveClipZoneCount(
                    buildContext.ApplyForestClip,
                    buildContext.ApplyBlockingClip));
            SortAndDedupeSamples(openThreshold);
            TrimToUploadBudget(maxSegments);

            if (SampleScratch.Count < 2 || CountClippedSamples(openThreshold) < 1)
            {
                return 0;
            }

            WriteSamplesToOutput(flatEye, maxRadius, buildContext, outDirections, outUploadLengths);
            return outDirections.Count;
        }

        private static void WriteSamplesToOutput(
            Vector3 flatEye,
            float maxRadius,
            in CombatForestFogLutBuildContext buildContext,
            List<float2> outDirections,
            List<float> outUploadLengths)
        {
            for (var i = 0; i < SampleScratch.Count; i++)
            {
                var sample = SampleScratch[i];
                outDirections.Add(sample.Direction);
                outUploadLengths.Add(
                    sample.Clipped
                        ? ResolveMeshUploadLength(sample, flatEye, maxRadius, buildContext)
                        : maxRadius + 1f);
            }
        }

        private static void TrimToUploadBudget(int maxSegments)
        {
            while (SampleScratch.Count > maxSegments)
            {
                var removeIndex = -1;
                for (var i = SampleScratch.Count - 1; i >= 0; i--)
                {
                    if (!SampleScratch[i].Essential && SampleScratch[i].Clipped)
                    {
                        removeIndex = i;
                        break;
                    }
                }

                if (removeIndex >= 0)
                {
                    SampleScratch.RemoveAt(removeIndex);
                    continue;
                }

                for (var i = SampleScratch.Count - 1; i >= 0; i--)
                {
                    if (!SampleScratch[i].Essential && !SampleScratch[i].Clipped)
                    {
                        removeIndex = i;
                        break;
                    }
                }

                if (removeIndex < 0)
                {
                    break;
                }

                SampleScratch.RemoveAt(removeIndex);
            }
        }

        private static bool ShouldInjectPolygonShapeSamples(
            in CombatForestFogLutBuildContext buildContext,
            int activeClipZoneCount)
        {
            // Multi-zone polygon anchors + sparse decimation chord across zones while moving.
            return !buildContext.RayStartedInsideForest && activeClipZoneCount <= 1;
        }

        /// <summary>
        /// LUT clipped bins carrying analytic depth distances (entry-side promoted to entry + depth).
        /// </summary>
        private static void InjectDepthClippedLutBins(
            float[] clipDistances,
            int lutSampleCount,
            Vector3 flatEye,
            float maxRadius,
            in CombatForestFogLutBuildContext buildContext,
            float openThreshold,
            int activeClipZoneCount)
        {
            if (clipDistances == null || lutSampleCount < 1)
            {
                return;
            }

            var lutFillZoneCount = activeClipZoneCount > 0
                ? activeClipZoneCount
                : CombatForestFogClipper.GetActiveClipZoneCount(
                    buildContext.ApplyForestClip,
                    buildContext.ApplyBlockingClip);
            var multiZone = lutFillZoneCount >= 2;
            var step = math.max(1, lutSampleCount / (multiZone ? 360 : 180));
            for (var i = 0; i < lutSampleCount; i += step)
            {
                var clip = clipDistances[i];
                if (clip >= openThreshold)
                {
                    continue;
                }

                var dir2 = GetDirection2D(i, lutSampleCount);
                clip = ResolveUploadClipDistance(flatEye, dir2, clip, maxRadius, buildContext);
                if (clip >= openThreshold)
                {
                    continue;
                }

                AddSample(
                    IndexToAngle(i, lutSampleCount),
                    dir2,
                    clip,
                    clipped: true,
                    essential: multiZone);
            }
        }

        private static void RefineOpenClippedTransitions(
            float[] clipDistances,
            int lutSampleCount,
            float maxRadius,
            Vector3 eyeWorld,
            in CombatForestFogLutBuildContext buildContext,
            float openThreshold)
        {
            for (var i = 0; i < lutSampleCount; i++)
            {
                var next = (i + 1) % lutSampleCount;
                var clippedA = clipDistances[i] < openThreshold;
                var clippedB = clipDistances[next] < openThreshold;
                if (clippedA == clippedB)
                {
                    continue;
                }

                var openIndex = clippedA ? next : i;
                var clippedIndex = clippedA ? i : next;
                var angleOpen = IndexToAngle(openIndex, lutSampleCount);
                var angleClipped = IndexToAngle(clippedIndex, lutSampleCount);
                if (angleClipped < angleOpen)
                {
                    angleClipped += math.PI * 2f;
                }

                var angleLow = angleOpen;
                var angleHigh = angleClipped;
                for (var r = 0; r < EdgeRefineIterations; r++)
                {
                    var mid = (angleLow + angleHigh) * 0.5f;
                    var clipMid = SampleClipAtAngle(mid, maxRadius, eyeWorld, buildContext);
                    if (clipMid < openThreshold)
                    {
                        angleHigh = mid;
                    }
                    else
                    {
                        angleLow = mid;
                    }
                }

                var refinedClipAngle = angleHigh % (math.PI * 2f);
                var refinedClipDir = AngleToDirection2D(refinedClipAngle);
                var refinedClip = SampleClipAtAngle(refinedClipAngle, maxRadius, eyeWorld, buildContext);
                refinedClip = ResolveUploadClipDistance(
                    eyeWorld,
                    refinedClipDir,
                    refinedClip,
                    maxRadius,
                    buildContext);
                if (refinedClip < openThreshold)
                {
                    AddSample(refinedClipAngle, refinedClipDir, refinedClip, true, essential: true);
                }

                var refinedOpenAngle = angleLow % (math.PI * 2f);
                var refinedOpenDir = AngleToDirection2D(refinedOpenAngle);
                AddSample(refinedOpenAngle, refinedOpenDir, maxRadius, clipped: false, essential: true);
            }
        }

        /// <summary>
        /// Thick forest uploads must sit on the depth limit (entry + depth), not the entry wall.
        /// Entry-side transitions were skipped before, leaving only coarse LUT stairs while moving.
        /// </summary>
        private static float ResolveUploadClipDistance(
            Vector3 flatEye,
            float2 direction2D,
            float clipDistance,
            float maxRadius,
            in CombatForestFogLutBuildContext buildContext)
        {
            if (!buildContext.HasForest || clipDistance >= maxRadius - DistanceEpsilonWorld)
            {
                return clipDistance;
            }

            var dirWorld = new Vector3(direction2D.x, 0f, direction2D.y);
            if (dirWorld.sqrMagnitude <= 1e-8f)
            {
                return clipDistance;
            }

            dirWorld.Normalize();
            var depthWorld = buildContext.DepthWorld;
            var resolved = clipDistance;
            var activeZones = CombatZone.ActiveZones;
            for (var z = 0; z < activeZones.Count; z++)
            {
                var zone = activeZones[z];
                var feature = zone?.TerrainFeature;
                if (zone == null
                    || feature == null
                    || feature.LineOfSightMode != CombatTerrainLineOfSightMode.LimitedDepth)
                {
                    continue;
                }

                if (zone.ContainsPoint(flatEye))
                {
                    continue;
                }

                if (!CombatForestFogClipper.TryGetZoneRayFootprintIntervalWorld(
                        zone,
                        flatEye,
                        dirWorld,
                        out var enter,
                        out var exit))
                {
                    continue;
                }

                if (enter <= DistanceEpsilonWorld
                    || exit <= enter + depthWorld * 0.5f)
                {
                    continue;
                }

                var depthClip = math.min(maxRadius, math.min(exit, enter + depthWorld));
                if (math.abs(clipDistance - enter) <= NearEntrySnapWorld
                    && clipDistance < enter + depthWorld * 0.25f
                    && clipDistance < exit - NearEntrySnapWorld)
                {
                    resolved = math.max(resolved, depthClip);
                }
            }

            return resolved;
        }

        private static float SampleClipAtAngle(
            float angleRadians,
            float maxRadius,
            Vector3 eyeWorld,
            in CombatForestFogLutBuildContext buildContext)
        {
            var dir2 = AngleToDirection2D(angleRadians);
            var directionWorld = new Vector3(dir2.x, 0f, dir2.y);
            return SampleClipAlongDirection(directionWorld, maxRadius, eyeWorld, buildContext);
        }

        private static float LookupLutClipAtDirection(
            float2 direction2D,
            float[] clipDistances,
            int lutSampleCount,
            float maxRadius)
        {
            if (clipDistances == null || lutSampleCount < 1)
            {
                return maxRadius;
            }

            var bestDot = -2f;
            var bestClip = maxRadius;
            for (var i = 0; i < lutSampleCount; i++)
            {
                var dir = GetDirection2D(i, lutSampleCount);
                var dot = math.dot(dir, direction2D);
                if (dot <= bestDot)
                {
                    continue;
                }

                bestDot = dot;
                bestClip = clipDistances[i];
            }

            return bestClip;
        }

        private static void InjectFootprintCorners(
            Vector3 eyeWorld,
            float maxRadius,
            in CombatForestFogLutBuildContext buildContext,
            float openThreshold,
            bool applyForestClip,
            bool applyBlockingClip,
            float[] clipDistances,
            int lutSampleCount)
        {
            if (!applyForestClip && !applyBlockingClip)
            {
                return;
            }

            var flatEye = eyeWorld;
            flatEye.y = 0f;
            var activeZones = CombatZone.ActiveZones;
            for (var z = 0; z < activeZones.Count; z++)
            {
                var zone = activeZones[z];
                var feature = zone?.TerrainFeature;
                if (zone == null || feature == null || !feature.UsesPassThroughFogClip)
                {
                    continue;
                }

                var isForest = feature.LineOfSightMode == CombatTerrainLineOfSightMode.LimitedDepth;
                var isCloudFog = feature.LineOfSightMode == CombatTerrainLineOfSightMode.BlocksCompletely;
                if (isForest && !applyForestClip)
                {
                    continue;
                }

                if (isCloudFog && !applyBlockingClip)
                {
                    continue;
                }

                if (!isForest && !isCloudFog)
                {
                    continue;
                }

                ZoneCornerScratch.Clear();
                zone.CollectFootprintCorners(ZoneCornerScratch);
                if (ZoneCornerScratch.Count < 2)
                {
                    continue;
                }

                for (var i = 0; i < ZoneCornerScratch.Count; i++)
                {
                    var corner = ZoneCornerScratch[i];
                    corner.y = flatEye.y;
                    var offset = corner - flatEye;
                    offset.y = 0f;
                    if (offset.sqrMagnitude <= 1e-8f)
                    {
                        continue;
                    }

                    var directionWorld = offset.normalized;
                    var clip = SampleClipAlongDirection(directionWorld, maxRadius, flatEye, buildContext);
                    var dir2 = math.normalize(new float2(directionWorld.x, directionWorld.z));
                    clip = ResolveUploadClipDistance(flatEye, dir2, clip, maxRadius, buildContext);
                    if (clip >= openThreshold
                        || !CombatForestFogClipper.ClipDistanceRelatesToZoneFootprint(
                            zone,
                            flatEye,
                            directionWorld,
                            clip,
                            buildContext.DepthWorld))
                    {
                        continue;
                    }

                    clip = CapThinForestExitClip(flatEye, dir2, clip, maxRadius, buildContext, isForest);
                    if (clip >= openThreshold)
                    {
                        continue;
                    }

                    var angle = math.atan2(dir2.y, dir2.x);
                    if (angle < 0f)
                    {
                        angle += math.PI * 2f;
                    }

                    AddSample(angle, dir2, clip, true, essential: true);
                }
            }
        }

        /// <summary>
        /// Subdivides polygon edges so wedge/chords follow straight footprint lines (not LUT stairs).
        /// Both near (entry-facing) and far (exit-facing) edges are sampled; distances stay analytic.
        /// </summary>
        private static void InjectPolygonEdgeSamples(
            Vector3 eyeWorld,
            float maxRadius,
            in CombatForestFogLutBuildContext buildContext,
            float openThreshold,
            bool applyForestClip,
            bool applyBlockingClip,
            float[] clipDistances,
            int lutSampleCount)
        {
            if (!applyForestClip && !applyBlockingClip)
            {
                return;
            }

            var flatEye = eyeWorld;
            flatEye.y = 0f;
            var subdivWorld = CombatScale.InchesToWorldUnits(EdgeSubdivisionWorld);
            var activeZones = CombatZone.ActiveZones;
            for (var z = 0; z < activeZones.Count; z++)
            {
                var zone = activeZones[z];
                var feature = zone?.TerrainFeature;
                if (zone == null || feature == null)
                {
                    continue;
                }

                if (zone == null || feature == null || !feature.UsesPassThroughFogClip)
                {
                    continue;
                }

                var isForest = feature.LineOfSightMode == CombatTerrainLineOfSightMode.LimitedDepth;
                var isCloudFog = feature.LineOfSightMode == CombatTerrainLineOfSightMode.BlocksCompletely;
                if (isForest && !applyForestClip)
                {
                    continue;
                }

                if (isCloudFog && !applyBlockingClip)
                {
                    continue;
                }

                if (!isForest && !isCloudFog)
                {
                    continue;
                }

                ZoneCornerScratch.Clear();
                zone.CollectFootprintCorners(ZoneCornerScratch);
                if (ZoneCornerScratch.Count < 2)
                {
                    continue;
                }

                for (var c = 0; c < ZoneCornerScratch.Count; c++)
                {
                    var edgeStart = ZoneCornerScratch[c];
                    var edgeEnd = ZoneCornerScratch[(c + 1) % ZoneCornerScratch.Count];
                    InjectSubdividedEdgeSamples(
                        zone,
                        edgeStart,
                        edgeEnd,
                        flatEye,
                        maxRadius,
                        buildContext,
                        openThreshold,
                        subdivWorld,
                        clipDistances,
                        lutSampleCount);
                }
            }
        }

        private static void BuildExitSilhouetteFallbackUpload(
            Vector3 flatEye,
            float maxRadius,
            in CombatForestFogLutBuildContext buildContext,
            float openThreshold,
            bool applyForestClip,
            bool applyBlockingClip,
            float[] clipDistances,
            int lutSampleCount)
        {
            var activeZones = CombatZone.ActiveZones;
            for (var z = 0; z < activeZones.Count; z++)
            {
                var zone = activeZones[z];
                var feature = zone?.TerrainFeature;
                if (zone == null || feature == null || !feature.UsesPassThroughFogClip)
                {
                    continue;
                }

                var isForest = feature.LineOfSightMode == CombatTerrainLineOfSightMode.LimitedDepth;
                var isCloudFog = feature.LineOfSightMode == CombatTerrainLineOfSightMode.BlocksCompletely;
                if (isForest && !applyForestClip)
                {
                    continue;
                }

                if (isCloudFog && !applyBlockingClip)
                {
                    continue;
                }

                if (!isForest && !isCloudFog)
                {
                    continue;
                }

                ZoneCornerScratch.Clear();
                zone.CollectFootprintCorners(ZoneCornerScratch);
                for (var c = 0; c < ZoneCornerScratch.Count; c++)
                {
                    var corner = ZoneCornerScratch[c];
                    corner.y = flatEye.y;
                    var offset = corner - flatEye;
                    offset.y = 0f;
                    if (offset.sqrMagnitude <= 1e-8f || offset.sqrMagnitude > maxRadius * maxRadius)
                    {
                        continue;
                    }

                    var dirWorld = offset.normalized;
                    var dir2 = math.normalize(new float2(dirWorld.x, dirWorld.z));
                    var clip = LookupLutClipAtDirection(dir2, clipDistances, lutSampleCount, maxRadius);
                    if (clip >= openThreshold)
                    {
                        clip = SampleClipAlongDirection(dirWorld, maxRadius, flatEye, buildContext);
                    }

                    if (clip >= openThreshold
                        || !CombatForestFogClipper.ClipDistanceRelatesToZoneFootprint(
                            zone,
                            flatEye,
                            dirWorld,
                            clip,
                            buildContext.DepthWorld))
                    {
                        continue;
                    }

                    clip = CapThinForestExitClip(flatEye, dir2, clip, maxRadius, buildContext, isForest);
                    if (clip >= openThreshold)
                    {
                        continue;
                    }

                    var angle = math.atan2(dir2.y, dir2.x);
                    if (angle < 0f)
                    {
                        angle += math.PI * 2f;
                    }

                    AddSample(angle, dir2, clip, true, essential: true);
                }
            }
        }

        private static void InjectSubdividedEdgeSamples(
            CombatZone zone,
            Vector3 edgeStart,
            Vector3 edgeEnd,
            Vector3 flatEye,
            float maxRadius,
            in CombatForestFogLutBuildContext buildContext,
            float openThreshold,
            float subdivWorld,
            float[] clipDistances,
            int lutSampleCount)
        {
            var isForest = zone?.TerrainFeature?.LineOfSightMode == CombatTerrainLineOfSightMode.LimitedDepth;
            edgeStart.y = flatEye.y;
            edgeEnd.y = flatEye.y;
            var edgeLen = Vector3.Distance(edgeStart, edgeEnd);
            var steps = math.min(
                16,
                math.max(1, (int)math.ceil(edgeLen / math.max(subdivWorld, 0.01f))));

            for (var s = 0; s <= steps; s++)
            {
                var t = s / (float)steps;
                var point = Vector3.Lerp(edgeStart, edgeEnd, t);
                var offset = point - flatEye;
                offset.y = 0f;
                var distSq = offset.sqrMagnitude;
                if (distSq <= 1e-8f || distSq > maxRadius * maxRadius)
                {
                    continue;
                }

                var distToPoint = math.sqrt(distSq);
                var dirWorld = offset / distToPoint;
                var dir2 = math.normalize(new float2(dirWorld.x, dirWorld.z));
                var clip = SampleClipAlongDirection(dirWorld, maxRadius, flatEye, buildContext);
                clip = ResolveUploadClipDistance(flatEye, dir2, clip, maxRadius, buildContext);
                if (clip >= openThreshold)
                {
                    continue;
                }

                clip = CapThinForestExitClip(flatEye, dir2, clip, maxRadius, buildContext, isForest);
                if (clip >= openThreshold)
                {
                    continue;
                }

                var angle = math.atan2(dir2.y, dir2.x);
                if (angle < 0f)
                {
                    angle += math.PI * 2f;
                }

                AddSample(angle, dir2, clip, true, essential: true);
            }
        }

        /// <summary>
        /// Prevents the shader from angular-lerping between two separate forest edges when budget
        /// decimation removed the open samples that used to sit between them.
        /// Uses analytic clip at the midpoint — open only when that direction is genuinely open.
        /// </summary>
        private static void InsertEssentialOpenBreaksBetweenClippedIslands(
            Vector3 flatEye,
            float maxRadius,
            float openThreshold,
            in CombatForestFogLutBuildContext buildContext,
            int activeClipZoneCount)
        {
            if (SampleScratch.Count < 2)
            {
                return;
            }

            OpenBreakInsertScratch.Clear();
            TryQueueBreakBetweenClippedPair(
                SampleScratch[SampleScratch.Count - 1],
                SampleScratch[0],
                wrapGap: true,
                flatEye,
                maxRadius,
                openThreshold,
                buildContext,
                activeClipZoneCount,
                OpenBreakInsertScratch);

            for (var i = 0; i < SampleScratch.Count - 1; i++)
            {
                TryQueueBreakBetweenClippedPair(
                    SampleScratch[i],
                    SampleScratch[i + 1],
                    wrapGap: false,
                    flatEye,
                    maxRadius,
                    openThreshold,
                    buildContext,
                    activeClipZoneCount,
                    OpenBreakInsertScratch);
            }

            if (OpenBreakInsertScratch.Count == 0)
            {
                return;
            }

            SampleScratch.AddRange(OpenBreakInsertScratch);
        }

        private const int WedgeSpanFillSteps = 4;

        private static void TryQueueBreakBetweenClippedPair(
            AngularSample from,
            AngularSample to,
            bool wrapGap,
            Vector3 flatEye,
            float maxRadius,
            float openThreshold,
            in CombatForestFogLutBuildContext buildContext,
            int activeClipZoneCount,
            List<AngularSample> output)
        {
            if (!from.Clipped || !to.Clipped)
            {
                return;
            }

            if (from.ClipDistance >= openThreshold || to.ClipDistance >= openThreshold)
            {
                return;
            }

            var gap = wrapGap
                ? (to.Angle + math.PI * 2f) - from.Angle
                : to.Angle - from.Angle;
            if (gap <= MaxClippedWedgeWithoutOpenRadians)
            {
                return;
            }

            if (HasOpenSampleBetween(from.Angle, to.Angle, wrapGap, openThreshold))
            {
                return;
            }

            var spanHasForestClip = false;
            for (var s = 1; s < WedgeSpanFillSteps; s++)
            {
                var t = s / (float)WedgeSpanFillSteps;
                var fillAngle = InterpolateSampleAngle(from.Angle, to.Angle, wrapGap, t);
                var fillDir = AngleToDirection2D(fillAngle);
                var fillDirWorld = new Vector3(fillDir.x, 0f, fillDir.y);
                var fillClip = SampleClipAlongDirection(fillDirWorld, maxRadius, flatEye, buildContext);
                fillClip = ResolveUploadClipDistance(flatEye, fillDir, fillClip, maxRadius, buildContext);

                if (fillClip < openThreshold)
                {
                    spanHasForestClip = true;
                    output.Add(new AngularSample
                    {
                        Angle = fillAngle,
                        Direction = fillDir,
                        ClipDistance = fillClip,
                        Clipped = true,
                        Essential = true,
                    });
                }
            }

            if (spanHasForestClip || activeClipZoneCount >= 2)
            {
                return;
            }

            for (var s = 1; s < WedgeSpanFillSteps; s++)
            {
                var t = s / (float)WedgeSpanFillSteps;
                var fillAngle = InterpolateSampleAngle(from.Angle, to.Angle, wrapGap, t);
                output.Add(new AngularSample
                {
                    Angle = fillAngle,
                    Direction = AngleToDirection2D(fillAngle),
                    ClipDistance = maxRadius + 1f,
                    Clipped = false,
                    Essential = true,
                });
            }
        }

        private static float InterpolateSampleAngle(
            float angleFrom,
            float angleTo,
            bool wrapGap,
            float t)
        {
            if (!wrapGap)
            {
                return angleFrom + (angleTo - angleFrom) * t;
            }

            var gap = (angleTo + math.PI * 2f) - angleFrom;
            return (angleFrom + gap * t) % (math.PI * 2f);
        }

        private static bool HasOpenSampleBetween(
            float angleFrom,
            float angleTo,
            bool wrapGap,
            float openThreshold)
        {
            for (var i = 0; i < SampleScratch.Count; i++)
            {
                var sample = SampleScratch[i];
                if (sample.Clipped && sample.ClipDistance < openThreshold)
                {
                    continue;
                }

                var angle = sample.Angle;
                if (wrapGap)
                {
                    if (angle > angleFrom + MinAngleSeparationRadians
                        || angle < angleTo - MinAngleSeparationRadians)
                    {
                        return true;
                    }
                }
                else if (angle > angleFrom + MinAngleSeparationRadians
                         && angle < angleTo - MinAngleSeparationRadians)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SortAndDedupeSamples(float openThreshold)
        {
            SampleScratch.Sort((a, b) => a.Angle.CompareTo(b.Angle));

            var write = 0;
            for (var read = 0; read < SampleScratch.Count; read++)
            {
                var sample = SampleScratch[read];
                if (write > 0)
                {
                    var prev = SampleScratch[write - 1];
                    if (math.abs(sample.Angle - prev.Angle) < MinAngleSeparationRadians)
                    {
                        if (sample.Clipped && !prev.Clipped)
                        {
                            sample.Essential = sample.Essential || prev.Essential;
                            SampleScratch[write - 1] = sample;
                        }
                        else if (!sample.Clipped && prev.Clipped)
                        {
                            prev.Essential = prev.Essential || sample.Essential;
                            SampleScratch[write - 1] = prev;
                        }
                        else if (sample.Clipped && (!prev.Clipped || sample.ClipDistance < prev.ClipDistance))
                        {
                            sample.Essential = sample.Essential || prev.Essential;
                            SampleScratch[write - 1] = sample;
                        }
                        else if (sample.Essential)
                        {
                            prev.Essential = true;
                            SampleScratch[write - 1] = prev;
                        }

                        continue;
                    }
                }

                SampleScratch[write++] = sample;
            }

            if (write < SampleScratch.Count)
            {
                SampleScratch.RemoveRange(write, SampleScratch.Count - write);
            }

            if (SampleScratch.Count >= 2)
            {
                var first = SampleScratch[0];
                var lastIndex = SampleScratch.Count - 1;
                var last = SampleScratch[lastIndex];
                if (math.abs((first.Angle + math.PI * 2f) - last.Angle) < MinAngleSeparationRadians)
                {
                    // Merge the 0/2pi seam duplicate into the high-angle entry so upload order
                    // stays monotonic (replacing last with first used to park ~0 rad at the tail and
                    // open a wedge across the whole circle).
                    if (first.Clipped && (!last.Clipped || first.ClipDistance < last.ClipDistance))
                    {
                        last.ClipDistance = first.ClipDistance;
                        last.Clipped = first.Clipped;
                    }

                    last.Essential = last.Essential || first.Essential;
                    SampleScratch[lastIndex] = last;
                    SampleScratch.RemoveAt(0);
                }
            }
        }

        private static void DecimateSamplesToBudget(int maxSegments)
        {
            if (SampleScratch.Count <= maxSegments)
            {
                return;
            }

            var decimationStep = 2;
            while (SampleScratch.Count > maxSegments && decimationStep <= 64)
            {
                var write = 0;
                var nonEssentialIndex = 0;
                for (var read = 0; read < SampleScratch.Count; read++)
                {
                    var sample = SampleScratch[read];
                    if (sample.Essential || !sample.Clipped)
                    {
                        SampleScratch[write++] = sample;
                        continue;
                    }

                    if ((nonEssentialIndex % decimationStep) == 0)
                    {
                        SampleScratch[write++] = sample;
                    }

                    nonEssentialIndex++;
                }

                if (write < SampleScratch.Count)
                {
                    SampleScratch.RemoveRange(write, SampleScratch.Count - write);
                }

                decimationStep *= 2;
            }
        }

        /// <summary>
        /// Open LUT bins only — clipped bins come from depth LUT / exit polygon samples.
        /// </summary>
        private static void InjectSparseOpenLutBins(
            float[] clipDistances,
            int lutSampleCount,
            float maxRadius,
            float openThreshold,
            int activeClipZoneCount)
        {
            if (clipDistances == null || lutSampleCount < 1)
            {
                return;
            }

            var multiZone = activeClipZoneCount >= 2;
            var step = math.max(1, lutSampleCount / (multiZone ? 360 : 72));
            for (var i = 0; i < lutSampleCount; i += step)
            {
                if (clipDistances[i] >= openThreshold)
                {
                    var angle = IndexToAngle(i, lutSampleCount);
                    AddSample(
                        angle,
                        GetDirection2D(i, lutSampleCount),
                        maxRadius + 1f,
                        clipped: false,
                        essential: multiZone);
                }
            }
        }

        private static int CountClippedSamples(float openThreshold)
        {
            var count = 0;
            for (var i = 0; i < SampleScratch.Count; i++)
            {
                if (SampleScratch[i].Clipped && SampleScratch[i].ClipDistance < openThreshold)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// When sparse edge selection misses forest depth, upload every limited LUT bin so the
        /// shader still receives clipped segments (wedge lerp needs both clipped and open samples).
        /// </summary>
        private static void BuildDenseClippedLutFallbackUpload(
            float[] clipDistances,
            int lutSampleCount,
            float maxRadius,
            float openThreshold)
        {
            if (clipDistances == null || lutSampleCount < 1)
            {
                return;
            }

            for (var i = 0; i < lutSampleCount; i++)
            {
                var clip = clipDistances[i];
                var clipped = clip < openThreshold;
                AddSample(
                    IndexToAngle(i, lutSampleCount),
                    GetDirection2D(i, lutSampleCount),
                    clipped ? clip : maxRadius + 1f,
                    clipped,
                    essential: clipped);
            }
        }

        private static float IndexToAngle(int index, int sampleCount)
        {
            return (index / (float)sampleCount) * math.PI * 2f;
        }

        private static float2 GetDirection2D(int index, int sampleCount)
        {
            return CombatForestFogAngularTables.GetDirection2D(index, sampleCount);
        }

        private static void AddSample(
            float angle,
            float2 direction,
            float clipDistance,
            bool clipped,
            bool essential = false)
        {
            if (angle < 0f)
            {
                angle += math.PI * 2f;
            }

            SampleScratch.Add(new AngularSample
            {
                Angle = angle,
                Direction = math.normalizesafe(direction, new float2(1f, 0f)),
                ClipDistance = clipDistance,
                Clipped = clipped,
                Essential = essential,
            });
        }

        /// <summary>
        /// Upload length is the analytic LUT depth clip; thin forest may cap at polygon exit.
        /// </summary>
        private static float ResolveMeshUploadLength(
            AngularSample sample,
            Vector3 flatEye,
            float maxRadius,
            in CombatForestFogLutBuildContext buildContext)
        {
            if (buildContext.RayStartedInsideForest)
            {
                return sample.ClipDistance;
            }

            return CapThinForestExitClip(
                flatEye,
                sample.Direction,
                sample.ClipDistance,
                maxRadius,
                buildContext,
                allowThinPassThrough: CombatForestFogClipper.ShouldApplyThinForestPassThroughCap(
                    flatEye,
                    new Vector3(sample.Direction.x, 0f, sample.Direction.y),
                    sample.ClipDistance,
                    maxRadius));
        }

        private static float CapThinForestExitClip(
            Vector3 flatEye,
            float2 direction2D,
            float clipDistance,
            float maxRadius,
            in CombatForestFogLutBuildContext buildContext,
            bool allowThinPassThrough)
        {
            if (!allowThinPassThrough || clipDistance >= maxRadius - DistanceEpsilonWorld)
            {
                return clipDistance;
            }

            var dirWorld = new Vector3(direction2D.x, 0f, direction2D.y);
            var exit = CombatForestFogClipper.TryGetFootprintExitForClipDistance(
                flatEye,
                dirWorld,
                clipDistance,
                maxRadius,
                buildContext.DepthWorld);
            if (exit > 0f)
            {
                return math.min(clipDistance, exit);
            }

            return clipDistance;
        }

        private static float SampleClipAlongDirection(
            Vector3 directionWorld,
            float maxRadius,
            Vector3 flatEye,
            in CombatForestFogLutBuildContext buildContext)
        {
            var limit = maxRadius;
            if (buildContext.HasForest)
            {
                limit = Mathf.Min(
                    limit,
                    CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                        buildContext,
                        directionWorld));
            }

            return limit;
        }

        private static float2 AngleToDirection2D(float angleRadians)
        {
            return new float2(math.cos(angleRadians), math.sin(angleRadians));
        }
    }
}
