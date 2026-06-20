using System.Collections.Generic;
using FOW;
using Unity.Mathematics;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Converts a uniform terrain clip LUT into sparse sight segments with refined edges and
    /// polygon corners — same wedge/chord rules as stock FOW walls.
    /// </summary>
    internal static class CombatForestFogTerrainSparseUploadBuilder
    {
        private const float DistanceEpsilonWorld = 0.01f;
        private const float CornerCrossThreshold = 0.002f;
        private const float MinAngleSeparationRadians = math.PI / 720f;
        private const int EdgeRefineIterations = 8;
        private const float EdgeSubdivisionWorld = 0.15f;

        private struct AngularSample
        {
            public float Angle;
            public float2 Direction;
            public float ClipDistance;
            public bool Clipped;
            public bool Essential;
            /// <summary>True when every LUT neighbor is also clipped — keeps full depth clip, no silhouette cap.</summary>
            public bool InteriorDepth;
        }

        private static readonly List<AngularSample> SampleScratch = new();
        private static readonly List<Vector3> CornerScratch = new();
        private static readonly List<Vector3> ZoneCornerScratch = new();
        private static readonly bool[] KeepBinScratch = new bool[CombatForestFogAngularClipperLut.SampleCount];
        private static readonly bool[] EssentialBinScratch = new bool[CombatForestFogAngularClipperLut.SampleCount];

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

            MarkTransitionAndCornerBins(clipDistances, lutSampleCount, openThreshold);
            CollectMarkedLutBins(clipDistances, lutSampleCount, openThreshold);
            RefineOpenClippedTransitions(clipDistances, lutSampleCount, maxRadius, eyeWorld, buildContext, openThreshold);
            InjectFootprintCorners(
                eyeWorld,
                maxRadius,
                buildContext,
                openThreshold,
                applyForestClip,
                applyBlockingClip);
            InjectPolygonEdgeSamples(
                eyeWorld,
                maxRadius,
                buildContext,
                openThreshold,
                applyForestClip,
                applyBlockingClip);
            SortAndDedupeSamples(openThreshold);

            if (SampleScratch.Count > maxSegments)
            {
                DecimateSamplesToBudget(maxSegments);
            }

            if (SampleScratch.Count > maxSegments)
            {
                BuildPrioritizedFallbackUpload(
                    clipDistances,
                    lutSampleCount,
                    maxRadius,
                    outDirections,
                    outUploadLengths,
                    maxSegments);
                return outDirections.Count;
            }

            var flatEye = eyeWorld;
            flatEye.y = 0f;
            for (var i = 0; i < SampleScratch.Count; i++)
            {
                var sample = SampleScratch[i];
                outDirections.Add(sample.Direction);
                outUploadLengths.Add(
                    sample.Clipped
                        ? ResolveMeshUploadLength(sample, flatEye, maxRadius, buildContext)
                        : maxRadius + 1f);
            }

            return outDirections.Count;
        }

        private static void MarkTransitionAndCornerBins(
            float[] clipDistances,
            int lutSampleCount,
            float openThreshold)
        {
            for (var i = 0; i < lutSampleCount; i++)
            {
                KeepBinScratch[i] = false;
                EssentialBinScratch[i] = false;
            }

            for (var i = 0; i < lutSampleCount; i++)
            {
                var prev = (i - 1 + lutSampleCount) % lutSampleCount;
                var next = (i + 1) % lutSampleCount;
                var clipped = clipDistances[i] < openThreshold;
                var prevClipped = clipDistances[prev] < openThreshold;
                var nextClipped = clipDistances[next] < openThreshold;

                if (clipped != prevClipped || clipped != nextClipped)
                {
                    KeepBinScratch[i] = true;
                    KeepBinScratch[prev] = true;
                    KeepBinScratch[next] = true;
                    EssentialBinScratch[i] = true;
                    EssentialBinScratch[prev] = true;
                    EssentialBinScratch[next] = true;
                }

                if (!clipped || !prevClipped || !nextClipped)
                {
                    continue;
                }

                var dir = GetDirection2D(i, lutSampleCount);
                var dirPrev = GetDirection2D(prev, lutSampleCount);
                var dirNext = GetDirection2D(next, lutSampleCount);
                var point = dir * clipDistances[i];
                var pointPrev = dirPrev * clipDistances[prev];
                var pointNext = dirNext * clipDistances[next];
                var edgePrev = point - pointPrev;
                var edgeNext = pointNext - point;
                var cross = edgePrev.x * edgeNext.y - edgePrev.y * edgeNext.x;
                if (math.abs(cross) > CornerCrossThreshold)
                {
                    KeepBinScratch[i] = true;
                    EssentialBinScratch[i] = true;
                }

                // Interior forest rays keep full depth clip; silhouette uses footprint boundary.
                if (clipped && prevClipped && nextClipped)
                {
                    KeepBinScratch[i] = true;
                }
            }
        }

        private static void CollectMarkedLutBins(
            float[] clipDistances,
            int lutSampleCount,
            float openThreshold)
        {
            for (var i = 0; i < lutSampleCount; i++)
            {
                if (!KeepBinScratch[i])
                {
                    continue;
                }

                var clipped = clipDistances[i] < openThreshold;
                var prev = (i - 1 + lutSampleCount) % lutSampleCount;
                var next = (i + 1) % lutSampleCount;
                var interiorDepth = clipped
                    && clipDistances[prev] < openThreshold
                    && clipDistances[next] < openThreshold;
                AddSample(
                    IndexToAngle(i, lutSampleCount),
                    GetDirection2D(i, lutSampleCount),
                    clipDistances[i],
                    clipped,
                    essential: EssentialBinScratch[i] || !clipped,
                    interiorDepth: interiorDepth);
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

                var refinedAngle = angleHigh % (math.PI * 2f);
                var refinedDir = AngleToDirection2D(refinedAngle);
                var refinedClip = SampleClipAtAngle(refinedAngle, maxRadius, eyeWorld, buildContext);
                AddSample(refinedAngle, refinedDir, refinedClip, refinedClip < openThreshold, essential: true);

                var openDir = AngleToDirection2D(angleOpen % (math.PI * 2f));
                AddSample(angleOpen % (math.PI * 2f), openDir, maxRadius, clipped: false, essential: true);
            }
        }

        private static void InjectFootprintCorners(
            Vector3 eyeWorld,
            float maxRadius,
            in CombatForestFogLutBuildContext buildContext,
            float openThreshold,
            bool applyForestClip,
            bool applyBlockingClip)
        {
            CornerScratch.Clear();
            if (applyForestClip || applyBlockingClip)
            {
                CombatForestFogClipper.CollectLimitedDepthZoneCornersWorld(CornerScratch);
            }

            var flatEye = eyeWorld;
            flatEye.y = 0f;
            for (var i = 0; i < CornerScratch.Count; i++)
            {
                var corner = CornerScratch[i];
                corner.y = flatEye.y;
                var offset = corner - flatEye;
                offset.y = 0f;
                if (offset.sqrMagnitude <= 1e-8f)
                {
                    continue;
                }

                var directionWorld = offset.normalized;
                var clip = SampleClipAlongDirection(directionWorld, maxRadius, flatEye, buildContext);
                if (clip >= openThreshold)
                {
                    continue;
                }

                var dir2 = math.normalize(new float2(directionWorld.x, directionWorld.z));
                var angle = math.atan2(dir2.y, dir2.x);
                if (angle < 0f)
                {
                    angle += math.PI * 2f;
                }

                AddSample(angle, dir2, clip, true, essential: true);
            }
        }

        /// <summary>
        /// Subdivides visible polygon edges so wedge/chords follow straight footprint lines (not LUT stairs).
        /// </summary>
        private static void InjectPolygonEdgeSamples(
            Vector3 eyeWorld,
            float maxRadius,
            in CombatForestFogLutBuildContext buildContext,
            float openThreshold,
            bool applyForestClip,
            bool applyBlockingClip)
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

                var eyeInside = zone.ContainsPoint(flatEye);
                for (var c = 0; c < ZoneCornerScratch.Count; c++)
                {
                    var edgeStart = ZoneCornerScratch[c];
                    var edgeEnd = ZoneCornerScratch[(c + 1) % ZoneCornerScratch.Count];
                    if (!IsPolygonEdgeFacingEye(edgeStart, edgeEnd, flatEye, eyeInside))
                    {
                        continue;
                    }

                    InjectSubdividedEdgeSamples(
                        edgeStart,
                        edgeEnd,
                        flatEye,
                        maxRadius,
                        buildContext,
                        openThreshold,
                        subdivWorld);
                }
            }
        }

        private static bool IsPolygonEdgeFacingEye(
            Vector3 edgeStart,
            Vector3 edgeEnd,
            Vector3 flatEye,
            bool eyeInsideZone)
        {
            var a = new Vector2(edgeStart.x, edgeStart.z);
            var b = new Vector2(edgeEnd.x, edgeEnd.z);
            var eye = new Vector2(flatEye.x, flatEye.z);
            var edge = b - a;
            if (edge.sqrMagnitude <= 1e-8f)
            {
                return false;
            }

            var normal = new Vector2(edge.y, -edge.x);
            var midpoint = (a + b) * 0.5f;
            var signedDist = math.dot(normal, eye - midpoint);
            return eyeInsideZone ? signedDist < 0f : signedDist > 0f;
        }

        private static void InjectSubdividedEdgeSamples(
            Vector3 edgeStart,
            Vector3 edgeEnd,
            Vector3 flatEye,
            float maxRadius,
            in CombatForestFogLutBuildContext buildContext,
            float openThreshold,
            float subdivWorld)
        {
            edgeStart.y = flatEye.y;
            edgeEnd.y = flatEye.y;
            var edgeLen = Vector3.Distance(edgeStart, edgeEnd);
            var steps = math.min(
                4,
                math.max(1, (int)math.ceil(edgeLen / math.max(subdivWorld, 0.01f))));
            var segStart2 = new Vector2(edgeStart.x, edgeStart.z);
            var segEnd2 = new Vector2(edgeEnd.x, edgeEnd.z);

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
                if (!CombatFogPlanarGeometry.TryRaySegmentHit(
                        new Vector2(flatEye.x, flatEye.z),
                        dir2,
                        segStart2,
                        segEnd2,
                        out var edgeHitDist))
                {
                    continue;
                }

                var clip = SampleClipAlongDirection(dirWorld, maxRadius, flatEye, buildContext);
                if (clip >= openThreshold)
                {
                    continue;
                }

                // Another feature blocks before this edge along the ray.
                if (clip + DistanceEpsilonWorld < edgeHitDist)
                {
                    continue;
                }

                var angle = math.atan2(dir2.y, dir2.x);
                if (angle < 0f)
                {
                    angle += math.PI * 2f;
                }

                var isCorner = s == 0 || s == steps;
                AddSample(angle, dir2, edgeHitDist, true, essential: isCorner);
            }
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
                        if (sample.Clipped && (!prev.Clipped || sample.ClipDistance < prev.ClipDistance))
                        {
                            sample.Essential = sample.Essential || prev.Essential;
                            sample.InteriorDepth = sample.InteriorDepth && prev.InteriorDepth;
                            SampleScratch[write - 1] = sample;
                        }
                        else if (sample.Essential)
                        {
                            prev.Essential = true;
                            prev.InteriorDepth = prev.InteriorDepth && sample.InteriorDepth;
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
                    last.InteriorDepth = last.InteriorDepth && first.InteriorDepth;
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

        private static void BuildPrioritizedFallbackUpload(
            float[] clipDistances,
            int lutSampleCount,
            float maxRadius,
            List<float2> outDirections,
            List<float> outUploadLengths,
            int maxSegments)
        {
            var openThreshold = maxRadius - DistanceEpsilonWorld;
            for (var i = 0; i < lutSampleCount && outDirections.Count < maxSegments; i++)
            {
                var clipped = clipDistances[i] < openThreshold;
                if (!clipped && !EssentialBinScratch[i])
                {
                    continue;
                }

                outDirections.Add(GetDirection2D(i, lutSampleCount));
                outUploadLengths.Add(clipped ? clipDistances[i] : maxRadius + 1f);
            }

            if (outDirections.Count >= 2)
            {
                return;
            }

            var step = math.max(1, lutSampleCount / math.max(2, maxSegments));
            for (var i = 0; i < lutSampleCount && outDirections.Count < maxSegments; i += step)
            {
                var clipped = clipDistances[i] < openThreshold;
                outDirections.Add(GetDirection2D(i, lutSampleCount));
                outUploadLengths.Add(clipped ? clipDistances[i] : maxRadius + 1f);
            }
        }

        private static void BuildUniformFallbackUpload(
            float[] clipDistances,
            int lutSampleCount,
            float maxRadius,
            List<float2> outDirections,
            List<float> outUploadLengths,
            int maxSegments)
        {
            var step = math.max(1, lutSampleCount / math.max(2, maxSegments));
            for (var i = 0; i < lutSampleCount && outDirections.Count < maxSegments; i += step)
            {
                var clipped = clipDistances[i] < maxRadius - DistanceEpsilonWorld;
                outDirections.Add(GetDirection2D(i, lutSampleCount));
                outUploadLengths.Add(clipped ? clipDistances[i] : maxRadius + 1f);
            }
        }

        private static void AddSample(
            float angle,
            float2 direction,
            float clipDistance,
            bool clipped,
            bool essential = false,
            bool interiorDepth = false)
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
                InteriorDepth = interiorDepth,
            });
        }

        /// <summary>
        /// Wedge mesh uses footprint silhouette distances at boundaries; interior rays keep depth clip.
        /// </summary>
        private static float ResolveMeshUploadLength(
            AngularSample sample,
            Vector3 flatEye,
            float maxRadius,
            in CombatForestFogLutBuildContext buildContext)
        {
            if (sample.InteriorDepth)
            {
                return sample.ClipDistance;
            }

            var boundary = TryGetFootprintBoundaryDistance(
                flatEye,
                sample.Direction,
                maxRadius,
                buildContext);
            if (boundary > 0f
                && sample.ClipDistance > boundary + DistanceEpsilonWorld)
            {
                return boundary;
            }

            return sample.ClipDistance;
        }

        private static float TryGetFootprintBoundaryDistance(
            Vector3 flatEye,
            float2 direction2D,
            float maxRadius,
            in CombatForestFogLutBuildContext buildContext)
        {
            var dirWorld = new Vector3(direction2D.x, 0f, direction2D.y);
            if (dirWorld.sqrMagnitude <= 1e-8f || maxRadius <= 0.001f)
            {
                return -1f;
            }

            dirWorld.Normalize();
            var best = -1f;
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
                if (isForest && !buildContext.ApplyForestClip)
                {
                    continue;
                }

                if (isCloudFog && !buildContext.ApplyBlockingClip)
                {
                    continue;
                }

                if (!isForest && !isCloudFog)
                {
                    continue;
                }

                var polygon = zone.GetComponent<CombatZonePolygonFootprint>();
                if (polygon == null
                    || !polygon.HasFootprint
                    || !polygon.TryGetRayFootprintIntervalWorld(flatEye, dirWorld, out var enter, out var exit))
                {
                    continue;
                }

                var eyeInside = zone.ContainsPoint(flatEye);
                var boundary = -1f;
                if (eyeInside && exit > 0f && exit <= maxRadius)
                {
                    boundary = exit;
                }
                else if (!eyeInside && enter > 0f && enter <= maxRadius)
                {
                    boundary = enter;
                }

                if (boundary > 0f && (best < 0f || boundary < best))
                {
                    best = boundary;
                }
            }

            return best;
        }

        private static float SampleClipAtAngle(
            float angleRadians,
            float maxRadius,
            Vector3 eyeWorld,
            in CombatForestFogLutBuildContext buildContext)
        {
            var directionWorld = AngleToDirectionWorld(angleRadians);
            return SampleClipAlongDirection(directionWorld, maxRadius, eyeWorld, buildContext);
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

        private static float IndexToAngle(int index, int sampleCount)
        {
            return (index / (float)sampleCount) * math.PI * 2f;
        }

        private static float2 GetDirection2D(int index, int sampleCount)
        {
            return CombatForestFogAngularTables.GetDirection2D(index, sampleCount);
        }

        private static float2 AngleToDirection2D(float angleRadians)
        {
            return new float2(math.cos(angleRadians), math.sin(angleRadians));
        }

        private static Vector3 AngleToDirectionWorld(float angleRadians)
        {
            var dir2 = AngleToDirection2D(angleRadians);
            return new Vector3(dir2.x, 0f, dir2.y);
        }
    }
}
