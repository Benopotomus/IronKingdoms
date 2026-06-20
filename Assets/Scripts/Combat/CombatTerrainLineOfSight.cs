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
            return CombatForestFogClipper.GetClipDistanceWorld(origin, planarDirection, maxDistanceWorld, originRadius);
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
        private static readonly List<CombatZone> CachedZones = new();
        private static int LastCacheFrame = -1;

        public static bool HasActiveZones => CachedZones.Count > 0;

        /// <summary>
        /// Appends footprint corners for blocking terrain zones (clouds, etc.).
        /// </summary>
        public static void CollectBlockingZoneCornersWorld(List<Vector3> corners)
        {
            EnsureCache();
            for (var i = 0; i < CachedZones.Count; i++)
            {
                CachedZones[i].CollectFootprintCorners(corners);
            }
        }

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
        /// Fog reveal delegates to <see cref="CombatForestFogClipper"/> so clouds and forests share
        /// identical pass-through depth math. Unit targeting still uses <see cref="IsObstructingLineOfSight"/>.
        /// </summary>
        public static float GetFogClipDistanceWorld(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld,
            float originRadius = 0f,
            bool? rayStartedInsideOverride = null)
        {
            _ = rayStartedInsideOverride;
            CombatForestFogClipper.EnsureCache();
            var depthWorld = CombatForestFogDepth.ResolveDepthWorld();
            CombatForestFogClipper.SetClipPassFilters(applyForestClip: false, applyBlockingClip: true);
            try
            {
                return CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                    origin,
                    planarDirection,
                    maxDistanceWorld,
                    depthWorld,
                    originRadius);
            }
            finally
            {
                CombatForestFogClipper.ResetClipPassFilters();
            }
        }

        public static float GetFogClipDistanceWorldSmoothed(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld,
            float originRadius = 0f,
            bool? rayStartedInsideOverride = null,
            float neighborHalfAngleRadians = -1f)
        {
            _ = rayStartedInsideOverride;
            CombatForestFogClipper.EnsureCache();
            var depthWorld = CombatForestFogDepth.ResolveDepthWorld();
            CombatForestFogClipper.SetClipPassFilters(applyForestClip: false, applyBlockingClip: true);
            try
            {
                return CombatForestFogClipper.GetFirstContactDepthClipDistanceWorldSmoothed(
                    origin,
                    planarDirection,
                    maxDistanceWorld,
                    depthWorld,
                    originRadius,
                    neighborHalfAngleRadians);
            }
            finally
            {
                CombatForestFogClipper.ResetClipPassFilters();
            }
        }
    }
}
