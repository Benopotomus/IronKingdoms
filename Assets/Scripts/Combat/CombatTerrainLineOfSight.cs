using System.Collections.Generic;
using UnityEngine;

namespace IronKingdoms.Combat
{
    public static class CombatTerrainLineOfSight
    {
        public static bool IsForestDepthBlockingLineOfSight(
            CombatLineOfSightVolume observer,
            CombatLineOfSightVolume target,
            ModelSize targetModelSize,
            UnitTypeDefinition observerDefinition,
            GameObject observerPawn)
        {
            if (CombatAbilitySolver.IgnoresForestWhenDeterminingLineOfSight(observerDefinition, observerPawn))
            {
                return false;
            }

            CombatLineOfSight.GetPlanarBaseEdgePoints(observer, target, out var originEdge, out var targetEdge);
            var delta = targetEdge - originEdge;
            delta.y = 0f;
            var planarDistanceWorld = delta.magnitude;
            if (planarDistanceWorld <= 0.001f)
            {
                return false;
            }

            var planarDirection = delta / planarDistanceWorld;
            CombatForestFogClipper.EnsureCache();

            var depthLimitInches = GetStrictestLimitedDepthInches(targetModelSize);
            if (depthLimitInches <= 0f)
            {
                return false;
            }

            var forestDepthWorld = CombatForestFogClipper.GetCumulativeForestDepthWorld(
                originEdge,
                planarDirection,
                planarDistanceWorld);

            return CombatScale.WorldUnitsToInches(forestDepthWorld) > depthLimitInches + 0.001f;
        }

        /// <summary>
        /// World-space distance along a horizontal fog ray where limited-depth terrain (e.g. forest) stops map reveal.
        /// Forest depth is cumulative thickness along the ray only; open ground beyond a forest edge is not blocked.
        /// </summary>
        public static float GetLimitedDepthFogClipDistanceWorld(
            Vector3 origin,
            Vector3 direction,
            float maxDistanceWorld,
            float originRadius,
            UnitTypeDefinition observerDefinition,
            GameObject observerPawn)
        {
            if (maxDistanceWorld <= 0.001f)
            {
                return maxDistanceWorld;
            }

            if (CombatAbilitySolver.IgnoresForestWhenDeterminingLineOfSight(observerDefinition, observerPawn))
            {
                return maxDistanceWorld;
            }

            var planarDirection = new Vector3(direction.x, 0f, direction.z);
            if (planarDirection.sqrMagnitude <= 1e-6f)
            {
                return maxDistanceWorld;
            }

            planarDirection.Normalize();
            CombatForestFogClipper.EnsureCache();
            var forestClip = CombatForestFogClipper.GetClipDistanceWorld(origin, planarDirection, maxDistanceWorld, originRadius);
            var blockingClip = CombatBlockingTerrainClipper.GetFogClipDistanceWorld(
                origin,
                planarDirection,
                maxDistanceWorld,
                originRadius);
            return Mathf.Min(forestClip, blockingClip);
        }

        private static float GetStrictestLimitedDepthInches(ModelSize targetModelSize)
        {
            var depthLimitInches = float.MaxValue;
            var activeZones = CombatZone.ActiveZones;
            for (var i = 0; i < activeZones.Count; i++)
            {
                var feature = activeZones[i]?.TerrainFeature;
                if (feature == null || feature.LineOfSightMode != CombatTerrainLineOfSightMode.LimitedDepth)
                {
                    continue;
                }

                if (targetModelSize.IsHugeBased() && feature.DoesNotLimitLineOfSightToHugeBasedTargets)
                {
                    continue;
                }

                if (feature.LineOfSightPassThroughDepthInches < depthLimitInches)
                {
                    depthLimitInches = feature.LineOfSightPassThroughDepthInches;
                }
            }

            return depthLimitInches == float.MaxValue ? 0f : depthLimitInches;
        }
    }

    /// <summary>
    /// Fog reveal for blocking terrain (clouds) using the same first-contact depth model as forests.
    /// Unit targeting still uses <see cref="IsObstructingLineOfSight"/> for full cloud blocking.
    /// </summary>
    public static class CombatBlockingTerrainClipper
    {
        private const float ClipVerificationMarginInches = 0.05f;
        private const float DefaultFogRevealDepthInches = 3f;
        private const int AngularSmoothingSamples = 16;
        private const float BoundaryClearanceInches = 0.25f;

        private static readonly List<CombatZone> CachedZones = new();
        private static int LastCacheFrame = -1;

        public static bool HasActiveZones => CachedZones.Count > 0;

        public static bool AnyCachedZoneWithinReach(Vector3 worldPoint, float reachWorld)
        {
            EnsureCache();
            if (CachedZones.Count == 0 || reachWorld <= 0.001f)
            {
                return false;
            }

            for (var i = 0; i < CachedZones.Count; i++)
            {
                if (CachedZones[i].IntersectsDisc(worldPoint, reachWorld))
                {
                    return true;
                }
            }

            return false;
        }

        public static void InvalidateCache()
        {
            LastCacheFrame = -1;
            CachedZones.Clear();
        }

        public static void EnsureCache()
        {
            var frame = Time.frameCount;
            if (frame == LastCacheFrame)
            {
                return;
            }

            LastCacheFrame = frame;
            CachedZones.Clear();

            var activeZones = CombatZone.ActiveZones;
            for (var i = 0; i < activeZones.Count; i++)
            {
                var zone = activeZones[i];
                var feature = zone?.TerrainFeature;
                if (zone != null
                    && feature != null
                    && feature.LineOfSightMode == CombatTerrainLineOfSightMode.BlocksCompletely)
                {
                    CachedZones.Add(zone);
                }
            }
        }

        public static bool IsInsideBlockingTerrain(Vector3 worldPoint, float radius = 0f)
        {
            EnsureCache();
            for (var i = 0; i < CachedZones.Count; i++)
            {
                var zone = CachedZones[i];
                if (radius > 0.001f)
                {
                    if (zone.IntersectsDisc(worldPoint, radius))
                    {
                        return true;
                    }
                }
                else if (zone.ContainsPoint(worldPoint))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsObstructingLineOfSight(CombatLineOfSightVolume observer, CombatLineOfSightVolume target)
        {
            EnsureCache();
            if (CachedZones.Count == 0)
            {
                return false;
            }

            CombatLineOfSight.GetPlanarBaseEdgePoints(observer, target, out var originEdge, out var targetEdge);
            for (var i = 0; i < CachedZones.Count; i++)
            {
                var zone = CachedZones[i];
                if (!zone.IntersectsPlanarSegment(originEdge, targetEdge))
                {
                    continue;
                }

                if (zone.ContainsUnitCompletely(observer.Position, observer.Radius)
                    && zone.ContainsUnitCompletely(target.Position, target.Radius))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Fog rays use the same first-contact depth rules as forests: up to 3" into cloud from
        /// each contact while based inside, with thin cloud slices passed through to open ground.
        /// </summary>
        public static float GetFogClipDistanceWorld(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld,
            float originRadius = 0f,
            bool? rayStartedInsideOverride = null)
        {
            return GetFogClipDistanceWorldSmoothed(
                origin,
                planarDirection,
                maxDistanceWorld,
                originRadius,
                rayStartedInsideOverride);
        }

        public static float GetFogClipDistanceWorldSmoothed(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld,
            float originRadius = 0f,
            bool? rayStartedInsideOverride = null,
            float neighborHalfAngleRadians = -1f)
        {
            var baseClip = GetFogClipDistanceWorldUnsmoothed(
                origin,
                planarDirection,
                maxDistanceWorld,
                originRadius,
                rayStartedInsideOverride);
            if (maxDistanceWorld <= 0.001f)
            {
                return baseClip;
            }

            var dir = new Vector2(planarDirection.x, planarDirection.z);
            if (dir.sqrMagnitude <= 1e-8f)
            {
                return baseClip;
            }

            dir.Normalize();
            var angle = Mathf.Atan2(dir.y, dir.x);
            var halfStep = neighborHalfAngleRadians > 1e-6f
                ? neighborHalfAngleRadians
                : (Mathf.PI * 2f) / AngularSmoothingSamples * 0.5f;
            var leftDir = new Vector3(Mathf.Cos(angle - halfStep), 0f, Mathf.Sin(angle - halfStep));
            var rightDir = new Vector3(Mathf.Cos(angle + halfStep), 0f, Mathf.Sin(angle + halfStep));

            var leftClip = GetFogClipDistanceWorldUnsmoothed(
                origin,
                leftDir,
                maxDistanceWorld,
                originRadius,
                rayStartedInsideOverride);
            var rightClip = GetFogClipDistanceWorldUnsmoothed(
                origin,
                rightDir,
                maxDistanceWorld,
                originRadius,
                rayStartedInsideOverride);

            return Median3(baseClip, leftClip, rightClip);
        }

        private static float GetFogClipDistanceWorldUnsmoothed(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld,
            float originRadius,
            bool? rayStartedInsideOverride)
        {
            EnsureCache();
            if (maxDistanceWorld <= 0.001f || CachedZones.Count == 0)
            {
                return maxDistanceWorld;
            }

            planarDirection.y = 0f;
            if (planarDirection.sqrMagnitude <= 1e-6f)
            {
                return maxDistanceWorld;
            }

            planarDirection.Normalize();
            origin.y = 0f;

            var depthLimitWorld = GetStrictestFogRevealDepthWorld();
            if (depthLimitWorld <= 0.001f)
            {
                depthLimitWorld = CombatScale.InchesToWorldUnits(DefaultFogRevealDepthInches);
            }

            var rayStartedInside = rayStartedInsideOverride
                ?? RayStartsInsideBlockingTerrain(origin, planarDirection, originRadius);
            return ComputeFirstContactDepthClipCandidate(
                origin,
                planarDirection,
                maxDistanceWorld,
                depthLimitWorld,
                rayStartedInside,
                originRadius);
        }

        private static float Median3(float a, float b, float c)
        {
            if (a > b)
            {
                (a, b) = (b, a);
            }

            if (b > c)
            {
                (b, c) = (c, b);
            }

            if (a > b)
            {
                (a, b) = (b, a);
            }

            return b;
        }

        private static float GetStrictestFogRevealDepthWorld()
        {
            var depthLimitWorld = float.MaxValue;
            for (var i = 0; i < CachedZones.Count; i++)
            {
                var feature = CachedZones[i]?.TerrainFeature;
                if (feature == null)
                {
                    continue;
                }

                var authoredDepth = feature.LineOfSightPassThroughDepthInches;
                var depthWorld = authoredDepth > 0.001f
                    ? CombatScale.InchesToWorldUnits(authoredDepth)
                    : CombatScale.InchesToWorldUnits(DefaultFogRevealDepthInches);
                if (depthWorld < depthLimitWorld)
                {
                    depthLimitWorld = depthWorld;
                }
            }

            return depthLimitWorld == float.MaxValue
                ? CombatScale.InchesToWorldUnits(DefaultFogRevealDepthInches)
                : depthLimitWorld;
        }

        private static float ComputeFirstContactDepthClipCandidate(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld,
            float depthLimitWorld,
            bool rayStartedInside,
            float originRadius = 0f)
        {
            origin.y = 0f;
            originRadius = Mathf.Max(0f, originRadius);
            var cursor = 0f;
            const float advanceEpsilon = 0.001f;
            var thinZoneEpsilon = CombatScale.InchesToWorldUnits(0.05f);
            var boundaryClearanceWorld = CombatScale.InchesToWorldUnits(BoundaryClearanceInches);

            if (rayStartedInside)
            {
                var exitFromUnit = FindBlockingTerrainExitAlongRay(
                    origin,
                    originRadius,
                    planarDirection,
                    maxDistanceWorld);

                if (exitFromUnit < 0f)
                {
                    return TryFinalizeClipDistance(
                        origin,
                        planarDirection,
                        depthLimitWorld,
                        maxDistanceWorld,
                        exitFromContact: -1f);
                }

                if (exitFromUnit > depthLimitWorld + thinZoneEpsilon)
                {
                    return TryFinalizeClipDistance(
                        origin,
                        planarDirection,
                        depthLimitWorld,
                        maxDistanceWorld,
                        exitFromUnit);
                }

                cursor = exitFromUnit + advanceEpsilon;
                rayStartedInside = false;
            }

            while (cursor < maxDistanceWorld - advanceEpsilon)
            {
                var entryDistance = FindNextBlockingTerrainEntryDistance(
                    origin,
                    planarDirection,
                    cursor,
                    maxDistanceWorld,
                    originRadius);
                if (entryDistance < 0f)
                {
                    return maxDistanceWorld;
                }

                var remainingFromEntry = maxDistanceWorld - entryDistance;
                var startInsideEpsilon = CombatScale.InchesToWorldUnits(0.02f);
                var probeStart = origin + planarDirection * Mathf.Min(
                    maxDistanceWorld,
                    entryDistance + startInsideEpsilon);
                if (!IsInsideBlockingTerrain(probeStart))
                {
                    probeStart = origin + planarDirection * entryDistance;
                }

                probeStart.y = 0f;
                var exitFromEntry = FindFirstOutsideDistanceFromInside(
                    probeStart,
                    planarDirection,
                    remainingFromEntry);

                if (exitFromEntry < 0f)
                {
                    var surroundedClip = Mathf.Min(maxDistanceWorld, entryDistance + depthLimitWorld);
                    return TryFinalizeClipDistance(
                        origin,
                        planarDirection,
                        surroundedClip,
                        maxDistanceWorld,
                        exitFromContact: -1f);
                }

                var absoluteExit = entryDistance + exitFromEntry;

                // Boundary fuzz after leaving a cloud: consume the shell without treating it
                // as a fresh outside approach (prevents angular banding wedges).
                if (!rayStartedInside
                    && entryDistance > 0.001f
                    && IsInsideBlockingTerrain(origin + planarDirection * entryDistance))
                {
                    cursor = absoluteExit + boundaryClearanceWorld;
                    continue;
                }

                if (!rayStartedInside)
                {
                    var outsideEntryClip = Mathf.Min(maxDistanceWorld, entryDistance + depthLimitWorld);
                    outsideEntryClip = Mathf.Min(outsideEntryClip, absoluteExit);
                    return TryFinalizeClipDistance(
                        origin,
                        planarDirection,
                        outsideEntryClip,
                        maxDistanceWorld,
                        exitFromEntry);
                }

                if (exitFromEntry <= depthLimitWorld + thinZoneEpsilon)
                {
                    cursor = absoluteExit + boundaryClearanceWorld;
                    rayStartedInside = false;
                    continue;
                }

                var clipDistance = Mathf.Min(maxDistanceWorld, entryDistance + depthLimitWorld);
                clipDistance = Mathf.Min(clipDistance, absoluteExit);
                return TryFinalizeClipDistance(
                    origin,
                    planarDirection,
                    clipDistance,
                    maxDistanceWorld,
                    exitFromEntry);
            }

            return maxDistanceWorld;
        }

        private static bool RayStartsInsideBlockingTerrain(
            Vector3 origin,
            Vector3 planarDirection,
            float originRadius = 0f)
        {
            _ = originRadius;
            origin.y = 0f;
            planarDirection.y = 0f;
            if (planarDirection.sqrMagnitude > 1e-6f)
            {
                planarDirection.Normalize();
            }

            if (IsInsideBlockingTerrain(origin))
            {
                return true;
            }

            var step = CombatScale.InchesToWorldUnits(0.05f);
            return IsInsideBlockingTerrain(origin + planarDirection * step);
        }

        private static float FindNextBlockingTerrainEntryDistance(
            Vector3 origin,
            Vector3 planarDirection,
            float searchStart,
            float maxDistanceWorld,
            float originRadius = 0f)
        {
            originRadius = Mathf.Max(0f, originRadius);
            if (searchStart <= 0.001f
                && RayStartsInsideBlockingTerrain(origin, planarDirection, originRadius))
            {
                return 0f;
            }

            var sampleAtStart = origin + planarDirection * searchStart;
            if (IsInsideBlockingTerrain(sampleAtStart))
            {
                return searchStart;
            }

            var coarseStep = Mathf.Max(CombatScale.InchesToWorldUnits(0.25f), 0.05f);
            var distance = Mathf.Max(0f, searchStart);
            var previousDistance = distance;
            while (distance < maxDistanceWorld - 0.001f)
            {
                var nextDistance = Mathf.Min(maxDistanceWorld, distance + coarseStep);
                var midpoint = distance + (nextDistance - distance) * 0.5f;
                var samplePoint = origin + planarDirection * midpoint;
                if (IsInsideBlockingTerrain(samplePoint))
                {
                    return RefineFirstContactDistance(origin, planarDirection, previousDistance, nextDistance);
                }

                previousDistance = distance;
                distance = nextDistance;
            }

            return -1f;
        }

        private static float FindBlockingTerrainExitAlongRay(
            Vector3 origin,
            float originRadius,
            Vector3 planarDirection,
            float maxDistanceWorld)
        {
            origin.y = 0f;
            if (!RayStartsInsideBlockingTerrain(origin, planarDirection, originRadius))
            {
                return -1f;
            }

            return FindFirstOutsideDistanceFromInside(origin, planarDirection, maxDistanceWorld);
        }

        private static float FindFirstOutsideDistanceFromInside(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld)
        {
            var coarseStep = Mathf.Max(CombatScale.InchesToWorldUnits(0.25f), 0.05f);
            var insideDistance = 0f;
            var distance = 0f;
            while (distance < maxDistanceWorld - 0.001f)
            {
                var nextDistance = Mathf.Min(maxDistanceWorld, distance + coarseStep);
                var midpoint = distance + (nextDistance - distance) * 0.5f;
                var samplePoint = origin + planarDirection * midpoint;
                if (!IsInsideBlockingTerrain(samplePoint))
                {
                    return RefineBoundaryDistance(
                        origin,
                        planarDirection,
                        insideDistance,
                        nextDistance,
                        findInsideToOutside: true);
                }

                insideDistance = nextDistance;
                distance = nextDistance;
            }

            return -1f;
        }

        private static float RefineFirstContactDistance(
            Vector3 origin,
            Vector3 planarDirection,
            float lowDistance,
            float highDistance)
        {
            return RefineBoundaryDistance(origin, planarDirection, lowDistance, highDistance, findInsideToOutside: false);
        }

        private static float RefineBoundaryDistance(
            Vector3 origin,
            Vector3 planarDirection,
            float lowDistance,
            float highDistance,
            bool findInsideToOutside)
        {
            var low = Mathf.Max(0f, lowDistance);
            var high = Mathf.Max(low, highDistance);
            for (var i = 0; i < 5; i++)
            {
                var mid = (low + high) * 0.5f;
                var inside = IsInsideBlockingTerrain(origin + planarDirection * mid);
                if (findInsideToOutside)
                {
                    if (inside)
                    {
                        low = mid;
                    }
                    else
                    {
                        high = mid;
                    }
                }
                else if (inside)
                {
                    high = mid;
                }
                else
                {
                    low = mid;
                }
            }

            return findInsideToOutside ? high : low;
        }

        private static float TryFinalizeClipDistance(
            Vector3 origin,
            Vector3 planarDirection,
            float clipDistance,
            float maxDistanceWorld,
            float exitFromContact)
        {
            if (clipDistance >= maxDistanceWorld - 0.001f)
            {
                return maxDistanceWorld;
            }

            var clipPoint = origin + planarDirection * clipDistance;
            var candidateInside = IsInsideBlockingTerrain(clipPoint);
            if (!candidateInside && exitFromContact < 0f)
            {
                return maxDistanceWorld;
            }

            if (clipDistance < maxDistanceWorld - 0.001f)
            {
                var verificationMargin = CombatScale.InchesToWorldUnits(ClipVerificationMarginInches);
                var verifyDistance = Mathf.Min(maxDistanceWorld, clipDistance + verificationMargin);
                var verifyPoint = origin + planarDirection * verifyDistance;
                if (!IsInsideBlockingTerrain(verifyPoint) && exitFromContact < 0f)
                {
                    return maxDistanceWorld;
                }
            }

            return clipDistance;
        }
    }
}
