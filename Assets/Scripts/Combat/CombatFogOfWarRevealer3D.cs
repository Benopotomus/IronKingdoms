using FOW;
using Unity.Mathematics;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Fog revealer that clips vision rays at Mk4 limited-depth terrain (forest) in addition to FogOccluder geometry.
    /// </summary>
    public class CombatFogOfWarRevealer3D : FogOfWarRevealer3D
    {
        private bool ignoresForestForLineOfSight;
        private float planarOriginRadius;

        public void ConfigureForUnit(UnitTypeDefinition definition)
        {
            ignoresForestForLineOfSight = definition != null
                && CombatAbilitySolver.IgnoresForestWhenDeterminingLineOfSight(definition, null);
            planarOriginRadius = definition?.Stats != null
                ? definition.Stats.modelSize.BaseDiameterWorldUnits() * 0.5f
                : 0f;
        }

        protected override void IterationOne(float firstAngle, float angleStep)
        {
            base.IterationOne(firstAngle, angleStep);

            if (!ignoresForestForLineOfSight)
            {
                PreReqJobHandle.Complete();
                ApplyLimitedDepthTerrainClippingToFirstIteration();
                PreReqJobHandle = default;
            }
        }

        protected override void RayCast(float angle, ref SightRay ray)
        {
            base.RayCast(angle, ref ray);

            if (ignoresForestForLineOfSight)
            {
                return;
            }

            ApplyLimitedDepthTerrainClip(angle, ref ray);
        }

        private void ApplyLimitedDepthTerrainClippingToFirstIteration()
        {
            if (FirstIteration == null || !FirstIteration.Distances.IsCreated)
            {
                return;
            }

            CombatForestFogClipper.EnsureCache();
            if (!CombatForestFogClipper.HasActiveZones)
            {
                return;
            }

            var origin = (Vector3)EyePosition;
            for (var i = 0; i < FirstIterationStepCount; i++)
            {
                var maxDistance = FirstIteration.Hits[i]
                    ? FirstIteration.Distances[i]
                    : TotalRevealerRadius;

                if (!TryGetTerrainClipDistance(origin, FirstIteration.RayAngles[i], maxDistance, out var terrainClip))
                {
                    continue;
                }

                var direction2D = GetVector2D(DirFromAngle(FirstIteration.RayAngles[i]));
                FirstIteration.Distances[i] = terrainClip;
                FirstIteration.Hits[i] = true;
                FirstIteration.Points[i] = GetVector2D(EyePosition) + direction2D * terrainClip;
                FirstIteration.Normals[i] = BuildClipNormal(direction2D);
            }
        }

        private void ApplyLimitedDepthTerrainClip(float angle, ref SightRay ray)
        {
            var maxDistance = ray.hit ? ray.distance : TotalRevealerRadius;
            if (!TryGetTerrainClipDistance((Vector3)EyePosition, angle, maxDistance, out var terrainClip))
            {
                return;
            }

            var direction2D = GetVector2D(DirFromAngle(angle));
            ray.distance = terrainClip;
            ray.hit = true;
            ray.point = GetVector2D(EyePosition) + direction2D * terrainClip;
            ray.normal = BuildClipNormal(direction2D);
        }

        private bool TryGetTerrainClipDistance(Vector3 origin, float angle, float maxDistance, out float terrainClip)
        {
            terrainClip = maxDistance;

            CombatForestFogClipper.EnsureCache();
            if (!CombatForestFogClipper.HasActiveZones)
            {
                return false;
            }

            var direction = (Vector3)DirFromAngle(angle);
            var planarDirection = new Vector3(direction.x, 0f, direction.z);
            if (planarDirection.sqrMagnitude <= 1e-6f)
            {
                return false;
            }

            planarDirection.Normalize();
            var clip = CombatForestFogClipper.GetClipDistanceWorld(
                origin,
                planarDirection,
                maxDistance,
                planarOriginRadius);
            if (clip >= maxDistance - 0.001f)
            {
                return false;
            }

            terrainClip = clip;
            return true;
        }

        private static float2 BuildClipNormal(float2 direction)
        {
            var length = math.length(direction);
            if (length <= 1e-4f)
            {
                return direction;
            }

            return -direction / length;
        }
    }
}
