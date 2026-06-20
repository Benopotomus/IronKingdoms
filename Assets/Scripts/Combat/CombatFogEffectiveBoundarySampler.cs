using System.Collections.Generic;
using FOW;
using Unity.Mathematics;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Authoritative effective fog boundary: baseline wall polygon + analytic forest/cloud clip per direction.
    /// Yellow debug reads the GPU upload from the same LOS pass (no second LUT build).
    /// </summary>
    internal static class CombatFogEffectiveBoundarySampler
    {
        private const float DistanceEpsilonWorld = 0.01f;

        public static int ContourSampleCount => CombatForestFogAngularClipperLut.SampleCount;

        private static readonly List<RaycastRevealer.SightSegment> BaselineSegmentsScratch = new();

        /// <summary>
        /// Terrain LUT bins uploaded to the fog shader — matches yellow contour resolution when budget allows.
        /// </summary>
        public static int ResolveTerrainLutUploadCount(
            int baselineSegmentCount,
            int maxUploadSegments,
            int desiredSampleCount = -1)
        {
            var terrainBudget = math.max(0, maxUploadSegments - baselineSegmentCount);
            var desiredCount = math.min(
                desiredSampleCount > 0 ? desiredSampleCount : ContourSampleCount,
                CombatForestFogPassSettings.MaxShaderLutSamples);
            desiredCount = math.min(desiredCount, ContourSampleCount);
            return math.min(desiredCount, terrainBudget);
        }

        public static void BuildEffectiveFogBoundaryContour(
            CombatFogOfWarRevealer3D revealer,
            Vector3 eyeWorld,
            Vector3 originGround,
            float maxSearchRadius,
            float originRadiusWorld,
            bool applyForestClip,
            bool applyBlockingClip,
            FogOfWarRevealer3D.PlaneProjection projection,
            List<Vector3> contourPoints)
        {
            contourPoints.Clear();

            if (maxSearchRadius <= 0f || revealer == null)
            {
                return;
            }

            var circleIsComplete = Mathf.Approximately(revealer.ViewAngle, 360f);
            var extraRadius = FogOfWarWorld.instance != null
                ? FogOfWarWorld.instance.SightExtraAmount
                : 0f;

            BuildBaselineUploadSegments(
                revealer.OutputDirections,
                revealer.OutputDistances,
                revealer.NumberOfPoints,
                maxSearchRadius,
                BaselineSegmentsScratch);

            var terrainDistances = revealer.TerrainClipUploadDistances;
            var terrainCount = revealer.TerrainClipUploadSegmentCount;
            var useUploadedTerrain = terrainCount >= 2
                && terrainDistances != null
                && terrainDistances.Length >= terrainCount;

            if (!useUploadedTerrain && (applyForestClip || applyBlockingClip))
            {
                BuildAndUploadFallbackContour(
                    revealer,
                    eyeWorld,
                    originGround,
                    maxSearchRadius,
                    originRadiusWorld,
                    applyForestClip,
                    applyBlockingClip,
                    projection,
                    circleIsComplete,
                    extraRadius,
                    contourPoints);
                return;
            }

            var sampleCount = useUploadedTerrain
                ? terrainCount
                : ContourSampleCount;

            for (var i = 0; i < sampleCount; i++)
            {
                var queryDir = CombatForestFogAngularTables.GetDirection2D(i, sampleCount);
                var direction = useUploadedTerrain
                    ? CombatFogProjection.Direction2DToWorld(queryDir, projection)
                    : CombatForestFogAngularTables.GetDirectionWorldXZ(i, sampleCount);

                var terrainClip = useUploadedTerrain
                    ? terrainDistances[i]
                    : maxSearchRadius;
                var limit = SampleEffectiveBoundaryDistance(
                    BaselineSegmentsScratch,
                    queryDir,
                    maxSearchRadius,
                    extraRadius,
                    circleIsComplete,
                    terrainClip);

                contourPoints.Add(originGround + (direction * limit));
            }
        }

        private static void BuildAndUploadFallbackContour(
            CombatFogOfWarRevealer3D revealer,
            Vector3 eyeWorld,
            Vector3 originGround,
            float maxSearchRadius,
            float originRadiusWorld,
            bool applyForestClip,
            bool applyBlockingClip,
            FogOfWarRevealer3D.PlaneProjection projection,
            bool circleIsComplete,
            float extraRadius,
            List<Vector3> contourPoints)
        {
            var scratch = new float[ContourSampleCount];
            CombatForestFogAngularClipperLut.BuildSmoothedClipDistances(
                eyeWorld,
                maxSearchRadius,
                originRadiusWorld,
                projection,
                scratch,
                ContourSampleCount,
                applyForestClip,
                applyBlockingClip);

            for (var i = 0; i < ContourSampleCount; i++)
            {
                var queryDir = CombatForestFogAngularTables.Directions2D[i];
                var direction = CombatForestFogAngularTables.DirectionsWorldXZ[i];
                var limit = SampleEffectiveBoundaryDistance(
                    BaselineSegmentsScratch,
                    queryDir,
                    maxSearchRadius,
                    extraRadius,
                    circleIsComplete,
                    scratch[i]);
                contourPoints.Add(originGround + (direction * limit));
            }
        }

        private static float SampleEffectiveBoundaryDistance(
            IReadOnlyList<RaycastRevealer.SightSegment> baselineSegments,
            float2 queryDir,
            float maxSearchRadius,
            float extraRadius,
            bool circleIsComplete,
            float terrainClip)
        {
            var baselineLimit = maxSearchRadius;
            if (baselineSegments.Count >= 2)
            {
                baselineLimit = CombatFogSparsePolygonQuery.GetBoundaryDistance(
                    baselineSegments,
                    queryDir,
                    maxSearchRadius,
                    circleIsComplete,
                    extraRadius);
            }

            if (baselineLimit > maxSearchRadius + 0.5f)
            {
                baselineLimit = maxSearchRadius;
            }

            var limit = Mathf.Min(baselineLimit, terrainClip);
            return CapOpenLimit(limit, maxSearchRadius);
        }

        private static float CapOpenLimit(float limit, float maxSearchRadius)
        {
            if (limit > maxSearchRadius - DistanceEpsilonWorld)
            {
                return maxSearchRadius;
            }

            return limit;
        }

        private static void BuildBaselineUploadSegments(
            float2[] directions,
            float[] uploadLengths,
            int count,
            float totalRevealerRadius,
            List<RaycastRevealer.SightSegment> segments)
        {
            segments.Clear();
            if (directions == null || uploadLengths == null || count <= 0)
            {
                return;
            }

            for (var i = 0; i < count; i++)
            {
                if (!TryParseBaselineUploadSegment(
                        directions[i],
                        uploadLengths[i],
                        totalRevealerRadius,
                        out var direction,
                        out var length,
                        out var didHit))
                {
                    continue;
                }

                segments.Add(new RaycastRevealer.SightSegment
                {
                    Direction = direction,
                    Radius = length,
                    DidHit = didHit,
                });
            }
        }

        private static bool TryParseBaselineUploadSegment(
            float2 direction2D,
            float uploadLength,
            float totalRevealerRadius,
            out float2 direction,
            out float length,
            out bool didHit)
        {
            direction = default;
            length = 0f;
            didHit = false;
            if (math.lengthsq(direction2D) <= 1e-8f)
            {
                return false;
            }

            direction = math.normalize(direction2D);
            didHit = uploadLength <= totalRevealerRadius - DistanceEpsilonWorld;
            length = didHit ? uploadLength : math.min(totalRevealerRadius, uploadLength - 1f);
            return true;
        }
    }
}
