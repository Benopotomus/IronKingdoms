using System;
using System.Collections.Generic;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Builds a BG3-style discrete LOS cell mask: angular vision rays per observer, then
    /// rasterized onto an inch-based ground grid.
    /// </summary>
    public sealed class CombatLosGridSampler
    {
        public const float DefaultCellSizeInches = 1f;
        public const int DefaultRayCount = 96;
        public const float DefaultProbeHeightInches = 1.75f;

        private readonly List<float> rayDistancesWorld = new(DefaultRayCount);
        private readonly List<ObserverVision> observers = new(8);
        private readonly RaycastHit[] raycastBuffer = new RaycastHit[24];
        private readonly HashSet<long> visibleCells = new();

        public float CellSizeInches { get; set; } = DefaultCellSizeInches;
        public int RayCount { get; set; } = DefaultRayCount;
        public IReadOnlyCollection<long> VisibleCells => visibleCells;

        public void Clear()
        {
            visibleCells.Clear();
            observers.Clear();
            rayDistancesWorld.Clear();
        }

        /// <summary>
        /// Samples enemy (or other observer) vision into <see cref="VisibleCells"/>.
        /// Cell keys pack XZ indices via <see cref="PackCellKey"/>.
        /// </summary>
        public void Rebuild(
            IReadOnlyList<Unit> visionObservers,
            IReadOnlyList<Unit> allUnits,
            Func<Vector3, Vector3, float, int, float> sampleWallDistanceWorld,
            float groundY = 0f)
        {
            Clear();
            if (visionObservers == null
                || visionObservers.Count == 0
                || CellSizeInches <= 0.01f
                || RayCount < 8
                || sampleWallDistanceWorld == null)
            {
                return;
            }

            var cellSizeWorld = CombatScale.InchesToWorldUnits(CellSizeInches);
            var probeHeightWorld = CombatScale.InchesToWorldUnits(DefaultProbeHeightInches);
            var probeRadiusWorld = cellSizeWorld * 0.35f;

            for (var i = 0; i < visionObservers.Count; i++)
            {
                var observer = visionObservers[i];
                if (observer == null || !observer.IsAlive || observer.Pawn == null || observer.Definition == null)
                {
                    continue;
                }

                var observerVolume = observer.GetLineOfSightVolume();
                var maxRangeInches = Mathf.Max(0.1f, observer.Definition.Stats.visibilityRange);
                var maxRangeWorld = CombatScale.InchesToWorldUnits(maxRangeInches) + observerVolume.Radius;
                if (maxRangeWorld <= 0.01f)
                {
                    continue;
                }

                CollectInterveningVolumes(observer, allUnits, out var intervening);
                SampleObserverRays(
                    observer,
                    observerVolume,
                    intervening,
                    maxRangeWorld,
                    sampleWallDistanceWorld,
                    rayDistancesWorld);

                observers.Add(new ObserverVision(
                    observerVolume.Position,
                    observerVolume.Radius,
                    maxRangeWorld,
                    rayDistancesWorld.ToArray()));
            }

            if (observers.Count == 0)
            {
                return;
            }

            RasterizeObserversToCells(cellSizeWorld, probeRadiusWorld, probeHeightWorld, groundY);
        }

        public static long PackCellKey(int cellX, int cellZ)
        {
            return ((long)cellX << 32) ^ (uint)cellZ;
        }

        public static void UnpackCellKey(long key, out int cellX, out int cellZ)
        {
            cellX = (int)(key >> 32);
            cellZ = (int)(key & 0xffffffffL);
        }

        public static Vector3 CellCenterWorld(int cellX, int cellZ, float cellSizeWorld, float groundY)
        {
            return new Vector3(
                (cellX + 0.5f) * cellSizeWorld,
                groundY,
                (cellZ + 0.5f) * cellSizeWorld);
        }

        private void RasterizeObserversToCells(
            float cellSizeWorld,
            float probeRadiusWorld,
            float probeHeightWorld,
            float groundY)
        {
            _ = probeRadiusWorld;
            _ = probeHeightWorld;

            var minX = float.PositiveInfinity;
            var maxX = float.NegativeInfinity;
            var minZ = float.PositiveInfinity;
            var maxZ = float.NegativeInfinity;

            for (var i = 0; i < observers.Count; i++)
            {
                var observer = observers[i];
                var reach = observer.MaxRangeWorld;
                minX = Mathf.Min(minX, observer.Position.x - reach);
                maxX = Mathf.Max(maxX, observer.Position.x + reach);
                minZ = Mathf.Min(minZ, observer.Position.z - reach);
                maxZ = Mathf.Max(maxZ, observer.Position.z + reach);
            }

            var minCellX = Mathf.FloorToInt(minX / cellSizeWorld);
            var maxCellX = Mathf.FloorToInt(maxX / cellSizeWorld);
            var minCellZ = Mathf.FloorToInt(minZ / cellSizeWorld);
            var maxCellZ = Mathf.FloorToInt(maxZ / cellSizeWorld);

            for (var cellX = minCellX; cellX <= maxCellX; cellX++)
            {
                for (var cellZ = minCellZ; cellZ <= maxCellZ; cellZ++)
                {
                    var center = CellCenterWorld(cellX, cellZ, cellSizeWorld, groundY);
                    if (IsCellVisibleToAnyObserver(center))
                    {
                        visibleCells.Add(PackCellKey(cellX, cellZ));
                    }
                }
            }
        }

        private bool IsCellVisibleToAnyObserver(Vector3 cellCenter)
        {
            for (var i = 0; i < observers.Count; i++)
            {
                if (IsCellVisibleToObserver(observers[i], cellCenter))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCellVisibleToObserver(in ObserverVision observer, Vector3 cellCenter)
        {
            var delta = cellCenter - observer.Position;
            delta.y = 0f;
            var planarDistance = delta.magnitude;
            var edgeDistance = Mathf.Max(0f, planarDistance - observer.Radius);
            if (edgeDistance > observer.MaxRangeWorld + 0.001f)
            {
                return false;
            }

            if (planarDistance <= observer.Radius + 0.001f)
            {
                return true;
            }

            var angle = Mathf.Atan2(delta.z, delta.x);
            if (angle < 0f)
            {
                angle += Mathf.PI * 2f;
            }

            var visionDistance = SamplePolarDistance(observer.RayDistancesWorld, angle);
            return planarDistance <= visionDistance + 0.001f;
        }

        public static float SamplePolarDistance(IReadOnlyList<float> rayDistancesWorld, float angleRadians)
        {
            if (rayDistancesWorld == null || rayDistancesWorld.Count == 0)
            {
                return 0f;
            }

            var count = rayDistancesWorld.Count;
            var step = (Mathf.PI * 2f) / count;
            var normalized = angleRadians / step;
            var index0 = Mathf.FloorToInt(normalized) % count;
            if (index0 < 0)
            {
                index0 += count;
            }

            var index1 = (index0 + 1) % count;
            var t = normalized - Mathf.Floor(normalized);
            return Mathf.Lerp(rayDistancesWorld[index0], rayDistancesWorld[index1], t);
        }

        private void SampleObserverRays(
            Unit observer,
            CombatLineOfSightVolume observerVolume,
            List<CombatLineOfSightVolume> intervening,
            float maxRangeWorld,
            Func<Vector3, Vector3, float, int, float> sampleWallDistanceWorld,
            List<float> distances)
        {
            distances.Clear();
            var eye = observerVolume.SightPoint;
            var originGround = observerVolume.Position;
            var originRadius = observerVolume.Radius;
            var layerMask = CombatLayers.LineOfSightBlockerMask;

            for (var ray = 0; ray < RayCount; ray++)
            {
                var angle = (ray / (float)RayCount) * Mathf.PI * 2f;
                var planarDir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var limit = maxRangeWorld;

                var wallDistance = sampleWallDistanceWorld(eye, planarDir, maxRangeWorld, layerMask);
                if (wallDistance < limit)
                {
                    limit = wallDistance;
                }

                var forestClip = CombatTerrainLineOfSight.GetLimitedDepthFogClipDistanceWorld(
                    originGround,
                    planarDir,
                    maxRangeWorld,
                    originRadius,
                    observer.Definition,
                    observer.Pawn);
                if (forestClip < limit)
                {
                    limit = forestClip;
                }

                var cloudClip = CombatBlockingTerrainClipper.GetFogClipDistanceWorld(
                    originGround,
                    planarDir,
                    maxRangeWorld,
                    originRadius);
                if (cloudClip < limit)
                {
                    limit = cloudClip;
                }

                var interveningClip = SampleInterveningModelDistance(originGround, planarDir, maxRangeWorld, intervening);
                if (interveningClip < limit)
                {
                    limit = interveningClip;
                }

                distances.Add(Mathf.Max(0f, limit));
            }
        }

        private static void CollectInterveningVolumes(
            Unit observer,
            IReadOnlyList<Unit> allUnits,
            out List<CombatLineOfSightVolume> intervening)
        {
            intervening = new List<CombatLineOfSightVolume>();
            if (allUnits == null)
            {
                return;
            }

            for (var i = 0; i < allUnits.Count; i++)
            {
                var candidate = allUnits[i];
                if (candidate == null
                    || !candidate.IsAlive
                    || candidate.Pawn == null
                    || ReferenceEquals(candidate, observer))
                {
                    continue;
                }

                intervening.Add(candidate.GetLineOfSightVolume());
            }
        }

        private static float SampleInterveningModelDistance(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld,
            List<CombatLineOfSightVolume> intervening)
        {
            if (intervening == null || intervening.Count == 0)
            {
                return maxDistanceWorld;
            }

            var origin2 = new Vector2(origin.x, origin.z);
            var dir2 = new Vector2(planarDirection.x, planarDirection.z);
            if (dir2.sqrMagnitude <= 1e-8f)
            {
                return maxDistanceWorld;
            }

            dir2.Normalize();
            var closest = maxDistanceWorld;
            for (var i = 0; i < intervening.Count; i++)
            {
                var volume = intervening[i];
                if (!CombatFogPlanarGeometry.TryRayDiscInterval(
                        origin2,
                        dir2,
                        new Vector2(volume.Position.x, volume.Position.z),
                        volume.Radius,
                        out var enter,
                        out _))
                {
                    continue;
                }

                if (enter >= 0f && enter < closest)
                {
                    closest = enter;
                }
            }

            return closest;
        }

        /// <summary>
        /// Default wall sampler using non-alloc physics raycasts at eye height.
        /// </summary>
        public float SampleWallDistanceWorld(
            Vector3 eyeWorld,
            Vector3 planarDirection,
            float maxDistanceWorld,
            int layerMask)
        {
            if (maxDistanceWorld <= 0.001f)
            {
                return 0f;
            }

            var direction = planarDirection;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 1e-8f)
            {
                return maxDistanceWorld;
            }

            direction.Normalize();
            var hitCount = Physics.RaycastNonAlloc(
                eyeWorld,
                direction,
                raycastBuffer,
                maxDistanceWorld,
                layerMask,
                QueryTriggerInteraction.Ignore);

            var closest = maxDistanceWorld;
            for (var i = 0; i < hitCount; i++)
            {
                var hit = raycastBuffer[i];
                if (hit.collider == null)
                {
                    continue;
                }

                // Project hit onto the planar ray so wall distance matches grid math.
                var hitDelta = hit.point - eyeWorld;
                hitDelta.y = 0f;
                var planarHit = Vector3.Dot(hitDelta, direction);
                if (planarHit >= 0f && planarHit < closest)
                {
                    closest = planarHit;
                }
            }

            return closest;
        }

        private readonly struct ObserverVision
        {
            public ObserverVision(Vector3 position, float radius, float maxRangeWorld, float[] rayDistancesWorld)
            {
                Position = position;
                Radius = radius;
                MaxRangeWorld = maxRangeWorld;
                RayDistancesWorld = rayDistancesWorld;
            }

            public Vector3 Position { get; }
            public float Radius { get; }
            public float MaxRangeWorld { get; }
            public float[] RayDistancesWorld { get; }
        }
    }
}
