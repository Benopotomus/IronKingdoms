using FOW;
using Unity.Mathematics;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Uniform angular clipper samples shared by the yellow debug contour and GPU fog.
    /// </summary>
    internal static class CombatForestFogAngularClipperLut
    {
        public const int SampleCount = 720;

        private static readonly float[] PostFilterScratch = new float[SampleCount];

        /// <summary>Half-width of one LUT bin — keeps smoothing local to adjacent rays.</summary>
        public static float NeighborHalfAngleRadians => Mathf.PI / SampleCount;

        public static float NeighborHalfAngleRadiansForCount(int sampleCount)
        {
            return sampleCount > 0 ? Mathf.PI / sampleCount : NeighborHalfAngleRadians;
        }

        public static float SampleClipDistanceWorld(
            Vector3 eyeWorld,
            float2 queryDirection,
            float maxSearchRadius,
            float originRadiusWorld,
            FogOfWarRevealer3D.PlaneProjection projection)
        {
            if (maxSearchRadius <= 0f || math.lengthsq(queryDirection) <= 1e-8f)
            {
                return maxSearchRadius;
            }

            var directionWorld = CombatFogProjection.Direction2DToWorld(
                math.normalize(queryDirection),
                projection);

            return SampleClipDistanceWorld(
                eyeWorld,
                directionWorld,
                maxSearchRadius,
                originRadiusWorld);
        }

        public static float SampleClipDistanceWorld(
            Vector3 eyeWorld,
            Vector3 directionWorld,
            float maxSearchRadius,
            float originRadiusWorld)
        {
            if (maxSearchRadius <= 0f || directionWorld.sqrMagnitude <= 1e-8f)
            {
                return maxSearchRadius;
            }

            CombatForestFogClipper.EnsureCache();

            return SampleClipDistanceWorldCached(
                eyeWorld,
                directionWorld,
                maxSearchRadius,
                originRadiusWorld,
                NeighborHalfAngleRadians,
                CombatForestFogPassSettings.UseAngularMedianSmoothing);
        }

        /// <summary>
        /// Fills smoothed clip samples for evenly spaced directions. Pass the same count that will
        /// be uploaded to the shader — do not always fill all 720 bins when fewer are needed.
        /// </summary>
        public static void BuildSmoothedClipDistances(
            Vector3 eyeWorld,
            float maxSearchRadius,
            float originRadiusWorld,
            FogOfWarRevealer3D.PlaneProjection projection,
            float[] clipDistances,
            int angularSampleCount = SampleCount,
            bool applyForestClip = true,
            bool applyBlockingClip = true,
            bool skipTerrainPostFilters = false)
        {
            var sampleCount = math.min(
                angularSampleCount > 0 ? angularSampleCount : SampleCount,
                clipDistances?.Length ?? 0);
            if (sampleCount <= 0)
            {
                return;
            }

            CombatForestFogClipper.EnsureCache();
            CombatForestFogClipper.SetClipPassFilters(applyForestClip, applyBlockingClip);

            try
            {
                var depthWorld = CombatForestFogDepth.ResolveDepthWorld();
                var halfAngle = NeighborHalfAngleRadiansForCount(sampleCount);
                var useMedianSmoothing = CombatForestFogPassSettings.UseAngularMedianSmoothing;

                if (!applyForestClip && !applyBlockingClip)
                {
                    for (var i = 0; i < sampleCount; i++)
                    {
                        clipDistances[i] = maxSearchRadius;
                    }

                    return;
                }

                var flatEye = eyeWorld;
                flatEye.y = 0f;
                var sampleDir = CombatForestFogAngularTables.GetDirectionWorldXZ(0, sampleCount);
                var buildContext = new CombatForestFogLutBuildContext(
                    flatEye,
                    maxSearchRadius,
                    originRadiusWorld,
                    depthWorld,
                    CombatForestFogClipper.ComputeRayStartedInsideForest(flatEye, sampleDir, originRadiusWorld),
                    applyForestClip,
                    applyBlockingClip);

                if (!buildContext.HasForest)
                {
                    for (var i = 0; i < sampleCount; i++)
                    {
                        clipDistances[i] = maxSearchRadius;
                    }

                    return;
                }

                var terrainInReach = buildContext.RayStartedInsideForest
                    || CombatForestFogClipper.AnyCachedZoneWithinReach(buildContext.FlatEye, maxSearchRadius);
                if (!terrainInReach)
                {
                    for (var i = 0; i < sampleCount; i++)
                    {
                        clipDistances[i] = maxSearchRadius;
                    }

                    return;
                }

                for (var i = 0; i < sampleCount; i++)
                {
                    var directionWorld = CombatForestFogAngularTables.GetDirectionWorldXZ(i, sampleCount);
                    if (!buildContext.RayStartedInsideForest
                        && !CombatForestFogClipper.RayMayHitAnyCachedZoneAabbPublic(
                            buildContext.FlatEye,
                            directionWorld,
                            buildContext.MaxSearchRadius))
                    {
                        clipDistances[i] = maxSearchRadius;
                        continue;
                    }

                    clipDistances[i] = SampleClipDistanceWorldCached(
                        buildContext,
                        i,
                        sampleCount,
                        halfAngle,
                        useMedianSmoothing);
                }

                // Post-filters that pull open bins inward are for outside-the-forest leaks only.
                // When the eye is inside forest, they destroy legitimate see-out wedges at the edge.
                if (!buildContext.RayStartedInsideForest && !skipTerrainPostFilters)
                {
                    RemoveOutwardAngularSpikes(clipDistances, sampleCount, maxSearchRadius, PostFilterScratch);
                    RemoveOpenBinsAdjacentToForestClip(clipDistances, sampleCount, maxSearchRadius, PostFilterScratch);
                }
            }
            finally
            {
                CombatForestFogClipper.ResetClipPassFilters();
            }
        }

        private static float SampleClipDistanceWorldCached(
            in CombatForestFogLutBuildContext ctx,
            int directionIndex,
            int sampleCount,
            float neighborHalfAngleRadians,
            bool useMedianSmoothing)
        {
            var directionWorld = CombatForestFogAngularTables.GetDirectionWorldXZ(directionIndex, sampleCount);
            return SampleClipDistanceWorldCached(
                ctx,
                directionWorld,
                neighborHalfAngleRadians,
                useMedianSmoothing);
        }

        private static float SampleClipDistanceWorldCached(
            in CombatForestFogLutBuildContext ctx,
            Vector3 directionWorld,
            float neighborHalfAngleRadians,
            bool useMedianSmoothing)
        {
            var limit = ctx.MaxSearchRadius;
            if (ctx.HasForest)
            {
                limit = Mathf.Min(
                    limit,
                    useMedianSmoothing
                        ? CombatForestFogClipper.GetFirstContactDepthClipDistanceWorldSmoothed(
                            ctx.FlatEye,
                            directionWorld,
                            ctx.MaxSearchRadius,
                            ctx.DepthWorld,
                            ctx.OriginRadiusWorld,
                            neighborHalfAngleRadians)
                        : CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                            ctx,
                            directionWorld));
            }

            return limit;
        }

        private static float SampleClipDistanceWorldCached(
            Vector3 eyeWorld,
            Vector3 directionWorld,
            float maxSearchRadius,
            float originRadiusWorld,
            float neighborHalfAngleRadians,
            bool useMedianSmoothing,
            bool applyForestClip = true,
            bool applyBlockingClip = true,
            float depthWorld = -1f)
        {
            if (depthWorld < 0f)
            {
                depthWorld = CombatForestFogDepth.ResolveDepthWorld();
            }

            CombatForestFogClipper.SetClipPassFilters(applyForestClip, applyBlockingClip);
            try
            {
                var flatEye = eyeWorld;
                flatEye.y = 0f;
                var ctx = new CombatForestFogLutBuildContext(
                    flatEye,
                    maxSearchRadius,
                    originRadiusWorld,
                    depthWorld,
                    CombatForestFogClipper.ComputeRayStartedInsideForest(flatEye, directionWorld, originRadiusWorld),
                    applyForestClip,
                    applyBlockingClip);

                return SampleClipDistanceWorldCached(
                    ctx,
                    directionWorld,
                    neighborHalfAngleRadians,
                    useMedianSmoothing);
            }
            finally
            {
                CombatForestFogClipper.ResetClipPassFilters();
            }
        }

        /// <summary>
        /// Any still-open bin touching a forest-limited bin inherits the shorter neighbor clip.
        /// Only used when the unit is outside forest — fixes thin leaks past circular edges.
        /// </summary>
        public static void RemoveOpenBinsAdjacentToForestClip(
            float[] clipDistances,
            int count,
            float maxSearchRadius,
            float[] scratch = null)
        {
            if (clipDistances == null || count < 3 || maxSearchRadius <= 0.001f)
            {
                return;
            }

            scratch ??= PostFilterScratch;
            var openThreshold = maxSearchRadius - CombatScale.InchesToWorldUnits(0.25f);

            for (var i = 0; i < count; i++)
            {
                var curr = clipDistances[i];
                if (curr < openThreshold)
                {
                    scratch[i] = curr;
                    continue;
                }

                var prev = clipDistances[(i - 1 + count) % count];
                var next = clipDistances[(i + 1) % count];
                var prevLimited = prev < openThreshold;
                var nextLimited = next < openThreshold;

                if (prevLimited && nextLimited)
                {
                    scratch[i] = Mathf.Min(prev, next);
                }
                else if (prevLimited)
                {
                    scratch[i] = prev;
                }
                else if (nextLimited)
                {
                    scratch[i] = next;
                }
                else
                {
                    scratch[i] = curr;
                }
            }

            for (var i = 0; i < count; i++)
            {
                clipDistances[i] = scratch[i];
            }
        }

        /// <summary>
        /// Only trims isolated full-radius spikes between two shorter neighbors.
        /// Unlike a wide min-envelope, this does not pull legitimate long clips inward.
        /// </summary>
        public static void RemoveOutwardAngularSpikes(
            float[] clipDistances,
            int count,
            float maxSearchRadius,
            float[] scratch = null)
        {
            if (clipDistances == null || count < 3 || maxSearchRadius <= 0.001f)
            {
                return;
            }

            scratch ??= PostFilterScratch;
            var spikeThreshold = CombatScale.InchesToWorldUnits(0.5f);
            var openThreshold = maxSearchRadius - CombatScale.InchesToWorldUnits(0.25f);

            for (var i = 0; i < count; i++)
            {
                var prev = clipDistances[(i - 1 + count) % count];
                var curr = clipDistances[i];
                var next = clipDistances[(i + 1) % count];
                var neighborMin = Mathf.Min(prev, next);

                if (curr > openThreshold
                    && neighborMin < openThreshold - spikeThreshold
                    && curr > neighborMin + spikeThreshold)
                {
                    scratch[i] = neighborMin;
                }
                else
                {
                    scratch[i] = curr;
                }
            }

            for (var i = 0; i < count; i++)
            {
                clipDistances[i] = scratch[i];
            }
        }
    }
}
