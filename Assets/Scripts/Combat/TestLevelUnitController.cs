using System.Collections.Generic;
using FOW;
using Pathfinding;
using UnityEngine;

namespace IronKingdoms.Combat
{
    public partial class TestLevelUnitController : MonoBehaviour
    {
        private const float AiInRangeTolerance = 0.95f;
        private const float AiDesiredStopFactor = 0.85f;
        private const float AiMinimumStopDistance = 0.2f;
        private const float RadiusToDiameterMultiplier = 2f;
        private const float PositionArrivalTolerance = 0.05f;
        private const float NavmeshContainmentTolerance = 0.02f;
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
        private const float DefaultTargetRingRadius = 0.6f;
        private const float TargetRingScaleFactor = 0.6f;
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
        private const float TerrainCostSampleStepInches = 0.25f;
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
        [SerializeField] private bool debugUseCrispFogRendering = true;
        [SerializeField] private bool autoSpawnOnStart = true;
        private MatchArmySpawner matchArmySpawner;

        private readonly List<RuntimeUnit> playerRuntimeUnits = new();
        private readonly List<RuntimeUnit> enemyRuntimeUnits = new();
        private readonly List<RuntimeUnit> allRuntimeUnits = new();
        private readonly Plane boardPlane = new(Vector3.up, Vector3.zero);
        private readonly RaycastHit[] terrainRaycastBuffer = new RaycastHit[16];
        private readonly RaycastHit[] lineOfSightRaycastBuffer = new RaycastHit[32];
        private readonly List<CombatLineOfSightVolume> lineOfSightInterveningVolumes = new();
        private RuntimeUnit selectedUnit;
        private TurnSide activeTurnSide = TurnSide.Player;
        private float aiThinkTimer;
        private RuntimeUnit activeEnemyUnit;
        private RuntimeUnit activeEnemyTarget;
        private int enemyActivationIndex;
        private bool enemyIssuedMoveForActiveUnit;
        private bool enemyResolvedActionForActiveUnit;
        private RuntimeUnit hoveredEnemyUnit;

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
        private GUIStyle floatingDamageStyle;
        private GUIStyle floatingDamageShadowStyle;
        private GUIStyle coverPopupStyle;
        private GUIStyle coverPopupShadowStyle;
        private GameObject destinationMarkerObject;
        private Material visualizerMaterial;
        private RuntimeUnit lastClickedPlayerUnit;
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

        private void Awake()
        {
            EnsureCameraManagerAssigned();
            EnsureNavPathBuilderAssigned();
            EnsureMatchArmySpawnerAssigned();
            EnsureDefinitionCatalogAssigned();
            EnsureFogOfWarWorldAssigned();
            ConfigureFogOfWarWorld();
            EnsureFogOfWarCameraEffectAssigned();
        }

        private void Start()
        {
            BuildVisualizers();
            if (autoSpawnOnStart)
            {
                SpawnUnits();
            }
        }

        private void Update()
        {
            UpdateUnitNavmeshCutActivation(GetPathingUnitForNavmeshClearance());
            cameraManager?.Tick(IsMouseOverGameplayUi());
            if (activeTurnSide == TurnSide.Player)
            {
                HandlePlayerInput();
            }

            TickMovement(Time.deltaTime);
            TickEnemyAi(Time.deltaTime);
            TickFloatingDamage(Time.deltaTime);
            UpdateMovementVisualizer();
            UpdateWeaponRangeRing();
            UpdateFogOfWarVisibility();
            UpdateHoveredEnemy();
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

            var unitPos = GetPawnFeetPosition(selectedUnit);

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
                if (!TryResolveChargePath(selectedUnit, hoverPos, previewPath, out _))
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
                var graphMask = GetPathGraphMask(selectedUnit);
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
                var previewUnitRadius = GetUnitCollisionRadius(selectedUnit);
                var fullLength = CalculatePathMovementCostInInches(selectedUnit, previewPath, previewUnitRadius);
                stagedMoveAmountInches = Mathf.Min(fullLength, effectiveBudget);
                stagedRoughTerrainInches = CalculatePathRoughTerrainPhysicalInches(selectedUnit, previewPath, stagedMoveAmountInches, previewUnitRadius);
                hasStagedMoveAmount = true;

                withinRange = fullLength <= effectiveBudget + WorldUnitsToInches(PositionArrivalTolerance);
                if (TryGetPathStopPointAtMovementBudget(selectedUnit, previewPath, effectiveBudget, out var stopPoint, previewUnitRadius))
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
                var radius = GetTargetRingRadius(enemy);
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

        private static float GetTargetRingRadius(RuntimeUnit target)
        {
            if (target?.Pawn == null)
            {
                return DefaultTargetRingRadius;
            }

            var col = target.Pawn.GetComponent<CapsuleCollider>();
            if (col == null)
            {
                return DefaultTargetRingRadius;
            }

            var scaledRadius = col.radius * 2f * TargetRingScaleFactor;
            return Mathf.Max(DefaultTargetRingRadius, scaledRadius);
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

        private RuntimeUnit GetPathingUnitForNavmeshClearance()
        {
            return activeTurnSide == TurnSide.Player
                && currentPlayerMode == UnitActionMode.Move
                && selectedUnit != null
                && selectedUnit.IsAlive
                ? selectedUnit
                : null;
        }

        private void UpdateUnitNavmeshCutActivation(RuntimeUnit pathingUnit = null)
        {
            var pathingRadius = pathingUnit != null ? GetUnitCollisionRadius(pathingUnit) : 0f;
            var pathingGraphMask = GetPathGraphMask(pathingUnit);
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

                if (unit.NavmeshCut.graphMask != pathingGraphMask)
                {
                    var cutWasEnabled = unit.NavmeshCut.enabled;
                    if (cutWasEnabled)
                    {
                        unit.NavmeshCut.enabled = false;
                    }

                    unit.NavmeshCut.graphMask = pathingGraphMask;
                    if (cutWasEnabled)
                    {
                        unit.NavmeshCut.enabled = true;
                    }

                    navmeshCutChanged = true;
                }

                var isPathingUnit = pathingUnit != null && ReferenceEquals(unit, pathingUnit);
                var targetRadius = GetUnitCollisionRadius(unit) + (isPathingUnit ? 0f : pathingRadius);
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
            if (!RaycastForMouseHit(ray, out var hit))
            {
                return;
            }

            for (var i = 0; i < playerRuntimeUnits.Count; i++)
            {
                if (playerRuntimeUnits[i].Pawn == hit.collider.gameObject && playerRuntimeUnits[i].IsAlive)
                {
                    HandlePlayerUnitClick(playerRuntimeUnits[i]);
                    return;
                }
            }
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

            // First, check whether the click landed on a player unit (unit-selection shortcut).
            if (RaycastForMouseHit(ray, out var hit))
            {
                for (var i = 0; i < playerRuntimeUnits.Count; i++)
                {
                    if (playerRuntimeUnits[i].Pawn == hit.collider.gameObject && playerRuntimeUnits[i].IsAlive)
                    {
                        HandlePlayerUnitClick(playerRuntimeUnits[i]);
                        return;
                    }
                }
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
                if (TryResolveChargePath(selectedUnit, destination, chargePathScratch, out _))
                {
                    selectedUnit.HasChargedThisTurn = true;
                    IssueMoveOrderFromPath(selectedUnit, chargePathScratch, movementBudget);
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
                    IssueMoveOrderFromPath(selectedUnit, previewPath, movementBudget);
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
            if (!RaycastForMouseHit(ray, out var hit))
            {
                return;
            }

            for (var i = 0; i < playerRuntimeUnits.Count; i++)
            {
                if (playerRuntimeUnits[i].Pawn == hit.collider.gameObject && playerRuntimeUnits[i].IsAlive)
                {
                    HandlePlayerUnitClick(playerRuntimeUnits[i]);
                    return;
                }
            }

            for (var i = 0; i < enemyRuntimeUnits.Count; i++)
            {
                var enemy = enemyRuntimeUnits[i];
                if (enemy.Pawn != hit.collider.gameObject || !enemy.IsAlive)
                {
                    continue;
                }

                if (enemy.IsVisibleToPlayer && CanUnitTarget(selectedUnit, enemy, attackWeapon))
                {
                    ResolveAttack(selectedUnit, enemy, attackWeapon);
                    selectedUnit.HasActedThisTurn = true;
                    SetCurrentMode(UnitActionMode.None);
                }

                return;
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

                var unitRadius = GetUnitCollisionRadius(unit);
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

                    var terrainSpeedMultiplier = GetMovementSpeedMultiplierAtPoint(unit, currentPosition, unitRadius);
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
                    var allowedStep = GetAffordableWorldStepAlongSegment(
                        unit,
                        currentPosition,
                        targetPosition,
                        rawStep,
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
                    nextPosition = GetGroundedPositionKeepingXZ(unit, nextPosition);
                    var movedDistance = Vector3.Distance(currentPosition, nextPosition);
                    if (movedDistance <= 0.0001f)
                    {
                        unit.MoveTarget = null;
                        unit.PathWaypoints = null;
                        unit.ActiveMovementStep = MovementStepOption.None;
                        break;
                    }

                    unit.Pawn.transform.position = nextPosition;
                    losDirtyVersion++;
                    var movementCost = CalculateMovementCostForSegmentInInches(unit, currentPosition, nextPosition, unitRadius);
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
                        nextWaypoint = GetGroundedPositionKeepingXZ(unit, nextWaypoint);
                        unit.MoveTarget = nextWaypoint;
                        continue;
                    }

                    unit.MoveTarget = null;
                    unit.PathWaypoints = null;
                    unit.ActiveMovementStep = MovementStepOption.None;
                    break;
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

        private void ResolveEnemyMovement(RuntimeUnit enemy, RuntimeUnit target)
        {
            if (target == null)
            {
                enemy.MoveTarget = null;
                return;
            }

            var enemyPosition = enemy.Pawn.transform.position;
            var targetPosition = target.Pawn.transform.position;
            var distance = GetPlanarDistance(enemyPosition, targetPosition);
            var desiredRange = GetLongestWeaponRange(enemy) + GetCombinedUnitRadiiInches(enemy, target);
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
            destination = GetGroundedPositionKeepingXZ(enemy, destination);
            IssueMoveOrder(enemy, destination);
        }

        private bool ResolveUnitAction(RuntimeUnit unit, List<RuntimeUnit> targets)
        {
            var target = FindNearestAliveUnit(unit, targets);
            return ResolveUnitAction(unit, target);
        }

        private bool ResolveUnitAction(RuntimeUnit unit, RuntimeUnit target)
        {
            if (target == null)
            {
                return false;
            }

            var distance = GetPlanarDistance(unit.Pawn.transform.position, target.Pawn.transform.position);
            var weapon = GetBestWeaponForDistance(unit, target, distance);
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

            activeEnemyTarget = FindNearestAliveUnit(activeEnemyUnit, playerRuntimeUnits);
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

        private void IssueMoveOrder(RuntimeUnit unit, Vector3 destination, float? movementBudgetOverride = null)
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

            var current = GetPawnFeetPosition(unit);
            var graphMask = GetPathGraphMask(unit);

            // Use NavPathBuilder to get a funnel-smoothed path, then clamp to budget.
            if (navPathBuilder != null)
            {
                UpdateUnitNavmeshCutActivation(unit);
                var smoothedPath = navPathBuilder.BuildSync(current, destination, graphMask);
                if (smoothedPath.Count >= 2)
                {
                    IssueMoveOrderFromPath(unit, smoothedPath, remaining);
                    return;
                }
            }

            // Nav-only movement: do not issue direct movement when no nav path is available.
            unit.MoveTarget = null;
            unit.PathWaypoints = null;
            unit.ActiveMovementStep = MovementStepOption.None;
        }

        /// <summary>
        /// Activates movement for <paramref name="unit"/> using an already-computed
        /// funnel-smoothed path (e.g. the one displayed during the preview).
        /// The path is clamped to <paramref name="movementBudget"/> before being assigned.
        /// </summary>
        private void IssueMoveOrderFromPath(RuntimeUnit unit, List<Vector3> smoothedPath, float movementBudget)
        {
            if (unit == null || !unit.IsAlive || unit.Pawn == null)
            {
                return;
            }

            if (unit.ActiveMovementStep == MovementStepOption.None)
            {
                unit.ActiveMovementStep = MovementStepOption.Advance;
            }

            var waypoints = ClampPathToMovementBudget(unit, smoothedPath, movementBudget, GetUnitCollisionRadius(unit));
            if (waypoints.Count >= 2)
            {
                unit.PathWaypoints = waypoints;
                unit.PathWaypointIndex = 1;
                var firstTarget = waypoints[1];
                firstTarget = GetGroundedPositionKeepingXZ(unit, firstTarget);
                unit.MoveTarget = firstTarget;
            }
        }

        private static float GetUnitCollisionRadius(RuntimeUnit unit)
        {
            if (unit?.Pawn == null)
            {
                return DefaultTargetRingRadius;
            }

            var col = unit.Pawn.GetComponent<CapsuleCollider>();
            if (col == null)
            {
                return DefaultTargetRingRadius;
            }

            return Mathf.Max(0.1f, col.radius);
        }

        private static bool RaycastForMouseHit(Ray ray, out RaycastHit hit)
        {
            return Physics.Raycast(ray, out hit, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        }

        /// <summary>
        /// Returns true if <paramref name="go"/> is a pawn belonging to any spawned unit.
        /// Used to distinguish unit colliders from terrain geometry when raycasting.
        /// </summary>
        private bool IsUnitPawn(GameObject go)
        {
            if (go == null)
            {
                return false;
            }

            for (var i = 0; i < allRuntimeUnits.Count; i++)
            {
                var pawn = allRuntimeUnits[i]?.Pawn;
                if (pawn == null)
                {
                    continue;
                }

                if (pawn == go || go.transform.IsChildOf(pawn.transform))
                {
                    return true;
                }
            }

            return false;
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

        private Vector3 GetNearestNavmeshPosition(Vector3 worldPosition, RuntimeUnit unit = null)
        {
            if (AstarPath.active == null)
            {
                return worldPosition;
            }

            var nearestNodeConstraint = NearestNodeConstraint.Walkable;
            nearestNodeConstraint.graphMask = GetPathGraphMask(unit);
            var nearest = AstarPath.active.GetNearest(worldPosition, nearestNodeConstraint);
            if (nearest.node == null)
            {
                return worldPosition;
            }

            // For a RecastGraph (navmesh), nearest.position is already the closest point on the
            // triangle surface — exact and precise, no grid-centre snapping.
            // For a GridGraph fallback, nearest.position is the cell centre; the caller receives
            // a snapped position in that case, which is acceptable given Recast is the target graph.
            return nearest.position;
        }

        private static Vector3 GetPawnFeetPosition(RuntimeUnit unit)
        {
            if (unit?.Pawn == null)
            {
                return Vector3.zero;
            }

            return unit.Pawn.transform.position;
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

        // The unit parameter is retained for API consistency and potential future per-unit
        // terrain-height adjustments; all callers pass the moving unit for context.
        private Vector3 GetGroundedNavmeshPositionForUnit(RuntimeUnit unit, Vector3 worldPosition)
        {
            return GetNearestNavmeshPosition(worldPosition, unit);
        }

        private static Vector3 GetPawnCenterPosition(RuntimeUnit unit)
        {
            if (unit?.Pawn == null)
            {
                return Vector3.zero;
            }

            var bodyHeight = unit.Definition.Stats.modelSize.GetPawnScale().y;
            return unit.Pawn.transform.position + Vector3.up * bodyHeight;
        }

        private Vector3 GetGroundedPositionKeepingXZ(RuntimeUnit unit, Vector3 worldPosition)
        {
            var groundedPosition = worldPosition;
            groundedPosition.y = GetGroundedNavmeshPositionForUnit(unit, worldPosition).y;
            return groundedPosition;
        }

        private void SnapUnitToNavmesh(RuntimeUnit unit)
        {
            if (unit?.Pawn == null)
            {
                return;
            }

            unit.Pawn.transform.position = GetGroundedNavmeshPositionForUnit(unit, unit.Pawn.transform.position);
        }

        private GraphMask GetPathGraphMask(RuntimeUnit unit)
        {
            if (unit?.Definition == null || navPathBuilder == null)
            {
                return GraphMask.everything;
            }

            return navPathBuilder.GetGraphMaskForModelSizeOrDefault(unit.Definition.Stats.modelSize);
        }

        /// <summary>
        /// Builds a two-point path along a straight line on the XZ plane (Mk4 charge movement).
        /// </summary>
        private static void BuildStraightLineChargePath(Vector3 from, Vector3 to, List<Vector3> path)
        {
            path.Clear();
            var start = from;
            var end = to;
            end.y = start.y;
            path.Add(start);
            path.Add(end);
        }

        /// <summary>
        /// Projects the click onto a straight charge line, snaps to the nearest walkable nav point
        /// along that ray, and clamps to the farthest valid straight segment on the navmesh.
        /// </summary>
        private bool TryResolveChargePath(RuntimeUnit unit, Vector3 clickPosition, List<Vector3> path, out Vector3 resolvedDestination)
        {
            resolvedDestination = clickPosition;
            if (unit == null || path == null || navPathBuilder == null)
            {
                return false;
            }

            var from = GetPawnFeetPosition(unit);
            if (!navPathBuilder.TryResolveStraightLineChargeDestination(
                    from,
                    clickPosition,
                    GetPathGraphMask(unit),
                    out resolvedDestination,
                    NavmeshContainmentTolerance))
            {
                return false;
            }

            BuildStraightLineChargePath(from, resolvedDestination, path);
            return true;
        }

        private List<Vector3> ClampPathToMovementBudget(RuntimeUnit unit, IReadOnlyList<Vector3> waypoints, float budget, float unitRadius = 0f)
        {
            var result = new List<Vector3>();
            if (waypoints == null || waypoints.Count == 0)
            {
                return result;
            }

            result.Add(waypoints[0]);
            if (budget <= MovementBudgetEpsilon)
            {
                return result;
            }

            var distanceCovered = 0f;

            for (var i = 1; i < waypoints.Count; i++)
            {
                if (TryGetSegmentStopPointAtMovementBudget(unit, waypoints[i - 1], waypoints[i], budget - distanceCovered, out var segmentStopPoint, unitRadius))
                {
                    result.Add(segmentStopPoint);

                    break;
                }

                result.Add(waypoints[i]);
                distanceCovered += CalculateMovementCostForSegmentInInches(unit, waypoints[i - 1], waypoints[i], unitRadius);
            }

            return result;
        }

        private bool TryGetPathStopPointAtMovementBudget(RuntimeUnit unit, IReadOnlyList<Vector3> waypoints, float budget, out Vector3 stopPoint, float unitRadius = 0f)
        {
            stopPoint = default;
            if (waypoints == null || waypoints.Count == 0)
            {
                return false;
            }

            budget = Mathf.Max(0f, budget);
            stopPoint = waypoints[0];
            var distanceCovered = 0f;
            for (var i = 1; i < waypoints.Count; i++)
            {
                if (TryGetSegmentStopPointAtMovementBudget(unit, waypoints[i - 1], waypoints[i], budget - distanceCovered, out stopPoint, unitRadius))
                {
                    return true;
                }

                distanceCovered += CalculateMovementCostForSegmentInInches(unit, waypoints[i - 1], waypoints[i], unitRadius);
                stopPoint = waypoints[i];
            }

            return true;
        }

        private float CalculatePathMovementCostInInches(RuntimeUnit unit, IReadOnlyList<Vector3> waypoints, float unitRadius = 0f)
        {
            if (waypoints == null || waypoints.Count < 2)
            {
                return 0f;
            }

            var movementCost = 0f;
            for (var i = 1; i < waypoints.Count; i++)
            {
                movementCost += CalculateMovementCostForSegmentInInches(unit, waypoints[i - 1], waypoints[i], unitRadius);
            }

            return movementCost;
        }

        private float CalculateMovementCostForSegmentInInches(RuntimeUnit unit, Vector3 from, Vector3 to, float unitRadius = 0f)
        {
            var totalDistance = Vector3.Distance(from, to);
            if (totalDistance <= MovementBudgetEpsilon)
            {
                return 0f;
            }

            var sampleCount = GetTerrainCostSampleCount(totalDistance);
            var movementCost = 0f;
            for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                var segmentStartT = (float)sampleIndex / sampleCount;
                var segmentEndT = (float)(sampleIndex + 1) / sampleCount;
                var segmentStart = Vector3.Lerp(from, to, segmentStartT);
                var segmentEnd = Vector3.Lerp(from, to, segmentEndT);
                var samplePoint = Vector3.Lerp(segmentStart, segmentEnd, 0.5f);
                var segmentDistanceInches = WorldUnitsToInches(Vector3.Distance(segmentStart, segmentEnd));
                var speedMultiplier = GetMovementSpeedMultiplierAtPoint(unit, samplePoint, unitRadius);
                movementCost += segmentDistanceInches / speedMultiplier;
            }

            return movementCost;
        }

        // Returns the total physical distance (in inches) traveled through rough-terrain zones
        // along the given path, walking only as far as the movement-cost budget allows.
        private float CalculatePathRoughTerrainPhysicalInches(RuntimeUnit unit, IReadOnlyList<Vector3> waypoints, float budget, float unitRadius = 0f)
        {
            if (UnitIgnoresRoughTerrainMovementCost(unit))
            {
                return 0f;
            }

            if (waypoints == null || waypoints.Count < 2)
            {
                return 0f;
            }

            var roughInches = 0f;
            var costCovered = 0f;
            for (var i = 1; i < waypoints.Count; i++)
            {
                var remaining = budget - costCovered;
                if (remaining <= MovementBudgetEpsilon)
                {
                    break;
                }

                roughInches += CalculateSegmentRoughTerrainPhysicalInches(unit, waypoints[i - 1], waypoints[i], remaining, out var segmentCostConsumed, unitRadius);
                costCovered += segmentCostConsumed;
            }

            return roughInches;
        }

        // Returns the physical rough-terrain inches for one segment, consuming up to budgetRemaining
        // movement cost. costConsumed receives the actual movement cost used from this segment.
        private float CalculateSegmentRoughTerrainPhysicalInches(RuntimeUnit unit, Vector3 from, Vector3 to, float budgetRemaining, out float costConsumed, float unitRadius = 0f)
        {
            costConsumed = 0f;
            var roughInches = 0f;
            var totalDistance = Vector3.Distance(from, to);
            if (totalDistance <= MovementBudgetEpsilon)
            {
                return 0f;
            }

            var sampleCount = GetTerrainCostSampleCount(totalDistance);
            for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                var subStartT = (float)sampleIndex / sampleCount;
                var subEndT = (float)(sampleIndex + 1) / sampleCount;
                var subStart = Vector3.Lerp(from, to, subStartT);
                var subEnd = Vector3.Lerp(from, to, subEndT);
                var samplePoint = Vector3.Lerp(subStart, subEnd, 0.5f);
                var subDistInches = WorldUnitsToInches(Vector3.Distance(subStart, subEnd));
                var speedMultiplier = GetMovementSpeedMultiplierAtPoint(unit, samplePoint, unitRadius);
                var subCost = subDistInches / speedMultiplier;
                var isRoughTerrain = IsPointInRoughTerrain(samplePoint, unitRadius);

                if (costConsumed + subCost > budgetRemaining + MovementBudgetEpsilon)
                {
                    // Partially consume this sub-segment up to the remaining budget.
                    var remaining = Mathf.Max(0f, budgetRemaining - costConsumed);
                    var fraction = subCost <= MovementBudgetEpsilon ? 0f : Mathf.Clamp01(remaining / subCost);
                    if (isRoughTerrain)
                    {
                        roughInches += subDistInches * fraction;
                    }

                    costConsumed += subCost * fraction;
                    break;
                }

                if (isRoughTerrain)
                {
                    roughInches += subDistInches;
                }

                costConsumed += subCost;
            }

            return roughInches;
        }

        private bool TryGetSegmentStopPointAtMovementBudget(RuntimeUnit unit, Vector3 segmentStart, Vector3 segmentEnd, float budgetRemaining, out Vector3 stopPoint, float unitRadius = 0f)
        {
            stopPoint = segmentStart;
            if (budgetRemaining <= MovementBudgetEpsilon)
            {
                return true;
            }

            var totalDistance = Vector3.Distance(segmentStart, segmentEnd);
            if (totalDistance <= MovementBudgetEpsilon)
            {
                return true;
            }

            var sampleCount = GetTerrainCostSampleCount(totalDistance);
            var movementCostCovered = 0f;
            for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                var subSegmentStartT = (float)sampleIndex / sampleCount;
                var subSegmentEndT = (float)(sampleIndex + 1) / sampleCount;
                var subSegmentStart = Vector3.Lerp(segmentStart, segmentEnd, subSegmentStartT);
                var subSegmentEnd = Vector3.Lerp(segmentStart, segmentEnd, subSegmentEndT);
                var samplePoint = Vector3.Lerp(subSegmentStart, subSegmentEnd, 0.5f);
                var subSegmentDistanceInches = WorldUnitsToInches(Vector3.Distance(subSegmentStart, subSegmentEnd));
                var speedMultiplier = GetMovementSpeedMultiplierAtPoint(unit, samplePoint, unitRadius);
                var subSegmentCost = subSegmentDistanceInches / speedMultiplier;

                if (movementCostCovered + subSegmentCost >= budgetRemaining - MovementBudgetEpsilon)
                {
                    var remainingCost = Mathf.Max(0f, budgetRemaining - movementCostCovered);
                    var t = subSegmentCost <= MovementBudgetEpsilon
                        ? 0f
                        : Mathf.Clamp01(remainingCost / subSegmentCost);
                    stopPoint = Vector3.Lerp(subSegmentStart, subSegmentEnd, t);
                    return true;
                }

                movementCostCovered += subSegmentCost;
                stopPoint = subSegmentEnd;
            }

            return false;
        }

        private static int GetTerrainCostSampleCount(float segmentDistanceWorldUnits)
        {
            var sampleStep = InchesToWorldUnits(TerrainCostSampleStepInches);
            if (sampleStep <= MovementBudgetEpsilon)
            {
                return 1;
            }

            return Mathf.Max(1, Mathf.CeilToInt(segmentDistanceWorldUnits / sampleStep));
        }

        private static void DrawUnitAdvantageDebug(RuntimeUnit unit)
        {
            if (unit?.Definition?.Stats == null)
            {
                return;
            }

            unit.Definition.Stats.EnsureAdvantageDefaults();
            var advantages = unit.Definition.Stats.advantages;
            if (advantages == null || advantages.Count == 0)
            {
                GUILayout.Label("Advantages: none");
                return;
            }

            var labels = new List<string>(advantages.Count);
            for (var i = 0; i < advantages.Count; i++)
            {
                if (advantages[i] != null)
                {
                    labels.Add(advantages[i].DisplayName);
                }
            }

            GUILayout.Label(labels.Count > 0 ? $"Advantages: {string.Join(", ", labels)}" : "Advantages: none");
        }

        private static void DrawUnitDefenseModifierDebug(RuntimeUnit unit)
        {
            if (unit?.Definition == null || unit.Pawn == null)
            {
                return;
            }

            var modifiers = CombatDefenseEvaluator.CollectActiveDefenseModifiers(unit.Definition, unit.Pawn);
            if (modifiers.Count == 0)
            {
                GUILayout.Label("Defense Modifiers: none");
                return;
            }

            for (var i = 0; i < modifiers.Count; i++)
            {
                var modifier = modifiers[i];
                if (modifier.Definition == null)
                {
                    continue;
                }

                GUILayout.Label(
                    $"Terrain Modifier: {modifier.Definition.DisplayName} (+{modifier.Definition.DefenseBonus} DEF) via {modifier.SourceLabel}");
            }
        }

        private static void DrawUnitTerrainStateDebug(RuntimeUnit unit)
        {
            if (unit?.Definition == null || unit.Pawn == null)
            {
                GUILayout.Label("Terrain: unknown");
                return;
            }

            var terrainState = CombatAbilitySolver.ResolveTerrainState(unit.Definition, unit.Pawn);
            GUILayout.Label($"Rough Terrain: {(terrainState.IsInRoughTerrain ? "Yes" : "No")}");
            GUILayout.Label($"Forest: {terrainState.ForestStatusLabel}");
        }

        private static void DrawUnitAbilityDebug(RuntimeUnit unit)
        {
            if (unit?.Definition == null)
            {
                return;
            }

            unit.Definition.Stats.EnsureAbilityDefaults();
            var abilities = unit.Definition.Stats.abilities;
            if (abilities == null || abilities.Count == 0)
            {
                GUILayout.Label("Abilities: none");
                return;
            }

            var abilityNames = new List<string>(abilities.Count);
            for (var i = 0; i < abilities.Count; i++)
            {
                if (abilities[i] != null)
                {
                    abilityNames.Add(abilities[i].DisplayName);
                }
            }

            GUILayout.Label(abilityNames.Count > 0 ? $"Abilities: {string.Join(", ", abilityNames)}" : "Abilities: none");

            var passives = CombatAbilitySolver.DescribeAbilityPassives(unit.Definition, unit.Pawn);
            for (var i = 0; i < passives.Count; i++)
            {
                var passive = passives[i];
                if (passive.Ability == null)
                {
                    continue;
                }

                var prefix = passive.IsActive ? "ACTIVE" : "inactive";
                GUILayout.Label($"  {prefix}: {passive.Ability.DisplayName} — {passive.EffectLabel}");
            }
        }

        private bool IsUnitInRoughTerrain(RuntimeUnit unit)
        {
            if (unit?.Definition == null || unit.Pawn == null)
            {
                return false;
            }

            return CombatAbilitySolver.ResolveTerrainState(unit.Definition, unit.Pawn).IsInRoughTerrain;
        }

        private bool IsPointInRoughTerrain(Vector3 worldPoint, float unitRadius = 0f)
        {
            var activeZones = CombatZone.ActiveZones;
            for (var i = 0; i < activeZones.Count; i++)
            {
                var zone = activeZones[i];
                if (zone == null || !zone.IsMovementZone)
                {
                    continue;
                }

                if (zone.IntersectsDisc(worldPoint, unitRadius))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsIntentionalAdvancingMovementStep(MovementStepOption step)
        {
            return step == MovementStepOption.Advance
                || step == MovementStepOption.Run
                || step == MovementStepOption.Charge;
        }

        private MovementStepOption GetRoughTerrainMovementStepForUnit(RuntimeUnit unit)
        {
            if (unit == null)
            {
                return MovementStepOption.None;
            }

            if (unit.ActiveMovementStep != MovementStepOption.None)
            {
                return unit.ActiveMovementStep;
            }

            return unit == selectedUnit ? selectedMovementOption : MovementStepOption.None;
        }

        private bool UnitIgnoresRoughTerrainMovementCost(RuntimeUnit unit)
        {
            if (unit?.Definition?.Stats == null)
            {
                return false;
            }

            unit.Definition.Stats.EnsureAdvantageDefaults();
            if (!unit.Definition.Stats.TreatsRoughTerrainAsOpenWhileAdvancing())
            {
                return false;
            }

            return IsIntentionalAdvancingMovementStep(GetRoughTerrainMovementStepForUnit(unit));
        }

        private float GetAffordableWorldStepAlongSegment(
            RuntimeUnit unit,
            Vector3 segmentStart,
            Vector3 segmentEnd,
            float maxPhysicalStep,
            float unitRadius)
        {
            if (maxPhysicalStep <= MovementBudgetEpsilon)
            {
                return 0f;
            }

            var segmentLength = Vector3.Distance(segmentStart, segmentEnd);
            if (segmentLength <= MovementBudgetEpsilon)
            {
                return 0f;
            }

            var step = Mathf.Min(maxPhysicalStep, segmentLength);
            var trialEnd = segmentStart + (segmentEnd - segmentStart) * (step / segmentLength);
            var movementCost = CalculateMovementCostForSegmentInInches(unit, segmentStart, trialEnd, unitRadius);
            var remaining = unit.RemainingMovementThisTurn;
            if (movementCost <= remaining + MovementBudgetEpsilon)
            {
                return step;
            }

            if (movementCost <= MovementBudgetEpsilon)
            {
                return step;
            }

            return step * Mathf.Clamp01(remaining / movementCost);
        }

        private float GetMovementSpeedMultiplierAtPoint(RuntimeUnit unit, Vector3 worldPoint, float unitRadius = 0f)
        {
            if (UnitIgnoresRoughTerrainMovementCost(unit))
            {
                return 1f;
            }

            var speedMultiplier = 1f;
            var activeZones = CombatZone.ActiveZones;
            for (var i = 0; i < activeZones.Count; i++)
            {
                var zone = activeZones[i];
                if (zone == null || !zone.IsMovementZone)
                {
                    continue;
                }

                if (!zone.IntersectsDisc(worldPoint, unitRadius))
                {
                    continue;
                }

                speedMultiplier = Mathf.Min(speedMultiplier, zone.MovementSpeedMultiplier);
                if (speedMultiplier <= MovementBudgetEpsilon)
                {
                    break;
                }
            }

            return Mathf.Max(MovementBudgetEpsilon, speedMultiplier);
        }

        private void StartPlayerTurn()
        {
            activeTurnSide = TurnSide.Player;
            ResetMovementForTurn(playerRuntimeUnits);
            SelectUnit(FindFirstAlive(playerRuntimeUnits));
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

        private static void ResetMovementForTurn(List<RuntimeUnit> units)
        {
            for (var i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                unit.RemainingMovementThisTurn = unit.Definition.Stats.speed;
                unit.HasActedThisTurn = false;
                unit.HasRunActionThisTurn = false;
                unit.HasAdvancedThisTurn = false;
                unit.HasChargedThisTurn = false;
                unit.IsAimingThisTurn = false;
                unit.MoveTarget = null;
                unit.PathWaypoints = null;
                unit.ActiveMovementStep = MovementStepOption.None;
            }
        }

        private void ResolveAttack(RuntimeUnit attacker, RuntimeUnit defender, WeaponProfile weapon)
        {
            var isMeleeAttack = weapon.AttackType == WeaponAttackType.Melee;
            var attackValue = GetAttackStatForWeapon(attacker, weapon);
            var attackModifier = GetToHitModifier(attacker);
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
            var effectiveDefense = GetEffectiveDefense(defender, attacker, weapon);
            if (!weapon.EvaluateAttackHit(atkDie1, atkDie2, attackRoll, effectiveDefense))
            {
                SpawnFloatingText(GetPawnCenterPosition(defender), "Miss!", new Color(1f, 0.9f, 0.2f, 1f));
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
            SpawnFloatingText(GetPawnCenterPosition(defender), damageText, damageColor);
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
                    SelectUnit(FindFirstAlive(playerRuntimeUnits));
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
            var radius = weapon.Range + GetUnitRadiusInches(selectedUnit);
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

        private static float CalculateHitChancePercent(RuntimeUnit attacker, RuntimeUnit defender, WeaponProfile weapon)
        {
            var attackStat = GetAttackStatForWeapon(attacker, weapon);
            var attackModifier = GetToHitModifier(attacker);
            var effectiveDefense = CombatDefenseEvaluator.GetEffectiveDefense(
                defender.Definition,
                defender.Pawn,
                attacker?.Definition?.Stats,
                weapon,
                attacker?.Pawn);
            var hits = 0;
            for (var d1 = 1; d1 <= 6; d1++)
            {
                for (var d2 = 1; d2 <= 6; d2++)
                {
                    var attackRoll = d1 + d2 + attackStat + attackModifier;
                    if (weapon.EvaluateAttackHit(d1, d2, attackRoll, effectiveDefense))
                    {
                        hits++;
                    }
                }
            }

            return hits / 36f * 100f;
        }

        private static int GetEffectiveDefense(RuntimeUnit defender, RuntimeUnit attacker, WeaponProfile weapon)
        {
            if (defender?.Definition == null)
            {
                return 0;
            }

            return CombatDefenseEvaluator.GetEffectiveDefense(
                defender.Definition,
                defender.Pawn,
                attacker?.Definition?.Stats,
                weapon,
                attacker?.Pawn);
        }

        private static int GetAttackStatForWeapon(RuntimeUnit attacker, WeaponProfile weapon)
        {
            var baseAttack = weapon.AttackType == WeaponAttackType.Melee
                ? attacker.Definition.Stats.meleeAttack
                : attacker.Definition.Stats.rangedAttack;
            return baseAttack + weapon.GetAttackModifier();
        }

        private static int GetToHitModifier(RuntimeUnit attacker)
        {
            if (attacker == null)
            {
                return 0;
            }

            return attacker.IsAimingThisTurn ? AimToHitBonus : 0;
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

        private void SelectUnit(RuntimeUnit unit)
        {
            selectedUnit = unit != null && unit.IsAlive ? unit : null;
            selectedAttackWeaponIndex = 0;
            selectedMovementOption = MovementStepOption.Advance;
            SetCurrentMode(UnitActionMode.None);
            UpdateMovePreviewSizeForUnit(selectedUnit);
            UpdateNavGraphGizmoVisibility(selectedUnit);
        }

        private void UpdateNavGraphGizmoVisibility(RuntimeUnit unit)
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

            var unitGraphMask = GetPathGraphMask(unit);
            foreach (var graph in graphs)
            {
                if (graph != null)
                {
                    graph.drawGizmos = unitGraphMask.Contains(graph);
                }
            }
        }

        private void UpdateMovePreviewSizeForUnit(RuntimeUnit unit)
        {
            var diameterScale = GetMovePreviewDiameter(unit);
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

        private static float GetMovePreviewDiameter(RuntimeUnit unit)
        {
            if (unit?.Pawn != null)
            {
                var col = unit.Pawn.GetComponent<CapsuleCollider>();
                if (col != null)
                {
                    return Mathf.Max(VisualizerLineWidth, col.radius * RadiusToDiameterMultiplier);
                }
            }

            return unit != null
                ? Mathf.Max(VisualizerLineWidth, unit.Definition.Stats.modelSize.GetPawnScale().x)
                : 1f;
        }

        private void HandlePlayerUnitClick(RuntimeUnit unit)
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

        private void FocusCameraOnUnit(RuntimeUnit unit)
        {
            if (unit == null || unit.Pawn == null)
            {
                return;
            }

            var focusPoint = unit.Pawn.transform.position;
            focusPoint.y = GroundYPosition;
            cameraManager?.FocusOnPoint(focusPoint);
        }

        private WeaponProfile GetSelectedAttackWeapon(RuntimeUnit unit)
        {
            if (unit.Weapons == null || unit.Weapons.Length == 0)
            {
                return WeaponProfile.CreateDefault();
            }

            return unit.Weapons[Mathf.Clamp(selectedAttackWeaponIndex, 0, unit.Weapons.Length - 1)];
        }

        private static float GetLongestWeaponRange(RuntimeUnit unit)
        {
            if (unit.Weapons == null || unit.Weapons.Length == 0)
            {
                return 1.5f;
            }

            var range = unit.Weapons[0].Range;
            for (var i = 1; i < unit.Weapons.Length; i++)
            {
                range = Mathf.Max(range, unit.Weapons[i].Range);
            }

            return range;
        }

        private static WeaponProfile GetBestWeaponForDistance(RuntimeUnit attacker, RuntimeUnit target, float distance)
        {
            if (attacker?.Weapons == null || attacker.Weapons.Length == 0)
            {
                return null;
            }

            var combinedRadii = GetCombinedUnitRadiiInches(attacker, target);
            WeaponProfile best = null;
            for (var i = 0; i < attacker.Weapons.Length; i++)
            {
                var weapon = attacker.Weapons[i];
                if (distance > weapon.Range + combinedRadii)
                {
                    continue;
                }

                if (best == null || weapon.Power > best.Power)
                {
                    best = weapon;
                }
            }

            return best;
        }

        private bool CanUnitTarget(RuntimeUnit attacker, RuntimeUnit target, WeaponProfile weapon)
        {
            if (!IsTargetInRange(attacker, target, weapon) || !HasLineOfSight(attacker, target))
            {
                return false;
            }

            return !attacker.IsPlayerControlled || HasPlayerFogVisionForTarget(attacker, target);
        }

        private bool HasPlayerFogVisionForTarget(RuntimeUnit observer, RuntimeUnit target)
        {
            if (!IsWithinObserverVisibilityRange(observer, target))
            {
                return false;
            }

            return IsInLiveFogVision(GetLineOfSightVolume(target).SightPoint);
        }

        private bool IsWithinObserverVisibilityRange(RuntimeUnit observer, RuntimeUnit target)
        {
            if (observer?.Pawn == null || target?.Pawn == null || observer.Definition?.Stats == null)
            {
                return false;
            }

            var distanceInches = CombatLineOfSight.GetPlanarEdgeToEdgeDistanceInches(
                GetLineOfSightVolume(observer),
                GetLineOfSightVolume(target));
            return distanceInches <= observer.Definition.Stats.visibilityRange + WorldUnitsToInches(PositionArrivalTolerance);
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
            // Shroud regrows to fogExploredShroudVisibility (~0.35); live vision is ~1.0.
            return Mathf.Max(0.5f, fogExploredShroudVisibility + 0.12f);
        }

        private static bool IsTargetInRange(RuntimeUnit attacker, RuntimeUnit target, WeaponProfile weapon)
        {
            if (attacker?.Pawn == null || target?.Pawn == null || weapon == null)
            {
                return false;
            }

            var distance = GetPlanarDistance(attacker.Pawn.transform.position, target.Pawn.transform.position);
            return distance <= weapon.Range + GetCombinedUnitRadiiInches(attacker, target) + WorldUnitsToInches(PositionArrivalTolerance);
        }

        private bool HasLineOfSight(RuntimeUnit observer, RuntimeUnit target)
        {
            if (observer?.Pawn == null || target?.Pawn == null || !observer.IsAlive || !target.IsAlive)
            {
                return false;
            }

            var observerVolume = GetLineOfSightVolume(observer);
            var targetVolume = GetLineOfSightVolume(target);
            if (IsTerrainBlockingLineOfSight(observerVolume, targetVolume))
            {
                return false;
            }

            if (CombatTerrainLineOfSight.IsForestDepthBlockingLineOfSight(
                    observerVolume,
                    targetVolume,
                    target.Definition.Stats.modelSize,
                    observer.Definition,
                    observer.Pawn))
            {
                return false;
            }

            lineOfSightInterveningVolumes.Clear();
            for (var i = 0; i < allRuntimeUnits.Count; i++)
            {
                var candidate = allRuntimeUnits[i];
                if (candidate == null || !candidate.IsAlive || ReferenceEquals(candidate, observer) || ReferenceEquals(candidate, target))
                {
                    continue;
                }

                lineOfSightInterveningVolumes.Add(GetLineOfSightVolume(candidate));
            }

            return CombatLineOfSight.HasLineOfSight(observerVolume, targetVolume, lineOfSightInterveningVolumes);
        }

        private CombatLineOfSightVolume GetLineOfSightVolume(RuntimeUnit unit)
        {
            return CombatLineOfSight.CreateVolume(GetPawnFeetPosition(unit), unit.Definition.Stats.modelSize);
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

        private static float GetCombinedUnitRadiiInches(RuntimeUnit first, RuntimeUnit second)
        {
            return GetUnitRadiusInches(first) + GetUnitRadiusInches(second);
        }

        private static float GetUnitRadiusInches(RuntimeUnit unit)
        {
            return WorldUnitsToInches(GetUnitCollisionRadius(unit));
        }

        private static float GetPlanarDistance(Vector3 from, Vector3 to)
        {
            var delta = to - from;
            delta.y = 0f;
            return WorldUnitsToInches(delta.magnitude);
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
            if (!RaycastForMouseHit(ray, out var hit))
            {
                return;
            }

            for (var i = 0; i < enemyRuntimeUnits.Count; i++)
            {
                var enemy = enemyRuntimeUnits[i];
                if (enemy.IsAlive && enemy.IsVisibleToPlayer && enemy.Pawn == hit.collider.gameObject)
                {
                    hoveredEnemyUnit = enemy;
                    return;
                }
            }
        }

        private void UpdateFogOfWarVisibility()
        {
            for (var i = 0; i < enemyRuntimeUnits.Count; i++)
            {
                var enemy = enemyRuntimeUnits[i];
                ApplyUnitVisibility(enemy, CanPlayerSeeUnit(enemy));
            }
        }

        private bool CanPlayerSeeUnit(RuntimeUnit target)
        {
            if (target == null || !target.IsAlive)
            {
                return false;
            }

            var targetSightPoint = GetLineOfSightVolume(target).SightPoint;
            if (!IsInLiveFogVision(targetSightPoint))
            {
                return false;
            }

            for (var i = 0; i < playerRuntimeUnits.Count; i++)
            {
                var observer = playerRuntimeUnits[i];
                if (observer.IsAlive
                    && IsWithinObserverVisibilityRange(observer, target)
                    && GetCachedHasLineOfSight(observer, target))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the HasLineOfSight result for an (observer, target) pair, reusing the cached
        /// value when no unit has moved since it was computed.  The cache is invalidated by
        /// <see cref="losDirtyVersion"/> which is incremented every time a pawn position changes.
        /// </summary>
        private bool GetCachedHasLineOfSight(RuntimeUnit observer, RuntimeUnit target)
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

        private static void ApplyUnitVisibility(RuntimeUnit unit, bool isVisible)
        {
            if (unit == null || unit.IsVisibleToPlayer == isVisible)
            {
                return;
            }

            unit.IsVisibleToPlayer = isVisible;
            if (unit.Renderers == null)
            {
                return;
            }

            for (var i = 0; i < unit.Renderers.Length; i++)
            {
                if (unit.Renderers[i] != null)
                {
                    unit.Renderers[i].enabled = isVisible;
                }
            }
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

        private static RuntimeUnit FindFirstAlive(List<RuntimeUnit> units)
        {
            for (var i = 0; i < units.Count; i++)
            {
                if (units[i].IsAlive)
                {
                    return units[i];
                }
            }

            return null;
        }

        private static RuntimeUnit FindNearestAliveUnit(RuntimeUnit source, List<RuntimeUnit> candidates)
        {
            RuntimeUnit best = null;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (!candidate.IsAlive)
                {
                    continue;
                }

                var distance = GetPlanarDistance(source.Pawn.transform.position, candidate.Pawn.transform.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        private void ApplyAim(RuntimeUnit unit)
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
