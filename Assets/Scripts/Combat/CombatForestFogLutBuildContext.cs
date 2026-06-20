using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Per-eye values shared by all angular LUT bins during one build pass.
    /// </summary>
    internal readonly struct CombatForestFogLutBuildContext
    {
        public CombatForestFogLutBuildContext(
            Vector3 flatEye,
            float maxSearchRadius,
            float originRadiusWorld,
            float depthWorld,
            bool rayStartedInsideForest,
            bool applyForestClip,
            bool applyBlockingClip)
        {
            FlatEye = flatEye;
            MaxSearchRadius = maxSearchRadius;
            OriginRadiusWorld = originRadiusWorld;
            DepthWorld = depthWorld;
            RayStartedInsideForest = rayStartedInsideForest;
            ApplyForestClip = applyForestClip;
            ApplyBlockingClip = applyBlockingClip;
            HasForest = depthWorld > 0.001f
                && CombatForestFogClipper.HasActiveZonesForClipPass(applyForestClip, applyBlockingClip);
            HasBlocking = false;
        }

        public Vector3 FlatEye { get; }
        public float MaxSearchRadius { get; }
        public float OriginRadiusWorld { get; }
        public float DepthWorld { get; }
        public bool RayStartedInsideForest { get; }
        public bool ApplyForestClip { get; }
        public bool ApplyBlockingClip { get; }
        public bool HasForest { get; }
        public bool HasBlocking { get; }
    }
}
