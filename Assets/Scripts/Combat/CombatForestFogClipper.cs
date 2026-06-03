using System.Collections.Generic;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Fast XZ analytic forest depth along sight lines (Mk4: up to 3" of forest may be seen through).
    /// Only forest thickness along the ray counts — open ground after a short forest exit does not consume depth.
    /// </summary>
    internal static class CombatForestFogClipper
    {
        private struct ClipInterval
        {
            public float Enter;
            public float Exit;
            public float DepthLimitWorld;
        }

        private struct ClipZone
        {
            public float MinX;
            public float MaxX;
            public float MinZ;
            public float MaxZ;
            public float DepthLimitWorld;
        }

        private static readonly List<ClipZone> CachedZones = new();
        private static readonly List<ClipInterval> IntervalScratch = new();
        private static int LastCacheFrame = -1;

        public static bool HasActiveZones => CachedZones.Count > 0;

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
                if (zone == null || feature == null || feature.LineOfSightMode != CombatTerrainLineOfSightMode.LimitedDepth)
                {
                    continue;
                }

                var collider = zone.GetComponent<Collider>();
                if (collider == null || !collider.enabled)
                {
                    continue;
                }

                var bounds = collider.bounds;
                CachedZones.Add(new ClipZone
                {
                    MinX = bounds.min.x,
                    MaxX = bounds.max.x,
                    MinZ = bounds.min.z,
                    MaxZ = bounds.max.z,
                    DepthLimitWorld = CombatScale.InchesToWorldUnits(feature.LineOfSightPassThroughDepthInches)
                });
            }
        }

        /// <summary>
        /// Total forest thickness in world units along a horizontal ray, up to <paramref name="maxDistanceWorld"/>.
        /// </summary>
        public static float GetCumulativeForestDepthWorld(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld,
            float originRadius = 0f)
        {
            originRadius = Mathf.Max(0f, originRadius);
            if (maxDistanceWorld <= 0.001f || CachedZones.Count == 0 || originRadius >= maxDistanceWorld - 0.001f)
            {
                return 0f;
            }

            var edgeOrigin = origin + planarDirection * originRadius;
            return GetCumulativeForestDepthFromPointWorld(edgeOrigin, planarDirection, maxDistanceWorld - originRadius);
        }

        public static float GetClipDistanceWorld(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld,
            float originRadius = 0f)
        {
            originRadius = Mathf.Max(0f, originRadius);
            if (maxDistanceWorld <= 0.001f || CachedZones.Count == 0)
            {
                return maxDistanceWorld;
            }

            if (originRadius >= maxDistanceWorld - 0.001f)
            {
                return maxDistanceWorld;
            }

            var edgeOrigin = origin + planarDirection * originRadius;
            var edgeClip = GetClipDistanceFromPointWorld(edgeOrigin, planarDirection, maxDistanceWorld - originRadius);
            return originRadius + edgeClip;
        }

        private static float GetCumulativeForestDepthFromPointWorld(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld)
        {
            if (maxDistanceWorld <= 0.001f || CachedZones.Count == 0)
            {
                return 0f;
            }

            CollectIntervals(origin, planarDirection, maxDistanceWorld);
            if (IntervalScratch.Count == 0)
            {
                return 0f;
            }

            IntervalScratch.Sort(static (a, b) => a.Enter.CompareTo(b.Enter));

            var totalDepth = 0f;
            var mergedEnter = IntervalScratch[0].Enter;
            var mergedExit = IntervalScratch[0].Exit;

            for (var i = 1; i < IntervalScratch.Count; i++)
            {
                var interval = IntervalScratch[i];
                if (interval.Enter > mergedExit)
                {
                    totalDepth += mergedExit - mergedEnter;
                    mergedEnter = interval.Enter;
                    mergedExit = interval.Exit;
                    continue;
                }

                if (interval.Exit > mergedExit)
                {
                    mergedExit = interval.Exit;
                }
            }

            totalDepth += mergedExit - mergedEnter;
            return totalDepth;
        }

        private static float GetClipDistanceFromPointWorld(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld)
        {
            if (maxDistanceWorld <= 0.001f || CachedZones.Count == 0)
            {
                return maxDistanceWorld;
            }

            CollectIntervals(origin, planarDirection, maxDistanceWorld);

            if (IntervalScratch.Count == 0)
            {
                return maxDistanceWorld;
            }

            IntervalScratch.Sort(static (a, b) => a.Enter.CompareTo(b.Enter));

            var accumulatedDepth = 0f;
            var mergedEnter = IntervalScratch[0].Enter;
            var mergedExit = IntervalScratch[0].Exit;
            var mergedDepthLimit = IntervalScratch[0].DepthLimitWorld;

            for (var i = 1; i < IntervalScratch.Count; i++)
            {
                var interval = IntervalScratch[i];
                if (interval.Enter > mergedExit)
                {
                    if (TryGetClipInSpan(mergedEnter, mergedExit, accumulatedDepth, mergedDepthLimit, out var clip))
                    {
                        return clip;
                    }

                    accumulatedDepth += mergedExit - mergedEnter;
                    mergedEnter = interval.Enter;
                    mergedExit = interval.Exit;
                    mergedDepthLimit = interval.DepthLimitWorld;
                    continue;
                }

                if (interval.Exit > mergedExit)
                {
                    mergedExit = interval.Exit;
                }

                if (interval.DepthLimitWorld < mergedDepthLimit)
                {
                    mergedDepthLimit = interval.DepthLimitWorld;
                }
            }

            if (TryGetClipInSpan(mergedEnter, mergedExit, accumulatedDepth, mergedDepthLimit, out var finalClip))
            {
                return finalClip;
            }

            return maxDistanceWorld;
        }

        private static void CollectIntervals(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld)
        {
            IntervalScratch.Clear();
            var originX = origin.x;
            var originZ = origin.z;
            var directionX = planarDirection.x;
            var directionZ = planarDirection.z;

            for (var i = 0; i < CachedZones.Count; i++)
            {
                var zone = CachedZones[i];
                if (TryGetRayAabbInterval(
                        originX,
                        originZ,
                        directionX,
                        directionZ,
                        maxDistanceWorld,
                        zone,
                        out var enter,
                        out var exit))
                {
                    IntervalScratch.Add(new ClipInterval
                    {
                        Enter = enter,
                        Exit = exit,
                        DepthLimitWorld = zone.DepthLimitWorld
                    });
                }
            }
        }

        private static bool TryGetClipInSpan(
            float enter,
            float exit,
            float accumulatedDepth,
            float depthLimitWorld,
            out float clipDistance)
        {
            clipDistance = 0f;
            var spanLength = exit - enter;
            if (spanLength <= 0f)
            {
                return false;
            }

            if (accumulatedDepth + spanLength <= depthLimitWorld + 0.001f)
            {
                return false;
            }

            clipDistance = enter + (depthLimitWorld - accumulatedDepth);
            return true;
        }

        private static bool TryGetRayAabbInterval(
            float originX,
            float originZ,
            float directionX,
            float directionZ,
            float maxDistance,
            ClipZone zone,
            out float enter,
            out float exit)
        {
            enter = 0f;
            exit = maxDistance;

            if (!SlabIntersect(originX, directionX, zone.MinX, zone.MaxX, ref enter, ref exit)
                || !SlabIntersect(originZ, directionZ, zone.MinZ, zone.MaxZ, ref enter, ref exit))
            {
                return false;
            }

            if (enter > exit || exit <= 0f || enter >= maxDistance)
            {
                return false;
            }

            enter = Mathf.Max(0f, enter);
            exit = Mathf.Min(maxDistance, exit);
            return exit > enter + 0.0001f;
        }

        private static bool SlabIntersect(
            float origin,
            float direction,
            float min,
            float max,
            ref float enter,
            ref float exit)
        {
            if (Mathf.Abs(direction) <= 1e-6f)
            {
                return origin >= min && origin <= max;
            }

            var inverseDirection = 1f / direction;
            var t0 = (min - origin) * inverseDirection;
            var t1 = (max - origin) * inverseDirection;
            if (t0 > t1)
            {
                (t0, t1) = (t1, t0);
            }

            if (t0 > enter)
            {
                enter = t0;
            }

            if (t1 < exit)
            {
                exit = t1;
            }

            return enter <= exit;
        }
    }
}
