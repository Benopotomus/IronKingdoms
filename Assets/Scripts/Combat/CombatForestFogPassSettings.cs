namespace IronKingdoms.Combat
{
    /// <summary>
    /// Runtime fog pass and moving-performance tuning. Values are pushed from CombatBootstrap
    /// (<see cref="TestLevelUnitController"/>) at startup.
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
        /// Upper bound on angular bins for shader terrain upload when stationary or at full fidelity.
        /// </summary>
        public static int MaxShaderLutSamples { get; set; } = 720;

        /// <summary>
        /// Stationary phase-1 wall raycast step in degrees (360° view: 1° → 360 rays).
        /// </summary>
        public static float WallRaycastResolutionDegrees { get; set; } = 1f;

        /// <summary>
        /// Master switch for the moving perf profile (throttle, coarser walls, reduced terrain LUT).
        /// </summary>
        public static bool EnableMovingPerfProfile { get; set; } = true;

        /// <summary>
        /// Full wall+terrain LOS recalculation rate while pathing. 0 = every rendered frame.
        /// Position-only GPU uploads still run between ticks so the reveal origin tracks the unit.
        /// </summary>
        public static float MovingLineOfSightTargetHz { get; set; } = 30f;

        /// <summary>
        /// In open ground (no clip zones in the inner reach disc), skip wall raycasts between full
        /// LOS ticks and re-upload the last polygon at the current eye position only.
        /// </summary>
        public static bool SkipFullLineOfSightInOpenGroundWhileMoving { get; set; } = true;

        /// <summary>
        /// Clip zones beyond this fraction of revealer radius do not block open-ground LOS throttle
        /// (forests at the outer edge of vision still use reduced-rate updates while pathing).
        /// </summary>
        public static float MovingOpenGroundZoneReachFraction { get; set; } = 0.55f;

        /// <summary>
        /// While moving, use pass-1 wall-ray terrain only when full terrain fidelity is required
        /// (inside / multi-zone / inner-reach forest). Open ground uses the cheaper LUT path.
        /// </summary>
        public static bool UseWallRayTerrainOnlyNearZonesWhileMoving { get; set; } = true;

        /// <summary>
        /// On anchor-only terrain frames, skip polygon edge re-merge when the eye moved less than
        /// this distance since the last full terrain sample.
        /// </summary>
        public static float MovingAnchorRefreshMaxEyeTravelWorld { get; set; } = 0.15f;

        /// <summary>
        /// Frame skip fallback when <see cref="MovingLineOfSightTargetHz"/> is 0 (2 ≈ 30 Hz at 60 FPS).
        /// </summary>
        public static int MovingLineOfSightUpdateInterval { get; set; } = 2;

        /// <summary>
        /// When true, phase-1 wall raycasts use <see cref="MovingWallRaycastResolutionDegrees"/> while pathing.
        /// Off by default — coarse fans miss corners unless <see cref="UseMovingWallEdgeRefinement"/> is on.
        /// </summary>
        public static bool UseMovingWallRaycastResolution { get; set; }

        /// <summary>
        /// Wall raycast step in degrees while pathing when <see cref="UseMovingWallRaycastResolution"/> is on.
        /// </summary>
        public static float MovingWallRaycastResolutionDegrees { get; set; } = 2f;

        /// <summary>
        /// When coarser moving wall rays are enabled, subdivide only at hit/miss transitions (stock FOW pass).
        /// Restores wall edge alignment without a full-density fan in open ground.
        /// </summary>
        public static bool UseMovingWallEdgeRefinement { get; set; } = true;

        /// <summary>
        /// Extra FOW subdivide passes while moving with coarse wall rays (0 = none, 2–4 typical).
        /// </summary>
        public static int MovingWallExtraIterations { get; set; } = 2;

        /// <summary>
        /// Stock FOW <c>RaycastRevealer</c> sub-iteration pool is sized for this many extra rays (+2 edge slots).
        /// Values above this overflow <see cref="FOW.RaycastRevealer"/> native buffers.
        /// </summary>
        public const int FowMaxExtraRaysPerSubIteration = 5;

        /// <summary>
        /// Rays per subdivide pass while moving with coarse wall rays (clamped to
        /// <see cref="FowMaxExtraRaysPerSubIteration"/>).
        /// </summary>
        public static int MovingWallExtraRaysPerIteration { get; set; } = 3;

        public static int ClampMovingWallExtraRaysPerIteration(int rays) =>
            UnityEngine.Mathf.Clamp(rays, 1, FowMaxExtraRaysPerSubIteration);

        /// <summary>
        /// Editor console perf reports for fog/LOS while pathing (see <see cref="CombatFogPerfLogger"/>).
        /// </summary>
        public static bool EnablePerfLogging { get; set; } = true;

        public static float PerfLogIntervalSeconds { get; set; } = 2f;

        public static bool PerfLogOnlyWhileMoving { get; set; } = true;

        /// <summary>
        /// When true, terrain LUT + post-filters use the reduced moving budget while pathing.
        /// </summary>
        public static bool UseReducedTerrainLutWhileMoving { get; set; } = true;

        /// <summary>
        /// Terrain LUT bin count while moving when reduced fidelity is allowed.
        /// </summary>
        public static int MovingTerrainLutSamples { get; set; } = 120;

        /// <summary>
        /// Skip angular terrain post-filters while moving (cheaper LUT build).
        /// </summary>
        public static bool MovingSkipTerrainPostFilters { get; set; } = true;

        /// <summary>
        /// While moving, sample forest/cloud clip on pass-1 wall ray directions (same grid as physics
        /// walls) instead of a separate coarse LUT so terrain edges track walls smoothly.
        /// </summary>
        public static bool UseWallRayDirectionsWhileMoving { get; set; } = true;

        /// <summary>
        /// When wall-ray terrain is on, full clipper passes run at <see cref="MovingLineOfSightTargetHz"/>.
        /// Skipped frames reuse the last ray fan and refresh polygon corner/edge anchors only.
        /// </summary>
        public static bool ThrottleMovingWallRayTerrainRecalc { get; set; } = true;

        /// <summary>
        /// Sample every Nth pass-1 direction for moving wall-ray terrain in open ground (near zones use 1).
        /// </summary>
        public static int MovingWallRayTerrainStrideOpenGround { get; set; } = 3;

        /// <summary>
        /// Polygon edge subdivision in world units while moving (stationary sparse builder uses 0.1).
        /// </summary>
        public static float MovingPolygonEdgeSubdivisionWorld { get; set; } = 0.35f;

        /// <summary>
        /// When true, reduced terrain LUT applies even near / inside forest or cloud zones while moving.
        /// Ignored when <see cref="UseWallRayDirectionsWhileMoving"/> is on. Turn off for sharper LUT edges.
        /// </summary>
        public static bool AllowReducedTerrainLutNearZonesWhileMoving { get; set; }

        /// <summary>
        /// Legacy alias kept for debug GUI toggles.
        /// </summary>
        public static bool UseAdaptiveFidelityWhileMoving
        {
            get => EnableMovingPerfProfile && UseReducedTerrainLutWhileMoving;
            set
            {
                EnableMovingPerfProfile = value;
                UseReducedTerrainLutWhileMoving = value;
            }
        }

        /// <summary>
        /// Legacy alias for <see cref="MovingTerrainLutSamples"/>.
        /// </summary>
        public static int MovingLutSamples
        {
            get => MovingTerrainLutSamples;
            set => MovingTerrainLutSamples = value;
        }

        /// <summary>
        /// When false, upload one LUT bin per direction (smooth curves, higher segment cost).
        /// When true, reduce to sparse edge/corner segments (lower cost, needs angular sampling in shader).
        /// </summary>
        public static bool UseSparseTerrainUpload { get; set; }

        public static int ResolveLutSampleCount(
            bool isMoving,
            bool requireFullTerrainFidelity,
            int activeClipZoneCount = 0)
        {
            if (!isMoving || !EnableMovingPerfProfile || !UseReducedTerrainLutWhileMoving)
            {
                return MaxShaderLutSamples;
            }

            if (!AllowReducedTerrainLutNearZonesWhileMoving
                && (requireFullTerrainFidelity || activeClipZoneCount >= 2))
            {
                return MaxShaderLutSamples;
            }

            return MovingTerrainLutSamples;
        }

        public static bool ShouldSkipTerrainPostFilters(
            bool isMoving,
            bool requireFullTerrainFidelity = false,
            int activeClipZoneCount = 0)
        {
            if (!isMoving || !EnableMovingPerfProfile || !MovingSkipTerrainPostFilters)
            {
                return false;
            }

            if (!AllowReducedTerrainLutNearZonesWhileMoving
                && (requireFullTerrainFidelity || activeClipZoneCount >= 2))
            {
                return false;
            }

            return UseReducedTerrainLutWhileMoving;
        }

        public static float ResolveWallRaycastResolutionDegrees(bool isMoving)
        {
            if (isMoving
                && EnableMovingPerfProfile
                && UseMovingWallRaycastResolution)
            {
                return MovingWallRaycastResolutionDegrees;
            }

            return WallRaycastResolutionDegrees;
        }

        public static bool ResolveUseWallRayTerrainUpload(
            bool isMoving,
            bool requireFullTerrainFidelity)
        {
            if (!isMoving || !UseWallRayDirectionsWhileMoving)
            {
                return false;
            }

            if (UseWallRayTerrainOnlyNearZonesWhileMoving)
            {
                return requireFullTerrainFidelity;
            }

            return true;
        }

        public static float ResolveMovingOpenGroundZoneReach(float revealerRadiusWorld) =>
            revealerRadiusWorld * UnityEngine.Mathf.Clamp01(MovingOpenGroundZoneReachFraction);

        /// <summary>
        /// True when a full moving LOS / terrain pass should be deferred to a later frame.
        /// </summary>
        public static bool ShouldDeferMovingFullUpdate(float lastFullUpdateTime, float currentTime, int frameSalt)
        {
            if (!EnableMovingPerfProfile)
            {
                return false;
            }

            var targetHz = MovingLineOfSightTargetHz;
            if (targetHz > 0.001f)
            {
                var minInterval = 1f / targetHz;
                return currentTime - lastFullUpdateTime + 0.0001f < minInterval;
            }

            var interval = MovingLineOfSightUpdateInterval;
            if (interval <= 1)
            {
                return false;
            }

            return (UnityEngine.Time.frameCount + frameSalt) % interval != 0;
        }

        /// <summary>Build terrain LUT bins on worker threads when footprints are cache-safe.</summary>
        public static bool UseParallelForestClipLutBuild { get; set; } = true;

        /// <summary>Minimum LUT bins before parallel clip build kicks in.</summary>
        public static int ParallelForestClipLutMinSamples { get; set; } = 24;

        /// <summary>
        /// When true and zone footprints are cache-safe, full forest clip samples run on worker threads
        /// (not just the AABB cull pass).
        /// </summary>
        public static bool UseParallelForestClipFullSample { get; set; } = true;

        /// <summary>Minimum ray/bin count before parallel full-sample clip is used.</summary>
        public static int ParallelForestClipFullSampleMinCount { get; set; } = 8;

        /// <summary>
        /// Allow worker-thread clip when full terrain fidelity is required while moving.
        /// </summary>
        public static bool AllowParallelForestClipWithFullFidelity { get; set; } = true;

        public static bool ShouldUseParallelForestClip(bool requireFullTerrainFidelity, int sampleCount)
        {
            if (!UseParallelForestClipLutBuild && !UseParallelForestClipFullSample)
            {
                return false;
            }

            if (sampleCount < ParallelForestClipFullSampleMinCount)
            {
                return false;
            }

            if (requireFullTerrainFidelity && !AllowParallelForestClipWithFullFidelity)
            {
                return false;
            }

            return CombatForestFogClipper.CanRunParallelForestClipSampling();
        }
    }
}
