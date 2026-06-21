using FOW;
using Unity.Mathematics;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Pass 1 (baseline): stock FindEdges walls, uploaded unchanged.
    /// Pass 2 (terrain): forest/cloud clip per ray only — no wall tests.
    /// Shader combines: walls from baseline, forest subtractive on open ground.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class CombatFogOfWarRevealer3D : FogOfWarRevealer3D
    {
        private const float StationaryEyeDistanceWorld = 0.02f;
        private const float StationaryEyeDistanceWorldSq = StationaryEyeDistanceWorld * StationaryEyeDistanceWorld;
        private const float StationaryYawDegrees = 0.5f;

        [Header("Forest Debug")]
        [SerializeField] private bool drawForestClipDebug = false;
        [SerializeField] private bool drawForestClipInGameView = true;
        [SerializeField] private bool drawWallBaselineProof = false;
        [SerializeField] private bool drawShaderUploadPolygons = false;
        [SerializeField] private Color debugClipRayColor = new(0.1f, 1f, 0.2f, 1f);
        [SerializeField] private Color debugBridgeRayColor = new(1f, 0.85f, 0.1f, 1f);
        [SerializeField] private Color debugContourColor = new(0.2f, 0.9f, 1f, 1f);
        [SerializeField] private Color debugSparseWallBaselineColor = new(1f, 0.1f, 1f, 1f);
        [SerializeField] private Color debugDenseWallHitColor = new(1f, 0.95f, 0.2f, 1f);
        [SerializeField] private Color debugWallViolationColor = new(1f, 0.1f, 0.1f, 1f);
        [SerializeField] private Color debugBaselineUploadColor = new(0.2f, 0.55f, 1f, 1f);
        [SerializeField] private Color debugTerrainUploadColor = new(0.1f, 0.95f, 0.35f, 1f);
        [SerializeField] private Color debugTerrainClipTickColor = new(1f, 0.55f, 0.1f, 1f);
        [SerializeField] private Color debugBaselineWallChordColor = new(1f, 0.2f, 0.85f, 1f);
        [SerializeField] private Color debugEffectiveBoundaryColor = new(0.95f, 0.95f, 0.2f, 1f);

        private readonly CombatForestFogRayPostProcessor forestPostProcessor = new();
        private readonly CombatForestFogDebugContour forestDebugContour = new();
        private readonly CombatForestFogWallBaselineProofDrawer wallBaselineProofDrawer = new();
        private readonly CombatFogShaderUploadPolygonDrawer shaderUploadPolygonDrawer = new();
        private LineRenderer wallProofLoopLine;
        private LineRenderer wallProofHitLine;
        private LineRenderer baselineUploadLoopLine;
        private LineRenderer terrainUploadLoopLine;
        private LineRenderer terrainUploadClipTicksLine;
        private LineRenderer baselineWallChordLine;
        private LineRenderer effectiveBoundaryLine;
        private const float WallProofLineYBoost = 0.15f;
        private const float ShaderUploadLineYBoost = 0.22f;
        private const float WallProofLoopWidth = 0.08f;
        private const float WallProofRayWidth = 0.04f;
        private const float BaselineUploadLoopWidth = 0.07f;
        private const float TerrainUploadLoopWidth = 0.05f;
        private const float TerrainClipTickWidth = 0.03f;
        private const float BaselineWallChordWidth = 0.06f;
        private const float EffectiveBoundaryWidth = 0.04f;

        public CombatForestFogWallBaselineReport WallBaselineReport => forestPostProcessor.LastWallBaselineReport;

        public int TerrainClipUploadSegmentCount => forestPostProcessor.TerrainClipSegmentCount;

        public float2[] TerrainClipUploadDirections => forestPostProcessor.TerrainClipDirections;

        public float[] TerrainClipUploadDistances => forestPostProcessor.TerrainClipUploadDistances;

        public bool DrawWallBaselineProof
        {
            get => drawWallBaselineProof;
            set => drawWallBaselineProof = value;
        }

        public bool DrawShaderUploadPolygons
        {
            get => drawShaderUploadPolygons;
            set => drawShaderUploadPolygons = value;
        }

        private bool ignoresForestForLineOfSight;
        private float baseRadiusWorld;
        private Vector3 lastCalculatedEyeWorld;
        private float lastCalculatedEyeYaw;
        private bool hasCalculatedLineOfSightPose;
        private bool pendingLineOfSightRecalculation;
        private bool wantsLocalFogContribution = true;
        private bool applyForestPassThisFrame;
        private bool applyForestClipThisFrame;
        private bool forestPassRanThisFrame;
        private bool pawnIsMoving;
        private float lastMovingLineOfSightUpdateTime;
        private bool skipMovingTerrainRecalcThisFrame;
        private bool skipFullLineOfSightThisFrame;
        private bool movingWallRaycastResolutionActive;
        private float stationaryRaycastResolution;
        private int stationaryNumExtraIterations;
        private int stationaryNumExtraRaysOnIteration;

        public bool ShouldContributeToLocalFog => wantsLocalFogContribution;

        public bool IsPawnMoving => pawnIsMoving;

        public bool IsContributingToFog => wantsLocalFogContribution && IsRegistered;

        public bool IgnoresForestLineOfSightLimits => ignoresForestForLineOfSight;

        public float BaseRadiusWorld => baseRadiusWorld;

        public bool ShouldApplyTerrainFeatureClipForFog()
        {
            if (!useOcclusion || !CombatForestFogPassSettings.UseForestPass)
            {
                return false;
            }

            return ShouldApplyForestClipThisFrame() || ShouldApplyBlockingTerrainClipThisFrame();
        }

        public bool ShouldApplyForestClipThisFrame()
        {
            if (!useOcclusion
                || !CombatForestFogPassSettings.UseForestPass
                || ignoresForestForLineOfSight)
            {
                return false;
            }

            CombatForestFogClipper.EnsureCache();
            return CombatForestFogClipper.HasActiveForestFogZones;
        }

        public bool ShouldApplyBlockingTerrainClipThisFrame()
        {
            if (!useOcclusion || !CombatForestFogPassSettings.UseForestPass)
            {
                return false;
            }

            CombatForestFogClipper.EnsureCache();
            return CombatForestFogClipper.HasActiveCloudFogZones;
        }

        public bool MatchesUnitVisionRules(Unit unit)
        {
            if (unit?.Definition?.Stats == null)
            {
                return unit == null;
            }

            return ignoresForestForLineOfSight
                == CombatAbilitySolver.IgnoresForestWhenDeterminingLineOfSight(unit);
        }

        public bool IsContributionStateSatisfied()
        {
            return wantsLocalFogContribution == IsRegistered;
        }

        public void ConfigureForUnit(Unit unit)
        {
            if (unit?.Definition?.Stats == null)
            {
                ClearConfiguration();
                return;
            }

            var nextIgnoresForest = CombatAbilitySolver.IgnoresForestWhenDeterminingLineOfSight(unit);
            var forestRulesChanged = ignoresForestForLineOfSight != nextIgnoresForest;
            ignoresForestForLineOfSight = nextIgnoresForest;
            baseRadiusWorld = ResolveUnitBaseRadiusWorld(unit);

            InvalidateLineOfSightPose();

            if (Application.isPlaying && forestRulesChanged)
            {
                RequestLineOfSightRecalculation();
            }
        }

        /// <summary>
        /// Controls whether this unit is registered with FogOfWarWorld. The component stays
        /// enabled; non-contributing revealers are deregistered only.
        /// </summary>
        public void SetLocalFogContribution(bool active)
        {
            if (wantsLocalFogContribution == active && IsContributionStateSatisfied())
            {
                return;
            }

            wantsLocalFogContribution = active;
            if (!active)
            {
                pendingLineOfSightRecalculation = false;
            }

            ApplyLocalFogContributionState();

            if (active)
            {
                RequestLineOfSightRecalculation();
                TryProcessPendingLineOfSightRecalculation();
            }
        }

        private void OnEnable()
        {
            EnsureCachedTransform();
            ApplyLocalFogContributionState();

            if (!wantsLocalFogContribution && IsRegistered)
            {
                DeregisterRevealer();
            }
        }

        private void ApplyLocalFogContributionState()
        {
            FogOfWarWorld.PendingRevealerRegister.Remove(this);

            if (!wantsLocalFogContribution)
            {
                if (IsRegistered)
                {
                    DeregisterRevealer();
                }

                return;
            }

            if (IsRegistered)
            {
                return;
            }

            var fow = FogOfWarWorld.instance;
            if (fow == null)
            {
                return;
            }

            if (fow.IsInPhasedUpdate)
            {
                if (!FogOfWarWorld.PendingRevealerRegister.Contains(this))
                {
                    FogOfWarWorld.PendingRevealerRegister.Add(this);
                }

                return;
            }

            RegisterRevealer();
        }

        /// <summary>
        /// Re-reads forest/ability vision rules from a live unit and schedules a safe FOW refresh.
        /// </summary>
        public void ApplyVisionRulesFromUnit(Unit unit)
        {
            ConfigureForUnit(unit);
            if (!Application.isPlaying)
            {
                return;
            }

            RequestLineOfSightRecalculation();
            TryProcessPendingLineOfSightRecalculation();
        }

        /// <summary>
        /// Marks this revealer dynamic and recalculates on the next safe frame boundary.
        /// Avoid calling ManualCalculateLineOfSight during FOW phased updates or revealer enable.
        /// </summary>
        public void RequestLineOfSightRecalculation()
        {
            SetRevealerAsStatic(false);
            InvalidateLineOfSightPose();
            pendingLineOfSightRecalculation = true;
        }

        public void ForceImmediateLineOfSightRecalculation()
        {
            RequestLineOfSightRecalculation();
            TryProcessPendingLineOfSightRecalculation();
        }

        /// <summary>
        /// Safe entry for manual LOS when transform/FOW state is valid. Skips deregistered or
        /// mid-phase revealers (e.g. toggles fired from OnGUI).
        /// </summary>
        public new void ManualCalculateLineOfSight()
        {
            if (!CanCalculateLineOfSight())
            {
                RequestLineOfSightRecalculation();
                return;
            }

            base.ManualCalculateLineOfSight();
        }

        public void NotifyPawnMoved()
        {
            pawnIsMoving = true;
            lastMovingLineOfSightUpdateTime = 0f;
            SetRevealerAsStatic(false);
            InvalidateLineOfSightPose();
            ApplyMovingWallRaycastResolutionIfNeeded();
        }

        /// <summary>
        /// Called when pathing finishes so the next LOS pass uses full stationary fidelity.
        /// </summary>
        public void NotifyPawnMovementEnded()
        {
            if (!pawnIsMoving)
            {
                return;
            }

            pawnIsMoving = false;
            lastMovingLineOfSightUpdateTime = 0f;
            skipMovingTerrainRecalcThisFrame = false;
            forestPostProcessor.ClearMovingTerrainCache();
            skipFullLineOfSightThisFrame = false;
            RestoreStationaryWallRaycastResolution();
            RequestLineOfSightRecalculation();
        }

        private void Update()
        {
            if (IsRegistered && Application.isPlaying)
            {
                UpdateStationaryState();
            }
        }

        private void LateUpdate()
        {
            TryProcessPendingLineOfSightRecalculation();
        }

        private void TryProcessPendingLineOfSightRecalculation()
        {
            if (!pendingLineOfSightRecalculation || !CanCalculateLineOfSight())
            {
                return;
            }

            pendingLineOfSightRecalculation = false;
            base.ManualCalculateLineOfSight();
        }

        private bool CanCalculateLineOfSight()
        {
            if (!isActiveAndEnabled || !IsRegistered)
            {
                return false;
            }

            EnsureCachedTransform();
            if (CachedTransform == null)
            {
                return false;
            }

            var fow = FogOfWarWorld.instance;
            return fow != null && !fow.IsInPhasedUpdate;
        }

        private void EnsureCachedTransform()
        {
            if (CachedTransform == null)
            {
                CachedTransform = transform;
            }
        }

        public void ConfigureForUnit(UnitTypeDefinition definition)
        {
            if (definition?.Stats == null)
            {
                ClearConfiguration();
                return;
            }

            ignoresForestForLineOfSight = CombatAbilitySolver.IgnoresForestWhenDeterminingLineOfSight(definition, null);
            baseRadiusWorld = Mathf.Max(0f, definition.Stats.modelSize.BaseDiameterWorldUnits() * 0.5f);

            InvalidateLineOfSightPose();
        }

        private void ClearConfiguration()
        {
            ignoresForestForLineOfSight = false;
            baseRadiusWorld = 0f;
            InvalidateLineOfSightPose();
        }

        private static float ResolveUnitBaseRadiusWorld(Unit unit)
        {
            if (unit?.Definition?.Stats == null || unit.Pawn == null)
            {
                return 0f;
            }

            var collider = unit.Pawn.GetComponent<CapsuleCollider>();
            if (collider != null)
            {
                return collider.radius;
            }

            return Mathf.Max(0f, unit.Definition.Stats.modelSize.BaseDiameterWorldUnits() * 0.5f);
        }

        public override void LineOfSightPhase1()
        {
            CombatFogPerfLogger.NotifyPawnMoving(pawnIsMoving);
            EvaluateMovingPerfFramePlan();

            if (skipFullLineOfSightThisFrame)
            {
                CombatFogPerfLogger.IncrementCounter("revealer.position_only");
                return;
            }

            if (skipMovingTerrainRecalcThisFrame)
            {
                CombatFogPerfLogger.IncrementCounter("revealer.terrain_anchor_only");
            }
            else
            {
                CombatFogPerfLogger.IncrementCounter("revealer.terrain_full");
            }

            CombatFogPerfLogger.IncrementCounter("revealer.full_los");
            forestPostProcessor.ClearDebugState();
            using (CombatFogPerfLogger.Measure("revealer.phase1.wall_rays"))
            {
                base.LineOfSightPhase1();
            }
        }

        public override void LineOfSightPhase2()
        {
            if (skipFullLineOfSightThisFrame)
            {
                using (CombatFogPerfLogger.Measure("revealer.position_only_upload"))
                {
                    ApplyRevealerPositionOnly();
                }

                return;
            }

            forestPostProcessor.ClearDebugState();

            applyForestClipThisFrame = ShouldApplyForestClipThisFrame();
            applyForestPassThisFrame = ShouldApplyTerrainFeatureClipForFog();
            forestPassRanThisFrame = applyForestPassThisFrame;

            if (!applyForestPassThisFrame)
            {
                ClearForestDebug();
            }

            using (CombatFogPerfLogger.Measure("revealer.phase2.wall_edges"))
            {
                base.LineOfSightPhase2();
            }

            applyForestPassThisFrame = false;
            CaptureLineOfSightPose();
            CaptureWallBaselineProofIfNeeded();
            CaptureShaderUploadPolygonsIfNeeded();

            if (drawForestClipDebug && drawForestClipInGameView && forestDebugContour.HasContour)
            {
                forestDebugContour.DrawRuntimeLines(debugClipRayColor, debugBridgeRayColor, debugContourColor);
            }

            if (drawWallBaselineProof && drawForestClipInGameView && wallBaselineProofDrawer.HasData)
            {
                wallBaselineProofDrawer.DrawRuntime(
                    debugSparseWallBaselineColor,
                    debugDenseWallHitColor,
                    debugWallViolationColor);
            }

            if (drawShaderUploadPolygons && drawForestClipInGameView && shaderUploadPolygonDrawer.HasData)
            {
                shaderUploadPolygonDrawer.DrawRuntimeLines(
                    debugBaselineUploadColor,
                    debugTerrainUploadColor,
                    debugTerrainClipTickColor,
                    debugBaselineWallChordColor,
                    debugEffectiveBoundaryColor);
            }

            if (!drawWallBaselineProof)
            {
                ClearWallProofGameViewLines();
            }

            if (!drawShaderUploadPolygons)
            {
                ClearShaderUploadGameViewLines();
            }
        }

        private void CaptureShaderUploadPolygonsIfNeeded()
        {
            if (!drawShaderUploadPolygons)
            {
                return;
            }

            var eyeWorld = (Vector3)GetEyePosition();
            shaderUploadPolygonDrawer.Capture(
                OutputDirections,
                OutputDistances,
                NumberOfPoints,
                forestPostProcessor.TerrainClipDirections,
                forestPostProcessor.TerrainClipUploadDistances,
                forestPostProcessor.TerrainClipSegmentCount,
                eyeWorld,
                TotalRevealerRadius,
                CircleIsComplete,
                Projection);

            if (drawForestClipInGameView)
            {
                ApplyShaderUploadGameViewLines();
            }
        }

        private void ApplyShaderUploadGameViewLines()
        {
            EnsureShaderUploadLineRenderers();
            shaderUploadPolygonDrawer.ApplyGameViewLines(
                baselineUploadLoopLine,
                terrainUploadLoopLine,
                terrainUploadClipTicksLine,
                baselineWallChordLine,
                effectiveBoundaryLine,
                ShaderUploadLineYBoost);
        }

        private void ClearShaderUploadGameViewLines()
        {
            if (baselineUploadLoopLine != null)
            {
                baselineUploadLoopLine.positionCount = 0;
            }

            if (terrainUploadLoopLine != null)
            {
                terrainUploadLoopLine.positionCount = 0;
            }

            if (terrainUploadClipTicksLine != null)
            {
                terrainUploadClipTicksLine.positionCount = 0;
            }

            if (baselineWallChordLine != null)
            {
                baselineWallChordLine.positionCount = 0;
            }

            if (effectiveBoundaryLine != null)
            {
                effectiveBoundaryLine.positionCount = 0;
            }
        }

        private void EnsureShaderUploadLineRenderers()
        {
            if (baselineUploadLoopLine == null)
            {
                baselineUploadLoopLine = CombatFogShaderUploadPolygonDrawer.CreateLoopLineRenderer(
                    transform,
                    "ShaderBaselineUploadLoop",
                    debugBaselineUploadColor,
                    BaselineUploadLoopWidth,
                    true);
            }

            if (terrainUploadLoopLine == null)
            {
                terrainUploadLoopLine = CombatFogShaderUploadPolygonDrawer.CreateLoopLineRenderer(
                    transform,
                    "ShaderTerrainUploadLoop",
                    debugTerrainUploadColor,
                    TerrainUploadLoopWidth,
                    true);
            }

            if (terrainUploadClipTicksLine == null)
            {
                terrainUploadClipTicksLine = CombatFogShaderUploadPolygonDrawer.CreateLoopLineRenderer(
                    transform,
                    "ShaderTerrainClipTicks",
                    debugTerrainClipTickColor,
                    TerrainClipTickWidth,
                    false);
            }

            if (baselineWallChordLine == null)
            {
                baselineWallChordLine = CombatFogShaderUploadPolygonDrawer.CreateLoopLineRenderer(
                    transform,
                    "ShaderBaselineWallChords",
                    debugBaselineWallChordColor,
                    BaselineWallChordWidth,
                    false);
            }

            if (effectiveBoundaryLine == null)
            {
                effectiveBoundaryLine = CombatFogShaderUploadPolygonDrawer.CreateLoopLineRenderer(
                    transform,
                    "ShaderEffectiveBoundary",
                    debugEffectiveBoundaryColor,
                    EffectiveBoundaryWidth,
                    true);
            }
        }

        private void CaptureWallBaselineProofIfNeeded()
        {
            if (!drawWallBaselineProof)
            {
                return;
            }

            var eyeWorld = (Vector3)GetEyePosition();
            if (!forestPassRanThisFrame)
            {
                forestPostProcessor.SnapshotWallPassForProof(ViewPoints, NumberOfPoints);
                var report = forestPostProcessor.BuildBaselineOnlyReport(ViewPoints, NumberOfPoints, TotalRevealerRadius);
                forestPostProcessor.SetLastWallBaselineReport(report);
            }

            wallBaselineProofDrawer.Capture(
                forestPostProcessor.WallPassSegments,
                ViewPoints,
                NumberOfPoints,
                eyeWorld,
                TotalRevealerRadius,
                CircleIsComplete,
                Projection);

            if (drawForestClipInGameView)
            {
                ApplyWallProofGameViewLines();
            }
        }

        private void ApplyWallProofGameViewLines()
        {
            EnsureWallProofLineRenderers();
            wallBaselineProofDrawer.ApplyGameViewLines(
                wallProofLoopLine,
                wallProofHitLine,
                WallProofLineYBoost);
        }

        private void ClearWallProofGameViewLines()
        {
            if (wallProofLoopLine != null)
            {
                wallProofLoopLine.positionCount = 0;
            }

            if (wallProofHitLine != null)
            {
                wallProofHitLine.positionCount = 0;
            }
        }

        private void EnsureWallProofLineRenderers()
        {
            if (wallProofLoopLine == null)
            {
                wallProofLoopLine = CombatForestFogWallBaselineProofDrawer.CreateProofLineRenderer(
                    transform,
                    "WallBaselineProofLoop",
                    debugSparseWallBaselineColor,
                    WallProofLoopWidth);
                wallProofLoopLine.loop = true;
            }

            if (wallProofHitLine == null)
            {
                wallProofHitLine = CombatForestFogWallBaselineProofDrawer.CreateProofLineRenderer(
                    transform,
                    "WallBaselineProofHits",
                    debugDenseWallHitColor,
                    WallProofRayWidth);
            }
        }

        protected override void OnAfterResolveEdges()
        {
            if (!applyForestPassThisFrame)
            {
                forestPostProcessor.ClearTerrainClipUpload();
                return;
            }

            using (CombatFogPerfLogger.Measure("revealer.terrain.clip"))
            {
                CompletePhaseOneBeforeForestClip();

                var eyeWorld = (Vector3)GetEyePosition();
                var eyeIntersectsForest = CombatForestFogClipper.IsInsideLimitedDepthForest(eyeWorld, 0f);
                var eyeIntersectsCloud = CombatBlockingTerrainClipper.IsInsideBlockingTerrain(eyeWorld, 0f);
                CombatForestFogClipper.EnsureCache();
                var applyBlockingClip = ShouldApplyBlockingTerrainClipThisFrame();
                var activeClipZoneCount = CombatForestFogClipper.GetActiveClipZoneCount(
                    applyForestClipThisFrame,
                    applyBlockingClip);
                var requireFullTerrainFidelity = eyeIntersectsForest
                    || eyeIntersectsCloud
                    || activeClipZoneCount >= 2
                    || CombatForestFogClipper.AnyCachedZoneIntersectsVisionDisc(
                        eyeWorld,
                        TotalRevealerRadius,
                        1f);
                var collectDebugState = drawForestClipDebug && drawForestClipInGameView && !pawnIsMoving;
                var maxUploadSegments = FogOfWarWorld.instance != null
                    ? FogOfWarWorld.instance.MaxPossibleSegmentsPerRevealer
                    : NumberOfPoints;

                var useWallRayTerrainUpload = CombatForestFogPassSettings.ResolveUseWallRayTerrainUpload(
                    pawnIsMoving,
                    requireFullTerrainFidelity);
                var desiredLutSamples = CombatForestFogPassSettings.ResolveLutSampleCount(
                    pawnIsMoving,
                    requireFullTerrainFidelity,
                    activeClipZoneCount);
                forestPostProcessor.BuildTerrainClipSegmentsForShader(
                    ViewPoints,
                    NumberOfPoints,
                    FirstIteration,
                    FirstIterationStepCount,
                    eyeWorld,
                    TotalRevealerRadius,
                    0f,
                    Projection,
                    eyeIntersectsForest,
                    eyeIntersectsCloud,
                    CircleIsComplete,
                    maxUploadSegments,
                    collectDebugState,
                    applyForestClipThisFrame,
                    applyBlockingClip,
                    desiredLutSamples,
                    CombatForestFogPassSettings.ShouldSkipTerrainPostFilters(
                        pawnIsMoving,
                        requireFullTerrainFidelity,
                        activeClipZoneCount),
                    useWallRayTerrainUpload,
                    skipMovingTerrainRecalcThisFrame,
                    requireFullTerrainFidelity);

                var terrainPathLabel = skipFullLineOfSightThisFrame
                    ? "position-only"
                    : useWallRayTerrainUpload
                        ? skipMovingTerrainRecalcThisFrame ? "wall-ray-anchors" : "wall-ray-full"
                        : skipMovingTerrainRecalcThisFrame ? "lut-anchors" : "lut";
                CombatFogPerfLogger.SetContext("name", gameObject.name);
                CombatFogPerfLogger.SetContext("moving", pawnIsMoving.ToString());
                CombatFogPerfLogger.SetContext("phase1Rays", FirstIterationStepCount.ToString());
                CombatFogPerfLogger.SetContext("wallSegs", NumberOfPoints.ToString());
                CombatFogPerfLogger.SetContext("terrainSegs", forestPostProcessor.TerrainClipSegmentCount.ToString());
                CombatFogPerfLogger.SetContext("terrainPath", terrainPathLabel);
                CombatFogPerfLogger.SetContext("extraIters", NumExtraIterations.ToString());
            }

            var eyeWorldForDebug = (Vector3)GetEyePosition();

            if (drawWallBaselineProof)
            {
                wallBaselineProofDrawer.Capture(
                    forestPostProcessor.WallPassSegments,
                    ViewPoints,
                    NumberOfPoints,
                    eyeWorldForDebug,
                    TotalRevealerRadius,
                    CircleIsComplete,
                    Projection);
            }

            if (!drawForestClipDebug)
            {
                forestDebugContour.Clear();
            }
            else
            {
                forestDebugContour.Capture(
                    forestPostProcessor.TerrainClipDirections,
                    forestPostProcessor.TerrainClipUploadDistances,
                    forestPostProcessor.TerrainClipSegmentCount,
                    eyeWorldForDebug,
                    TotalRevealerRadius,
                    Projection,
                    forestPostProcessor.BridgedRayIndices);
            }
        }

        protected override void ApplyData()
        {
            using (CombatFogPerfLogger.Measure("revealer.gpu_upload"))
            {
                RevealerDataStruct.RevealerPosition = RevealerPosition;
                RevealerDataStruct.RevealerHeight = RevealerHeightPosition + ShaderEyeOffset;
                RevealerDataStruct.NumSegments = NumberOfPoints;

                var terrainCount = forestPostProcessor.TerrainClipSegmentCount;
                RevealerInfoStruct.NumTerrainClipSegments = terrainCount;
                FogOfWarWorld.instance.UpdateRevealerInfo(RevealerGPUDataPosition, RevealerInfoStruct);

                FogOfWarWorld.instance.UpdateRevealerData(
                    RevealerGPUDataPosition,
                    RevealerDataStruct,
                    NumberOfPoints,
                    OutputDirections,
                    OutputDistances,
                    terrainCount,
                    forestPostProcessor.TerrainClipDirections,
                    forestPostProcessor.TerrainClipUploadDistances);

                SparseRevealerGrid.UpdateRevealerBuckets(this, RevealerPosition);
            }
        }

        private void UpdateStationaryState()
        {
            if (!hasCalculatedLineOfSightPose)
            {
                return;
            }

            if (HasMovedSinceLastLineOfSightCalculation())
            {
                if (CurrentlyStaticRevealer)
                {
                    SetRevealerAsStatic(false);
                }
            }
            else if (pawnIsMoving)
            {
                pawnIsMoving = false;
                lastMovingLineOfSightUpdateTime = 0f;
                skipMovingTerrainRecalcThisFrame = false;
                skipFullLineOfSightThisFrame = false;
                RestoreStationaryWallRaycastResolution();
                SetRevealerAsStatic(false);
            }
            else if (!CurrentlyStaticRevealer)
            {
                SetRevealerAsStatic(true);
            }
        }

        private void EvaluateMovingPerfFramePlan()
        {
            skipFullLineOfSightThisFrame = false;
            skipMovingTerrainRecalcThisFrame = false;

            if (!pawnIsMoving || !CombatForestFogPassSettings.EnableMovingPerfProfile)
            {
                return;
            }

            var eyeWorld = (Vector3)GetEyePosition();
            var insideForest = CombatForestFogClipper.IsInsideLimitedDepthForest(eyeWorld, 0f);
            var insideCloud = CombatBlockingTerrainClipper.IsInsideBlockingTerrain(eyeWorld, 0f);
            var innerZoneReach = CombatForestFogClipper.AnyCachedZoneIntersectsVisionDisc(
                eyeWorld,
                TotalRevealerRadius,
                CombatForestFogPassSettings.MovingOpenGroundZoneReachFraction);
            var isOpenGroundForSkip = !insideForest && !insideCloud && !innerZoneReach;

            if (!CombatForestFogPassSettings.ShouldDeferMovingFullUpdate(
                    lastMovingLineOfSightUpdateTime,
                    Time.time,
                    GetInstanceID()))
            {
                return;
            }

            if (isOpenGroundForSkip
                && CombatForestFogPassSettings.SkipFullLineOfSightInOpenGroundWhileMoving
                && hasCalculatedLineOfSightPose)
            {
                skipFullLineOfSightThisFrame = true;
                return;
            }

            if (insideForest || insideCloud)
            {
                return;
            }

            if (CombatForestFogPassSettings.UseWallRayDirectionsWhileMoving
                && !CombatForestFogPassSettings.ThrottleMovingWallRayTerrainRecalc)
            {
                return;
            }

            skipMovingTerrainRecalcThisFrame = true;
        }

        private void ApplyMovingWallRaycastResolutionIfNeeded()
        {
            if (!CombatForestFogPassSettings.EnableMovingPerfProfile
                || !CombatForestFogPassSettings.UseMovingWallRaycastResolution)
            {
                return;
            }

            if (!movingWallRaycastResolutionActive)
            {
                stationaryRaycastResolution = RaycastResolution;
                stationaryNumExtraIterations = NumExtraIterations;
                stationaryNumExtraRaysOnIteration = NumExtraRaysOnIteration;
            }

            var movingResolution = CombatForestFogPassSettings.MovingWallRaycastResolutionDegrees;
            if (Mathf.Abs(RaycastResolution - movingResolution) > 0.001f)
            {
                RaycastResolution = movingResolution;
            }

            if (CombatForestFogPassSettings.UseMovingWallEdgeRefinement)
            {
                var targetIterations = CombatForestFogPassSettings.MovingWallExtraIterations;
                var targetRays = CombatForestFogPassSettings.ClampMovingWallExtraRaysPerIteration(
                    CombatForestFogPassSettings.MovingWallExtraRaysPerIteration);
                if (NumExtraIterations != targetIterations)
                {
                    NumExtraIterations = targetIterations;
                }

                if (NumExtraRaysOnIteration != targetRays)
                {
                    NumExtraRaysOnIteration = targetRays;
                }
            }

            movingWallRaycastResolutionActive = true;
        }

        private void RestoreStationaryWallRaycastResolution()
        {
            if (!movingWallRaycastResolutionActive)
            {
                return;
            }

            if (Mathf.Abs(RaycastResolution - stationaryRaycastResolution) > 0.001f)
            {
                RaycastResolution = stationaryRaycastResolution;
            }

            if (NumExtraIterations != stationaryNumExtraIterations)
            {
                NumExtraIterations = stationaryNumExtraIterations;
            }

            if (NumExtraRaysOnIteration != stationaryNumExtraRaysOnIteration)
            {
                NumExtraRaysOnIteration = stationaryNumExtraRaysOnIteration;
            }

            movingWallRaycastResolutionActive = false;
        }

        /// <summary>
        /// Re-uploads the last sight polygon at the current eye position without raycasts.
        /// </summary>
        private void ApplyRevealerPositionOnly()
        {
            if (!IsRegistered)
            {
                return;
            }

            var fow = FogOfWarWorld.instance;
            if (fow == null)
            {
                return;
            }

            EyePosition = GetEyePosition();
            SetPositionAndHeight();
            ApplyData();
        }

        private bool HasMovedSinceLastLineOfSightCalculation()
        {
            var eyeWorld = (Vector3)GetEyePosition();
            var eyeYaw = transform.eulerAngles.y;
            return (eyeWorld - lastCalculatedEyeWorld).sqrMagnitude > StationaryEyeDistanceWorldSq
                || Mathf.Abs(Mathf.DeltaAngle(eyeYaw, lastCalculatedEyeYaw)) > StationaryYawDegrees;
        }

        private void CaptureLineOfSightPose()
        {
            lastCalculatedEyeWorld = (Vector3)GetEyePosition();
            lastCalculatedEyeYaw = transform.eulerAngles.y;
            hasCalculatedLineOfSightPose = true;
            if (pawnIsMoving && !skipMovingTerrainRecalcThisFrame)
            {
                lastMovingLineOfSightUpdateTime = Time.time;
            }
        }

        private void InvalidateLineOfSightPose()
        {
            hasCalculatedLineOfSightPose = false;
        }

        private void CompletePhaseOneBeforeForestClip()
        {
            PreReqJobHandle.Complete();
            FirstIterationPointsAndConditionsJobHandle.Complete();
        }

        private void ClearForestDebug()
        {
            forestDebugContour.Clear();
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (drawForestClipDebug && forestDebugContour.HasContour)
            {
                forestDebugContour.DrawGizmos(debugClipRayColor, debugBridgeRayColor, debugContourColor);
            }

            if (drawWallBaselineProof && wallBaselineProofDrawer.HasData)
            {
                wallBaselineProofDrawer.DrawGizmos(
                    debugSparseWallBaselineColor,
                    debugDenseWallHitColor,
                    debugWallViolationColor);
            }
            if (drawShaderUploadPolygons && shaderUploadPolygonDrawer.HasData)
            {
                shaderUploadPolygonDrawer.DrawGizmos(
                    debugBaselineUploadColor,
                    debugTerrainUploadColor,
                    debugTerrainClipTickColor,
                    debugBaselineWallChordColor,
                    debugEffectiveBoundaryColor);
            }
        }

        private void OnDisable()
        {
            pendingLineOfSightRecalculation = false;
            ClearWallProofGameViewLines();
            ClearShaderUploadGameViewLines();
        }
    }
}
