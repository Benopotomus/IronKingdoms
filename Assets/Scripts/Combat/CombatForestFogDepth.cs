using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Resolves the forest pass-through depth used by fog-of-war clipping.
    /// Terrain features own the authored inch value; this helper only converts it and applies
    /// the Mk4 fallback used when no active limited-depth zone reports a value.
    /// </summary>
    internal static class CombatForestFogDepth
    {
        public const float DefaultPassThroughDepthInches = 3f;

        public static float ResolveDepthWorld()
        {
            var depthWorld = CombatForestFogClipper.GetStrictestLimitedDepthWorld();
            return depthWorld > 0.001f
                ? depthWorld
                : CombatScale.InchesToWorldUnits(DefaultPassThroughDepthInches);
        }
    }
}
