namespace IronKingdoms.Combat
{
    /// <summary>
    /// Runtime switch between baseline stock FOW contours and combat forest clipping.
    /// </summary>
    public static class CombatForestFogPassSettings
    {
        public static bool UseForestPass { get; set; } = true;

        /// <summary>
        /// When false, LUT bins use one analytic clip call each (post-filters still apply).
        /// Median-of-three angular smoothing is ~3× more expensive and mainly helps debug contours.
        /// </summary>
        public static bool UseAngularMedianSmoothing { get; set; }

        /// <summary>
        /// Upper bound on angular bins for shader terrain upload. Matches yellow contour (720).
        /// </summary>
        public static int MaxShaderLutSamples { get; set; } = 720;

        /// <summary>
        /// Lower wall/LUT cost while the pawn is moving; full fidelity when stationary.
        /// </summary>
        public static bool UseAdaptiveFidelityWhileMoving { get; set; } = true;

        /// <summary>
        /// Phase-1 wall raycast step in degrees when stationary.
        /// Lower = sharper walls but more physics raycasts (720 at 0.5°, 360 at 1.0°).
        /// </summary>
        public static float WallRaycastResolutionDegrees { get; set; } = 1f;

        /// <summary>
        /// Coarser wall raycasts while moving (120 rays at 3°).
        /// </summary>
        public static float MovingWallRaycastResolutionDegrees { get; set; } = 3f;

        /// <summary>
        /// Fewer terrain LUT bins while moving; restored to MaxShaderLutSamples when stopped.
        /// </summary>
        public static int MovingLutSamples { get; set; } = 180;

        /// <summary>
        /// Recalculate moving fog every N frames (2 = half cost). Stale frames reuse the last upload.
        /// </summary>
        public static int MovingLineOfSightUpdateInterval { get; set; } = 2;

        /// <summary>
        /// Skip FindEdges binary search on walls while moving.
        /// </summary>
        public static bool MovingSkipWallEdgeResolve { get; set; } = true;

        /// <summary>
        /// Skip extra corner wall segments while moving.
        /// </summary>
        public static bool MovingSkipWallCorners { get; set; } = true;

        /// <summary>
        /// Skip angular terrain post-filters while moving.
        /// </summary>
        public static bool MovingSkipTerrainPostFilters { get; set; } = true;

        public static float ResolveWallRaycastResolutionDegrees(bool isMoving)
        {
            if (isMoving && UseAdaptiveFidelityWhileMoving)
            {
                return MovingWallRaycastResolutionDegrees;
            }

            return WallRaycastResolutionDegrees;
        }

        public static int ResolveLutSampleCount(bool isMoving)
        {
            if (isMoving && UseAdaptiveFidelityWhileMoving)
            {
                return MovingLutSamples;
            }

            return MaxShaderLutSamples;
        }

        public static bool ShouldSkipTerrainPostFilters(bool isMoving)
        {
            return isMoving
                && UseAdaptiveFidelityWhileMoving
                && MovingSkipTerrainPostFilters;
        }
    }
}
