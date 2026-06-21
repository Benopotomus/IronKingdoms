using System.Collections.Generic;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Thread-safe forest clip snapshots built during <see cref="CombatForestFogClipper.EnsureCache"/>.
    /// Worker jobs may read these lists only — never CombatZone or EnsureCache.
    /// </summary>
    internal static class CombatForestFogClipperParallelCache
    {
        private enum FootprintKind : byte
        {
            Unsupported = 0,
            Polygon = 1,
            Disc = 2,
            OrientedBox = 3,
        }

        private readonly struct FootprintSnapshot
        {
            public readonly FootprintKind Kind;
            public readonly int PolygonStart;
            public readonly int PolygonCount;
            public readonly float DiscCenterX;
            public readonly float DiscCenterZ;
            public readonly float DiscRadius;
            public readonly Matrix4x4 WorldToLocal;
            public readonly float LocalBoxMinX;
            public readonly float LocalBoxMaxX;
            public readonly float LocalBoxMinZ;
            public readonly float LocalBoxMaxZ;
            public readonly float FootprintAreaWorld;

            public FootprintSnapshot(
                FootprintKind kind,
                int polygonStart,
                int polygonCount,
                float discCenterX,
                float discCenterZ,
                float discRadius,
                Matrix4x4 worldToLocal,
                float localBoxMinX,
                float localBoxMaxX,
                float localBoxMinZ,
                float localBoxMaxZ,
                float footprintAreaWorld)
            {
                Kind = kind;
                PolygonStart = polygonStart;
                PolygonCount = polygonCount;
                DiscCenterX = discCenterX;
                DiscCenterZ = discCenterZ;
                DiscRadius = discRadius;
                WorldToLocal = worldToLocal;
                LocalBoxMinX = localBoxMinX;
                LocalBoxMaxX = localBoxMaxX;
                LocalBoxMinZ = localBoxMinZ;
                LocalBoxMaxZ = localBoxMaxZ;
                FootprintAreaWorld = footprintAreaWorld;
            }
        }

        private static readonly List<FootprintSnapshot> Footprints = new();
        private static readonly List<Vector2> PolygonVerticesWorld = new();
        private static FootprintSnapshot[] jobFootprintSnapshot;
        private static Vector2[] jobVertexSnapshot;
        private static bool allZonesThreadSafe;

        internal static bool AllZonesThreadSafe => allZonesThreadSafe && Footprints.Count > 0;

        internal static void BeginJobSnapshot()
        {
            jobFootprintSnapshot = Footprints.Count > 0 ? Footprints.ToArray() : System.Array.Empty<FootprintSnapshot>();
            jobVertexSnapshot = PolygonVerticesWorld.Count > 0
                ? PolygonVerticesWorld.ToArray()
                : System.Array.Empty<Vector2>();
        }

        internal static void EndJobSnapshot()
        {
            jobFootprintSnapshot = null;
            jobVertexSnapshot = null;
        }

        private static int ActiveFootprintCount =>
            jobFootprintSnapshot != null ? jobFootprintSnapshot.Length : Footprints.Count;

        private static FootprintSnapshot GetFootprintAt(int index) =>
            jobFootprintSnapshot != null ? jobFootprintSnapshot[index] : Footprints[index];

        private static IReadOnlyList<Vector2> ActivePolygonVertices =>
            jobVertexSnapshot != null ? jobVertexSnapshot : PolygonVerticesWorld;

        private static int WrapPolygonVertexIndex(int edgeIndex, int count) =>
            count <= 0 ? 0 : (edgeIndex + 1 < count ? edgeIndex + 1 : 0);

        internal static void Clear()
        {
            Footprints.Clear();
            PolygonVerticesWorld.Clear();
            allZonesThreadSafe = true;
            EndJobSnapshot();
        }

        internal static void BeginZone(int expectedCount)
        {
            Footprints.Clear();
            PolygonVerticesWorld.Clear();
            allZonesThreadSafe = true;
            if (Footprints.Capacity < expectedCount)
            {
                Footprints.Capacity = expectedCount;
            }
        }

        internal static void AddUnsupportedZone()
        {
            Footprints.Add(default);
            allZonesThreadSafe = false;
        }

        internal static void AddZoneFootprint(CombatZone zone)
        {
            if (zone == null)
            {
                AddUnsupportedZone();
                return;
            }

            if (zone.TryGetPolygonFootprint(out var polygonFootprint) && polygonFootprint.HasFootprint)
            {
                var start = polygonFootprint.AppendWorldPolygonSnapshot(PolygonVerticesWorld);
                if (start < 0)
                {
                    AddUnsupportedZone();
                    return;
                }

                var count = PolygonVerticesWorld.Count - start;
                if (count < 3)
                {
                    AddUnsupportedZone();
                    return;
                }

                polygonFootprint.TryGetFootprintAreaWorld(out var area);
                Footprints.Add(new FootprintSnapshot(
                    FootprintKind.Polygon,
                    start,
                    count,
                    0f,
                    0f,
                    0f,
                    Matrix4x4.identity,
                    0f,
                    0f,
                    0f,
                    0f,
                    area));
                return;
            }

            if (!zone.TryGetFootprintCollider(out var collider) || collider == null || !collider.enabled)
            {
                AddUnsupportedZone();
                return;
            }

            if (collider is SphereCollider sphere)
            {
                var t = sphere.transform;
                var worldCenter = t.TransformPoint(sphere.center);
                var scale = t.lossyScale;
                var radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                var area = Mathf.PI * radius * radius;
                Footprints.Add(new FootprintSnapshot(
                    FootprintKind.Disc,
                    0,
                    0,
                    worldCenter.x,
                    worldCenter.z,
                    radius,
                    Matrix4x4.identity,
                    0f,
                    0f,
                    0f,
                    0f,
                    area));
                return;
            }

            if (collider is BoxCollider box)
            {
                var center = box.center;
                var half = box.size * 0.5f;
                var area = Mathf.Abs(box.size.x * box.transform.lossyScale.x * box.size.z * box.transform.lossyScale.z);
                Footprints.Add(new FootprintSnapshot(
                    FootprintKind.OrientedBox,
                    0,
                    0,
                    0f,
                    0f,
                    0f,
                    box.transform.worldToLocalMatrix,
                    center.x - half.x,
                    center.x + half.x,
                    center.z - half.z,
                    center.z + half.z,
                    area));
                return;
            }

            AddUnsupportedZone();
        }

        internal static float GetFirstContactDepthClipDistanceWorldParallelSafe(
            in CombatForestFogLutBuildContext ctx,
            Vector3 planarDirection)
        {
            if (ctx.MaxSearchRadius <= 0.001f || ctx.DepthWorld <= 0.001f || !ctx.HasForest || !AllZonesThreadSafe)
            {
                return ctx.MaxSearchRadius;
            }

            planarDirection.y = 0f;
            if (planarDirection.sqrMagnitude <= 1e-8f)
            {
                return ctx.MaxSearchRadius;
            }

            if (!ctx.RayStartedInsideForest
                && !CombatForestFogClipper.RayMayHitAnyCachedZoneAabbPublic(
                    ctx.FlatEye,
                    planarDirection,
                    ctx.MaxSearchRadius))
            {
                return ctx.MaxSearchRadius;
            }

            return ComputeFirstContactDepthClipCandidateCached(
                ctx.FlatEye,
                planarDirection,
                ctx.MaxSearchRadius,
                ctx.DepthWorld,
                ctx.RayStartedInsideForest,
                ctx.OriginRadiusWorld);
        }

        private static float ComputeFirstContactDepthClipCandidateCached(
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
                && !CombatForestFogClipper.RayMayHitAnyCachedZoneAabbPublic(origin, planarDirection, maxDistanceWorld))
            {
                return maxDistanceWorld;
            }

            if (eyeInsideForest)
            {
                var exitFromUnit = FindForestExitDistanceAlongRayCached(
                    origin,
                    originRadius,
                    planarDirection,
                    maxDistanceWorld);

                if (exitFromUnit < 0f)
                {
                    return TryFinalizeClipDistanceCached(
                        origin,
                        planarDirection,
                        depthLimitWorld,
                        maxDistanceWorld,
                        exitFromContact: -1f);
                }

                if (exitFromUnit > depthLimitWorld + thinForestEpsilon)
                {
                    return TryFinalizeClipDistanceCached(
                        origin,
                        planarDirection,
                        depthLimitWorld,
                        maxDistanceWorld,
                        exitFromUnit);
                }

                var originZoneIndex = TryGetZoneAtExitCrossingCached(origin, planarDirection, exitFromUnit, maxDistanceWorld);
                if (originZoneIndex < 0)
                {
                    originZoneIndex = TryGetInnermostClipZoneIndexAt(origin);
                }

                cursor = exitFromUnit + advanceEpsilon;
                eyeInsideForest = false;

                var nextEntry = FindNextForestEntryDistanceCached(
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
                    var nextZoneIndex = TryGetInnermostClipZoneIndexAt(nextProbe);

                    if (originZoneIndex >= 0
                        && nextZoneIndex >= 0
                        && originZoneIndex != nextZoneIndex)
                    {
                        return TryFinalizeClipDistanceCached(
                            origin,
                            planarDirection,
                            exitFromUnit,
                            maxDistanceWorld,
                            exitFromUnit);
                    }

                    if (originZoneIndex >= 0 && nextZoneIndex == originZoneIndex)
                    {
                        return maxDistanceWorld;
                    }
                }
            }

            while (cursor < maxDistanceWorld - advanceEpsilon)
            {
                var entryDistance = FindNextForestEntryDistanceCached(
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
                if (!IsInsideClipZoneForClip(probeStart))
                {
                    var nudgeDistance = entryDistance + startInsideEpsilon;
                    for (var attempt = 0; attempt < 6; attempt++)
                    {
                        if (IsInsideClipZoneForClip(probeStart))
                        {
                            break;
                        }

                        nudgeDistance += CombatScale.InchesToWorldUnits(0.05f);
                        probeStart = origin + planarDirection * Mathf.Min(maxDistanceWorld, nudgeDistance);
                    }
                }

                if (!IsInsideClipZoneForClip(probeStart))
                {
                    probeStart = origin + planarDirection * entryDistance;
                }

                probeStart.y = 0f;
                var exitFromEntry = FindFirstOutsideDistanceFromInsideCached(
                    probeStart,
                    planarDirection,
                    remainingFromEntry);

                var entryZoneIndex = TryGetInnermostClipZoneIndexAt(probeStart);
                var zoneDepthLimit = GetCachedZoneDepthLimitWorld(entryZoneIndex, depthLimitWorld);
                var isBlockingEntry = IsBlockingClipZoneCached(entryZoneIndex);

                if (exitFromEntry < 0f)
                {
                    var surroundedClip = Mathf.Min(maxDistanceWorld, entryDistance + zoneDepthLimit);
                    return TryFinalizeClipDistanceCached(
                        origin,
                        planarDirection,
                        surroundedClip,
                        maxDistanceWorld,
                        exitFromContact: -1f,
                        failClosed: isBlockingEntry);
                }

                var outsideEntryClip = ComputeOutsideEntryClipDistance(
                    entryDistance,
                    exitFromEntry,
                    zoneDepthLimit,
                    maxDistanceWorld);
                return TryFinalizeClipDistanceCached(
                    origin,
                    planarDirection,
                    outsideEntryClip,
                    maxDistanceWorld,
                    exitFromEntry,
                    failClosed: isBlockingEntry);
            }

            return maxDistanceWorld;
        }

        private static float FindForestExitDistanceAlongRayCached(
            Vector3 origin,
            float originRadius,
            Vector3 planarDirection,
            float maxDistanceWorld)
        {
            origin.y = 0f;
            if (!IsInsideLimitedDepthForestCached(origin, originRadius))
            {
                return -1f;
            }

            return FindFirstOutsideDistanceFromInsideCached(origin, planarDirection, maxDistanceWorld);
        }

        private static float FindNextForestEntryDistanceCached(
            Vector3 origin,
            Vector3 planarDirection,
            float searchStart,
            float maxDistanceWorld,
            float originRadius = 0f)
        {
            origin.y = 0f;
            originRadius = Mathf.Max(0f, originRadius);
            if (searchStart <= 0.001f && IsInsideLimitedDepthForestCached(origin, originRadius))
            {
                return 0f;
            }

            var analytic = TryFindNextForestEntryDistanceAnalyticCached(
                origin,
                planarDirection,
                searchStart,
                maxDistanceWorld);
            if (analytic >= 0f)
            {
                return analytic;
            }

            return MarchFirstForestEntryDistanceCached(
                origin,
                planarDirection,
                searchStart,
                maxDistanceWorld);
        }

        private static float MarchFirstForestEntryDistanceCached(
            Vector3 origin,
            Vector3 planarDirection,
            float searchStart,
            float maxDistanceWorld)
        {
            var sampleAtStart = origin + planarDirection * searchStart;
            if (IsInsideClipZoneForClip(sampleAtStart))
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
                if (IsInsideClipZoneForClip(samplePoint))
                {
                    return RefineFirstContactDistanceCached(
                        origin,
                        planarDirection,
                        previousDistance,
                        nextDistance);
                }

                previousDistance = distance;
                distance = nextDistance;
            }

            return -1f;
        }

        private static float RefineFirstContactDistanceCached(
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
                if (IsInsideClipZoneForClip(sample))
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

        private static float RefineBoundaryDistanceCached(
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
                var inside = IsInsideClipZoneForClip(sample);
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

            return high;
        }

        private static float TryFindNextForestEntryDistanceAnalyticCached(
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
            var zoneCount = ActiveFootprintCount;

            for (var z = 0; z < zoneCount; z++)
            {
                if (!CombatForestFogClipper.IsCachedZoneActiveForCurrentClipPass(z))
                {
                    continue;
                }

                if (TryFindCachedForestZoneBoundaryDistance(
                        z,
                        origin,
                        planarDirection,
                        CombatForestFogClipper.ForestZoneBoundaryKind.Entry,
                        searchStart,
                        maxDistanceWorld,
                        transitionEpsilon,
                        ref best))
                {
                    continue;
                }

                if (!TryGetPolygonVertexRange(z, out var polyStart, out var polyCount) || polyCount < 3)
                {
                    continue;
                }

                for (var i = 0; i < polyCount; i++)
                {
                    var a2 = ActivePolygonVertices[polyStart + i];
                    var b2 = ActivePolygonVertices[polyStart + WrapPolygonVertexIndex(i, polyCount)];
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
                    if (!CachedZoneContainsPoint(z, before) && CachedZoneContainsPoint(z, after))
                    {
                        best = hitT;
                    }
                }
            }

            return best < float.MaxValue ? best : -1f;
        }

        private static float FindFirstOutsideDistanceFromInsideCached(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld)
        {
            var analytic = TryFindFirstOutsideDistanceAnalyticCached(origin, planarDirection, maxDistanceWorld);
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
                if (!IsInsideClipZoneForClip(samplePoint))
                {
                    return RefineBoundaryDistanceCached(
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

        private static float TryFindFirstOutsideDistanceAnalyticCached(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld)
        {
            if (!IsInsideClipZoneForClip(origin))
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
            var zoneCount = ActiveFootprintCount;

            for (var z = 0; z < zoneCount; z++)
            {
                if (!CombatForestFogClipper.IsCachedZoneActiveForCurrentClipPass(z))
                {
                    continue;
                }

                if (TryFindCachedForestZoneBoundaryDistance(
                        z,
                        origin,
                        planarDirection,
                        CombatForestFogClipper.ForestZoneBoundaryKind.Exit,
                        searchStart: 0f,
                        maxDistanceWorld,
                        transitionEpsilon,
                        ref best))
                {
                    continue;
                }

                if (!TryGetPolygonVertexRange(z, out var polyStart, out var polyCount) || polyCount < 3)
                {
                    continue;
                }

                for (var i = 0; i < polyCount; i++)
                {
                    var a2 = ActivePolygonVertices[polyStart + i];
                    var b2 = ActivePolygonVertices[polyStart + WrapPolygonVertexIndex(i, polyCount)];
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
                    if (CachedZoneContainsPoint(z, before) && !CachedZoneContainsPoint(z, after))
                    {
                        best = hitT;
                    }
                }
            }

            return best < float.MaxValue ? best : -1f;
        }

        private static bool TryFindCachedForestZoneBoundaryDistance(
            int zoneIndex,
            Vector3 origin,
            Vector3 planarDirection,
            CombatForestFogClipper.ForestZoneBoundaryKind kind,
            float searchStart,
            float maxDistanceWorld,
            float transitionEpsilon,
            ref float best)
        {
            if (kind == CombatForestFogClipper.ForestZoneBoundaryKind.Exit && !CachedZoneContainsPoint(zoneIndex, origin))
            {
                return true;
            }

            if (!TryGetCachedZoneRayInterval(zoneIndex, origin, planarDirection, out var enterT, out var exitT))
            {
                return false;
            }

            var hitT = kind == CombatForestFogClipper.ForestZoneBoundaryKind.Entry ? enterT : exitT;
            if (kind == CombatForestFogClipper.ForestZoneBoundaryKind.Entry)
            {
                if (hitT < searchStart - transitionEpsilon || hitT >= best || hitT > maxDistanceWorld)
                {
                    return true;
                }

                var before = origin + planarDirection * Mathf.Max(0f, hitT - transitionEpsilon);
                var after = origin + planarDirection * Mathf.Min(maxDistanceWorld, hitT + transitionEpsilon);
                if (!CachedZoneContainsPoint(zoneIndex, before) && CachedZoneContainsPoint(zoneIndex, after))
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
            if (CachedZoneContainsPoint(zoneIndex, exitBefore) && !CachedZoneContainsPoint(zoneIndex, exitAfter))
            {
                best = hitT;
            }

            return true;
        }

        private static int TryGetZoneAtExitCrossingCached(
            Vector3 origin,
            Vector3 planarDirection,
            float exitDistance,
            float maxDistanceWorld)
        {
            if (exitDistance <= 0.001f)
            {
                return -1;
            }

            origin.y = 0f;
            planarDirection.y = 0f;
            if (planarDirection.sqrMagnitude <= 1e-8f)
            {
                return -1;
            }

            planarDirection.Normalize();
            var transitionEpsilon = CombatScale.InchesToWorldUnits(0.02f);
            var bestExitDelta = float.MaxValue;
            var match = -1;
            var zoneCount = ActiveFootprintCount;

            for (var z = 0; z < zoneCount; z++)
            {
                if (!CombatForestFogClipper.IsCachedZoneActiveForCurrentClipPass(z))
                {
                    continue;
                }

                if (!TryGetCachedZoneRayInterval(z, origin, planarDirection, out _, out var exitT))
                {
                    continue;
                }

                var exitDelta = Mathf.Abs(exitT - exitDistance);
                if (exitDelta > transitionEpsilon || exitDelta >= bestExitDelta)
                {
                    continue;
                }

                var before = origin + planarDirection * Mathf.Max(0f, exitDistance - transitionEpsilon);
                var after = origin + planarDirection * Mathf.Min(maxDistanceWorld, exitDistance + transitionEpsilon);
                if (CachedZoneContainsPoint(z, before) && !CachedZoneContainsPoint(z, after))
                {
                    bestExitDelta = exitDelta;
                    match = z;
                }
            }

            return match;
        }

        private static int TryGetInnermostClipZoneIndexAt(Vector3 worldPoint)
        {
            var bestIndex = -1;
            var bestArea = float.MaxValue;
            var zoneCount = ActiveFootprintCount;
            for (var i = 0; i < zoneCount; i++)
            {
                if (!CombatForestFogClipper.IsCachedZoneActiveForCurrentClipPass(i))
                {
                    continue;
                }

                if (!CachedZoneContainsPoint(i, worldPoint))
                {
                    continue;
                }

                var area = GetFootprintAt(i).FootprintAreaWorld;
                if (area <= 1e-8f)
                {
                    return i;
                }

                if (area < bestArea)
                {
                    bestArea = area;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static float GetCachedZoneDepthLimitWorld(int zoneIndex, float fallbackDepthLimitWorld)
        {
            return zoneIndex >= 0
                ? CombatForestFogClipper.GetCachedZoneDepthLimitWorld(zoneIndex, fallbackDepthLimitWorld)
                : fallbackDepthLimitWorld;
        }

        private static bool IsBlockingClipZoneCached(int zoneIndex)
        {
            return zoneIndex >= 0 && CombatForestFogClipper.IsCachedBlockingClipZone(zoneIndex);
        }

        private static bool IsInsideLimitedDepthForestCached(Vector3 worldPoint, float radius = 0f)
        {
            worldPoint.y = 0f;
            radius = Mathf.Max(0f, radius);
            var zoneCount = ActiveFootprintCount;
            for (var i = 0; i < zoneCount; i++)
            {
                if (!CombatForestFogClipper.IsCachedLimitedDepthZone(i))
                {
                    continue;
                }

                if (!CombatForestFogClipper.IsCachedZoneActiveForCurrentClipPass(i))
                {
                    continue;
                }

                if (radius <= 0.001f)
                {
                    if (CachedZoneContainsPoint(i, worldPoint))
                    {
                        return true;
                    }
                }
                else if (CachedZoneIntersectsDisc(i, worldPoint, radius))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsInsideClipZoneForClip(Vector3 worldPoint)
        {
            var zoneCount = ActiveFootprintCount;
            for (var i = 0; i < zoneCount; i++)
            {
                if (!CombatForestFogClipper.IsCachedZoneActiveForCurrentClipPass(i))
                {
                    continue;
                }

                if (CachedZoneContainsPoint(i, worldPoint))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CachedZoneContainsPoint(int zoneIndex, Vector3 worldPoint)
        {
            if (zoneIndex < 0 || zoneIndex >= ActiveFootprintCount)
            {
                return false;
            }

            var snapshot = GetFootprintAt(zoneIndex);
            var point2 = new Vector2(worldPoint.x, worldPoint.z);
            switch (snapshot.Kind)
            {
                case FootprintKind.Polygon:
                    if (!TryGetPolygonVertexRange(zoneIndex, out var polyStart, out var polyCount))
                    {
                        return false;
                    }

                    return CombatPolygonFootprintGeometry.ContainsPointLocal(
                        point2,
                        ActivePolygonVertices,
                        polyStart,
                        polyCount);
                case FootprintKind.Disc:
                    var dx = point2.x - snapshot.DiscCenterX;
                    var dz = point2.y - snapshot.DiscCenterZ;
                    return dx * dx + dz * dz <= snapshot.DiscRadius * snapshot.DiscRadius + 1e-8f;
                case FootprintKind.OrientedBox:
                    var local = snapshot.WorldToLocal.MultiplyPoint3x4(worldPoint);
                    return local.x >= snapshot.LocalBoxMinX - 1e-5f
                        && local.x <= snapshot.LocalBoxMaxX + 1e-5f
                        && local.z >= snapshot.LocalBoxMinZ - 1e-5f
                        && local.z <= snapshot.LocalBoxMaxZ + 1e-5f;
                default:
                    return false;
            }
        }

        private static bool CachedZoneIntersectsDisc(int zoneIndex, Vector3 center, float radius)
        {
            if (radius <= 0.001f)
            {
                return CachedZoneContainsPoint(zoneIndex, center);
            }

            if (CachedZoneContainsPoint(zoneIndex, center))
            {
                return true;
            }

            const int sampleCount = 8;
            for (var i = 0; i < sampleCount; i++)
            {
                var angle = (Mathf.PI * 2f * i) / sampleCount;
                var edgePoint = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                if (CachedZoneContainsPoint(zoneIndex, edgePoint))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool TryGetCachedZoneRayInterval(
            int zoneIndex,
            Vector3 origin,
            Vector3 planarDirection,
            out float enterWorld,
            out float exitWorld)
        {
            enterWorld = -1f;
            exitWorld = -1f;
            if (zoneIndex < 0 || zoneIndex >= ActiveFootprintCount)
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
            var snapshot = GetFootprintAt(zoneIndex);
            switch (snapshot.Kind)
            {
                case FootprintKind.Polygon:
                    if (!TryGetPolygonVertexRange(zoneIndex, out var polyStart, out var polyCount))
                    {
                        return false;
                    }

                    return CombatPolygonFootprintGeometry.TryRayPolygonIntervalLocal(
                        new Vector2(origin.x, origin.z),
                        new Vector2(planarDirection.x, planarDirection.z),
                        ActivePolygonVertices,
                        polyStart,
                        polyCount,
                        out enterWorld,
                        out exitWorld);
                case FootprintKind.Disc:
                    return CombatFogPlanarGeometry.TryRayDiscInterval(
                        new Vector2(origin.x, origin.z),
                        new Vector2(planarDirection.x, planarDirection.z),
                        new Vector2(snapshot.DiscCenterX, snapshot.DiscCenterZ),
                        snapshot.DiscRadius,
                        out enterWorld,
                        out exitWorld);
                case FootprintKind.OrientedBox:
                    var localOrigin = snapshot.WorldToLocal.MultiplyPoint3x4(origin);
                    var localDirection = snapshot.WorldToLocal.MultiplyVector(planarDirection);
                    localOrigin.y = 0f;
                    localDirection.y = 0f;
                    if (localDirection.sqrMagnitude <= 1e-12f)
                    {
                        return false;
                    }

                    var localMin = new Vector3(snapshot.LocalBoxMinX, 0f, snapshot.LocalBoxMinZ);
                    var localMax = new Vector3(snapshot.LocalBoxMaxX, 0f, snapshot.LocalBoxMaxZ);
                    return CombatFogPlanarGeometry.TryRayAabbInterval(
                        localOrigin,
                        localDirection,
                        localMin,
                        localMax,
                        out enterWorld,
                        out exitWorld);
                default:
                    return false;
            }
        }

        private static bool TryGetPolygonVertexRange(int zoneIndex, out int start, out int count)
        {
            start = 0;
            count = 0;
            if (zoneIndex < 0 || zoneIndex >= ActiveFootprintCount)
            {
                return false;
            }

            var snapshot = GetFootprintAt(zoneIndex);
            if (snapshot.Kind != FootprintKind.Polygon || snapshot.PolygonCount < 3)
            {
                return false;
            }

            start = snapshot.PolygonStart;
            count = snapshot.PolygonCount;
            if (start < 0 || count < 3 || start + count > ActivePolygonVertices.Count)
            {
                start = 0;
                count = 0;
                return false;
            }

            return true;
        }

        private static float TryFinalizeClipDistanceCached(
            Vector3 origin,
            Vector3 planarDirection,
            float clipDistance,
            float maxDistanceWorld,
            float exitFromContact,
            bool failClosed = false)
        {
            if (clipDistance >= maxDistanceWorld - 0.001f)
            {
                return maxDistanceWorld;
            }

            if (failClosed)
            {
                return clipDistance;
            }

            var clipPoint = origin + planarDirection * clipDistance;
            var candidateInside = IsInsideCandidateNeighborhoodCached(clipPoint, planarDirection);
            if (!candidateInside && exitFromContact < 0f)
            {
                return maxDistanceWorld;
            }

            if (clipDistance < maxDistanceWorld - 0.001f)
            {
                var verificationMargin = CombatScale.InchesToWorldUnits(0.05f);
                var verifyDistance = Mathf.Min(maxDistanceWorld, clipDistance + verificationMargin);
                var verifyPoint = origin + planarDirection * verifyDistance;
                var verifyInside = IsInsideCandidateNeighborhoodCached(verifyPoint, planarDirection);
                if (!verifyInside && exitFromContact < 0f)
                {
                    return maxDistanceWorld;
                }
            }

            return clipDistance;
        }

        private static bool IsInsideCandidateNeighborhoodCached(Vector3 point, Vector3 planarDirection)
        {
            if (IsInsideClipZoneForClip(point))
            {
                return true;
            }

            var radius = CombatScale.InchesToWorldUnits(0.2f);
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
            var offsets = new[] { -1f, -0.5f, 0.5f, 1f };
            for (var i = 0; i < offsets.Length; i++)
            {
                var samplePoint = point + perpendicular * (offsets[i] * radius);
                sampleCount++;
                if (IsInsideClipZoneForClip(samplePoint))
                {
                    hits++;
                }
            }

            return hits * 2 >= sampleCount;
        }

        private static float ComputeOutsideEntryClipDistance(
            float entryDistance,
            float exitFromEntry,
            float depthLimitWorld,
            float maxDistanceWorld)
        {
            var passThroughClip = Mathf.Min(maxDistanceWorld, entryDistance + depthLimitWorld);
            if (exitFromEntry < 0f)
            {
                return passThroughClip;
            }

            var absoluteExit = entryDistance + exitFromEntry;
            return Mathf.Min(passThroughClip, absoluteExit);
        }
    }
}
