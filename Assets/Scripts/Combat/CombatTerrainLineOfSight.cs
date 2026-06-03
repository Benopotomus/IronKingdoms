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
}
