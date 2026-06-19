using System.Collections.Generic;
using FOW;
using Unity.Mathematics;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Yellow debug contour: baseline wall polygon + analytic forest/cloud clipper per direction.
    /// </summary>
    internal static class CombatFogEffectiveBoundarySampler
    {
        private const float DistanceEpsilonWorld = 0.01f;

        private static readonly List<RaycastRevealer.SightSegment> BaselineSegmentsScratch = new();
        private static readonly float[] TerrainClipScratch = new float[CombatForestFogAngularClipperLut.SampleCount];

        public static void BuildEffectiveFogBoundaryContour(
            CombatFogOfWarRevealer3D revealer,
            Vector3 eyeWorld,
            Vector3 originGround,
            float maxSearchRadius,
            float originRadiusWorld,
            bool applyTerrainClip,
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

            if (applyTerrainClip)
            {
                CombatForestFogAngularClipperLut.BuildSmoothedClipDistances(
                    eyeWorld,
                    maxSearchRadius,
                    originRadiusWorld,
                    projection,
                    TerrainClipScratch);
            }

            var sampleCount = CombatForestFogAngularClipperLut.SampleCount;
            for (var i = 0; i < sampleCount; i++)
            {
                var angle = (i / (float)sampleCount) * Mathf.PI * 2f;
                var queryDir = math.normalize(new float2(Mathf.Cos(angle), Mathf.Sin(angle)));
                var direction = CombatFogProjection.Direction2DToWorld(queryDir, projection);

                var terrainClip = applyTerrainClip ? TerrainClipScratch[i] : maxSearchRadius;
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
