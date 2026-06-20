using System.Collections.Generic;
using FOW;
using Pathfinding;
using UnityEngine;

namespace IronKingdoms.Combat
{
    [DefaultExecutionOrder(-150)]
    public partial class TestLevelUnitController : MonoBehaviour
    {
        private const float AiInRangeTolerance = 0.95f;
        private const float AiDesiredStopFactor = 0.85f;
        private const float AiMinimumStopDistance = 0.2f;
        private const float RadiusToDiameterMultiplier = 2f;
        private const float PositionArrivalTolerance = 0.05f;
        private const float MovementBudgetEpsilon = 0.001f;
        private const float VisualizerLineWidth = 0.06f;
        // 5 mm physical base height: cylinder native height = 2 units, base scale = 30 mm per unit → scaleY = 5/(30×2)
        private const float PawnBaseHeightScale = 5f / 60f;
        private const int AttackRingSegments = 48;
        private const float GroundYPosition = 0f;
        private const float MinimumVectorSqrMagnitude = 0.0001f;
        private const int LeftMouseButton = 0;
        private const int RightMouseButton = 1;
        private const int MiddleMouseButton = 2;
        private const float RosterAreaX = 12f;
        private const float RosterAreaY = 12f;
        private const float RosterAreaWidth = 320f;
        private const float RosterAreaHeight = 300f;
        private const float SelectedUnitPanelWidth = 280f;
        private const float SelectedUnitPanelHeight = 310f;
        private const float SelectedUnitPanelChromeHeight = 28f;
        private const float SelectedUnitPanelOffsetX = 12f;
        private const float SelectedUnitPanelOffsetY = 12f;
        private const float ActionBarWidth = 560f;
        private const float ActionBarHeight = 132f;
        private const float ActionBarBottomMargin = 12f;
        private const float HoverPanelWidth = 280f;
        private const float HoverPanelHeight = 122f;
        private const float HoverPanelScreenPadding = 4f;
        private const float HoverPanelMouseOffset = 14f;
        private const float CombatLogPanelWidth = 380f;
        private const float CombatLogPanelHeight = 240f;
        private const float CombatLogPanelRightMargin = 12f;
        private const float CombatLogPanelTopMargin = 12f;
        private const float CameraControlsPanelWidth = 460f;
        private const float CameraControlsPanelHeight = 54f;
        private const float CameraControlsPanelTopMargin = 12f;
        private const int CombatLogMaxEntries = 20;
        private const float DoubleClickIntervalSeconds = 0.3f;
        private const float PathPreviewUpdateDistance = 0.4f;
        private const float PathPreviewReuseToleranceMultiplier = 1.5f;
        private const float PathPreviewMinInterval = 0.08f;
        private const float PathVisualizationHeight = 0.05f;
        private const int UnitNavmeshCutCircleResolution = 24;
        private const float UnitNavmeshCutMinimumHeight = 1f;
        private const float UnitNavmeshCutUpdateDistance = 0.1f;
        private const float NavmeshCutHeightMultiplier = 2f;
        private const int WeaponRangeRingSegments = 64;
        private const float FloatingDamageLifetime = 1.2f;
        private const float FloatingDamageRiseSpeed = 55f;
        private const float HoverPanelAttackExtraHeight = 34f;
        private const float RunMovementMultiplier = 2f;
        private const float ChargeMovementBonus = 3f;
        private const int AimToHitBonus = 2;
        private const float MaxExecutedMoveSpeedWorldUnitsPerSecond = 5f;

        [SerializeField] private List<UnitTypeDefinition> playerUnits = new();
        [SerializeField] private List<UnitTypeDefinition> enemyUnits = new();
        [SerializeField] private Transform playerSpawnAnchor;
        [SerializeField] private Transform enemySpawnAnchor;
        [SerializeField, Min(0.5f)] private float spawnSpacing = 2f;
        [SerializeField, Min(0.1f)] private float aiThinkInterval = 0.5f;
        [SerializeField] private CombatCameraManager cameraManager;
        [SerializeField] private NavPathBuilder navPathBuilder;
        [SerializeField] private FogOfWarWorld fogOfWarWorld;
        [SerializeField] private CombatDefinitionCatalog definitionCatalog;
        [SerializeField] private Vector2 fogWorldBoundsCenter = Vector2.zero;
        [SerializeField] private Vector2 fogWorldBoundsSize = new Vector2(24f, 24f);
        [SerializeField, Range(0.05f, 1f)] private float fogExploredShroudVisibility = 0.35f;
        [SerializeField, Min(0.05f)] private float fogVisionEdgeSoftenDistance = 0.75f;
        [SerializeField, Min(1)] private int maxFogRevealersPerFrame = 12;

        [Header("Fog — Stationary")]
        [SerializeField, Range(0.25f, 2f)] private float fogWallRaycastResolution = 1f;

        [Header("Fog — Moving Performance")]
        [Tooltip("Master switch for throttled LOS, coarser wall rays, and reduced terrain LUT while pathing.")]
        [SerializeField] private bool fogEnableMovingPerfProfile = true;
        [Tooltip("Full wall+terrain LOS rate while moving. 0 = every frame. 30 ≈ half the raycast/LUT cost at 60 FPS.")]
        [SerializeField, Range(0f, 60f)] private float fogMovingLineOfSightTargetHz = 30f;
        [Tooltip("Used only when Target Hz is 0. Skip N-1 of every N frames.")]
        [SerializeField, Range(1, 8)] private int fogMovingLineOfSightFrameInterval = 2;
        [Tooltip("Uses a wider wall ray step while moving. Keep off unless you need more CPU; enable edge refinement below if you turn this on.")]
        [SerializeField] private bool fogUseCoarserWallRaysWhileMoving;
        [SerializeField, Range(0.5f, 4f)] private float fogMovingWallRaycastResolution = 2f;
        [Tooltip("Subdivide coarse moving wall rays at hit/miss transitions so corners still line up.")]
        [SerializeField] private bool fogUseMovingWallEdgeRefinement = true;
        [SerializeField, Range(0, 4)] private int fogMovingWallExtraIterations = 2;
        [Tooltip("Stock FOW sub-iteration buffers hold at most 5 extra rays per pass.")]
        [SerializeField, Range(1, 5)] private int fogMovingWallExtraRaysPerIteration = 3;
        [SerializeField] private bool fogUseReducedTerrainLutWhileMoving = true;
        [SerializeField, Range(60, 720)] private int fogMovingTerrainLutSamples = 120;
        [SerializeField] private bool fogSkipTerrainPostFiltersWhileMoving = true;
        [Tooltip("When on, reduced LUT applies inside/near forest and cloud while moving (much cheaper).")]
        [SerializeField] private bool fogAllowReducedLutNearTerrainWhileMoving = true;

        [Header("Fog — Debug")]
        [SerializeField] private bool debugUseCrispFogRendering = true;
        [SerializeField] private bool debugUseForestFogPass = true;
        [SerializeField] private bool debugShowWallBaselineProof = false;
        [SerializeField] private bool debugShowShaderUploadPolygons = false;
        [SerializeField] private bool debugShowFogTextureBoundary = false;
        [SerializeField] private bool autoSpawnOnStart = true;
        private MatchArmySpawner matchArmySpawner;

        private readonly List<Unit> playerRuntimeUnits = new();
        private readonly List<Unit> enemyRuntimeUnits = new();
        private readonly List<Unit> allRuntimeUnits = new();
        private readonly Plane boardPlane = new(Vector3.up, Vector3.zero);
        private readonly RaycastHit[] terrainRaycastBuffer = new RaycastHit[16];
        private readonly RaycastHit[] lineOfSightRaycastBuffer = new RaycastHit[32];
        private Unit selectedUnit;
        private TurnSide activeTurnSide = TurnSide.Player;
        private float aiThinkTimer;
        private Unit activeEnemyUnit;
        private Unit activeEnemyTarget;
        private int enemyActivationIndex;
        private bool enemyIssuedMoveForActiveUnit;
        private bool enemyResolvedActionForActiveUnit;
        private Unit hoveredEnemyUnit;

        private UnitActionMode currentPlayerMode = UnitActionMode.None;
        private MovementStepOption selectedMovementOption = MovementStepOption.Advance;
        private int selectedAttackWeaponIndex;
        private LineRenderer movementPathLine;
        private LineRenderer weaponRangeRingLine;
        private readonly List<LineRenderer> attackTargetRings = new();
        private readonly List<FloatingDamageEntry> floatingDamageEntries = new();
        private readonly List<string> combatLog = new();
        private Vector2 combatLogScrollPosition;
        private Vector2 selectedUnitPanelScrollPosition;
        private readonly CombatFogTextureBoundaryDrawer fogTextureBoundaryDrawer = new();
        private GUIStyle floatingDamageStyle;
        private GUIStyle floatingDamageShadowStyle;
        private GUIStyle coverPopupStyle;
        private GUIStyle coverPopupShadowStyle;
        private GameObject destinationMarkerObject;
        private Material visualizerMaterial;
        private Unit lastClickedPlayerUnit;
        private float lastClickedPlayerUnitClickTime = float.NegativeInfinity;
        private int uiCancelFrame = -1;

        // Movement preview state ----------------------------------------------------------------
        // previewPath holds waypoints for the current hover (nav path, or a straight line for charge).
        // Nav paths are set asynchronously; charge paths are built synchronously each hover update.
        private List<Vector3> previewPath;
        private bool previewPathPending;
        private Vector3 previewPathTo;
        private float previewMovementBudget;
        private float stagedMoveAmountInches;
        private float stagedRoughTerrainInches;
        private bool hasStagedMoveAmount;
        private float lastPathPreviewTime;
        private readonly List<Vector3> chargePathScratch = new(2);

        // Line-of-sight result cache ------------------------------------------------------------
        // Avoids repeated Physics.Raycast calls every frame for fog visibility. The cache is keyed
        // by [observerIndex * unitCount + targetIndex] and invalidated whenever any unit moves.
        private bool[] losCacheValid;
        private bool[] losCacheResult;
        private int losDirtyVersion;
        private int losCachedVersion = -1;
        private bool playerFogRevealerActivationDirty;
        private bool fogRevealerSettingsDirty;

        private void Awake()
        {
            CombatStartupLog.Log($"Awake on '{name}'. autoSpawnOnStart={autoSpawnOnStart}.");
            EnsureCameraManagerAssigned();
            EnsureNavPathBuilderAssigned();
            EnsureMatchArmySpawnerAssigned();
            EnsureDefinitionCatalogAssigned();
            EnsureFogOfWarWorldAssigned();
            ConfigureFogOfWarWorld();
            EnsureFogOfWarCameraEffectAssigned();
            ApplyFogPassSettingsFromBootstrap();
        }

        private void ApplyFogPassSettingsFromBootstrap()
        {
            CombatForestFogPassSettings.UseForestPass = debugUseForestFogPass;
            CombatForestFogPassSettings.WallRaycastResolutionDegrees = fogWallRaycastResolution;
            CombatForestFogPassSettings.EnableMovingPerfProfile = fogEnableMovingPerfProfile;
            CombatForestFogPassSettings.MovingLineOfSightTargetHz = fogMovingLineOfSightTargetHz;
            CombatForestFogPassSettings.MovingLineOfSightUpdateInterval = fogMovingLineOfSightFrameInterval;
            CombatForestFogPassSettings.UseMovingWallRaycastResolution = fogUseCoarserWallRaysWhileMoving;
            CombatForestFogPassSettings.MovingWallRaycastResolutionDegrees = fogMovingWallRaycastResolution;
            CombatForestFogPassSettings.UseMovingWallEdgeRefinement = fogUseMovingWallEdgeRefinement;
            CombatForestFogPassSettings.MovingWallExtraIterations = fogMovingWallExtraIterations;
            CombatForestFogPassSettings.MovingWallExtraRaysPerIteration =
                CombatForestFogPassSettings.ClampMovingWallExtraRaysPerIteration(fogMovingWallExtraRaysPerIteration);
            CombatForestFogPassSettings.UseReducedTerrainLutWhileMoving = fogUseReducedTerrainLutWhileMoving;
            CombatForestFogPassSettings.MovingTerrainLutSamples = fogMovingTerrainLutSamples;
            CombatForestFogPassSettings.MovingSkipTerrainPostFilters = fogSkipTerrainPostFiltersWhileMoving;
            CombatForestFogPassSettings.AllowReducedTerrainLutNearZonesWhileMoving =
                fogAllowReducedLutNearTerrainWhileMoving;
        }

        private void Start()
        {
            if (!autoSpawnOnStart)
            {
                CombatStartupLog.Log(
                    $"Start on '{name}': autoSpawnOnStart=false (expecting CombatMapSetup or manual spawn). phase={CombatMatchSetup.CurrentPhase}.");
                return;
            }

            CombatStartupLog.Log($"Start on '{name}': launching RunMatchPhases coroutine (no additive map load).");
            StartCoroutine(CombatMatchSetup.RunMatchPhases(this));
        }

        private void Update()
        {
            UpdateUnitNavmeshCutActivation(GetPathingUnitForNavmeshClearance());
            cameraManager?.Tick(IsMouseOverGameplayUi());
            TickFloatingDamage(Time.deltaTime);

            if (!IsMatchReady)
            {
                return;
            }

            if (activeTurnSide == TurnSide.Player)
            {
                if (Input.GetKeyDown(KeyCode.Escape) && selectedUnit != null && currentPlayerMode == UnitActionMode.None)
                {
                    SelectUnit(null);
                    return;
                }

                HandlePlayerInput();
            }

            TickMovement(Time.deltaTime);
            TickEnemyAi(Time.deltaTime);
            UpdateMovementVisualizer();
            UpdateWeaponRangeRing();
            UpdateFogOfWarVisibility();
            UpdateHoveredEnemy();
        }

        private void LateUpdate()
        {
            if (!IsMatchReady)
            {
                return;
            }

            var fow = FogOfWarWorld.instance;
            if (fow == null || !fow.IsInPhasedUpdate)
            {
                var fogChanged = SyncPlayerFogRevealerActivation();
                if (fogChanged || playerFogRevealerActivationDirty)
                {
                    UpdateFogOfWarVisibility();
                }

                if (fogRevealerSettingsDirty)
                {
                    fogRevealerSettingsDirty = false;
                    ApplyFogPassSettingsFromBootstrap();
                    RefreshAllFogRevealersAfterForestPassToggle();
                }
            }

            DrawFogTextureBoundaryDebugIfNeeded();
        }

        private void DrawFogTextureBoundaryDebugIfNeeded()
        {
            fogTextureBoundaryDrawer.SetLineParent(transform);

            if (!debugShowFogTextureBoundary || selectedUnit == null)
            {
                fogTextureBoundaryDrawer.ClearGameViewLines();
                return;
            }

            if (FogOfWarWorld.instance == null)
            {
                fogTextureBoundaryDrawer.ClearGameViewLines();
                return;
            }

            var revealer = GetFogRevealer(selectedUnit);
            if (revealer == null || !revealer.isActiveAndEnabled)
            {
                fogTextureBoundaryDrawer.ClearGameViewLines();
                return;
            }

            if (revealer.IsPawnMoving && (Time.frameCount & 1) == 0)
            {
                return;
            }

            fogTextureBoundaryDrawer.DrawEffectiveFogBoundaryAroundRevealer(
                revealer,
                boundaryColor: new Color(1f, 0.85f, 0.1f, 1f),
                drawForestFootprints: !revealer.IsPawnMoving);
        }

        private void MarkPlayerFogRevealerActivationDirty()
        {
            playerFogRevealerActivationDirty = true;
        }

        private void TryApplyPlayerFogRevealerActivationIfSafe()
        {
            if (!IsMatchReady)
            {
                return;
            }

            var fow = FogOfWarWorld.instance;
            if (fow != null && fow.IsInPhasedUpdate)
            {
                return;
            }

            playerFogRevealerActivationDirty = false;
            if (SyncPlayerFogRevealerActivation())
            {
                UpdateFogOfWarVisibility();
            }
        }

        private void BuildVisualizers()
        {
            if (movementPathLine != null)
            {
                return;
            }

            var foundShader = Shader.Find("Sprites/Default")
                ?? Shader.Find("Hidden/Internal-Colored")
                ?? Shader.Find("Unlit/Color");
            if (foundShader != null)
            {
                visualizerMaterial = new Material(foundShader);
            }

            var lineObj = new GameObject("MovementPathLine");
            lineObj.transform.SetParent(transform);
            movementPathLine = lineObj.AddComponent<LineRenderer>();
            movementPathLine.widthMultiplier = VisualizerLineWidth;
            movementPathLine.positionCount = 2;
            movementPathLine.useWorldSpace = true;
            ApplyMovementPathLineWorldUp();
            if (visualizerMaterial != null)
            {
                movementPathLine.material = visualizerMaterial;
            }

            movementPathLine.enabled = false;

            var rangeRingObj = new GameObject("WeaponRangeRing");
            rangeRingObj.transform.SetParent(transform);
            weaponRangeRingLine = rangeRingObj.AddComponent<LineRenderer>();
            weaponRangeRingLine.widthMultiplier = VisualizerLineWidth;
            weaponRangeRingLine.positionCount = WeaponRangeRingSegments + 1;
            weaponRangeRingLine.useWorldSpace = true;
            weaponRangeRingLine.loop = false;
            if (visualizerMaterial != null)
            {
                weaponRangeRingLine.material = visualizerMaterial;
            }

            weaponRangeRingLine.enabled = false;

            destinationMarkerObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            destinationMarkerObject.name = "DestinationMarker";
            destinationMarkerObject.transform.SetParent(transform);
            destinationMarkerObject.transform.localScale = new Vector3(1f, PawnBaseHeightScale, 1f);
            var markerCollider = destinationMarkerObject.GetComponent<Collider>();
            if (markerCollider != null)
            {
                Destroy(markerCollider);
            }

            var markerRenderer = destinationMarkerObject.GetComponent<Renderer>();
            if (markerRenderer != null && visualizerMaterial != null)
            {
                markerRenderer.material = new Material(visualizerMaterial);
            }

            destinationMarkerObject.SetActive(false);
        }

        private void UpdateMovementVisualizer()
        {
            hasStagedMoveAmount = false;
            stagedMoveAmountInches = 0f;
            stagedRoughTerrainInches = 0f;

            if (movementPathLine == null)
            {
                return;
            }

            if (selectedUnit == null || !selectedUnit.IsAlive || currentPlayerMode != UnitActionMode.Move || activeTurnSide != TurnSide.Player)
            {
                movementPathLine.enabled = false;
                destinationMarkerObject.SetActive(false);
                return;
            }

            var activeCamera = cameraManager != null ? cameraManager.ActiveCamera : Camera.main;
            if (activeCamera == null)
            {
                movementPathLine.enabled = false;
                destinationMarkerObject.SetActive(false);
                return;
            }

            var ray = activeCamera.ScreenPointToRay(Input.mousePosition);
            if (!TryGetTerrainHitPoint(ray, out var hoverPos))
            {
                movementPathLine.enabled = false;
                destinationMarkerObject.SetActive(false);
                return;
            }
            if (!IsFiniteWorldPoint(hoverPos))
            {
                movementPathLine.enabled = false;
                destinationMarkerObject.SetActive(false);
                return;
            }

            var unitPos = selectedUnit.GetFeetPosition();

            // Recompute effective budget; invalidate cached path if it changed.
            var effectiveBudget = GetEffectivePreviewMovementBudget();
            if (!Mathf.Approximately(effectiveBudget, previewMovementBudget))
            {
                previewMovementBudget = effectiveBudget;
                previewPath = null;
                previewPathTo = Vector3.positiveInfinity;
            }

            // Charge: straight line on the XZ plane, snapped to the nearest valid nav point along that ray.
            var horizontalDist = new Vector2(previewPathTo.x - hoverPos.x, previewPathTo.z - hoverPos.z).magnitude;
            if (selectedMovementOption == MovementStepOption.Charge)
            {
                previewPathTo = hoverPos;
                previewPath ??= new List<Vector3>(2);
                if (!selectedUnit.TryResolveChargePath(navPathBuilder, hoverPos, previewPath, out _))
                {
                    previewPath.Clear();
                }
            }
            else if (navPathBuilder != null && !previewPathPending && (horizontalDist >= PathPreviewUpdateDistance
                || Time.unscaledTime - lastPathPreviewTime >= PathPreviewMinInterval && horizontalDist > 0.01f))
            {
                previewPathTo = hoverPos;
                lastPathPreviewTime = Time.unscaledTime;
                previewPathPending = true;

                UpdateUnitNavmeshCutActivation(selectedUnit);
                var graphMask = selectedUnit.GetPathGraphMask(navPathBuilder);
                navPathBuilder.RequestAsync(unitPos, hoverPos, result =>
                {
                    previewPathPending = false;
                    // Only accept the result if we're still in Move mode for the same unit.
                    if (selectedUnit == null || currentPlayerMode != UnitActionMode.Move
                        || selectedMovementOption == MovementStepOption.Charge)
                    {
                        return;
                    }

                    previewPath = IsValidPreviewPath(result) ? result : null;
                }, graphMask);
            }

            // Determine reachability for colour: compare full path length to budget.
            var hasPreviewPath = IsValidPreviewPath(previewPath);
            var withinRange = hasPreviewPath;
            Vector3? movementStopPoint = null;
            if (hasPreviewPath)
            {
                var previewUnitRadius = selectedUnit.GetCollisionRadius();
                var fullLength = selectedUnit.CalculatePathMovementCostInInches(
                    previewPath, selectedUnit, selectedMovementOption, previewUnitRadius);
                stagedMoveAmountInches = Mathf.Min(fullLength, effectiveBudget);
                stagedRoughTerrainInches = selectedUnit.CalculatePathRoughTerrainPhysicalInches(
                    previewPath, stagedMoveAmountInches, selectedUnit, selectedMovementOption, previewUnitRadius);
                hasStagedMoveAmount = true;

                withinRange = fullLength <= effectiveBudget + CombatScale.WorldUnitsToInches(PositionArrivalTolerance);
                if (selectedUnit.TryGetPathStopPointAtMovementBudget(
                        previewPath, effectiveBudget, selectedUnit, selectedMovementOption, out var stopPoint, previewUnitRadius))
                {
                    movementStopPoint = stopPoint;
                }
            }

            var pathColor = withinRange
                ? new Color(0.15f, 0.85f, 0.85f, 0.85f)
                : new Color(0.95f, 0.35f, 0.15f, 0.85f);
            var pathFadeColor = withinRange
                ? new Color(0.15f, 0.85f, 0.85f, 0.35f)
                : new Color(0.95f, 0.35f, 0.15f, 0.35f);
            var markerColor = withinRange
                ? new Color(0.15f, 0.85f, 0.85f, 0.8f)
                : new Color(0.95f, 0.35f, 0.15f, 0.8f);

            // Draw path waypoints (nav path or straight-line charge).
            movementPathLine.enabled = hasPreviewPath;
            if (hasPreviewPath)
            {
                ApplyMovementPathLineWorldUp();
                var previewPointCount = previewPath.Count;
                movementPathLine.positionCount = previewPointCount;
                var flatPathY = previewPath[previewPointCount - 1].y + PathVisualizationHeight;
                for (var i = 0; i < previewPointCount; i++)
                {
                    var wp = previewPath[i];
                    wp.y = flatPathY;
                    movementPathLine.SetPosition(i, wp);
                }
                movementPathLine.startColor = pathColor;
                movementPathLine.endColor = pathFadeColor;
            }

            // Destination marker reflects the effective movement endpoint from the nav path.
            var dest = movementStopPoint ?? (hasPreviewPath ? previewPath[previewPath.Count - 1] : hoverPos);
            dest.y = Mathf.Max(GroundYPosition + 0.01f, dest.y + 0.01f);
            if (!IsFiniteWorldPoint(dest))
            {
                destinationMarkerObject.SetActive(false);
                return;
            }

            destinationMarkerObject.SetActive(true);
            destinationMarkerObject.transform.position = dest;
            var markerRenderer = destinationMarkerObject.GetComponent<Renderer>();
            if (markerRenderer != null)
            {
                markerRenderer.material.color = markerColor;
            }
        }

        private void RefreshAttackRangeRing()
        {
            HideAttackTargetRings();
            if (selectedUnit == null || !selectedUnit.IsAlive || currentPlayerMode != UnitActionMode.Attack)
            {
                return;
            }

            var weapon = GetSelectedAttackWeapon(selectedUnit);
            var ringColor = new Color(0.95f, 0.55f, 0.1f, 0.75f);
            var ringIndex = 0;
            foreach (var enemy in enemyRuntimeUnits)
            {
                if (!enemy.IsAlive || !enemy.IsVisibleToPlayer || !CanUnitTarget(selectedUnit, enemy, weapon))
                {
                    continue;
                }

                var ring = GetOrCreateAttackTargetRing(ringIndex);
                var radius = enemy.GetTargetRingRadius();
                DrawRing(ring, enemy.Pawn.transform.position, radius, ringColor);
                ringIndex++;
            }
        }

        private void HideAttackTargetRings()
        {
            foreach (var ring in attackTargetRings)
            {
                ring.enabled = false;
            }
        }

        private LineRenderer GetOrCreateAttackTargetRing(int index)
        {
            if (index < attackTargetRings.Count)
            {
                return attackTargetRings[index];
            }

            var ringObj = new GameObject($"AttackTargetRing_{index}");
            ringObj.transform.SetParent(transform);
            var ring = ringObj.AddComponent<LineRenderer>();
            ring.widthMultiplier = VisualizerLineWidth;
            ring.positionCount = AttackRingSegments + 1;
            ring.useWorldSpace = true;
            ring.loop = false;
            if (visualizerMaterial != null)
            {
                ring.material = visualizerMaterial;
            }

            ring.enabled = false;
            attackTargetRings.Add(ring);
            return ring;
        }

        private void DrawRing(LineRenderer ring, Vector3 center, float radius, Color color)
        {
            ring.enabled = true;
            ring.startColor = color;
            ring.endColor = color;
            for (var i = 0; i <= AttackRingSegments; i++)
            {
                var angle = (float)i / AttackRingSegments * Mathf.PI * 2f;
                ring.SetPosition(i, new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    0.05f,
                    center.z + Mathf.Sin(angle) * radius));
            }
        }

        private void SetCurrentMode(UnitActionMode mode)
        {
            currentPlayerMode = mode;
            if (mode == UnitActionMode.Attack && selectedUnit != null)
            {
                if (selectedUnit.Weapons == null || selectedUnit.Weapons.Length == 0)
                {
                    selectedAttackWeaponIndex = 0;
                    currentPlayerMode = UnitActionMode.None;
                    return;
                }

                selectedAttackWeaponIndex = Mathf.Clamp(selectedAttackWeaponIndex, 0, selectedUnit.Weapons.Length - 1);
            }

            UpdateUnitNavmeshCutActivation(GetPathingUnitForNavmeshClearance());

            if (mode != UnitActionMode.Move)
            {
                previewPath = null;
                previewPathPending = false;
                previewPathTo = Vector3.positiveInfinity;
                hasStagedMoveAmount = false;
                stagedMoveAmountInches = 0f;

                if (movementPathLine != null)
                {
                    movementPathLine.enabled = false;
                }

                if (destinationMarkerObject != null)
                {
                    destinationMarkerObject.SetActive(false);
                }
            }

            if (mode == UnitActionMode.Attack)
            {
                RefreshAttackRangeRing();
            }
            else
            {
                HideAttackTargetRings();
            }
        }

        private void HideAllVisualizers()
        {
            if (movementPathLine != null)
            {
                movementPathLine.enabled = false;
            }

            HideAttackTargetRings();

            if (weaponRangeRingLine != null)
            {
                weaponRangeRingLine.enabled = false;
            }

            if (destinationMarkerObject != null)
            {
                destinationMarkerObject.SetActive(false);
            }
        }

        private Unit GetPathingUnitForNavmeshClearance()
        {
            return activeTurnSide == TurnSide.Player
                && currentPlayerMode == UnitActionMode.Move
                && selectedUnit != null
                && selectedUnit.IsAlive
                ? selectedUnit
                : null;
        }

        private void UpdateUnitNavmeshCutActivation(Unit pathingUnit = null)
        {
            var pathingRadius = pathingUnit != null ? pathingUnit.GetCollisionRadius() : 0f;
            var pathingGraphMask = pathingUnit != null
                ? pathingUnit.GetPathGraphMask(navPathBuilder)
                : default(GraphMask);
            var navmeshCutChanged = false;
            for (var i = 0; i < allRuntimeUnits.Count; i++)
            {
                var unit = allRuntimeUnits[i];
                if (unit?.Pawn == null)
                {
                    continue;
                }

                if (unit.NavmeshCut == null)
                {
                    continue;
                }

                var targetGraphMask = pathingUnit != null
                    ? pathingGraphMask
                    : unit.GetPathGraphMask(navPathBuilder);
                if (unit.NavmeshCut.graphMask != targetGraphMask)
                {
                    var cutWasEnabled = unit.NavmeshCut.enabled;
                    if (cutWasEnabled)
                    {
                        unit.NavmeshCut.enabled = false;
                    }

                    unit.NavmeshCut.graphMask = targetGraphMask;
                    if (cutWasEnabled)
                    {
                        unit.NavmeshCut.enabled = true;
                    }

                    navmeshCutChanged = true;
                }

                var isPathingUnit = pathingUnit != null && ReferenceEquals(unit, pathingUnit);
                var targetRadius = unit.GetCollisionRadius() + (isPathingUnit ? 0f : pathingRadius);
                if (!Mathf.Approximately(unit.NavmeshCut.circleRadius, targetRadius))
                {
                    unit.NavmeshCut.circleRadius = targetRadius;
                    navmeshCutChanged = true;
                }

                var targetEnabled = !unit.MoveTarget.HasValue && !isPathingUnit;
                if (unit.NavmeshCut.enabled != targetEnabled)
                {
                    unit.NavmeshCut.enabled = targetEnabled;
                    navmeshCutChanged = true;
                }
            }

            if (navmeshCutChanged)
            {
                NavPathBuilder.MarkNavmeshDirty();
            }
        }

        private void HandlePlayerInput()
        {
            if (TryConsumeUiClick())
            {
                return;
            }

            switch (currentPlayerMode)
            {
                case UnitActionMode.Move:
                    HandleMoveModeInput();
                    break;
                case UnitActionMode.Attack:
                    HandleAttackModeInput();
                    break;
                default:
                    HandleSelectionInput();
                    break;
            }
        }

        private void HandleSelectionInput()
        {
            if (!Input.GetMouseButtonDown(0))
            {
                return;
            }

            var activeCamera = cameraManager != null ? cameraManager.ActiveCamera : Camera.main;
            if (activeCamera == null)
            {
                return;
            }

            var ray = activeCamera.ScreenPointToRay(Input.mousePosition);
            if (TryGetClosestUnitFromRay(
                    ray,
                    unit => unit.IsPlayerControlled && unit.IsAlive,
                    out var clickedUnit))
            {
                HandlePlayerUnitClick(clickedUnit);
                return;
            }

            if (!RaycastForMouseHit(ray, out _))
            {
                return;
            }

            SelectUnit(null);
        }

        private void HandleMoveModeInput()
        {
            if (TryCancelModeOnRightClick())
            {
                return;
            }

            if (!Input.GetMouseButtonDown(0))
            {
                return;
            }

            if (selectedUnit == null || !selectedUnit.IsAlive)
            {
                return;
            }

            if (selectedUnit.HasActedThisTurn && !selectedUnit.HasRunActionThisTurn)
            {
                SetCurrentMode(UnitActionMode.None);
                return;
            }

            if (selectedUnit.HasChargedThisTurn)
            {
                return;
            }

            if (selectedMovementOption == MovementStepOption.Charge
                && (selectedUnit.HasAdvancedThisTurn || selectedUnit.HasRunActionThisTurn))
            {
                return;
            }

            if (selectedMovementOption == MovementStepOption.Run && selectedUnit.HasAdvancedThisTurn)
            {
                return;
            }

            if (selectedMovementOption == MovementStepOption.Advance
                && (selectedUnit.HasRunActionThisTurn || selectedUnit.HasChargedThisTurn))
            {
                return;
            }

            var activeCamera = cameraManager != null ? cameraManager.ActiveCamera : Camera.main;
            if (activeCamera == null)
            {
                return;
            }

            var ray = activeCamera.ScreenPointToRay(Input.mousePosition);

            if (TryGetClosestUnitFromRay(
                    ray,
                    unit => unit.IsPlayerControlled && unit.IsAlive,
                    out var clickedUnit))
            {
                HandlePlayerUnitClick(clickedUnit);
                return;
            }

            // Resolve the exact terrain point the player clicked (not just a flat plane).
            if (!TryGetTerrainHitPoint(ray, out var destination))
            {
                return;
            }

            if (selectedUnit.HasRunActionThisTurn)
            {
                selectedMovementOption = MovementStepOption.Run;
            }

            var movementBudget = selectedUnit.RemainingMovementThisTurn;
            switch (selectedMovementOption)
            {
                case MovementStepOption.Run:
                    if (!selectedUnit.HasRunActionThisTurn)
                    {
                        movementBudget *= RunMovementMultiplier;
                        selectedUnit.HasRunActionThisTurn = true;
                        selectedUnit.HasActedThisTurn = true;
                    }
                    break;
                case MovementStepOption.Charge:
                    if (GetSelectedAttackWeapon(selectedUnit).attackType == WeaponAttackType.Melee)
                    {
                        movementBudget += ChargeMovementBonus;
                    }

                    break;
            }

            selectedUnit.RemainingMovementThisTurn = movementBudget;
            selectedUnit.IsAimingThisTurn = false;
            selectedUnit.ActiveMovementStep = selectedMovementOption;

            if (selectedMovementOption == MovementStepOption.Charge)
            {
                if (selectedUnit.TryResolveChargePath(navPathBuilder, destination, chargePathScratch, out _))
                {
                    selectedUnit.HasChargedThisTurn = true;
                    selectedUnit.IssueMoveOrderFromPath(
                        navPathBuilder, chargePathScratch, movementBudget, selectedUnit, selectedMovementOption);
                }
            }
            else
            {
                if (selectedMovementOption == MovementStepOption.Advance)
                {
                    selectedUnit.HasAdvancedThisTurn = true;
                }

                // Reuse the preview path if it was computed for a position close enough to where
                // the player just clicked, so the unit follows exactly the path they saw.
                var clickedNearPreview = previewPath != null && previewPath.Count >= 2
                    && new Vector2(previewPathTo.x - destination.x, previewPathTo.z - destination.z).magnitude
                       <= PathPreviewUpdateDistance * PathPreviewReuseToleranceMultiplier;

                if (clickedNearPreview)
                {
                    selectedUnit.IssueMoveOrderFromPath(
                        navPathBuilder, previewPath, movementBudget, selectedUnit, selectedMovementOption);
                }
                else
                {
                    IssueMoveOrder(selectedUnit, destination, movementBudget);
                }
            }

            SetCurrentMode(UnitActionMode.None);
        }

        private void HandleAttackModeInput()
        {
            if (TryCancelModeOnRightClick())
            {
                return;
            }

            if (!Input.GetMouseButtonDown(0))
            {
                return;
            }

            if (selectedUnit == null || !selectedUnit.IsAlive || selectedUnit.HasActedThisTurn || selectedUnit.HasRunActionThisTurn)
            {
                return;
            }

            var attackWeapon = GetSelectedAttackWeapon(selectedUnit);

            var activeCamera = cameraManager != null ? cameraManager.ActiveCamera : Camera.main;
            if (activeCamera == null)
            {
                return;
            }

            var ray = activeCamera.ScreenPointToRay(Input.mousePosition);
            if (TryGetClosestUnitFromRay(
                    ray,
                    unit => unit.IsPlayerControlled && unit.IsAlive,
                    out var clickedPlayer))
            {
                HandlePlayerUnitClick(clickedPlayer);
                return;
            }

            if (TryGetClosestUnitFromRay(
                    ray,
                    unit => !unit.IsPlayerControlled && unit.IsAlive,
                    out var clickedEnemy)
                && clickedEnemy.IsVisibleToPlayer
                && CanUnitTarget(selectedUnit, clickedEnemy, attackWeapon))
            {
                ResolveAttack(selectedUnit, clickedEnemy, attackWeapon);
                selectedUnit.HasActedThisTurn = true;
                SetCurrentMode(UnitActionMode.None);
            }
        }

        private void TickMovement(float deltaTime)
        {
            for (var i = 0; i < allRuntimeUnits.Count; i++)
            {
                var unit = allRuntimeUnits[i];
                if (!unit.IsAlive || !unit.MoveTarget.HasValue)
                {
                    continue;
                }

                var unitRadius = unit.GetCollisionRadius();
                var remainingFrameTime = deltaTime;
                var safetySteps = 0;
                while (unit.MoveTarget.HasValue && remainingFrameTime > 0.0001f && safetySteps++ < 32)
                {
                    var targetPosition = unit.MoveTarget.Value;
                    var currentPosition = unit.Pawn.transform.position;
                    var distanceToTarget = Vector3.Distance(currentPosition, targetPosition);
                    if (distanceToTarget <= PositionArrivalTolerance)
                    {
                        distanceToTarget = 0f;
                    }

                    var terrainSpeedMultiplier = unit.GetMovementSpeedMultiplierAtPoint(
                        currentPosition, selectedUnit, selectedMovementOption, unitRadius);
                    var worldSpeedPerSecond = InchesToWorldUnits(unit.Definition.Stats.speed * terrainSpeedMultiplier);
                    worldSpeedPerSecond = Mathf.Min(worldSpeedPerSecond, MaxExecutedMoveSpeedWorldUnitsPerSecond);
                    if (worldSpeedPerSecond <= 0.0001f)
                    {
                        unit.MoveTarget = null;
                        unit.PathWaypoints = null;
                        unit.ActiveMovementStep = MovementStepOption.None;
                        break;
                    }

                    var maxStepForRemainingTime = worldSpeedPerSecond * remainingFrameTime;
                    var rawStep = Mathf.Min(maxStepForRemainingTime, distanceToTarget);
                    var allowedStep = unit.GetAffordableWorldStepAlongSegment(
                        currentPosition,
                        targetPosition,
                        rawStep,
                        selectedUnit,
                        selectedMovementOption,
                        unitRadius);
                    if (allowedStep <= 0f)
                    {
                        unit.MoveTarget = null;
                        unit.PathWaypoints = null;
                        unit.ActiveMovementStep = MovementStepOption.None;
                        break;
                    }

                    var nextPosition = Vector3.MoveTowards(currentPosition, targetPosition, allowedStep);
                    // Keep units grounded on terrain without forcing XZ onto navmesh every frame,
                    // so they do not stall when their path runs close to walls or other units.
                    nextPosition = unit.GetGroundedPositionKeepingXZ(nextPosition, navPathBuilder);
                    var movedDistance = Vector3.Distance(currentPosition, nextPosition);
                    if (movedDistance <= 0.0001f)
                    {
                        unit.MoveTarget = null;
                        unit.PathWaypoints = null;
                        unit.ActiveMovementStep = MovementStepOption.None;
                        break;
                    }

                    unit.Pawn.transform.position = nextPosition;
                    NotifyFogRevealerMoved(unit);
                    losDirtyVersion++;
                    var movementCost = unit.CalculateMovementCostForSegmentInInches(
                        currentPosition, nextPosition, selectedUnit, selectedMovementOption, unitRadius);
                    unit.RemainingMovementThisTurn = Mathf.Max(0f, unit.RemainingMovementThisTurn - movementCost);

                    var timeConsumed = movedDistance / worldSpeedPerSecond;
                    remainingFrameTime = Mathf.Max(0f, remainingFrameTime - timeConsumed);

                    var reachedCurrentTarget = Vector3.Distance(nextPosition, targetPosition) <= PositionArrivalTolerance
                        || unit.RemainingMovementThisTurn <= MovementBudgetEpsilon;
                    if (!reachedCurrentTarget)
                    {
                        break;
                    }

                    // Advance to the next waypoint if one is available.
                    var waypoints = unit.PathWaypoints;
                    var nextIndex = unit.PathWaypointIndex + 1;
                    if (waypoints != null && nextIndex < waypoints.Count
                        && unit.RemainingMovementThisTurn > MovementBudgetEpsilon)
                    {
                        unit.PathWaypointIndex = nextIndex;
                        var nextWaypoint = waypoints[nextIndex];
                        nextWaypoint = unit.GetGroundedPositionKeepingXZ(nextWaypoint, navPathBuilder);
                        unit.MoveTarget = nextWaypoint;
                        continue;
                    }

                    unit.MoveTarget = null;
                    unit.PathWaypoints = null;
                    unit.ActiveMovementStep = MovementStepOption.None;
                    break;
                }

                if (!unit.MoveTarget.HasValue)
                {
                    NotifyFogRevealerMovementEnded(unit);
                }
            }
        }

        private void TickEnemyAi(float deltaTime)
        {
            if (activeTurnSide != TurnSide.Enemy)
            {
                return;
            }

            aiThinkTimer -= deltaTime;
            if (aiThinkTimer > 0f)
            {
                return;
            }

            aiThinkTimer = aiThinkInterval;
            if (activeEnemyUnit == null)
            {
                if (!TryActivateNextEnemyUnit())
                {
                    StartPlayerTurn();
                    return;
                }

                return;
            }

            if (!activeEnemyUnit.IsAlive)
            {
                CompleteEnemyActivation();
                return;
            }

            if (!EnsureActiveEnemyTarget())
            {
                CompleteEnemyActivation();
                return;
            }

            if (!enemyIssuedMoveForActiveUnit)
            {
                enemyIssuedMoveForActiveUnit = true;
                ResolveEnemyMovement(activeEnemyUnit, activeEnemyTarget);
                return;
            }

            if (activeEnemyUnit.MoveTarget.HasValue)
            {
                return;
            }

            if (!enemyResolvedActionForActiveUnit)
            {
                enemyResolvedActionForActiveUnit = true;
                ResolveUnitAction(activeEnemyUnit, activeEnemyTarget);
                return;
            }

            CompleteEnemyActivation();
        }

        private void ResolveEnemyMovement(Unit enemy, Unit target)
        {
            if (target == null)
            {
                enemy.MoveTarget = null;
                return;
            }

            var enemyPosition = enemy.Pawn.transform.position;
            var targetPosition = target.Pawn.transform.position;
            var distance = Unit.GetPlanarDistance(enemyPosition, targetPosition);
            var desiredRange = enemy.GetLongestWeaponRange() + Unit.GetCombinedRadiiInches(enemy, target);
            if (distance <= desiredRange * AiInRangeTolerance)
            {
                enemy.MoveTarget = null;
                return;
            }

            var toTarget = targetPosition - enemyPosition;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < MinimumVectorSqrMagnitude)
            {
                enemy.MoveTarget = null;
                return;
            }

            var direction = toTarget.normalized;
            var stopDistance = Mathf.Max(AiMinimumStopDistance, desiredRange * AiDesiredStopFactor);
            var destination = targetPosition - direction * stopDistance;
            destination = enemy.GetGroundedPositionKeepingXZ(destination, navPathBuilder);
            IssueMoveOrder(enemy, destination);
        }

        private bool ResolveUnitAction(Unit unit, List<Unit> targets)
        {
            var target = Unit.FindNearestAlive(unit, targets);
            return ResolveUnitAction(unit, target);
        }

        private bool ResolveUnitAction(Unit unit, Unit target)
        {
            if (target == null)
            {
                return false;
            }

            var distance = unit.GetPlanarDistanceTo(target);
            var weapon = unit.GetBestWeaponForDistance(target, distance);
            if (weapon == null)
            {
                return false;
            }

            if (!CanUnitTarget(unit, target, weapon))
            {
                return false;
            }

            ResolveAttack(unit, target, weapon);
            unit.HasActedThisTurn = true;
            return true;
        }

        private bool TryActivateNextEnemyUnit()
        {
            while (enemyActivationIndex < enemyRuntimeUnits.Count)
            {
                var candidate = enemyRuntimeUnits[enemyActivationIndex++];
                if (!candidate.IsAlive)
                {
                    continue;
                }

                activeEnemyUnit = candidate;
                activeEnemyTarget = null;
                if (!EnsureActiveEnemyTarget())
                {
                    continue;
                }

                enemyIssuedMoveForActiveUnit = false;
                enemyResolvedActionForActiveUnit = false;
                return true;
            }

            return false;
        }

        private void CompleteEnemyActivation()
        {
            activeEnemyUnit = null;
            activeEnemyTarget = null;
            enemyIssuedMoveForActiveUnit = false;
            enemyResolvedActionForActiveUnit = false;
        }

        private bool EnsureActiveEnemyTarget()
        {
            if (activeEnemyUnit == null || !activeEnemyUnit.IsAlive)
            {
                activeEnemyTarget = null;
                return false;
            }

            if (activeEnemyTarget != null && activeEnemyTarget.IsAlive)
            {
                return true;
            }

            activeEnemyTarget = Unit.FindNearestAlive(activeEnemyUnit, playerRuntimeUnits);
            return activeEnemyTarget != null;
        }

        /// <summary>
        /// Returns the movement budget that will be applied if the player clicks right now, accounting
        /// for the current step option (Advance / Run / Charge). Used to keep the preview in sync with
        /// the actual movement that will be issued on click.
        /// </summary>
        private float GetEffectivePreviewMovementBudget()
        {
            if (selectedUnit == null)
            {
                return 0f;
            }

            var budget = selectedUnit.RemainingMovementThisTurn;
            switch (selectedMovementOption)
            {
                case MovementStepOption.Run:
                    if (!selectedUnit.HasRunActionThisTurn)
                    {
                        budget *= RunMovementMultiplier;
                    }
                    break;
                case MovementStepOption.Charge:
                    if (GetSelectedAttackWeapon(selectedUnit).attackType == WeaponAttackType.Melee)
                    {
                        budget += ChargeMovementBonus;
                    }

                    break;
            }

            return budget;
        }

        private void IssueMoveOrder(Unit unit, Vector3 destination, float? movementBudgetOverride = null)
        {
            if (unit == null || !unit.IsAlive || unit.Pawn == null || (unit.HasActedThisTurn && !unit.HasRunActionThisTurn))
            {
                return;
            }

            var remaining = movementBudgetOverride.HasValue
                ? Mathf.Max(0f, movementBudgetOverride.Value)
                : Mathf.Max(0f, unit.RemainingMovementThisTurn);
            if (remaining <= 0f)
            {
                unit.MoveTarget = null;
                unit.PathWaypoints = null;
                unit.ActiveMovementStep = MovementStepOption.None;
                return;
            }

            var current = unit.GetFeetPosition();
            var graphMask = unit.GetPathGraphMask(navPathBuilder);

            // Use NavPathBuilder to get a funnel-smoothed path, then clamp to budget.
            if (navPathBuilder != null)
            {
                UpdateUnitNavmeshCutActivation(unit);
                var smoothedPath = navPathBuilder.BuildSync(current, destination, graphMask);
                if (smoothedPath.Count >= 2)
                {
                    unit.IssueMoveOrderFromPath(
                        navPathBuilder, smoothedPath, remaining, selectedUnit, selectedMovementOption);
                    return;
                }
            }

            // Nav-only movement: do not issue direct movement when no nav path is available.
            unit.MoveTarget = null;
            unit.PathWaypoints = null;
            unit.ActiveMovementStep = MovementStepOption.None;
        }

        private static bool RaycastForMouseHit(Ray ray, out RaycastHit hit)
        {
            return Physics.Raycast(ray, out hit, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        }

        private bool TryGetClosestUnitFromRay(Ray ray, System.Func<Unit, bool> predicate, out Unit unit)
        {
            unit = null;
            if (predicate == null)
            {
                return false;
            }

            var hitCount = Physics.RaycastNonAlloc(
                ray,
                terrainRaycastBuffer,
                Mathf.Infinity,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);

            var closestDistance = float.MaxValue;
            for (var i = 0; i < hitCount; i++)
            {
                var hit = terrainRaycastBuffer[i];
                if (hit.collider == null || !UnitPawn.TryGetRuntimeUnit(hit.collider.gameObject, out var runtimeUnit))
                {
                    continue;
                }

                if (!predicate(runtimeUnit))
                {
                    continue;
                }

                if (hit.distance >= closestDistance)
                {
                    continue;
                }

                closestDistance = hit.distance;
                unit = runtimeUnit;
            }

            return unit != null;
        }

        /// <summary>
        /// Returns true if <paramref name="go"/> is a pawn belonging to any spawned unit.
        /// Used to distinguish unit colliders from terrain geometry when raycasting.
        /// </summary>
        private bool IsUnitPawn(GameObject go)
        {
            return go != null && go.GetComponentInParent<UnitPawn>() != null;
        }

        private static bool IsRoughTerrainCollider(Collider collider)
        {
            if (collider == null)
            {
                return false;
            }

            var zone = collider.GetComponentInParent<CombatZone>();
            return zone != null && zone.IsRoughTerrain;
        }

        private static bool IsForestFogZoneBlockerCollider(Collider collider)
        {
            return collider != null && collider.GetComponent<CombatForestFogBlocker>() != null;
        }

        /// <summary>
        /// Casts a ray against the 3D scene geometry and returns the first terrain hit point
        /// (ignoring unit pawns).  Falls back to <paramref name="boardPlane"/> when no geometry
        /// is hit, so the method always produces a valid world position.
        /// </summary>
        private bool TryGetTerrainHitPoint(Ray ray, out Vector3 point)
        {
            var hitCount = Physics.RaycastNonAlloc(
                ray,
                terrainRaycastBuffer,
                Mathf.Infinity,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);
            var closestDist = float.MaxValue;
            var found = false;
            point = Vector3.zero;
            for (var i = 0; i < hitCount; i++)
            {
                var h = terrainRaycastBuffer[i];
                if (!IsUnitPawn(h.collider.gameObject)
                    && !IsRoughTerrainCollider(h.collider)
                    && !IsForestFogZoneBlockerCollider(h.collider)
                    && h.distance < closestDist)
                {
                    closestDist = h.distance;
                    point = h.point;
                    found = true;
                }
            }

            if (found)
            {
                return true;
            }

            // No terrain geometry hit — fall back to the flat board plane.
            if (boardPlane.Raycast(ray, out var enter))
            {
                point = ray.GetPoint(enter);
                return true;
            }

            return false;
        }

        private static bool IsValidPreviewPath(IReadOnlyList<Vector3> path)
        {
            if (path == null || path.Count < 2)
            {
                return false;
            }

            foreach (var point in path)
            {
                if (!IsFiniteWorldPoint(point))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsFiniteWorldPoint(Vector3 point)
        {
            return float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z);
        }


        private void StartPlayerTurn()
        {
            activeTurnSide = TurnSide.Player;
            ResetMovementForTurn(playerRuntimeUnits);
            SelectUnit(Unit.FindFirstAlive(playerRuntimeUnits));
        }

        private void EndPlayerTurn()
        {
            if (activeTurnSide != TurnSide.Player)
            {
                return;
            }

            for (var i = 0; i < playerRuntimeUnits.Count; i++)
            {
                var unit = playerRuntimeUnits[i];
                if (!unit.IsAlive || unit.HasActedThisTurn)
                {
                    continue;
                }

                ResolveUnitAction(unit, enemyRuntimeUnits);
            }

            HideAllVisualizers();
            currentPlayerMode = UnitActionMode.None;
            StartEnemyTurn();
        }

        private void StartEnemyTurn()
        {
            activeTurnSide = TurnSide.Enemy;
            ResetMovementForTurn(enemyRuntimeUnits);
            aiThinkTimer = aiThinkInterval;
            enemyActivationIndex = 0;
            CompleteEnemyActivation();
        }

        private static void ResetMovementForTurn(List<Unit> units)
        {
            for (var i = 0; i < units.Count; i++)
            {
                units[i].ResetMovementForTurn();
            }
        }

        private void ResolveAttack(Unit attacker, Unit defender, WeaponProfile weapon)
        {
            var isMeleeAttack = weapon.AttackType == WeaponAttackType.Melee;
            var attackValue = attacker.GetAttackStatForWeapon(weapon);
            var attackModifier = attacker.GetToHitModifier();
            var attackStatLabel = isMeleeAttack ? "MAT" : "RAT";
            var atkDie1 = Random.Range(1, 7);
            var atkDie2 = Random.Range(1, 7);
            var extraDice = Mathf.Max(0, weapon.GetAttackDiceCount(false) - 2);
            var extraDiceTotal = 0;
            for (var i = 0; i < extraDice; i++)
            {
                extraDiceTotal += Random.Range(1, 7);
            }

            var attackRoll = atkDie1 + atkDie2 + extraDiceTotal + attackValue + attackModifier;
            var modifierText = FormatAttackModifierText(attackModifier);
            var effectiveDefense = defender.GetEffectiveDefense(attacker, weapon);
            if (!weapon.EvaluateAttackHit(atkDie1, atkDie2, attackRoll, effectiveDefense))
            {
                SpawnFloatingText(defender.GetCenterPosition(), "Miss!", new Color(1f, 0.9f, 0.2f, 1f));
                AddCombatLogEntry(
                    $"{attacker.Definition.DisplayName} → {defender.Definition.DisplayName}  " +
                    $"ATK [{atkDie1}+{atkDie2}]+{attackValue}{modifierText} {attackStatLabel} = {attackRoll} vs DEF {effectiveDefense} → Miss");
                attacker.IsAimingThisTurn = false;
                return;
            }

            var dmgDie1 = Random.Range(1, 7);
            var dmgDie2 = Random.Range(1, 7);
            var extraDmgDice = Mathf.Max(0, weapon.GetDamageDiceCount(false) - 2);
            var extraDmgTotal = 0;
            for (var i = 0; i < extraDmgDice; i++)
            {
                extraDmgTotal += Random.Range(1, 7);
            }

            var damageRoll = dmgDie1 + dmgDie2 + extraDmgTotal;
            var damage = weapon.EvaluateDamage(damageRoll, defender.Definition.Stats.armor);
            defender.Health = Mathf.Max(0, defender.Health - damage);
            var damageText = damage > 0 ? $"-{damage}" : "Blocked";
            var damageColor = damage > 0 ? new Color(1f, 0.15f, 0.15f, 1f) : new Color(0.7f, 0.7f, 0.7f, 1f);
            SpawnFloatingText(defender.GetCenterPosition(), damageText, damageColor);
            var logResult = damage > 0 ? $"-{damage} HP" : "Blocked";
            AddCombatLogEntry(
                $"{attacker.Definition.DisplayName} → {defender.Definition.DisplayName}  " +
                $"ATK [{atkDie1}+{atkDie2}]+{attackValue}{modifierText} {attackStatLabel} = {attackRoll} vs DEF {effectiveDefense} → Hit!  " +
                $"DMG [{dmgDie1}+{dmgDie2}]+{weapon.Power} POW = {damageRoll + weapon.Power} vs ARM {defender.Definition.Stats.armor} → {logResult}");
            attacker.IsAimingThisTurn = false;
            if (!defender.IsAlive)
            {
                defender.Pawn.SetActive(false);
                losDirtyVersion++;
                AddCombatLogEntry($"{defender.Definition.DisplayName} defeated!");
                if (ReferenceEquals(defender, selectedUnit))
                {
                    SelectUnit(Unit.FindFirstAlive(playerRuntimeUnits));
                }
            }
        }

        private void UpdateWeaponRangeRing()
        {
            if (weaponRangeRingLine == null)
            {
                return;
            }

            if (selectedUnit == null || !selectedUnit.IsAlive
                || currentPlayerMode != UnitActionMode.Attack
                || activeTurnSide != TurnSide.Player)
            {
                weaponRangeRingLine.enabled = false;
                return;
            }

            var weapon = GetSelectedAttackWeapon(selectedUnit);
            var center = selectedUnit.Pawn.transform.position;
            var radius = weapon.Range + selectedUnit.GetRadiusInches();
            radius = InchesToWorldUnits(radius);
            var color = new Color(0.95f, 0.85f, 0.1f, 0.7f);
            weaponRangeRingLine.enabled = true;
            weaponRangeRingLine.startColor = color;
            weaponRangeRingLine.endColor = color;
            weaponRangeRingLine.positionCount = WeaponRangeRingSegments + 1;
            for (var i = 0; i <= WeaponRangeRingSegments; i++)
            {
                var angle = (float)i / WeaponRangeRingSegments * Mathf.PI * 2f;
                weaponRangeRingLine.SetPosition(i, new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    0.05f,
                    center.z + Mathf.Sin(angle) * radius));
            }
        }

        private static string FormatAttackModifierText(int attackModifier)
        {
            if (attackModifier > 0)
            {
                return $" +{attackModifier}";
            }

            if (attackModifier < 0)
            {
                return $" {attackModifier}";
            }

            return string.Empty;
        }

        private void SelectUnit(Unit unit)
        {
            selectedUnit = unit != null && unit.IsAlive ? unit : null;
            selectedAttackWeaponIndex = 0;
            selectedMovementOption = MovementStepOption.Advance;
            SetCurrentMode(UnitActionMode.None);
            UpdateMovePreviewSizeForUnit(selectedUnit);
            UpdateNavGraphGizmoVisibility(selectedUnit);
            MarkPlayerFogRevealerActivationDirty();
            TryApplyPlayerFogRevealerActivationIfSafe();
            SyncWallBaselineProofOnRevealers();
            UpdateFogOfWarVisibility();
        }

        private static CombatFogOfWarRevealer3D GetFogRevealer(Unit unit)
        {
            return unit?.Pawn != null
                ? unit.Pawn.GetComponentInChildren<CombatFogOfWarRevealer3D>(true)
                : null;
        }

        private static void NotifyFogRevealerMoved(Unit unit)
        {
            var revealer = GetFogRevealer(unit);
            if (revealer != null && revealer.isActiveAndEnabled)
            {
                revealer.NotifyPawnMoved();
            }
        }

        private static void NotifyFogRevealerMovementEnded(Unit unit)
        {
            var revealer = GetFogRevealer(unit);
            if (revealer != null && revealer.isActiveAndEnabled)
            {
                revealer.NotifyPawnMovementEnded();
            }
        }

        /// <summary>
        /// When a player unit is selected, only that unit's fog revealer contributes to the map.
        /// With no selection, every friendly revealer is active.
        /// </summary>
        private bool SyncPlayerFogRevealerActivation()
        {
            var useFocusedVision = selectedUnit != null && selectedUnit.IsAlive;
            var refreshVisionRules = playerFogRevealerActivationDirty;
            var changed = false;

            for (var i = 0; i < playerRuntimeUnits.Count; i++)
            {
                var playerUnit = playerRuntimeUnits[i];
                var unitPawn = playerUnit.Pawn != null ? playerUnit.Pawn.GetComponent<UnitPawn>() : null;
                unitPawn?.SyncAdditionalLoadoutTo(playerUnit, notifyVisionRulesChanged: false);

                var revealer = GetFogRevealer(playerUnit);
                if (revealer == null)
                {
                    continue;
                }

                var shouldContribute = !useFocusedVision || ReferenceEquals(playerUnit, selectedUnit);
                if (revealer.ShouldContributeToLocalFog != shouldContribute
                    || !revealer.IsContributionStateSatisfied())
                {
                    revealer.SetLocalFogContribution(shouldContribute);
                    changed = true;
                }

                if (shouldContribute
                    && (refreshVisionRules || !revealer.MatchesUnitVisionRules(playerUnit)))
                {
                    revealer.ApplyVisionRulesFromUnit(playerUnit);
                    changed = true;
                }
                else if (!revealer.MatchesUnitVisionRules(playerUnit))
                {
                    revealer.ApplyVisionRulesFromUnit(playerUnit);
                }
            }

            if (changed)
            {
                RefreshActivePlayerFogRevealers();
            }

            playerFogRevealerActivationDirty = false;
            return changed;
        }

        private void RefreshActivePlayerFogRevealers()
        {
            var fow = FogOfWarWorld.instance;

            for (var i = 0; i < playerRuntimeUnits.Count; i++)
            {
                var revealer = GetFogRevealer(playerRuntimeUnits[i]);
                if (revealer == null || !revealer.isActiveAndEnabled)
                {
                    continue;
                }

                revealer.SetRevealerAsStatic(false);
                if (!revealer.IsContributingToFog)
                {
                    continue;
                }

                if (fow != null && !fow.IsInPhasedUpdate)
                {
                    revealer.ManualCalculateLineOfSight();
                }
                else
                {
                    revealer.RequestLineOfSightRecalculation();
                }
            }

            if (fow == null || fow.IsInPhasedUpdate || fow.FOWSamplingMode != FogOfWarWorld.FogSampleMode.Texture)
            {
                return;
            }

            FlushFogGpuUploadsAndRenderTexture();
        }

        private void FlushFogGpuUploadsAndRenderTexture()
        {
            var fow = FogOfWarWorld.instance;
            if (fow == null || fow.FOWSamplingMode != FogOfWarWorld.FogSampleMode.Texture)
            {
                return;
            }

            if (fow.UseStagedGPUUploads)
            {
                fow.FlushStagedRevealerData();
            }

            fow.RenderFogTexture();
        }

        private void SyncWallBaselineProofOnRevealers()
        {
            for (var i = 0; i < allRuntimeUnits.Count; i++)
            {
                var revealer = GetFogRevealer(allRuntimeUnits[i]);
                if (revealer == null)
                {
                    continue;
                }

                var isSelected = selectedUnit != null && ReferenceEquals(allRuntimeUnits[i], selectedUnit);
                revealer.DrawWallBaselineProof = debugShowWallBaselineProof && isSelected;
                revealer.DrawShaderUploadPolygons = debugShowShaderUploadPolygons && isSelected;
            }
        }

        private void MarkFogRevealerSettingsDirty()
        {
            fogRevealerSettingsDirty = true;
        }

        private void RefreshAllFogRevealersAfterForestPassToggle()
        {
            SyncWallBaselineProofOnRevealers();
            var fow = FogOfWarWorld.instance;
            if (fow == null || fow.IsInPhasedUpdate)
            {
                MarkFogRevealerSettingsDirty();
                return;
            }

            for (var i = 0; i < allRuntimeUnits.Count; i++)
            {
                var revealer = GetFogRevealer(allRuntimeUnits[i]);
                if (revealer == null || !revealer.isActiveAndEnabled)
                {
                    continue;
                }

                revealer.SetRevealerAsStatic(false);
                if (!revealer.IsContributingToFog)
                {
                    continue;
                }

                revealer.ManualCalculateLineOfSight();
            }

            FlushFogGpuUploadsAndRenderTexture();
        }

        private bool IsSpottedByAnyPlayerUnit(Unit target)
        {
            if (target == null || !target.IsAlive)
            {
                return false;
            }

            for (var i = 0; i < playerRuntimeUnits.Count; i++)
            {
                var observer = playerRuntimeUnits[i];
                if (observer.IsAlive
                    && observer.IsWithinVisibilityRangeOf(target)
                    && GetCachedHasLineOfSight(observer, target))
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateNavGraphGizmoVisibility(Unit unit)
        {
            if (AstarPath.active?.data?.graphs == null)
            {
                return;
            }

            var graphs = AstarPath.active.data.graphs;
            if (unit == null)
            {
                foreach (var graph in graphs)
                {
                    if (graph != null)
                    {
                        graph.drawGizmos = true;
                    }
                }
                return;
            }

            var unitGraphMask = unit.GetPathGraphMask(navPathBuilder);
            foreach (var graph in graphs)
            {
                if (graph != null)
                {
                    graph.drawGizmos = unitGraphMask.Contains(graph);
                }
            }
        }

        private void UpdateMovePreviewSizeForUnit(Unit unit)
        {
            if (destinationMarkerObject != null)
            {
                destinationMarkerObject.SetActive(unit != null);
            }

            if (unit == null)
            {
                return;
            }

            var diameterScale = unit.GetMovePreviewDiameter(VisualizerLineWidth);
            if (destinationMarkerObject != null)
            {
                destinationMarkerObject.transform.localScale = new Vector3(diameterScale, PawnBaseHeightScale, diameterScale);
            }

            if (movementPathLine != null)
            {
                ApplyMovementPathLineWorldUp();
                movementPathLine.widthMultiplier = diameterScale;
            }
        }

        private void ApplyMovementPathLineWorldUp()
        {
            if (movementPathLine == null)
            {
                return;
            }

            movementPathLine.alignment = LineAlignment.TransformZ;
            movementPathLine.transform.forward = Vector3.up;
        }

        private void HandlePlayerUnitClick(Unit unit)
        {
            if (unit == null || !unit.IsAlive)
            {
                return;
            }

            var isDoubleClick = ReferenceEquals(lastClickedPlayerUnit, unit)
                && Time.unscaledTime - lastClickedPlayerUnitClickTime <= DoubleClickIntervalSeconds;
            lastClickedPlayerUnit = unit;
            lastClickedPlayerUnitClickTime = Time.unscaledTime;

            SelectUnit(unit);
            if (isDoubleClick)
            {
                FocusCameraOnUnit(unit);
            }
        }

        private void FocusCameraOnUnit(Unit unit)
        {
            if (unit == null || unit.Pawn == null)
            {
                return;
            }

            var focusPoint = unit.Pawn.transform.position;
            focusPoint.y = GroundYPosition;
            cameraManager?.FocusOnPoint(focusPoint);
        }

        private WeaponProfile GetSelectedAttackWeapon(Unit unit)
        {
            if (unit.Weapons == null || unit.Weapons.Length == 0)
            {
                return WeaponProfile.CreateDefault();
            }

            return unit.Weapons[Mathf.Clamp(selectedAttackWeaponIndex, 0, unit.Weapons.Length - 1)];
        }

        private bool CanUnitTarget(Unit attacker, Unit target, WeaponProfile weapon)
        {
            if (!attacker.IsTargetInRange(target, weapon) || !HasLineOfSight(attacker, target))
            {
                return false;
            }

            return !attacker.IsPlayerControlled || HasPlayerFogVisionForTarget(attacker, target);
        }

        private bool HasPlayerFogVisionForTarget(Unit observer, Unit target)
        {
            if (!observer.IsWithinVisibilityRangeOf(target))
            {
                return false;
            }

            return IsInLiveFogVision(target.GetLineOfSightVolume().SightPoint);
        }

        private bool IsInLiveFogVision(Vector3 worldPosition)
        {
            if (fogOfWarWorld == null || FogOfWarWorld.instance == null)
            {
                return true;
            }

            if (FogOfWarWorld.instance.FOWSamplingMode != FogOfWarWorld.FogSampleMode.Texture)
            {
                return true;
            }

            return FogOfWarWorld.SampleFogTextureColorAtPoint(worldPosition) > GetLiveFogVisibilityThreshold();
        }

        private float GetLiveFogVisibilityThreshold()
        {
            // Without regrow, texture visibility is binary (0 = fogged, 1 = lit).
            return 0.5f;
        }

        private bool HasLineOfSight(Unit observer, Unit target)
        {
            return Unit.HasLineOfSight(observer, target, allRuntimeUnits, IsTerrainBlockingLineOfSight);
        }

        private bool IsTerrainBlockingLineOfSight(CombatLineOfSightVolume observer, CombatLineOfSightVolume target)
        {
            var origin = CombatLineOfSight.GetSightPointAtPlanarEdgeToward(observer, target.Position);
            var targetPoint = CombatLineOfSight.GetSightPointAtPlanarEdgeToward(target, observer.Position);
            var delta = targetPoint - origin;
            var distance = delta.magnitude;
            if (distance <= PositionArrivalTolerance)
            {
                return false;
            }

            var direction = delta / distance;
            var blockerMask = CombatLayers.LineOfSightBlockerMask;

            if (CombatMapSceneProvider.TryGetMapPhysicsScene(out var mapPhysicsScene))
            {
                if (mapPhysicsScene.Raycast(origin, direction, out var hit, distance, blockerMask, QueryTriggerInteraction.Ignore)
                    && hit.collider != null
                    && !IsUnitPawn(hit.collider.gameObject)
                    && !IsRoughTerrainCollider(hit.collider)
                    && !IsForestFogZoneBlockerCollider(hit.collider))
                {
                    return true;
                }

                return false;
            }

            var ray = new Ray(origin, direction);
            var hitCount = Physics.RaycastNonAlloc(
                ray,
                lineOfSightRaycastBuffer,
                distance,
                blockerMask,
                QueryTriggerInteraction.Ignore);

            for (var i = 0; i < hitCount; i++)
            {
                var collider = lineOfSightRaycastBuffer[i].collider;
                if (collider == null
                    || IsUnitPawn(collider.gameObject)
                    || IsRoughTerrainCollider(collider)
                    || IsForestFogZoneBlockerCollider(collider))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static float InchesToWorldUnits(float inches)
        {
            return CombatScale.InchesToWorldUnits(inches);
        }

        private static float WorldUnitsToInches(float worldUnits)
        {
            return CombatScale.WorldUnitsToInches(worldUnits);
        }

        private void UpdateHoveredEnemy()
        {
            hoveredEnemyUnit = null;
            if (IsMouseOverGameplayUi())
            {
                return;
            }

            var activeCamera = cameraManager != null ? cameraManager.ActiveCamera : Camera.main;
            if (activeCamera == null)
            {
                return;
            }

            var ray = activeCamera.ScreenPointToRay(Input.mousePosition);
            if (TryGetClosestUnitFromRay(
                    ray,
                    unit => !unit.IsPlayerControlled && unit.IsAlive && unit.IsVisibleToPlayer,
                    out var hoveredEnemy))
            {
                hoveredEnemyUnit = hoveredEnemy;
            }
        }

        private void UpdateFogOfWarVisibility()
        {
            for (var i = 0; i < enemyRuntimeUnits.Count; i++)
            {
                var enemy = enemyRuntimeUnits[i];
                enemy.ApplyVisibility(CanPlayerSeeUnit(enemy));
            }
        }

        /// <summary>
        /// Enemy models stay visible when any living friendly unit can spot them.
        /// Map fog may still focus on the selected unit's revealer only.
        /// </summary>
        private bool CanPlayerSeeUnit(Unit target)
        {
            return IsSpottedByAnyPlayerUnit(target);
        }

        /// <summary>
        /// Returns the HasLineOfSight result for an (observer, target) pair, reusing the cached
        /// value when no unit has moved since it was computed.  The cache is invalidated by
        /// <see cref="losDirtyVersion"/> which is incremented every time a pawn position changes.
        /// </summary>
        private bool GetCachedHasLineOfSight(Unit observer, Unit target)
        {
            var unitCount = allRuntimeUnits.Count;
            var observerIdx = allRuntimeUnits.IndexOf(observer);
            var targetIdx = allRuntimeUnits.IndexOf(target);

            // Fall back to uncached when indices aren't found (e.g. during spawn transitions).
            if (observerIdx < 0 || targetIdx < 0)
            {
                return HasLineOfSight(observer, target);
            }

            var key = observerIdx * unitCount + targetIdx;
            var cacheSize = unitCount * unitCount;

            // Rebuild or clear the arrays when the dirty version advances or sizes don't match.
            if (losCachedVersion != losDirtyVersion)
            {
                if (losCacheValid == null || losCacheValid.Length < cacheSize)
                {
                    losCacheValid = new bool[cacheSize];
                    losCacheResult = new bool[cacheSize];
                }
                else
                {
                    System.Array.Clear(losCacheValid, 0, cacheSize);
                }

                losCachedVersion = losDirtyVersion;
            }

            // Guard against stale indices that could exceed the used cache region.
            if (key >= cacheSize)
            {
                return HasLineOfSight(observer, target);
            }

            if (losCacheValid[key])
            {
                return losCacheResult[key];
            }

            var result = HasLineOfSight(observer, target);
            losCacheValid[key] = true;
            losCacheResult[key] = result;
            return result;
        }

        private bool TryCancelModeOnRightClick()
        {
            if (!Input.GetMouseButtonDown(1))
            {
                return false;
            }

            SetCurrentMode(UnitActionMode.None);
            return true;
        }

        private void ApplyAim(Unit unit)
        {
            if (unit == null || !unit.IsAlive || unit.HasActedThisTurn || unit.HasRunActionThisTurn)
            {
                return;
            }

            unit.MoveTarget = null;
            unit.PathWaypoints = null;
            unit.ActiveMovementStep = MovementStepOption.None;
            unit.RemainingMovementThisTurn = 0f;
            unit.IsAimingThisTurn = true;
            AddCombatLogEntry($"{unit.Definition.DisplayName} aims (+{AimToHitBonus} to hit).");
            SetCurrentMode(UnitActionMode.None);
        }

        private bool TryConsumeUiClick()
        {
            if (!IsAnyMouseButtonDown())
            {
                return false;
            }

            if (!IsMouseOverGameplayUi())
            {
                return false;
            }

            // Clicks inside the action bar are handled by IMGUI (weapon/move/attack buttons).
            // Don't cancel the current mode so those button clicks can be processed normally.
            var mouseGuiPosition = GetMouseGuiPosition();
            var isOverActionBar = selectedUnit != null && activeTurnSide == TurnSide.Player
                && GetActionBarRect().Contains(mouseGuiPosition);
            if (!isOverActionBar && currentPlayerMode != UnitActionMode.None)
            {
                uiCancelFrame = Time.frameCount;
                SetCurrentMode(UnitActionMode.None);
            }

            return true;
        }

        private bool WasUiCancelTriggeredThisFrame()
        {
            return uiCancelFrame == Time.frameCount;
        }

        private bool IsMouseOverGameplayUi()
        {
            var mouseGuiPosition = GetMouseGuiPosition();
            if (IsMouseOverCameraControlsPanel(mouseGuiPosition))
            {
                return true;
            }

            if (new Rect(RosterAreaX, RosterAreaY, RosterAreaWidth, RosterAreaHeight).Contains(mouseGuiPosition))
            {
                return true;
            }

            if (selectedUnit != null)
            {
                if (GetSelectedUnitPanelRect().Contains(mouseGuiPosition))
                {
                    return true;
                }

                if (activeTurnSide == TurnSide.Player)
                {
                    if (GetActionBarRect().Contains(mouseGuiPosition))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsMouseOverCameraControlsPanel(Vector2 mouseGuiPosition)
        {
            var areaX = (Screen.width - CameraControlsPanelWidth) * 0.5f;
            var panelRect = new Rect(areaX, CameraControlsPanelTopMargin, CameraControlsPanelWidth, CameraControlsPanelHeight);
            return panelRect.Contains(mouseGuiPosition);
        }

        private static Vector2 GetMouseGuiPosition()
        {
            var mousePosition = Input.mousePosition;
            return new Vector2(mousePosition.x, Screen.height - mousePosition.y);
        }

        private static bool IsAnyMouseButtonDown()
        {
            return Input.GetMouseButtonDown(LeftMouseButton)
                || Input.GetMouseButtonDown(RightMouseButton)
                || Input.GetMouseButtonDown(MiddleMouseButton);
        }

    }
}
