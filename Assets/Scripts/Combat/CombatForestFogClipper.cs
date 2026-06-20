using System;
using System.Collections.Generic;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Fast XZ analytic forest depth along sight lines (Mk4: up to 3" of forest may be seen through).
    /// Only forest thickness along the ray counts — open ground after a short forest exit does not consume depth.
    /// </summary>
    public static class CombatForestFogClipper
    {
        private const float ClipVerificationMarginInches = 0.05f;
        private const float CandidateSmoothingRadiusInches = 0.2f;
        private const int AngularSmoothingSamples = 16;

        private struct ClipInterval
        {
            public float Enter;
            public float Exit;
            public float DepthLimitWorld;
        }

        private struct ClipZone
        {
            public CombatZone Zone;
            public CombatTerrainLineOfSightMode LineOfSightMode;
            public float MinX;
            public float MaxX;
            public float MinZ;
            public float MaxZ;
            public float DepthLimitWorld;
        }

        private static readonly List<ClipZone> CachedZones = new();
        [ThreadStatic] private static List<ClipInterval> _intervalScratch;
        [ThreadStatic] private static List<Vector3> _footprintCornerScratch;
        private static int LastCacheFrame = -1;
        private static bool applyForestClipPass = true;
        private static bool applyBlockingClipPass = true;

        private static List<ClipInterval> IntervalScratch
        {
            get
            {
                if (_intervalScratch == null)
                {
                    _intervalScratch = new List<ClipInterval>(16);
                }

                return _intervalScratch;
            }
        }

        private static List<Vector3> FootprintCornerScratch
        {
            get
            {
                if (_footprintCornerScratch == null)
                {
                    _footprintCornerScratch = new List<Vector3>(16);
                }

                return _footprintCornerScratch;
            }
        }

        public static bool HasActiveZones => CachedZones.Count > 0;

        public static bool HasActiveForestFogZones
        {
            get
            {
                EnsureCache();
                for (var i = 0; i < CachedZones.Count; i++)
                {
                    if (CachedZones[i].LineOfSightMode == CombatTerrainLineOfSightMode.LimitedDepth)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public static bool HasActiveCloudFogZones
        {
            get
            {
                EnsureCache();
                for (var i = 0; i < CachedZones.Count; i++)
                {
                    if (CachedZones[i].LineOfSightMode == CombatTerrainLineOfSightMode.BlocksCompletely)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public static bool HasActiveZonesForClipPass(bool applyForestClip, bool applyBlockingClip)
        {
            EnsureCache();
            for (var i = 0; i < CachedZones.Count; i++)
            {
                if (IsZoneActiveForClipPass(CachedZones[i], applyForestClip, applyBlockingClip))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Number of terrain zones in the current clip pass (forest and/or cloud).
        /// </summary>
        public static int GetActiveClipZoneCount(bool applyForestClip, bool applyBlockingClip)
        {
            EnsureCache();
            var count = 0;
            for (var i = 0; i < CachedZones.Count; i++)
            {
                if (IsZoneActiveForClipPass(CachedZones[i], applyForestClip, applyBlockingClip))
                {
                    count++;
                }
            }

            return count;
        }

        internal static void SetClipPassFilters(bool applyForestClip, bool applyBlockingClip)
        {
            applyForestClipPass = applyForestClip;
            applyBlockingClipPass = applyBlockingClip;
        }

        internal static void ResetClipPassFilters()
        {
            applyForestClipPass = true;
            applyBlockingClipPass = true;
        }

        private static bool IsZoneActiveForClipPass(in ClipZone zone, bool applyForestClip, bool applyBlockingClip)
        {
            return zone.LineOfSightMode switch
            {
                CombatTerrainLineOfSightMode.LimitedDepth => applyForestClip,
                CombatTerrainLineOfSightMode.BlocksCompletely => applyBlockingClip,
                _ => false,
            };
        }

        private static bool IsZoneActiveForClipPass(in ClipZone zone)
        {
            return IsZoneActiveForClipPass(zone, applyForestClipPass, applyBlockingClipPass);
        }

        /// <summary>
        /// Planar corners of every active limited-depth zone footprint (XZ at collider center Y).
        /// </summary>
        public static void CollectLimitedDepthZoneCornersWorld(List<Vector3> corners)
        {
            corners.Clear();
            EnsureCache();

            var activeZones = CombatZone.ActiveZones;
            for (var i = 0; i < activeZones.Count; i++)
            {
                var zone = activeZones[i];
                var feature = zone?.TerrainFeature;
                if (zone == null || feature == null || !feature.UsesPassThroughFogClip)
                {
                    continue;
                }

                if (!IsZoneActiveForClipPass(
                        new ClipZone { LineOfSightMode = feature.LineOfSightMode },
                        applyForestClipPass,
                        applyBlockingClipPass))
                {
                    continue;
                }

                zone.CollectFootprintCorners(corners);
            }
        }

        /// <summary>
        /// When <paramref name="origin"/> is inside forest, returns distance along the ray to the
        /// first point outside the forest footprint. Returns -1 when not inside or no exit in range.
        /// </summary>
        public static float GetForestExitDistanceFromInsideWorld(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld,
            float originRadius = 0f)
        {
            EnsureCache();
            if (maxDistanceWorld <= 0.001f)
            {
                return -1f;
            }

            if (!RayStartsInsideForest(origin, planarDirection, originRadius))
            {
                return -1f;
            }

            origin.y = 0f;
            return FindForestExitDistanceAlongRay(origin, originRadius, planarDirection, maxDistanceWorld);
        }

        /// <summary>
        /// Distance along the ray from the eye to the forest exit boundary (Mk4 3" gate).
        /// </summary>
        private static float FindForestExitDistanceAlongRay(
            Vector3 origin,
            float originRadius,
            Vector3 planarDirection,
            float maxDistanceWorld)
        {
            origin.y = 0f;
            if (!RayStartsInsideForest(origin, planarDirection, originRadius))
            {
                return -1f;
            }

            return FindFirstOutsideDistanceFromInside(origin, planarDirection, maxDistanceWorld);
        }

        /// <summary>
        /// True when <paramref name="worldPoint"/> (optionally expanded by <paramref name="radius"/> on XZ)
        /// lies inside a limited-depth terrain zone such as forest.
        /// </summary>
        public static bool IsInsideLimitedDepthForest(Vector3 worldPoint, float radius = 0f)
        {
            var activeZones = CombatZone.ActiveZones;
            for (var i = 0; i < activeZones.Count; i++)
            {
                var zone = activeZones[i];
                var feature = zone?.TerrainFeature;
                if (zone == null || feature == null || !feature.UsesPassThroughFogClip)
                {
                    continue;
                }

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

        public static float GetStrictestLimitedDepthWorld()
        {
            var depthLimitWorld = float.MaxValue;
            var activeZones = CombatZone.ActiveZones;
            for (var i = 0; i < activeZones.Count; i++)
            {
                var feature = activeZones[i]?.TerrainFeature;
                if (feature == null || !feature.UsesPassThroughFogClip)
                {
                    continue;
                }

                var limit = CombatScale.InchesToWorldUnits(feature.LineOfSightPassThroughDepthInches);
                if (limit < depthLimitWorld)
                {
                    depthLimitWorld = limit;
                }
            }

            if (depthLimitWorld == float.MaxValue)
            {
                EnsureCache();
                for (var i = 0; i < CachedZones.Count; i++)
                {
                    if (CachedZones[i].DepthLimitWorld < depthLimitWorld)
                    {
                        depthLimitWorld = CachedZones[i].DepthLimitWorld;
                    }
                }

            }

            return depthLimitWorld == float.MaxValue ? 0f : depthLimitWorld;
        }

        public static void InvalidateCache()
        {
            LastCacheFrame = -1;
            CachedZones.Clear();
        }

        /// <summary>
        /// Editor/batch fallback when zone lifecycle registration is unavailable.
        /// </summary>
        public static void SeedCachedZoneFromBounds(Bounds bounds, float depthLimitWorld)
        {
            InvalidateCache();
            CachedZones.Add(new ClipZone
            {
                MinX = bounds.min.x,
                MaxX = bounds.max.x,
                MinZ = bounds.min.z,
                MaxZ = bounds.max.z,
                DepthLimitWorld = depthLimitWorld
            });
            LastCacheFrame = Time.frameCount;
        }

        public static void EnsureCache()
        {
            var frame = Time.frameCount;
            if (frame == LastCacheFrame && CachedZones.Count > 0)
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
                if (zone == null || feature == null || !feature.UsesPassThroughFogClip)
                {
                    continue;
                }

                if (zone != null
                    && zone.TryGetPolygonFootprint(out var polygonFootprint)
                    && polygonFootprint.TryGetFootprintBounds(out var polygonBounds))
                {
                    CachedZones.Add(new ClipZone
                    {
                        Zone = zone,
                        LineOfSightMode = feature.LineOfSightMode,
                        MinX = polygonBounds.min.x,
                        MaxX = polygonBounds.max.x,
                        MinZ = polygonBounds.min.z,
                        MaxZ = polygonBounds.max.z,
                        DepthLimitWorld = CombatScale.InchesToWorldUnits(feature.LineOfSightPassThroughDepthInches)
                    });
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
                    Zone = zone,
                    LineOfSightMode = feature.LineOfSightMode,
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
            _ = originRadius;
            if (maxDistanceWorld <= 0.001f || CachedZones.Count == 0)
            {
                return maxDistanceWorld;
            }

            return GetClipDistanceFromPointWorld(origin, planarDirection, maxDistanceWorld);
        }

        /// <summary>
        /// Precise fog clip using actual zone footprint checks in XZ (not collider bounds AABB intervals).
        /// Clips at first forest contact plus strictest limited-depth distance.
        /// </summary>
        public static float GetClipDistanceWorldPrecise(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld)
        {
            if (maxDistanceWorld <= 0.001f)
            {
                return maxDistanceWorld;
            }

            EnsureCache();
            var depthLimitWorld = GetStrictestLimitedDepthWorld();
            if (depthLimitWorld <= 0.001f)
            {
                return maxDistanceWorld;
            }

            var sampleStep = Mathf.Max(CombatScale.InchesToWorldUnits(0.05f), 0.01f);
            var firstContactDistance = -1f;

            var distance = 0f;
            while (distance < maxDistanceWorld - 0.001f && firstContactDistance < 0f)
            {
                var nextDistance = Mathf.Min(maxDistanceWorld, distance + sampleStep);
                var midpoint = distance + (nextDistance - distance) * 0.5f;
                var samplePoint = origin + planarDirection * midpoint;
                if (IsInsideLimitedDepthZoneForClip(samplePoint))
                {
                    firstContactDistance = midpoint;
                    break;
                }

                distance = nextDistance;
            }

            if (firstContactDistance < 0f)
            {
                return maxDistanceWorld;
            }

            // Keep fog reveal depth into forest invariant with observer distance:
            // always allow exactly depthLimit from first forest contact.
            return Mathf.Min(maxDistanceWorld, firstContactDistance + depthLimitWorld);
        }

        /// <summary>
        /// For fog rays: from the first forest contact on this ray, reveal up to
        /// <paramref name="depthLimitWorld"/> deeper. Rays that begin outside forest clip before
        /// they leave that forest span, so thin forests do not reveal open ground behind them.
        /// </summary>
        public static float GetFirstContactDepthClipDistanceWorld(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld,
            float depthLimitWorld,
            float originRadius = 0f)
        {
            if (maxDistanceWorld <= 0.001f || depthLimitWorld <= 0.001f)
            {
                return maxDistanceWorld;
            }

            EnsureCache();
            if (CachedZones.Count == 0)
            {
                return maxDistanceWorld;
            }

            if (!IsEyeInsideLimitedDepthForestForClip(origin, originRadius)
                && !RayMayHitAnyCachedZoneAabb(origin, planarDirection, maxDistanceWorld))
            {
                return maxDistanceWorld;
            }

            // Walk forest segments along the ray so separate zones (e.g. square vs
            // circular) each contribute clips even when the eye is inside another forest.
            return ComputeFirstContactDepthClipCandidate(
                origin,
                planarDirection,
                maxDistanceWorld,
                depthLimitWorld,
                originRadius);
        }

        /// <summary>
        /// LUT hot path: skips redundant cache/inside checks when the build context is shared per eye.
        /// </summary>
        internal static float GetFirstContactDepthClipDistanceWorld(
            in CombatForestFogLutBuildContext ctx,
            Vector3 planarDirection)
        {
            if (ctx.MaxSearchRadius <= 0.001f || ctx.DepthWorld <= 0.001f || !ctx.HasForest)
            {
                return ctx.MaxSearchRadius;
            }

            if (!ctx.RayStartedInsideForest
                && !RayMayHitAnyCachedZoneAabb(ctx.FlatEye, planarDirection, ctx.MaxSearchRadius))
            {
                return ctx.MaxSearchRadius;
            }

            return ComputeFirstContactDepthClipCandidate(
                ctx.FlatEye,
                planarDirection,
                ctx.MaxSearchRadius,
                ctx.DepthWorld,
                ctx.RayStartedInsideForest,
                ctx.OriginRadiusWorld);
        }

        public static float GetFirstContactDepthClipDistanceWorld(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld,
            float depthLimitWorld,
            bool rayStartedInsideForest)
        {
            if (maxDistanceWorld <= 0.001f || depthLimitWorld <= 0.001f)
            {
                return maxDistanceWorld;
            }

            EnsureCache();
            if (CachedZones.Count == 0)
            {
                return maxDistanceWorld;
            }

            return ComputeFirstContactDepthClipCandidate(
                origin,
                planarDirection,
                maxDistanceWorld,
                depthLimitWorld,
                rayStartedInsideForest,
                originRadius: 0f);
        }

        /// <summary>
        /// True when the eye position lies inside a limited-depth forest zone (optionally expanded by base radius).
        /// Does not use per-ray grazing contact — that path belongs to the outside first-contact clipper.
        /// </summary>
        public static bool IsEyeInsideLimitedDepthForestForClip(Vector3 origin, float originRadius = 0f)
        {
            return IsInsideLimitedDepthForest(origin, originRadius);
        }

        /// <summary>
        /// Shared per-eye inside-forest flag for LUT builds and upload (eye containment only).
        /// </summary>
        public static bool ComputeRayStartedInsideForest(
            Vector3 origin,
            Vector3 sampleDirection,
            float originRadius = 0f)
        {
            _ = sampleDirection;
            return IsEyeInsideLimitedDepthForestForClip(origin, originRadius);
        }

        private static bool RayStartsInsideForest(
            Vector3 origin,
            Vector3 planarDirection,
            float originRadius = 0f)
        {
            _ = planarDirection;
            return IsEyeInsideLimitedDepthForestForClip(origin, originRadius);
        }

        /// <summary>
        /// Smoothed variant of first-contact depth clipping to reduce single-ray boundary notches.
        /// Samples neighboring angular directions and returns a conservative median clip distance.
        /// </summary>
        public static float GetFirstContactDepthClipDistanceWorldSmoothed(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld,
            float depthLimitWorld,
            float originRadius = 0f,
            float neighborHalfAngleRadians = -1f)
        {
            var baseClip = GetFirstContactDepthClipDistanceWorld(
                origin,
                planarDirection,
                maxDistanceWorld,
                depthLimitWorld,
                originRadius);
            if (maxDistanceWorld <= 0.001f || depthLimitWorld <= 0.001f)
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

            var leftAngle = angle - halfStep;
            var rightAngle = angle + halfStep;
            var leftDir = new Vector3(Mathf.Cos(leftAngle), 0f, Mathf.Sin(leftAngle));
            var rightDir = new Vector3(Mathf.Cos(rightAngle), 0f, Mathf.Sin(rightAngle));

            var leftClip = GetFirstContactDepthClipDistanceWorld(
                origin,
                leftDir,
                maxDistanceWorld,
                depthLimitWorld,
                originRadius);
            var rightClip = GetFirstContactDepthClipDistanceWorld(
                origin,
                rightDir,
                maxDistanceWorld,
                depthLimitWorld,
                originRadius);

            // Median-of-three preserves boundary location while removing isolated dips/spikes.
            return SmoothForestClipPreservingSeeOut(baseClip, leftClip, rightClip, maxDistanceWorld);
        }

        public static float GetFirstContactDepthClipDistanceWorldSmoothed(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld,
            float depthLimitWorld,
            bool rayStartedInsideForest,
            float neighborHalfAngleRadians)
        {
            var baseClip = GetFirstContactDepthClipDistanceWorld(
                origin,
                planarDirection,
                maxDistanceWorld,
                depthLimitWorld,
                rayStartedInsideForest);
            if (maxDistanceWorld <= 0.001f || depthLimitWorld <= 0.001f)
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

            var leftAngle = angle - halfStep;
            var rightAngle = angle + halfStep;
            var leftDir = new Vector3(Mathf.Cos(leftAngle), 0f, Mathf.Sin(leftAngle));
            var rightDir = new Vector3(Mathf.Cos(rightAngle), 0f, Mathf.Sin(rightAngle));

            var leftClip = GetFirstContactDepthClipDistanceWorld(
                origin,
                leftDir,
                maxDistanceWorld,
                depthLimitWorld,
                rayStartedInsideForest);
            var rightClip = GetFirstContactDepthClipDistanceWorld(
                origin,
                rightDir,
                maxDistanceWorld,
                depthLimitWorld,
                rayStartedInsideForest);

            return SmoothForestClipPreservingSeeOut(baseClip, leftClip, rightClip, maxDistanceWorld);
        }

        /// <summary>
        /// Neighbor median for forest-interior rays only. See-out (open) rays keep full radius so
        /// edge wedges are not pulled down to 3" interior neighbors.
        /// </summary>
        private static float SmoothForestClipPreservingSeeOut(
            float baseClip,
            float leftClip,
            float rightClip,
            float maxDistanceWorld)
        {
            var openThreshold = maxDistanceWorld - CombatScale.InchesToWorldUnits(0.25f);
            if (baseClip > openThreshold)
            {
                return baseClip;
            }

            return Median3(baseClip, leftClip, rightClip);
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

        /// <summary>
        /// Debug helper for first-contact+depth rule. Returns detailed intermediate values.
        /// </summary>
        public static float GetFirstContactDepthClipDebugWorld(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld,
            float depthLimitWorld,
            out float firstContactDistance,
            out float candidateDistance,
            out bool candidateInsideForest)
        {
            origin.y = 0f;
            EnsureCache();
            if (CachedZones.Count == 0 || maxDistanceWorld <= 0.001f || depthLimitWorld <= 0.001f)
            {
                firstContactDistance = -1f;
                candidateDistance = maxDistanceWorld;
                candidateInsideForest = false;
                return maxDistanceWorld;
            }

            // Contact search mirrors primary path.
            firstContactDistance = -1f;
            if (IsInsideAnyLimitedDepthZone(origin))
            {
                firstContactDistance = 0f;
            }
            else
            {
                var coarseStep = Mathf.Max(CombatScale.InchesToWorldUnits(0.25f), 0.05f);
                var distance = 0f;
                var previousDistance = 0f;
                while (distance < maxDistanceWorld - 0.001f)
                {
                    var nextDistance = Mathf.Min(maxDistanceWorld, distance + coarseStep);
                    var midpoint = distance + (nextDistance - distance) * 0.5f;
                    var samplePoint = origin + planarDirection * midpoint;
                    if (IsInsideLimitedDepthZoneForClip(samplePoint))
                    {
                        firstContactDistance = RefineFirstContactDistance(origin, planarDirection, previousDistance, nextDistance);
                        break;
                    }

                    previousDistance = distance;
                    distance = nextDistance;
                }
            }

            if (firstContactDistance < 0f)
            {
                candidateDistance = maxDistanceWorld;
                candidateInsideForest = false;
                return maxDistanceWorld;
            }

            candidateDistance = Mathf.Min(maxDistanceWorld, firstContactDistance + depthLimitWorld);
            var clipPoint = origin + planarDirection * candidateDistance;
            candidateInsideForest = IsInsideLimitedDepthZoneForClip(clipPoint);
            if (candidateInsideForest && candidateDistance < maxDistanceWorld - 0.001f)
            {
                var verificationMargin = CombatScale.InchesToWorldUnits(ClipVerificationMarginInches);
                var verifyDistance = Mathf.Min(maxDistanceWorld, candidateDistance + verificationMargin);
                var verifyPoint = origin + planarDirection * verifyDistance;
                candidateInsideForest = IsInsideLimitedDepthZoneForClip(verifyPoint);
            }

            return candidateInsideForest ? candidateDistance : maxDistanceWorld;
        }

        private static float ComputeFirstContactDepthClipCandidate(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld,
            float depthLimitWorld,
            float originRadius = 0f)
        {
            origin.y = 0f;
            var eyeInsideForest = IsEyeInsideLimitedDepthForestForClip(origin, originRadius);
            return ComputeFirstContactDepthClipCandidate(
                origin,
                planarDirection,
                maxDistanceWorld,
                depthLimitWorld,
                eyeInsideForest,
                originRadius);
        }

        private static float ComputeFirstContactDepthClipCandidate(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld,
            float depthLimitWorld,
            bool eyeInsideForest,
            float originRadius = 0f)
        {
            origin.y = 0f;
            var cursor = 0f;
            const float advanceEpsilon = 0.001f;
            var thinForestEpsilon = CombatScale.InchesToWorldUnits(0.05f);
            var startInsideEpsilon = CombatScale.InchesToWorldUnits(0.02f);
            var adjacentForestGapWorld = thinForestEpsilon + CombatScale.InchesToWorldUnits(0.25f);

            if (!eyeInsideForest
                && !RayMayHitAnyCachedZoneAabb(origin, planarDirection, maxDistanceWorld))
            {
                return maxDistanceWorld;
            }

            // From inside forest: you may only see out when the exit edge is within the depth
            // limit of the unit — not an arbitrary 3" peek from deep in the trees.
            if (eyeInsideForest)
            {
                var exitFromUnit = FindForestExitDistanceAlongRay(
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

                if (exitFromUnit > depthLimitWorld + thinForestEpsilon)
                {
                    return TryFinalizeClipDistance(
                        origin,
                        planarDirection,
                        depthLimitWorld,
                        maxDistanceWorld,
                        exitFromUnit);
                }

                // Exit within depth limit: see out past this patch, then march for any further
                // forest contacts (outside approach uses first contact + 3" depth).
                var originZone = TryGetZoneAtExitCrossing(
                        origin,
                        planarDirection,
                        exitFromUnit,
                        maxDistanceWorld)
                    ?? TryGetInnermostLimitedDepthZoneAt(origin);
                cursor = exitFromUnit + advanceEpsilon;
                eyeInsideForest = false;

                var nextEntry = FindNextForestEntryDistance(
                    origin,
                    planarDirection,
                    cursor,
                    maxDistanceWorld,
                    originRadius);
                if (nextEntry < 0f)
                {
                    return maxDistanceWorld;
                }

                if (nextEntry - exitFromUnit <= adjacentForestGapWorld)
                {
                    var nextProbe = origin + planarDirection * Mathf.Min(
                        maxDistanceWorld,
                        nextEntry + startInsideEpsilon);
                    var nextZone = TryGetInnermostLimitedDepthZoneAt(nextProbe);

                    if (originZone != null
                        && nextZone != null
                        && originZone != nextZone)
                    {
                        return TryFinalizeClipDistance(
                            origin,
                            planarDirection,
                            exitFromUnit,
                            maxDistanceWorld,
                            exitFromUnit);
                    }

                    if (originZone != null && nextZone == originZone)
                    {
                        return maxDistanceWorld;
                    }
                }
            }

            while (cursor < maxDistanceWorld - advanceEpsilon)
            {
                var entryDistance = FindNextForestEntryDistance(
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
                var probeStart = origin + planarDirection * Mathf.Min(
                    maxDistanceWorld,
                    entryDistance + startInsideEpsilon);
                if (!IsInsideLimitedDepthZoneForClip(probeStart))
                {
                    var nudgeDistance = entryDistance + startInsideEpsilon;
                    for (var attempt = 0; attempt < 6; attempt++)
                    {
                        if (IsInsideLimitedDepthZoneForClip(probeStart))
                        {
                            break;
                        }

                        nudgeDistance += CombatScale.InchesToWorldUnits(0.05f);
                        probeStart = origin + planarDirection * Mathf.Min(maxDistanceWorld, nudgeDistance);
                    }
                }

                if (!IsInsideLimitedDepthZoneForClip(probeStart))
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
                var outsideEntryClip = Mathf.Min(maxDistanceWorld, entryDistance + depthLimitWorld);
                outsideEntryClip = Mathf.Min(outsideEntryClip, absoluteExit);
                return TryFinalizeClipDistance(
                    origin,
                    planarDirection,
                    outsideEntryClip,
                    maxDistanceWorld,
                    exitFromEntry);
            }

            return maxDistanceWorld;
        }

        private static float FindNextForestEntryDistance(
            Vector3 origin,
            Vector3 planarDirection,
            float searchStart,
            float maxDistanceWorld,
            float originRadius = 0f)
        {
            origin.y = 0f;
            originRadius = Mathf.Max(0f, originRadius);
            if (searchStart <= 0.001f
                && IsEyeInsideLimitedDepthForestForClip(origin, originRadius))
            {
                return 0f;
            }

            var analytic = TryFindNextForestEntryDistanceAnalytic(
                origin,
                planarDirection,
                searchStart,
                maxDistanceWorld);
            if (analytic >= 0f)
            {
                return analytic;
            }

            return MarchFirstForestEntryDistance(
                origin,
                planarDirection,
                searchStart,
                maxDistanceWorld);
        }

        /// <summary>
        /// Ground-truth nearest forest entry by sampling along the ray. Catches cases where
        /// analytic hits on a farther zone were accepted while a nearer forest was skipped.
        /// </summary>
        private static float MarchFirstForestEntryDistance(
            Vector3 origin,
            Vector3 planarDirection,
            float searchStart,
            float maxDistanceWorld)
        {
            var sampleAtStart = origin + planarDirection * searchStart;
            if (IsInsideLimitedDepthZoneForClip(sampleAtStart))
            {
                return searchStart;
            }

            var coarseStep = Mathf.Max(CombatScale.InchesToWorldUnits(0.1f), 0.05f);
            var distance = Mathf.Max(0f, searchStart);
            var previousDistance = distance;
            while (distance < maxDistanceWorld - 0.001f)
            {
                var nextDistance = Mathf.Min(maxDistanceWorld, distance + coarseStep);
                var midpoint = distance + (nextDistance - distance) * 0.5f;
                var samplePoint = origin + planarDirection * midpoint;
                if (IsInsideLimitedDepthZoneForClip(samplePoint))
                {
                    return RefineFirstContactDistance(origin, planarDirection, previousDistance, nextDistance);
                }

                previousDistance = distance;
                distance = nextDistance;
            }

            return -1f;
        }

        /// <summary>
        /// XZ tabletop ray interval through a zone footprint (polygon, disc, or oriented box).
        /// Used by depth clip and terrain upload — keep callers on this path, not polygon-only queries.
        /// </summary>
        internal static bool TryGetZoneRayFootprintIntervalWorld(
            CombatZone zone,
            Vector3 origin,
            Vector3 planarDirection,
            out float enterWorld,
            out float exitWorld)
        {
            return TryGetForestZoneRayInterval(zone, origin, planarDirection, out enterWorld, out exitWorld);
        }

        /// <summary>
        /// True when <paramref name="clipDistance"/> lies on this zone's ray interval (with depth slack).
        /// Filters grazing hits from unrelated forests during multi-zone upload edge selection.
        /// </summary>
        internal static bool ClipDistanceRelatesToZoneFootprint(
            CombatZone zone,
            Vector3 origin,
            Vector3 planarDirection,
            float clipDistance,
            float depthLimitWorld)
        {
            if (zone == null
                || !TryGetZoneRayFootprintIntervalWorld(zone, origin, planarDirection, out var enter, out var exit))
            {
                return false;
            }

            var slack = CombatScale.InchesToWorldUnits(0.08f);
            if (clipDistance + slack < enter)
            {
                return false;
            }

            return clipDistance - slack <= exit + depthLimitWorld;
        }

        /// <summary>
        /// Exit edge for the forest patch that owns <paramref name="clipDistance"/> on this ray.
        /// Ignores unrelated zones whose footprint only grazes the ray.
        /// </summary>
        internal static float TryGetFootprintExitForClipDistance(
            Vector3 origin,
            Vector3 planarDirection,
            float clipDistance,
            float maxDistanceWorld,
            float depthLimitWorld)
        {
            origin.y = 0f;
            planarDirection.y = 0f;
            if (planarDirection.sqrMagnitude <= 1e-8f
                || maxDistanceWorld <= 0.001f
                || clipDistance >= maxDistanceWorld - 0.001f)
            {
                return -1f;
            }

            planarDirection.Normalize();
            var slack = CombatScale.InchesToWorldUnits(0.08f);
            var bestEnter = -1f;
            var bestExit = -1f;
            var activeZones = CombatZone.ActiveZones;
            for (var z = 0; z < activeZones.Count; z++)
            {
                var zone = activeZones[z];
                var feature = zone?.TerrainFeature;
                if (zone == null || feature == null || !feature.UsesPassThroughFogClip)
                {
                    continue;
                }

                if (!IsZoneActiveForClipPass(
                        new ClipZone { LineOfSightMode = feature.LineOfSightMode },
                        applyForestClipPass,
                        applyBlockingClipPass))
                {
                    continue;
                }

                if (!TryGetZoneRayFootprintIntervalWorld(
                        zone,
                        origin,
                        planarDirection,
                        out var enter,
                        out var exit))
                {
                    continue;
                }

                if (clipDistance + slack < enter || clipDistance - slack > exit + depthLimitWorld)
                {
                    continue;
                }

                if (enter > bestEnter)
                {
                    bestEnter = enter;
                    bestExit = exit;
                }
            }

            if (bestExit > slack && bestExit <= maxDistanceWorld)
            {
                return bestExit;
            }

            return -1f;
        }

        private static bool TryGetForestZoneRayInterval(
            CombatZone zone,
            Vector3 origin,
            Vector3 planarDirection,
            out float enterT,
            out float exitT)
        {
            enterT = -1f;
            exitT = -1f;
            if (zone == null)
            {
                return false;
            }

            origin.y = 0f;
            planarDirection.y = 0f;
            if (planarDirection.sqrMagnitude <= 1e-8f)
            {
                return false;
            }

            planarDirection.Normalize();

            if (zone.TryGetPolygonFootprint(out var polygonFootprint)
                && polygonFootprint.TryGetRayFootprintIntervalWorld(origin, planarDirection, out enterT, out exitT))
            {
                return true;
            }

            if (!zone.TryGetFootprintCollider(out var collider))
            {
                return false;
            }

            if (collider is SphereCollider sphere)
            {
                var t = sphere.transform;
                var worldCenter = t.TransformPoint(sphere.center);
                var center2 = new Vector2(worldCenter.x, worldCenter.z);
                var scale = t.lossyScale;
                var radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                if (radius <= 1e-6f)
                {
                    return false;
                }

                var origin2 = new Vector2(origin.x, origin.z);
                var dir2 = new Vector2(planarDirection.x, planarDirection.z);
                return CombatFogPlanarGeometry.TryRayDiscInterval(origin2, dir2, center2, radius, out enterT, out exitT);
            }

            if (collider is BoxCollider box)
            {
                return CombatFogPlanarGeometry.TryRayOrientedBoxFootprintInterval(
                    origin,
                    planarDirection,
                    box,
                    out enterT,
                    out exitT);
            }

            return false;
        }

        private enum ForestZoneBoundaryKind
        {
            Entry,
            Exit
        }

        /// <summary>
        /// Shared entry/exit search for every box and circular forest zone.
        /// </summary>
        private static bool TryFindForestZoneBoundaryDistance(
            CombatZone zone,
            Vector3 origin,
            Vector3 planarDirection,
            ForestZoneBoundaryKind kind,
            float searchStart,
            float maxDistanceWorld,
            float transitionEpsilon,
            ref float best)
        {
            if (kind == ForestZoneBoundaryKind.Exit && !zone.ContainsPoint(origin))
            {
                return true;
            }

            if (!TryGetForestZoneRayInterval(zone, origin, planarDirection, out var enterT, out var exitT))
            {
                return false;
            }

            var hitT = kind == ForestZoneBoundaryKind.Entry ? enterT : exitT;
            if (kind == ForestZoneBoundaryKind.Entry)
            {
                if (hitT < searchStart - transitionEpsilon || hitT >= best || hitT > maxDistanceWorld)
                {
                    return true;
                }

                var before = origin + planarDirection * Mathf.Max(0f, hitT - transitionEpsilon);
                var after = origin + planarDirection * Mathf.Min(maxDistanceWorld, hitT + transitionEpsilon);
                if (!zone.ContainsPoint(before) && zone.ContainsPoint(after))
                {
                    best = hitT;
                }

                return true;
            }

            if (hitT <= transitionEpsilon || hitT >= best || hitT > maxDistanceWorld)
            {
                return true;
            }

            var exitBefore = origin + planarDirection * Mathf.Max(0f, hitT - transitionEpsilon);
            var exitAfter = origin + planarDirection * Mathf.Min(maxDistanceWorld, hitT + transitionEpsilon);
            if (zone.ContainsPoint(exitBefore) && !zone.ContainsPoint(exitAfter))
            {
                best = hitT;
            }

            return true;
        }

        private static float TryFindNextForestEntryDistanceAnalytic(
            Vector3 origin,
            Vector3 planarDirection,
            float searchStart,
            float maxDistanceWorld)
        {
            planarDirection.y = 0f;
            if (planarDirection.sqrMagnitude <= 1e-8f || maxDistanceWorld <= searchStart + 0.001f)
            {
                return -1f;
            }

            planarDirection.Normalize();
            var origin2 = new Vector2(origin.x, origin.z);
            var dir2 = new Vector2(planarDirection.x, planarDirection.z);
            var transitionEpsilon = CombatScale.InchesToWorldUnits(0.02f);
            var best = float.MaxValue;

            for (var z = 0; z < CachedZones.Count; z++)
            {
                var cachedZone = CachedZones[z];
                if (!IsZoneActiveForClipPass(cachedZone))
                {
                    continue;
                }

                var zone = cachedZone.Zone;
                if (zone == null)
                {
                    continue;
                }

                if (TryFindForestZoneBoundaryDistance(
                        zone,
                        origin,
                        planarDirection,
                        ForestZoneBoundaryKind.Entry,
                        searchStart,
                        maxDistanceWorld,
                        transitionEpsilon,
                        ref best))
                {
                    continue;
                }

                FootprintCornerScratch.Clear();
                zone.CollectFootprintCorners(FootprintCornerScratch);
                var corners = FootprintCornerScratch;
                if (corners.Count < 3)
                {
                    continue;
                }

                for (var i = 0; i < corners.Count; i++)
                {
                    var a = corners[i];
                    var b = corners[(i + 1) % corners.Count];
                    var a2 = new Vector2(a.x, a.z);
                    var b2 = new Vector2(b.x, b.z);
                    if (!CombatFogPlanarGeometry.TryRaySegmentHit(origin2, dir2, a2, b2, out var hitT))
                    {
                        continue;
                    }

                    if (hitT < searchStart - transitionEpsilon || hitT >= best || hitT > maxDistanceWorld)
                    {
                        continue;
                    }

                    var before = origin + planarDirection * Mathf.Max(0f, hitT - transitionEpsilon);
                    var after = origin + planarDirection * Mathf.Min(maxDistanceWorld, hitT + transitionEpsilon);
                    if (!zone.ContainsPoint(before) && zone.ContainsPoint(after))
                    {
                        best = hitT;
                    }
                }
            }

            return best < float.MaxValue ? best : -1f;
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
            var candidateInside = IsInsideCandidateNeighborhood(clipPoint, planarDirection);
            if (!candidateInside && exitFromContact < 0f)
            {
                return maxDistanceWorld;
            }

            if (clipDistance < maxDistanceWorld - 0.001f)
            {
                var verificationMargin = CombatScale.InchesToWorldUnits(ClipVerificationMarginInches);
                var verifyDistance = Mathf.Min(maxDistanceWorld, clipDistance + verificationMargin);
                var verifyPoint = origin + planarDirection * verifyDistance;
                var verifyInside = IsInsideCandidateNeighborhood(verifyPoint, planarDirection);
                if (!verifyInside && exitFromContact < 0f)
                {
                    return maxDistanceWorld;
                }
            }

            return clipDistance;
        }

        private static float FindFirstOutsideDistanceFromInside(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld)
        {
            var analytic = TryFindFirstOutsideDistanceAnalytic(origin, planarDirection, maxDistanceWorld);
            if (analytic >= 0f)
            {
                return analytic;
            }

            var coarseStep = Mathf.Max(CombatScale.InchesToWorldUnits(0.25f), 0.05f);
            var insideDistance = 0f;
            var distance = 0f;
            while (distance < maxDistanceWorld - 0.001f)
            {
                var nextDistance = Mathf.Min(maxDistanceWorld, distance + coarseStep);
                var midpoint = distance + (nextDistance - distance) * 0.5f;
                var samplePoint = origin + planarDirection * midpoint;
                var inside = IsInsideLimitedDepthZoneForClip(samplePoint);
                if (!inside)
                {
                    return RefineBoundaryDistance(origin, planarDirection, insideDistance, nextDistance, findInsideToOutside: true);
                }

                insideDistance = nextDistance;
                distance = nextDistance;
            }

            return -1f;
        }

        private static float TryFindFirstOutsideDistanceAnalytic(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld)
        {
            if (!IsInsideLimitedDepthZoneForClip(origin))
            {
                return -1f;
            }

            planarDirection.y = 0f;
            if (planarDirection.sqrMagnitude <= 1e-8f)
            {
                return -1f;
            }

            planarDirection.Normalize();
            var origin2 = new Vector2(origin.x, origin.z);
            var dir2 = new Vector2(planarDirection.x, planarDirection.z);
            var transitionEpsilon = CombatScale.InchesToWorldUnits(0.02f);
            var best = float.MaxValue;

            var activeZones = CombatZone.ActiveZones;
            for (var z = 0; z < activeZones.Count; z++)
            {
                var zone = activeZones[z];
                var feature = zone?.TerrainFeature;
                if (zone == null || feature == null || !feature.UsesPassThroughFogClip)
                {
                    continue;
                }

                if (!IsZoneActiveForClipPass(
                        new ClipZone { LineOfSightMode = feature.LineOfSightMode },
                        applyForestClipPass,
                        applyBlockingClipPass))
                {
                    continue;
                }

                if (TryFindForestZoneBoundaryDistance(
                        zone,
                        origin,
                        planarDirection,
                        ForestZoneBoundaryKind.Exit,
                        searchStart: 0f,
                        maxDistanceWorld,
                        transitionEpsilon,
                        ref best))
                {
                    continue;
                }

                FootprintCornerScratch.Clear();
                zone.CollectFootprintCorners(FootprintCornerScratch);
                var corners = FootprintCornerScratch;
                if (corners.Count < 3)
                {
                    continue;
                }

                for (var i = 0; i < corners.Count; i++)
                {
                    var a = corners[i];
                    var b = corners[(i + 1) % corners.Count];
                    var a2 = new Vector2(a.x, a.z);
                    var b2 = new Vector2(b.x, b.z);
                    if (!CombatFogPlanarGeometry.TryRaySegmentHit(origin2, dir2, a2, b2, out var hitT))
                    {
                        continue;
                    }

                    if (hitT <= transitionEpsilon || hitT >= best || hitT > maxDistanceWorld)
                    {
                        continue;
                    }

                    var before = origin + planarDirection * Mathf.Max(0f, hitT - transitionEpsilon);
                    var after = origin + planarDirection * Mathf.Min(maxDistanceWorld, hitT + transitionEpsilon);
                    if (zone.ContainsPoint(before) && !zone.ContainsPoint(after))
                    {
                        best = hitT;
                    }
                }
            }

            return best < float.MaxValue ? best : -1f;
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
                var sample = origin + planarDirection * mid;
                var inside = IsInsideLimitedDepthZoneForClip(sample);
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
                else
                {
                    if (inside)
                    {
                        high = mid;
                    }
                    else
                    {
                        low = mid;
                    }
                }
            }

            return high;
        }

        private static float RefineFirstContactDistance(
            Vector3 origin,
            Vector3 planarDirection,
            float minDistance,
            float maxDistance)
        {
            origin.y = 0f;
            var low = Mathf.Max(0f, minDistance);
            var high = Mathf.Max(low, maxDistance);

            for (var i = 0; i < 5; i++)
            {
                var mid = (low + high) * 0.5f;
                var sample = origin + planarDirection * mid;
                var inside = IsInsideLimitedDepthZoneForClip(sample);
                if (inside)
                {
                    high = mid;
                }
                else
                {
                    low = mid;
                }
            }

            return high;
        }

        /// <summary>
        /// Stabilizes the clipped/open transition near forest boundaries by sampling
        /// a tiny neighborhood around the candidate point, instead of a single point.
        /// This prevents one-ray notches caused by tiny containment jitter.
        /// </summary>
        private static bool IsInsideCandidateNeighborhood(Vector3 point, Vector3 planarDirection)
        {
            if (IsInsideLimitedDepthZoneForClip(point))
            {
                return true;
            }

            var radius = CombatScale.InchesToWorldUnits(CandidateSmoothingRadiusInches);
            if (radius <= 0.0001f)
            {
                return false;
            }

            var perpendicular = new Vector3(-planarDirection.z, 0f, planarDirection.x);
            if (perpendicular.sqrMagnitude <= 1e-8f)
            {
                return false;
            }

            perpendicular.Normalize();
            var hits = 0;
            var sampleCount = 0;

            // Offsets biased across the boundary normal to smooth jagged transitions.
            var offsets = new[] { -1f, -0.5f, 0.5f, 1f };
            for (var i = 0; i < offsets.Length; i++)
            {
                var samplePoint = point + perpendicular * (offsets[i] * radius);
                sampleCount++;
                if (IsInsideLimitedDepthZoneForClip(samplePoint))
                {
                    hits++;
                }
            }

            // Majority vote over neighborhood samples.
            return hits * 2 >= sampleCount;
        }

        private static bool IsInsideLimitedDepthZoneForClip(Vector3 worldPoint)
        {
            EnsureCache();
            if (CachedZones.Count > 0)
            {
                return IsInsideAnyLimitedDepthZoneFast(worldPoint);
            }

            return IsInsideAnyLimitedDepthZone(worldPoint);
        }

        private static bool IsInsideAnyLimitedDepthZone(Vector3 worldPoint)
        {
            var activeZones = CombatZone.ActiveZones;
            for (var i = 0; i < activeZones.Count; i++)
            {
                var zone = activeZones[i];
                var feature = zone?.TerrainFeature;
                if (zone == null || feature == null || !feature.UsesPassThroughFogClip)
                {
                    continue;
                }

                if (!IsZoneActiveForClipPass(
                        new ClipZone { LineOfSightMode = feature.LineOfSightMode },
                        applyForestClipPass,
                        applyBlockingClipPass))
                {
                    continue;
                }

                if (zone.ContainsPoint(worldPoint))
                {
                    return true;
                }
            }

            return false;
        }

        private static CombatZone TryGetLimitedDepthZoneAt(Vector3 worldPoint)
        {
            return TryGetInnermostLimitedDepthZoneAt(worldPoint);
        }

        /// <summary>
        /// When several forests overlap, pick the smallest footprint so zone identity stays stable while moving.
        /// </summary>
        private static CombatZone TryGetInnermostLimitedDepthZoneAt(Vector3 worldPoint)
        {
            CombatZone bestZone = null;
            var bestArea = float.MaxValue;
            var activeZones = CombatZone.ActiveZones;
            for (var i = 0; i < activeZones.Count; i++)
            {
                var zone = activeZones[i];
                var feature = zone?.TerrainFeature;
                if (zone == null || feature == null || !feature.UsesPassThroughFogClip)
                {
                    continue;
                }

                if (!IsZoneActiveForClipPass(
                        new ClipZone { LineOfSightMode = feature.LineOfSightMode },
                        applyForestClipPass,
                        applyBlockingClipPass))
                {
                    continue;
                }

                if (!zone.ContainsPoint(worldPoint))
                {
                    continue;
                }

                if (!TryGetZoneFootprintAreaWorld(zone, out var area) || area <= 1e-8f)
                {
                    return zone;
                }

                if (area < bestArea)
                {
                    bestArea = area;
                    bestZone = zone;
                }
            }

            return bestZone;
        }

        private static CombatZone TryGetZoneAtExitCrossing(
            Vector3 origin,
            Vector3 planarDirection,
            float exitDistance,
            float maxDistanceWorld)
        {
            if (exitDistance <= 0.001f)
            {
                return null;
            }

            origin.y = 0f;
            planarDirection.y = 0f;
            if (planarDirection.sqrMagnitude <= 1e-8f)
            {
                return null;
            }

            planarDirection.Normalize();
            var transitionEpsilon = CombatScale.InchesToWorldUnits(0.02f);
            CombatZone match = null;
            var bestExitDelta = float.MaxValue;
            var activeZones = CombatZone.ActiveZones;
            for (var i = 0; i < activeZones.Count; i++)
            {
                var zone = activeZones[i];
                var feature = zone?.TerrainFeature;
                if (zone == null || feature == null || !feature.UsesPassThroughFogClip)
                {
                    continue;
                }

                if (!IsZoneActiveForClipPass(
                        new ClipZone { LineOfSightMode = feature.LineOfSightMode },
                        applyForestClipPass,
                        applyBlockingClipPass))
                {
                    continue;
                }

                if (!TryGetForestZoneRayInterval(zone, origin, planarDirection, out _, out var exitT))
                {
                    continue;
                }

                var exitDelta = Mathf.Abs(exitT - exitDistance);
                if (exitDelta > transitionEpsilon || exitDelta >= bestExitDelta)
                {
                    continue;
                }

                var before = origin + planarDirection * Mathf.Max(0f, exitT - transitionEpsilon);
                var after = origin + planarDirection * Mathf.Min(maxDistanceWorld, exitT + transitionEpsilon);
                if (zone.ContainsPoint(before) && !zone.ContainsPoint(after))
                {
                    bestExitDelta = exitDelta;
                    match = zone;
                }
            }

            return match;
        }

        private static bool TryGetZoneFootprintAreaWorld(CombatZone zone, out float areaWorld)
        {
            areaWorld = 0f;
            if (zone == null)
            {
                return false;
            }

            if (zone.TryGetPolygonFootprint(out var polygonFootprint)
                && polygonFootprint.TryGetFootprintBounds(out var polygonBounds))
            {
                var size = polygonBounds.size;
                areaWorld = Mathf.Abs(size.x * size.z);
                return true;
            }

            if (!zone.TryGetFootprintCollider(out var collider) || collider == null)
            {
                return false;
            }

            if (collider is SphereCollider sphere)
            {
                var t = sphere.transform;
                var scale = t.lossyScale;
                var radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                areaWorld = Mathf.PI * radius * radius;
                return true;
            }

            if (collider is BoxCollider box)
            {
                var size = Vector3.Scale(box.size, box.transform.lossyScale);
                areaWorld = Mathf.Abs(size.x * size.z);
                return true;
            }

            var bounds = collider.bounds;
            areaWorld = Mathf.Abs(bounds.size.x * bounds.size.z);
            return true;
        }

        private static bool IsInsideAnyLimitedDepthZoneFast(Vector3 worldPoint)
        {
            var x = worldPoint.x;
            var z = worldPoint.z;
            for (var i = 0; i < CachedZones.Count; i++)
            {
                var zone = CachedZones[i];
                if (!IsZoneActiveForClipPass(zone))
                {
                    continue;
                }

                if (x < zone.MinX || x > zone.MaxX || z < zone.MinZ || z > zone.MaxZ)
                {
                    continue;
                }

                if (zone.Zone != null && zone.Zone.ContainsPoint(worldPoint))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool RayMayHitAnyCachedZoneAabbPublic(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld)
        {
            return RayMayHitAnyCachedZoneAabb(origin, planarDirection, maxDistanceWorld);
        }

        internal static bool AnyCachedZoneWithinReach(Vector3 worldPoint, float reachWorld)
        {
            EnsureCache();
            if (CachedZones.Count == 0 || reachWorld <= 0.001f)
            {
                return false;
            }

            var px = worldPoint.x;
            var pz = worldPoint.z;
            var reachSq = reachWorld * reachWorld;
            for (var i = 0; i < CachedZones.Count; i++)
            {
                var zone = CachedZones[i];
                if (!IsZoneActiveForClipPass(zone))
                {
                    continue;
                }

                var cx = Mathf.Clamp(px, zone.MinX, zone.MaxX);
                var cz = Mathf.Clamp(pz, zone.MinZ, zone.MaxZ);
                var dx = px - cx;
                var dz = pz - cz;
                if (dx * dx + dz * dz <= reachSq)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RayMayHitAnyCachedZoneAabb(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld)
        {
            if (CachedZones.Count == 0 || maxDistanceWorld <= 0.001f)
            {
                return false;
            }

            var origin2 = new Vector2(origin.x, origin.z);
            var direction2 = new Vector2(planarDirection.x, planarDirection.z);
            if (direction2.sqrMagnitude <= 1e-8f)
            {
                return false;
            }

            direction2.Normalize();
            for (var i = 0; i < CachedZones.Count; i++)
            {
                var zone = CachedZones[i];
                if (!IsZoneActiveForClipPass(zone))
                {
                    continue;
                }

                if (CombatFogPlanarGeometry.RayMayHitHorizontalAabb(
                        origin2,
                        direction2,
                        maxDistanceWorld,
                        zone.MinX,
                        zone.MaxX,
                        zone.MinZ,
                        zone.MaxZ))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Approximate planar distance from a point to nearest limited-depth zone boundary.
        /// If the point is inside a zone, this is distance to exit.
        /// If outside, this is distance to first entry.
        /// </summary>
        public static float GetApproxDistanceToLimitedDepthBoundaryWorld(Vector3 worldPoint, float maxProbeWorld = 0f)
        {
            EnsureCache();
            if (CachedZones.Count == 0)
            {
                return float.PositiveInfinity;
            }

            var probeLimit = maxProbeWorld > 0.001f
                ? maxProbeWorld
                : CombatScale.InchesToWorldUnits(24f);
            var step = Mathf.Max(CombatScale.InchesToWorldUnits(0.1f), 0.02f);
            var isInside = IsInsideLimitedDepthZoneForClip(worldPoint);

            var best = probeLimit;
            const int radialSamples = 24;
            for (var i = 0; i < radialSamples; i++)
            {
                var angle = (Mathf.PI * 2f * i) / radialSamples;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var distance = 0f;
                while (distance < probeLimit - 0.001f)
                {
                    var next = Mathf.Min(probeLimit, distance + step);
                    var midpoint = distance + (next - distance) * 0.5f;
                    var samplePoint = worldPoint + direction * midpoint;
                    var sampleInside = IsInsideLimitedDepthZoneForClip(samplePoint);
                    if (sampleInside != isInside)
                    {
                        if (midpoint < best)
                        {
                            best = midpoint;
                        }

                        break;
                    }

                    distance = next;
                }
            }

            return best;
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
