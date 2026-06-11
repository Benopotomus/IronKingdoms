using System.Collections.Generic;
using FOW;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Applies forest pass-through depth to stock FOW phase-1 raycast results.
    /// Wall and normal occluder hits are already in the ray buffers when this runs; forest only
    /// shortens rays that the analytic clipper says should stop sooner.
    /// </summary>
    internal sealed class CombatForestFogRayPostProcessor
    {
        private const int ForestLimitedAngularScanSteps = 96;

        private readonly HashSet<int> bridgedRayIndices = new();

        public HashSet<int> BridgedRayIndices => bridgedRayIndices;

        public void ClearDebugState()
        {
            bridgedRayIndices.Clear();
        }

        public void Apply(
            RaycastRevealer.SightIteration firstIteration,
            int stepCount,
            Vector3 eyeWorld,
            float maxRadius,
            FogOfWarRevealer3D.PlaneProjection projection,
            bool baseIntersectsForest)
        {
            bridgedRayIndices.Clear();

            var depthWorld = CombatForestFogDepth.ResolveDepthWorld();
            var projectedEye = projection.Project((float3)eyeWorld);

            ApplyForestClipToFirstIteration(firstIteration, stepCount, eyeWorld, maxRadius, depthWorld, baseIntersectsForest, projectedEye, projection);
            FillForestMissBridges(firstIteration, stepCount, eyeWorld, maxRadius, depthWorld, baseIntersectsForest, projectedEye, projection);
        }

        public void ForceContourConditions(
            RaycastRevealer.SightIteration firstIteration,
            NativeArray<bool> firstIterationConditions,
            int stepCount,
            Vector3 eyeWorld,
            float maxRadius,
            FogOfWarRevealer3D.PlaneProjection projection,
            bool baseIntersectsForest)
        {
            var depthWorld = CombatForestFogDepth.ResolveDepthWorld();
            ForceForestContourViewPoints(firstIteration, firstIterationConditions, stepCount, maxRadius);
            ForceForestAdjacentOpenContourPoints(firstIteration, firstIterationConditions, stepCount, eyeWorld, maxRadius, depthWorld, baseIntersectsForest, projection);
        }

        private static void ApplyForestClipToFirstIteration(
            RaycastRevealer.SightIteration firstIteration,
            int stepCount,
            Vector3 eyeWorld,
            float maxRadius,
            float depthWorld,
            bool baseIntersectsForest,
            float2 projectedEye,
            FogOfWarRevealer3D.PlaneProjection projection)
        {
            for (var i = 0; i < stepCount; i++)
            {
                if (!TryGetRayDirections(firstIteration, i, projection, out var dir2, out var dir3))
                {
                    continue;
                }

                var forestClip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                    eyeWorld,
                    dir3,
                    maxRadius,
                    depthWorld,
                    baseIntersectsForest);

                var physicsHit = firstIteration.Hits[i];
                var physicsDistance = physicsHit ? firstIteration.Distances[i] : maxRadius;
                var finalDistance = Mathf.Min(physicsDistance, forestClip);

                if (finalDistance >= maxRadius - 0.001f)
                {
                    firstIteration.Hits[i] = false;
                    firstIteration.Distances[i] = maxRadius;
                    firstIteration.Points[i] = projectedEye + (dir2 * maxRadius);
                    firstIteration.Normals[i] = -dir2;
                    firstIteration.IsForestHit[i] = false;
                    continue;
                }

                var forestIsTighter = forestClip < physicsDistance - 0.001f;
                firstIteration.Hits[i] = true;
                firstIteration.Distances[i] = finalDistance;
                firstIteration.Points[i] = projectedEye + (dir2 * finalDistance);
                firstIteration.IsForestHit[i] = forestIsTighter || !physicsHit;
                if (forestIsTighter || !physicsHit)
                {
                    firstIteration.Normals[i] = -dir2;
                }
            }
        }

        /// <summary>
        /// Per-ray bridge: any sample the clipper would limit gets a hit so stock SortData does
        /// not draw a miss chord across a forest-limited arc.
        /// </summary>
        private void FillForestMissBridges(
            RaycastRevealer.SightIteration firstIteration,
            int stepCount,
            Vector3 eyeWorld,
            float maxRadius,
            float depthWorld,
            bool baseIntersectsForest,
            float2 projectedEye,
            FogOfWarRevealer3D.PlaneProjection projection)
        {
            for (var i = 0; i < stepCount; i++)
            {
                if (firstIteration.Hits[i])
                {
                    continue;
                }

                if (!TryGetRayDirections(firstIteration, i, projection, out var dir2, out var dir3))
                {
                    continue;
                }

                var forestClip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                    eyeWorld,
                    dir3,
                    maxRadius,
                    depthWorld,
                    baseIntersectsForest);
                if (forestClip >= maxRadius - 0.01f)
                {
                    continue;
                }

                BridgeForestRay(firstIteration, i, dir2, forestClip, projectedEye);
            }
        }

        private void BridgeForestRay(
            RaycastRevealer.SightIteration firstIteration,
            int index,
            float2 direction,
            float bridgeDistance,
            float2 projectedEye)
        {
            firstIteration.Hits[index] = true;
            firstIteration.Distances[index] = bridgeDistance;
            firstIteration.Points[index] = projectedEye + (direction * bridgeDistance);
            firstIteration.Normals[index] = -direction;
            firstIteration.IsForestHit[index] = true;
            bridgedRayIndices.Add(index);
        }

        /// <summary>
        /// Stock FOW SortData can skip clipped rays; force every forest-limited sample into the contour.
        /// </summary>
        private static void ForceForestContourViewPoints(
            RaycastRevealer.SightIteration firstIteration,
            NativeArray<bool> firstIterationConditions,
            int stepCount,
            float maxRadius)
        {
            for (var i = 0; i < stepCount; i++)
            {
                if (!firstIteration.Hits[i] || firstIteration.Distances[i] >= maxRadius - 0.01f)
                {
                    continue;
                }

                firstIterationConditions[i] = true;
            }
        }

        /// <summary>
        /// Genuinely open rays near the forest-depth arc must stay in the contour even when many
        /// consecutive clipped samples sit between them and the arc.
        /// </summary>
        private static void ForceForestAdjacentOpenContourPoints(
            RaycastRevealer.SightIteration firstIteration,
            NativeArray<bool> firstIterationConditions,
            int stepCount,
            Vector3 eyeWorld,
            float maxRadius,
            float depthWorld,
            bool baseIntersectsForest,
            FogOfWarRevealer3D.PlaneProjection projection)
        {
            for (var i = 0; i < stepCount; i++)
            {
                if (!IsOpenMissRay(firstIteration, i, maxRadius))
                {
                    continue;
                }

                if (!TryGetRayDirections(firstIteration, i, projection, out _, out var dir3))
                {
                    continue;
                }

                var forestClip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                    eyeWorld,
                    dir3,
                    maxRadius,
                    depthWorld,
                    baseIntersectsForest);
                if (forestClip < maxRadius - 0.01f)
                {
                    continue;
                }

                if (!ForestLimitedHitWithinAngularScan(firstIteration, i, maxRadius, stepCount))
                {
                    continue;
                }

                firstIterationConditions[i] = true;
            }
        }

        private static bool ForestLimitedHitWithinAngularScan(
            RaycastRevealer.SightIteration firstIteration,
            int index,
            float maxRadius,
            int count)
        {
            for (var d = 1; d <= ForestLimitedAngularScanSteps; d++)
            {
                var previous = index - d;
                if (previous >= 0 && IsForestLimitedHit(firstIteration, previous, maxRadius))
                {
                    return true;
                }

                var next = index + d;
                if (next < count && IsForestLimitedHit(firstIteration, next, maxRadius))
                {
                    return true;
                }
            }

            return false;
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

        private static bool IsOpenMissRay(RaycastRevealer.SightIteration firstIteration, int index, float maxRadius)
        {
            return !firstIteration.Hits[index] && firstIteration.Distances[index] >= maxRadius - 0.01f;
        }

        private static bool IsForestLimitedHit(RaycastRevealer.SightIteration firstIteration, int index, float maxRadius)
        {
            return firstIteration.Hits[index] && firstIteration.Distances[index] < maxRadius - 0.01f;
        }
    }
}
