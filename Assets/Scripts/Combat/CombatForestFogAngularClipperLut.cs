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

        /// <summary>Half-width of one LUT bin — keeps smoothing local to adjacent rays.</summary>
        public static float NeighborHalfAngleRadians => Mathf.PI / SampleCount;

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
            CombatBlockingTerrainClipper.EnsureCache();

            var limit = maxSearchRadius;
            var depthWorld = CombatForestFogDepth.ResolveDepthWorld();
            if (depthWorld > 0.001f && CombatForestFogClipper.HasActiveZones)
            {
                limit = Mathf.Min(
                    limit,
                    CombatForestFogClipper.GetFirstContactDepthClipDistanceWorldSmoothed(
                        eyeWorld,
                        directionWorld,
                        maxSearchRadius,
                        depthWorld,
                        originRadiusWorld,
                        NeighborHalfAngleRadians));
            }

            if (CombatBlockingTerrainClipper.HasActiveZones)
            {
                limit = Mathf.Min(
                    limit,
                    CombatBlockingTerrainClipper.GetFogClipDistanceWorldSmoothed(
                        eyeWorld,
                        directionWorld,
                        maxSearchRadius,
                        originRadiusWorld,
                        rayStartedInsideOverride: null,
                        NeighborHalfAngleRadians));
            }

            return limit;
        }

        /// <summary>
        /// Fills one smoothed clip sample per degree bin, then applies a local min envelope
        /// so isolated full-radius spikes do not jag the fog boundary on straight forest edges.
        /// </summary>
        public static void BuildSmoothedClipDistances(
            Vector3 eyeWorld,
            float maxSearchRadius,
            float originRadiusWorld,
            FogOfWarRevealer3D.PlaneProjection projection,
            float[] clipDistances)
        {
            var sampleCount = math.min(SampleCount, clipDistances?.Length ?? 0);
            if (sampleCount <= 0)
            {
                return;
            }

            CombatForestFogClipper.EnsureCache();
            CombatBlockingTerrainClipper.EnsureCache();

            var depthWorld = CombatForestFogDepth.ResolveDepthWorld();
            var halfAngle = NeighborHalfAngleRadians;

            for (var i = 0; i < sampleCount; i++)
            {
                var angle = (i / (float)sampleCount) * math.PI * 2f;
                var dir2 = new float2(math.cos(angle), math.sin(angle));
                var dir3 = CombatFogProjection.Direction2DToWorld(dir2, projection);

                var limit = maxSearchRadius;
                if (depthWorld > 0.001f && CombatForestFogClipper.HasActiveZones)
                {
                    limit = Mathf.Min(
                        limit,
                        CombatForestFogClipper.GetFirstContactDepthClipDistanceWorldSmoothed(
                            eyeWorld,
                            dir3,
                            maxSearchRadius,
                            depthWorld,
                            originRadiusWorld,
                            halfAngle));
                }

                if (CombatBlockingTerrainClipper.HasActiveZones)
                {
                    limit = Mathf.Min(
                        limit,
                        CombatBlockingTerrainClipper.GetFogClipDistanceWorldSmoothed(
                            eyeWorld,
                            dir3,
                            maxSearchRadius,
                            originRadiusWorld,
                            rayStartedInsideOverride: null,
                            halfAngle));
                }

                clipDistances[i] = limit;
            }

            // Post-filters that pull open bins inward are for outside-the-forest leaks only.
            // When the eye is inside forest, they destroy legitimate see-out wedges at the edge.
            if (!CombatForestFogClipper.IsInsideLimitedDepthForest(eyeWorld, 0f))
            {
                RemoveOutwardAngularSpikes(clipDistances, sampleCount, maxSearchRadius);
                RemoveOpenBinsAdjacentToForestClip(clipDistances, sampleCount, maxSearchRadius);
            }
        }

        /// <summary>
        /// Any still-open bin touching a forest-limited bin inherits the shorter neighbor clip.
        /// Only used when the unit is outside forest — fixes thin leaks past circular edges.
        /// </summary>
        public static void RemoveOpenBinsAdjacentToForestClip(float[] clipDistances, int count, float maxSearchRadius)
        {
            if (clipDistances == null || count < 3 || maxSearchRadius <= 0.001f)
            {
                return;
            }

            var openThreshold = maxSearchRadius - CombatScale.InchesToWorldUnits(0.25f);
            var scratch = new float[count];

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
        public static void RemoveOutwardAngularSpikes(float[] clipDistances, int count, float maxSearchRadius)
        {
            if (clipDistances == null || count < 3 || maxSearchRadius <= 0.001f)
            {
                return;
            }

            var spikeThreshold = CombatScale.InchesToWorldUnits(0.5f);
            var openThreshold = maxSearchRadius - CombatScale.InchesToWorldUnits(0.25f);
            var scratch = new float[count];

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
