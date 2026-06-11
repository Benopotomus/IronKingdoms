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
                if (zone == null || feature == null || feature.LineOfSightMode != CombatTerrainLineOfSightMode.LimitedDepth)
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
            float maxDistanceWorld)
        {
            EnsureCache();
            if (maxDistanceWorld <= 0.001f)
            {
                return -1f;
            }

            if (!RayStartsInsideForest(origin, planarDirection))
            {
                return -1f;
            }

            origin.y = 0f;
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
                if (zone == null || feature == null || feature.LineOfSightMode != CombatTerrainLineOfSightMode.LimitedDepth)
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
                if (feature == null || feature.LineOfSightMode != CombatTerrainLineOfSightMode.LimitedDepth)
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
                    Zone = zone,
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
            // Inside a forest volume, measure the depth budget from the eye. Base-edge offset
            // can miss the zone on some rays across large forest colliders.
            if (IsInsideLimitedDepthForest(origin, 0f))
            {
                originRadius = 0f;
            }

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
            float depthLimitWorld)
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

            // Walk forest segments along the ray so separate zones (e.g. square vs
            // circular) each contribute clips even when the eye is inside another forest.
            return ComputeFirstContactDepthClipCandidate(
                origin,
                planarDirection,
                maxDistanceWorld,
                depthLimitWorld);
        }

        /// <summary>
        /// True when this ray leaves the eye from inside forest (origin inside, or the ray
        /// immediately enters forest). Used instead of a global observer inside/outside flag.
        /// </summary>
        private static bool RayStartsInsideForest(Vector3 origin, Vector3 planarDirection)
        {
            origin.y = 0f;
            if (IsInsideAnyLimitedDepthZone(origin))
            {
                return true;
            }

            var step = CombatScale.InchesToWorldUnits(0.05f);
            return IsInsideAnyLimitedDepthZone(origin + planarDirection * step);
        }

        /// <summary>
        /// Smoothed variant of first-contact depth clipping to reduce single-ray boundary notches.
        /// Samples neighboring angular directions and returns a conservative median clip distance.
        /// </summary>
        public static float GetFirstContactDepthClipDistanceWorldSmoothed(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld,
            float depthLimitWorld)
        {
            var baseClip = GetFirstContactDepthClipDistanceWorld(origin, planarDirection, maxDistanceWorld, depthLimitWorld);
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
            var halfStep = (Mathf.PI * 2f) / AngularSmoothingSamples * 0.5f;

            var leftAngle = angle - halfStep;
            var rightAngle = angle + halfStep;
            var leftDir = new Vector3(Mathf.Cos(leftAngle), 0f, Mathf.Sin(leftAngle));
            var rightDir = new Vector3(Mathf.Cos(rightAngle), 0f, Mathf.Sin(rightAngle));

            var leftClip = GetFirstContactDepthClipDistanceWorld(origin, leftDir, maxDistanceWorld, depthLimitWorld);
            var rightClip = GetFirstContactDepthClipDistanceWorld(origin, rightDir, maxDistanceWorld, depthLimitWorld);

            // Median-of-three preserves boundary location while removing isolated dips/spikes.
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
                    if (IsInsideAnyLimitedDepthZoneFast(samplePoint) && IsInsideAnyLimitedDepthZone(samplePoint))
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
            candidateInsideForest = IsInsideAnyLimitedDepthZoneFast(clipPoint) && IsInsideAnyLimitedDepthZone(clipPoint);
            if (candidateInsideForest && candidateDistance < maxDistanceWorld - 0.001f)
            {
                var verificationMargin = CombatScale.InchesToWorldUnits(ClipVerificationMarginInches);
                var verifyDistance = Mathf.Min(maxDistanceWorld, candidateDistance + verificationMargin);
                var verifyPoint = origin + planarDirection * verifyDistance;
                candidateInsideForest = IsInsideAnyLimitedDepthZoneFast(verifyPoint) && IsInsideAnyLimitedDepthZone(verifyPoint);
            }

            return candidateInsideForest ? candidateDistance : maxDistanceWorld;
        }

        private static float ComputeFirstContactDepthClipCandidate(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld,
            float depthLimitWorld)
        {
            origin.y = 0f;
            var rayStartedInsideForest = IsInsideLimitedDepthZoneForClip(origin);
            var cursor = 0f;
            const float advanceEpsilon = 0.001f;
            var thinForestEpsilon = CombatScale.InchesToWorldUnits(0.05f);

            while (cursor < maxDistanceWorld - advanceEpsilon)
            {
                var entryDistance = FindNextForestEntryDistance(
                    origin,
                    planarDirection,
                    cursor,
                    maxDistanceWorld);
                if (entryDistance < 0f)
                {
                    return maxDistanceWorld;
                }

                var remainingFromEntry = maxDistanceWorld - entryDistance;
                var startInsideEpsilon = CombatScale.InchesToWorldUnits(0.02f);
                var probeStart = origin + planarDirection * Mathf.Min(
                    maxDistanceWorld,
                    entryDistance + startInsideEpsilon);
                if (!(IsInsideAnyLimitedDepthZoneFast(probeStart) && IsInsideAnyLimitedDepthZone(probeStart)))
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
                if (!rayStartedInsideForest)
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

                if (exitFromEntry <= depthLimitWorld + thinForestEpsilon)
                {
                    cursor = entryDistance + exitFromEntry + advanceEpsilon;
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

        private static float FindNextForestEntryDistance(
            Vector3 origin,
            Vector3 planarDirection,
            float searchStart,
            float maxDistanceWorld)
        {
            origin.y = 0f;
            if (searchStart <= 0.001f && IsInsideLimitedDepthZoneForClip(origin))
            {
                return 0f;
            }

            var sampleAtStart = origin + planarDirection * searchStart;
            if (IsInsideLimitedDepthZoneForClip(sampleAtStart))
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
                if (IsInsideLimitedDepthZoneForClip(samplePoint))
                {
                    return RefineFirstContactDistance(origin, planarDirection, previousDistance, nextDistance);
                }

                previousDistance = distance;
                distance = nextDistance;
            }

            return -1f;
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
            var coarseStep = Mathf.Max(CombatScale.InchesToWorldUnits(0.25f), 0.05f);
            var insideDistance = 0f;
            var distance = 0f;
            while (distance < maxDistanceWorld - 0.001f)
            {
                var nextDistance = Mathf.Min(maxDistanceWorld, distance + coarseStep);
                var midpoint = distance + (nextDistance - distance) * 0.5f;
                var samplePoint = origin + planarDirection * midpoint;
                var inside = IsInsideAnyLimitedDepthZoneFast(samplePoint) && IsInsideAnyLimitedDepthZone(samplePoint);
                if (!inside)
                {
                    return RefineBoundaryDistance(origin, planarDirection, insideDistance, nextDistance, findInsideToOutside: true);
                }

                insideDistance = nextDistance;
                distance = nextDistance;
            }

            return -1f;
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
                var inside = IsInsideAnyLimitedDepthZoneFast(sample) && IsInsideAnyLimitedDepthZone(sample);
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
                var inside = IsInsideAnyLimitedDepthZoneFast(sample) && IsInsideAnyLimitedDepthZone(sample);
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
            if (IsInsideAnyLimitedDepthZoneFast(point) && IsInsideAnyLimitedDepthZone(point))
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
                if (IsInsideAnyLimitedDepthZoneFast(samplePoint) && IsInsideAnyLimitedDepthZone(samplePoint))
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
                if (zone == null || feature == null || feature.LineOfSightMode != CombatTerrainLineOfSightMode.LimitedDepth)
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

        private static bool IsInsideAnyLimitedDepthZoneFast(Vector3 worldPoint)
        {
            var x = worldPoint.x;
            var z = worldPoint.z;
            for (var i = 0; i < CachedZones.Count; i++)
            {
                var zone = CachedZones[i];
                if (x < zone.MinX || x > zone.MaxX || z < zone.MinZ || z > zone.MaxZ)
                {
                    continue;
                }

                if (zone.Zone == null || zone.Zone.ContainsPoint(worldPoint))
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
            var isInside = IsInsideAnyLimitedDepthZoneFast(worldPoint) && IsInsideAnyLimitedDepthZone(worldPoint);

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
                    var sampleInside = IsInsideAnyLimitedDepthZoneFast(samplePoint) && IsInsideAnyLimitedDepthZone(samplePoint);
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
