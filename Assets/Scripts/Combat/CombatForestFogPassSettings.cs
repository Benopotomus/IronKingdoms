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
        /// Lower terrain LUT cost while moving. Off by default — coarser moving uploads can show a
        /// circular depth-limit boundary instead of the full forest silhouette.
        /// </summary>
        public static bool UseAdaptiveFidelityWhileMoving { get; set; }

        /// <summary>
        /// Phase-1 wall raycast step in degrees. Stock FOW pass 1 always uses this (never coarsened while moving).
        /// Lower = sharper walls but more physics raycasts (720 at 0.5°, 360 at 1.0°).
        /// </summary>
        public static float WallRaycastResolutionDegrees { get; set; } = 1f;

        /// <summary>
        /// Fewer terrain LUT bins while moving; restored to MaxShaderLutSamples when stopped.
        /// Pass 1 (stock FOW walls) is unchanged while moving.
        /// </summary>
        public static int MovingLutSamples { get; set; } = 180;

        /// <summary>
        /// Recalculate moving fog every N frames (2 = half cost). Stale frames reuse the last upload.
        /// </summary>
        public static int MovingLineOfSightUpdateInterval { get; set; } = 2;

        /// <summary>
        /// Skip angular terrain post-filters while moving.
        /// </summary>
        public static bool MovingSkipTerrainPostFilters { get; set; } = true;

        /// <summary>
        /// When false, upload one LUT bin per direction (smooth curves, higher segment cost).
        /// When true, reduce to sparse edge/corner segments (lower cost, needs angular sampling in shader).
        /// </summary>
        public static bool UseSparseTerrainUpload { get; set; }

        public static int ResolveLutSampleCount(
            bool isMoving,
            bool requireFullTerrainFidelity = false,
            int activeClipZoneCount = 0)
        {
            if (requireFullTerrainFidelity
                || activeClipZoneCount >= 2
                || !isMoving
                || !UseAdaptiveFidelityWhileMoving)
            {
                return MaxShaderLutSamples;
            }

            return MovingLutSamples;
        }

        public static bool ShouldSkipTerrainPostFilters(
            bool isMoving,
            bool requireFullTerrainFidelity = false,
            int activeClipZoneCount = 0)
        {
            if (requireFullTerrainFidelity || activeClipZoneCount >= 2)
            {
                return false;
            }

            return isMoving
                && UseAdaptiveFidelityWhileMoving
                && MovingSkipTerrainPostFilters;
        }
    }
}
