using System.Collections.Generic;
using System.Text;
using Pathfinding;
using UnityEngine;

namespace IronKingdoms.Combat
{
    public class TestLevelUnitController : MonoBehaviour
    {
        private const float AiInRangeTolerance = 0.95f;
        private const float AiDesiredStopFactor = 0.85f;
        private const float AiMinimumStopDistance = 0.2f;
        private const float RadiusToDiameterMultiplier = 2f;
        private const float PositionArrivalTolerance = 0.05f;
        private const float NavmeshContainmentTolerance = 0.02f;
        private const float MovementBudgetEpsilon = 0.001f;
        private const float MillimetersPerInch = 25.4f;
        private const float MillimetersPerWorldUnit = 30f;
        private const float WorldUnitsPerInch = MillimetersPerInch / MillimetersPerWorldUnit;
        private const float InchesPerWorldUnit = MillimetersPerWorldUnit / MillimetersPerInch;
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

        private enum TurnSide
        {
            Player,
            Enemy
        }

        private enum UnitActionMode
        {
            None,
            Move,
            Attack
        }

        private enum MovementStepOption
        {
            Advance,
            Run,
            Charge
        }

        [SerializeField] private List<UnitTypeDefinition> playerUnits = new();
        [SerializeField] private List<UnitTypeDefinition> enemyUnits = new();
        [SerializeField] private Transform playerSpawnAnchor;
        [SerializeField] private Transform enemySpawnAnchor;
        [SerializeField, Min(0.5f)] private float spawnSpacing = 2f;
        [SerializeField, Min(0.1f)] private float aiThinkInterval = 0.5f;
        [SerializeField] private CombatCameraManager cameraManager;
        [SerializeField] private NavPathBuilder navPathBuilder;
        [SerializeField] private bool autoSpawnOnStart = true;
        private MatchArmySpawner matchArmySpawner;

        private readonly List<RuntimeUnit> playerRuntimeUnits = new();
        private readonly List<RuntimeUnit> enemyRuntimeUnits = new();
        private readonly List<RuntimeUnit> allRuntimeUnits = new();
        private readonly Plane boardPlane = new(Vector3.up, Vector3.zero);
        private readonly RaycastHit[] terrainRaycastBuffer = new RaycastHit[16];
        private RuntimeUnit selectedUnit;
        private TurnSide activeTurnSide = TurnSide.Player;
        private float aiThinkTimer;
        private RuntimeUnit activeEnemyUnit;
        private RuntimeUnit activeEnemyTarget;
        private int enemyActivationIndex;
        private bool enemyIssuedMoveForActiveUnit;
        private bool enemyResolvedActionForActiveUnit;
        private RuntimeUnit hoveredEnemyUnit;

        private struct FloatingDamageEntry
        {
            public Vector3 WorldPosition;
            public string Text;
            public float Age;
            public Color Color;
        }

        private UnitActionMode currentPlayerMode = UnitActionMode.None;
        private MovementStepOption selectedMovementOption = MovementStepOption.Advance;
        private int selectedAttackWeaponIndex;
        private LineRenderer movementPathLine;
        private LineRenderer weaponRangeRingLine;
        private readonly List<LineRenderer> attackTargetRings = new();
        private readonly List<FloatingDamageEntry> floatingDamageEntries = new();
        private readonly List<string> combatLog = new();
        private Vector2 combatLogScrollPosition;
        private GUIStyle floatingDamageStyle;
        private GUIStyle floatingDamageShadowStyle;
        private GameObject destinationMarkerObject;
        private Material visualizerMaterial;
        private RuntimeUnit lastClickedPlayerUnit;
        private float lastClickedPlayerUnitClickTime = float.NegativeInfinity;
        private int uiCancelFrame = -1;

        // Movement preview state ----------------------------------------------------------------
        // previewPath holds the last NavPathBuilder-computed waypoints for the current hover.
        // It is set asynchronously; the most recent result is always used for both visualisation
        // and (on click-confirm) for the actual movement order, so preview and execution are 100%
        // identical.
        private List<Vector3> previewPath;
        private bool previewPathPending;
        private Vector3 previewPathTo;
        private float previewMovementBudget;
        private float lastPathPreviewTime;

        private void Awake()
        {
            EnsureCameraManagerAssigned();
            EnsureNavPathBuilderAssigned();
            EnsureMatchArmySpawnerAssigned();
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
            UpdateHoveredEnemy();
        }

        private void EnsureCameraManagerAssigned()
        {
            if (cameraManager == null)
            {
                cameraManager = GetComponent<CombatCameraManager>();
            }
        }

        private void EnsureNavPathBuilderAssigned()
        {
            if (navPathBuilder == null)
            {
                navPathBuilder = GetComponent<NavPathBuilder>();
            }

            if (navPathBuilder == null)
            {
                navPathBuilder = NavPathBuilder.instance;
            }

            if (navPathBuilder == null)
            {
                var go = new GameObject("NavPathBuilder");
                go.transform.SetParent(transform);
                navPathBuilder = go.AddComponent<NavPathBuilder>();
            }
        }

        private void EnsureMatchArmySpawnerAssigned()
        {
            matchArmySpawner ??= new MatchArmySpawner(navPathBuilder);
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

            // Request a new path from NavPathBuilder when the hover target has moved enough
            // and no request is already in flight.
            var horizontalDist = new Vector2(previewPathTo.x - hoverPos.x, previewPathTo.z - hoverPos.z).magnitude;
            if (navPathBuilder != null && !previewPathPending && (horizontalDist >= PathPreviewUpdateDistance
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
                    if (selectedUnit == null || currentPlayerMode != UnitActionMode.Move)
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
                var fullLength = CalculatePathMovementCostInInches(previewPath, previewUnitRadius);

                withinRange = fullLength <= effectiveBudget + WorldUnitsToInches(PositionArrivalTolerance);
                if (TryGetPathStopPointAtMovementBudget(previewPath, effectiveBudget, out var stopPoint, previewUnitRadius))
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

            // Draw only nav-path waypoints; no straight-line fallback.
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
                if (!enemy.IsAlive || !IsTargetInRange(selectedUnit, enemy, weapon))
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

        [ContextMenu("Spawn Units")]
        public void SpawnUnits()
        {
            BuildVisualizers();
            ClearSpawnedUnits();
            SpawnArmy(playerUnits, playerSpawnAnchor, playerRuntimeUnits, true, new Color(0.2f, 0.5f, 1f));
            SpawnArmy(enemyUnits, enemySpawnAnchor, enemyRuntimeUnits, false, new Color(1f, 0.3f, 0.3f));
            StartPlayerTurn();
        }

        public void SetSpawnAnchors(Transform playerAnchor, Transform enemyAnchor)
        {
            playerSpawnAnchor = playerAnchor;
            enemySpawnAnchor = enemyAnchor;
        }

        /// <summary>
        /// Prevents <see cref="SpawnUnits"/> from being called automatically in Start.
        /// Call this from <see cref="CombatMapSetup"/> before the map scene has finished loading
        /// so that units are not spawned before their spawn-point anchors are resolved.
        /// </summary>
        public void DisableAutoSpawn()
        {
            autoSpawnOnStart = false;
        }

        private void SpawnArmy(List<UnitTypeDefinition> units, Transform anchor, List<RuntimeUnit> destination, bool isPlayerControlled, Color color)
        {
            if (units == null)
            {
                return;
            }

            EnsureMatchArmySpawnerAssigned();
            var placements = matchArmySpawner.BuildPlacements(units, anchor, spawnSpacing);
            for (var i = 0; i < placements.Count; i++)
            {
                var unitDefinition = placements[i].UnitDefinition;
                unitDefinition.Stats.EnsureWeaponDefaults();
                var pawnScale = unitDefinition.Stats.modelSize.GetPawnScale();
                var spawnPos = placements[i].Position;

                GameObject pawn;
                if (unitDefinition.VisualPrefab != null)
                {
                    pawn = Instantiate(unitDefinition.VisualPrefab, spawnPos, Quaternion.identity, transform);
                }
                else
                {
                    pawn = BuildProceduralPawn(pawnScale, spawnPos);
                }

                pawn.name = $"{unitDefinition.DisplayName} ({(isPlayerControlled ? "Player" : "Enemy")})";

                var baseColor = new Color(color.r * 0.55f, color.g * 0.55f, color.b * 0.55f);
                var bodyTransform = pawn.transform.Find("Body");
                var baseTransform = pawn.transform.Find("Base");
                if (bodyTransform != null)
                {
                    var bodyRenderer = bodyTransform.GetComponent<Renderer>();
                    if (bodyRenderer != null)
                    {
                        bodyRenderer.material.color = color;
                    }
                }

                if (baseTransform != null)
                {
                    var baseRenderer = baseTransform.GetComponent<Renderer>();
                    if (baseRenderer != null)
                    {
                        baseRenderer.material.color = baseColor;
                    }
                }

                var pawnCollider = pawn.GetComponent<CapsuleCollider>();
                if (pawnCollider != null)
                {
                    pawnCollider.isTrigger = true;
                }

                ConfigureUnitNavmeshCut(pawn, pawnCollider, pawnScale);

                var runtimeUnit = new RuntimeUnit(unitDefinition, isPlayerControlled, pawn);
                SnapUnitToNavmesh(runtimeUnit);
                destination.Add(runtimeUnit);
                allRuntimeUnits.Add(runtimeUnit);
            }
        }

        private void ConfigureUnitNavmeshCut(GameObject pawn, CapsuleCollider pawnCollider, Vector3 pawnScale)
        {
            if (pawn == null)
            {
                return;
            }

            var navmeshCut = pawn.GetComponent<NavmeshCut>();
            if (navmeshCut == null)
            {
                navmeshCut = pawn.AddComponent<NavmeshCut>();
            }

            var radius = pawnCollider != null
                ? pawnCollider.radius
                : Mathf.Max(0.1f, pawnScale.x * 0.5f);
            navmeshCut.type = NavmeshCut.MeshType.Circle;
            navmeshCut.circleRadius = radius;
            navmeshCut.circleResolution = UnitNavmeshCutCircleResolution;
            navmeshCut.height = Mathf.Max(UnitNavmeshCutMinimumHeight, pawnScale.y * NavmeshCutHeightMultiplier);
            navmeshCut.center = Vector3.zero;
            navmeshCut.isDual = false;
            navmeshCut.updateDistance = UnitNavmeshCutUpdateDistance;
            navmeshCut.radiusExpansionMode = NavmeshCut.RadiusExpansionMode.DontExpand;
            navmeshCut.useRotationAndScale = false;
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

        private GameObject BuildProceduralPawn(Vector3 pawnScale, Vector3 spawnPos)
        {
            var root = new GameObject();
            root.transform.SetPositionAndRotation(spawnPos, Quaternion.identity);
            root.transform.SetParent(transform);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform);
            body.transform.localPosition = new Vector3(0f, pawnScale.y, 0f);
            body.transform.localScale = pawnScale;
            var bodyCollider = body.GetComponent<Collider>();
            if (bodyCollider != null)
            {
                bodyCollider.enabled = false;
            }

            var baseDisk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseDisk.name = "Base";
            baseDisk.transform.SetParent(root.transform);
            baseDisk.transform.localPosition = new Vector3(0f, PawnBaseHeightScale, 0f);
            baseDisk.transform.localScale = new Vector3(pawnScale.x, PawnBaseHeightScale, pawnScale.z);
            var baseCollider = baseDisk.GetComponent<Collider>();
            if (baseCollider != null)
            {
                baseCollider.enabled = false;
            }

            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 1;
            col.center = new Vector3(0f, pawnScale.y, 0f);
            col.radius = pawnScale.x * 0.5f;
            col.height = pawnScale.y * 2f;

            return root;
        }

        private void ClearSpawnedUnits()
        {
            for (var i = 0; i < allRuntimeUnits.Count; i++)
            {
                var runtimeUnit = allRuntimeUnits[i];
                if (runtimeUnit.Pawn != null)
                {
                    Destroy(runtimeUnit.Pawn);
                }
            }

            playerRuntimeUnits.Clear();
            enemyRuntimeUnits.Clear();
            allRuntimeUnits.Clear();
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

                if (IsTargetInRange(selectedUnit, enemy, attackWeapon))
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

                var targetPosition = unit.MoveTarget.Value;
                var unitRadius = GetUnitCollisionRadius(unit);
                var terrainSpeedMultiplier = GetMovementSpeedMultiplierAtPoint(unit.Pawn.transform.position, unitRadius);
                var maxStepThisFrame = InchesToWorldUnits(unit.Definition.Stats.speed * terrainSpeedMultiplier) * deltaTime;
                var allowedStep = Mathf.Min(maxStepThisFrame, InchesToWorldUnits(unit.RemainingMovementThisTurn));
                if (allowedStep <= 0f)
                {
                    unit.MoveTarget = null;
                    unit.PathWaypoints = null;
                    continue;
                }

                var currentPosition = unit.Pawn.transform.position;
                var nextPosition = Vector3.MoveTowards(currentPosition, targetPosition, allowedStep);
                // Keep units grounded on terrain without forcing XZ onto navmesh every frame.
                nextPosition = GetGroundedPositionKeepingXZ(unit, nextPosition);
                // Safety guard: if movement drifts off navmesh, snap back to the nearest
                // walkable point so units cannot leave the nav surface.
                nextPosition = ConstrainPositionToNavmesh(unit, nextPosition);
                var movedDistance = Vector3.Distance(currentPosition, nextPosition);
                unit.Pawn.transform.position = nextPosition;
                var movementCost = CalculateMovementCostForSegmentInInches(currentPosition, nextPosition, unitRadius);
                unit.RemainingMovementThisTurn = Mathf.Max(0f, unit.RemainingMovementThisTurn - movementCost);

                var reachedCurrentTarget = Vector3.Distance(nextPosition, targetPosition) <= PositionArrivalTolerance
                    || unit.RemainingMovementThisTurn <= MovementBudgetEpsilon;

                if (reachedCurrentTarget)
                {
                    // Advance to the next waypoint if one is available.
                    var waypoints = unit.PathWaypoints;
                    var nextIndex = unit.PathWaypointIndex + 1;
                    if (waypoints != null && nextIndex < waypoints.Count
                        && unit.RemainingMovementThisTurn > MovementBudgetEpsilon)
                    {
                        unit.PathWaypointIndex = nextIndex;
                        var nextWaypoint = waypoints[nextIndex];
                        nextWaypoint = GetGroundedNavmeshPositionForUnit(unit, nextWaypoint);
                        unit.MoveTarget = nextWaypoint;
                    }
                    else
                    {
                        unit.MoveTarget = null;
                        unit.PathWaypoints = null;
                    }
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

            var waypoints = ClampPathToMovementBudget(smoothedPath, movementBudget, GetUnitCollisionRadius(unit));
            if (waypoints.Count >= 2)
            {
                unit.PathWaypoints = waypoints;
                unit.PathWaypointIndex = 0;
                var firstTarget = waypoints[1];
                firstTarget = GetGroundedNavmeshPositionForUnit(unit, firstTarget);
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
            return zone != null && zone.ZoneType == CombatZoneType.RoughTerrain;
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
                if (!IsUnitPawn(h.collider.gameObject) && !IsRoughTerrainCollider(h.collider) && h.distance < closestDist)
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

        private Vector3 ConstrainPositionToNavmesh(RuntimeUnit unit, Vector3 worldPosition)
        {
            var navPosition = GetGroundedNavmeshPositionForUnit(unit, worldPosition);
            var horizontalDelta = new Vector2(worldPosition.x - navPosition.x, worldPosition.z - navPosition.z).magnitude;
            return horizontalDelta > NavmeshContainmentTolerance ? navPosition : worldPosition;
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

        private List<Vector3> ClampPathToMovementBudget(IReadOnlyList<Vector3> waypoints, float budget, float unitRadius = 0f)
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
                if (TryGetSegmentStopPointAtMovementBudget(waypoints[i - 1], waypoints[i], budget - distanceCovered, out var segmentStopPoint, unitRadius))
                {
                    result.Add(segmentStopPoint);

                    break;
                }

                result.Add(waypoints[i]);
                distanceCovered += CalculateMovementCostForSegmentInInches(waypoints[i - 1], waypoints[i], unitRadius);
            }

            return result;
        }

        private bool TryGetPathStopPointAtMovementBudget(IReadOnlyList<Vector3> waypoints, float budget, out Vector3 stopPoint, float unitRadius = 0f)
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
                if (TryGetSegmentStopPointAtMovementBudget(waypoints[i - 1], waypoints[i], budget - distanceCovered, out stopPoint, unitRadius))
                {
                    return true;
                }

                distanceCovered += CalculateMovementCostForSegmentInInches(waypoints[i - 1], waypoints[i], unitRadius);
                stopPoint = waypoints[i];
            }

            return true;
        }

        private float CalculatePathMovementCostInInches(IReadOnlyList<Vector3> waypoints, float unitRadius = 0f)
        {
            if (waypoints == null || waypoints.Count < 2)
            {
                return 0f;
            }

            var movementCost = 0f;
            for (var i = 1; i < waypoints.Count; i++)
            {
                movementCost += CalculateMovementCostForSegmentInInches(waypoints[i - 1], waypoints[i], unitRadius);
            }

            return movementCost;
        }

        private float CalculateMovementCostForSegmentInInches(Vector3 from, Vector3 to, float unitRadius = 0f)
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
                var speedMultiplier = GetMovementSpeedMultiplierAtPoint(samplePoint, unitRadius);
                movementCost += segmentDistanceInches / speedMultiplier;
            }

            return movementCost;
        }

        private bool TryGetSegmentStopPointAtMovementBudget(Vector3 segmentStart, Vector3 segmentEnd, float budgetRemaining, out Vector3 stopPoint, float unitRadius = 0f)
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
                var speedMultiplier = GetMovementSpeedMultiplierAtPoint(samplePoint, unitRadius);
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

        private bool IsUnitInRoughTerrain(RuntimeUnit unit)
        {
            var position = unit.Pawn.transform.position;
            var activeZones = CombatZone.ActiveZones;
            for (var i = 0; i < activeZones.Count; i++)
            {
                var zone = activeZones[i];
                if (zone != null && zone.ZoneType == CombatZoneType.RoughTerrain && zone.ContainsPoint(position))
                {
                    return true;
                }
            }

            return false;
        }

        private float GetMovementSpeedMultiplierAtPoint(Vector3 worldPoint, float unitRadius = 0f)
        {
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
                unit.IsAimingThisTurn = false;
                unit.MoveTarget = null;
                unit.PathWaypoints = null;
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
            if (!weapon.EvaluateAttackHit(atkDie1, atkDie2, attackRoll, defender.Definition.Stats.defense))
            {
                SpawnFloatingText(GetPawnCenterPosition(defender), "Miss!", new Color(1f, 0.9f, 0.2f, 1f));
                AddCombatLogEntry(
                    $"{attacker.Definition.DisplayName} → {defender.Definition.DisplayName}  " +
                    $"ATK [{atkDie1}+{atkDie2}]+{attackValue}{modifierText} {attackStatLabel} = {attackRoll} vs DEF {defender.Definition.Stats.defense} → Miss");
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
                $"ATK [{atkDie1}+{atkDie2}]+{attackValue}{modifierText} {attackStatLabel} = {attackRoll} vs DEF {defender.Definition.Stats.defense} → Hit!  " +
                $"DMG [{dmgDie1}+{dmgDie2}]+{weapon.Power} POW = {damageRoll + weapon.Power} vs ARM {defender.Definition.Stats.armor} → {logResult}");
            attacker.IsAimingThisTurn = false;
            if (!defender.IsAlive)
            {
                defender.Pawn.SetActive(false);
                AddCombatLogEntry($"{defender.Definition.DisplayName} defeated!");
                if (ReferenceEquals(defender, selectedUnit))
                {
                    SelectUnit(FindFirstAlive(playerRuntimeUnits));
                }
            }
        }

        private void AddCombatLogEntry(string entry)
        {
            combatLog.Add(entry);
            if (combatLog.Count > CombatLogMaxEntries)
            {
                combatLog.RemoveAt(0);
            }

            combatLogScrollPosition = new Vector2(0f, float.MaxValue);
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

        private void TickFloatingDamage(float deltaTime)
        {
            for (var i = floatingDamageEntries.Count - 1; i >= 0; i--)
            {
                var entry = floatingDamageEntries[i];
                entry.Age += deltaTime;
                if (entry.Age >= FloatingDamageLifetime)
                {
                    floatingDamageEntries.RemoveAt(i);
                }
                else
                {
                    floatingDamageEntries[i] = entry;
                }
            }
        }

        private void SpawnFloatingText(Vector3 worldPosition, string text, Color color)
        {
            worldPosition.y += 0.5f;
            floatingDamageEntries.Add(new FloatingDamageEntry
            {
                WorldPosition = worldPosition,
                Text = text,
                Age = 0f,
                Color = color
            });
        }

        private static float CalculateHitChancePercent(RuntimeUnit attacker, RuntimeUnit defender, WeaponProfile weapon)
        {
            var attackStat = GetAttackStatForWeapon(attacker, weapon);
            var attackModifier = GetToHitModifier(attacker);
            var hits = 0;
            for (var d1 = 1; d1 <= 6; d1++)
            {
                for (var d2 = 1; d2 <= 6; d2++)
                {
                    var attackRoll = d1 + d2 + attackStat + attackModifier;
                    if (weapon.EvaluateAttackHit(d1, d2, attackRoll, defender.Definition.Stats.defense))
                    {
                        hits++;
                    }
                }
            }

            return hits / 36f * 100f;
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

        private void DrawFloatingDamageNumbers()
        {
            if (floatingDamageEntries.Count == 0)
            {
                return;
            }

            var activeCamera = cameraManager != null ? cameraManager.ActiveCamera : Camera.main;
            if (activeCamera == null)
            {
                return;
            }

            if (floatingDamageStyle == null)
            {
                floatingDamageStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 20,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                floatingDamageShadowStyle = new GUIStyle(floatingDamageStyle);
            }

            for (var i = 0; i < floatingDamageEntries.Count; i++)
            {
                var entry = floatingDamageEntries[i];
                var t = entry.Age / FloatingDamageLifetime;
                var fadeAlpha = 1f - (t * t);
                var screenPos = activeCamera.WorldToScreenPoint(entry.WorldPosition);
                if (screenPos.z <= 0f)
                {
                    continue;
                }

                var riseOffset = entry.Age * FloatingDamageRiseSpeed;
                var guiX = screenPos.x - 40f;
                var guiY = Screen.height - screenPos.y - riseOffset - 20f;
                var labelRect = new Rect(guiX, guiY, 80f, 30f);

                var textColor = entry.Color;
                textColor.a = fadeAlpha;
                floatingDamageStyle.normal.textColor = textColor;
                floatingDamageShadowStyle.normal.textColor = new Color(0f, 0f, 0f, fadeAlpha * 0.65f);

                GUI.Label(new Rect(guiX + 1f, guiY + 1f, 80f, 30f), entry.Text, floatingDamageShadowStyle);
                GUI.Label(labelRect, entry.Text, floatingDamageStyle);
            }
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

        private static bool IsTargetInRange(RuntimeUnit attacker, RuntimeUnit target, WeaponProfile weapon)
        {
            if (attacker?.Pawn == null || target?.Pawn == null || weapon == null)
            {
                return false;
            }

            var distance = GetPlanarDistance(attacker.Pawn.transform.position, target.Pawn.transform.position);
            return distance <= weapon.Range + GetCombinedUnitRadiiInches(attacker, target) + WorldUnitsToInches(PositionArrivalTolerance);
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
            return inches * WorldUnitsPerInch;
        }

        private static float WorldUnitsToInches(float worldUnits)
        {
            return worldUnits * InchesPerWorldUnit;
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
                if (enemy.IsAlive && enemy.Pawn == hit.collider.gameObject)
                {
                    hoveredEnemyUnit = enemy;
                    return;
                }
            }
        }

        private static string BuildHealthBoxes(int health, int maxHealth)
        {
            var clampedCurrent = Mathf.Clamp(health, 0, maxHealth);
            var sb = new StringBuilder(maxHealth + (maxHealth / 10) + 1);
            for (var i = 0; i < maxHealth; i++)
            {
                if (i > 0 && i % 10 == 0)
                {
                    sb.Append(' ');
                }

                sb.Append(i < clampedCurrent ? '■' : '□');
            }

            return sb.ToString();
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

        private void OnGUI()
        {
            cameraManager?.DrawGui();
            DrawFloatingDamageNumbers();
            DrawCombatLog();

            GUILayout.BeginArea(new Rect(RosterAreaX, RosterAreaY, RosterAreaWidth, RosterAreaHeight), "Player-Controlled Units", GUI.skin.window);
            GUILayout.Label($"Active Turn: {activeTurnSide}");
            if (activeTurnSide == TurnSide.Player && GUILayout.Button("End Turn"))
            {
                if (!WasUiCancelTriggeredThisFrame())
                {
                    EndPlayerTurn();
                }
            }

            GUILayout.Space(6f);
            if (playerRuntimeUnits.Count == 0)
            {
                GUILayout.Label("No units assigned.");
            }
            else
            {
                for (var i = 0; i < playerRuntimeUnits.Count; i++)
                {
                    var unit = playerRuntimeUnits[i];
                    var label = $"{i + 1}. {unit.Definition.DisplayName} - HP {unit.Health}";
                    if (!unit.IsAlive)
                    {
                        label += " (defeated)";
                    }

                    if (GUILayout.Button(label))
                    {
                        if (!WasUiCancelTriggeredThisFrame())
                        {
                            SelectUnit(unit);
                        }
                    }
                }
            }

            GUILayout.Space(8f);
            GUILayout.Label("Enemies");
            for (var i = 0; i < enemyRuntimeUnits.Count; i++)
            {
                var enemy = enemyRuntimeUnits[i];
                var enemyLabel = $"{enemy.Definition.DisplayName} - HP {enemy.Health}/{enemy.Definition.Stats.health}";
                if (!enemy.IsAlive)
                {
                    enemyLabel += " (defeated)";
                }

                GUILayout.Label(enemyLabel);
            }
            GUILayout.EndArea();

            if (selectedUnit == null)
            {
                DrawHoveredEnemyHealth();
                return;
            }

            GUILayout.BeginArea(GetSelectedUnitPanelRect(), "Selected Unit", GUI.skin.window);
            GUILayout.Label(selectedUnit.Definition.DisplayName);
            GUILayout.Label($"Role: {selectedUnit.Definition.Role}");
            GUILayout.Label($"HP: {selectedUnit.Health}/{selectedUnit.Definition.Stats.health}");
            GUILayout.Label(BuildHealthBoxes(selectedUnit.Health, selectedUnit.Definition.Stats.health));
            GUILayout.Label($"Speed: {selectedUnit.Definition.Stats.speed:0.0}  |  Move left: {selectedUnit.RemainingMovementThisTurn:0.0}\"");
            var inRoughTerrain = IsUnitInRoughTerrain(selectedUnit);
            GUILayout.Label($"In Rough Terrain: {(inRoughTerrain ? "Yes" : "No")}");
            GUILayout.Label($"Model Size: {selectedUnit.Definition.Stats.modelSize.DisplayName()}");
            var selectedWeapon = GetSelectedAttackWeapon(selectedUnit);
            GUILayout.Label($"Weapon: {selectedWeapon.DisplayName}");
            GUILayout.Label($"Type: {selectedWeapon.AttackType}  |  Range: {selectedWeapon.Range:0.0}\"");
            GUILayout.Label($"Weapon Power: {selectedWeapon.Power}");
            if (selectedUnit.IsAimingThisTurn)
            {
                GUILayout.Label($"Aiming: +{AimToHitBonus} to hit (next attack)");
            }
            GUILayout.Label($"MAT Mod: {selectedWeapon.MatModifier:+#;-#;0}  |  RAT Mod: {selectedWeapon.RatModifier:+#;-#;0}");
            var effectiveMat = selectedUnit.Definition.Stats.meleeAttack + selectedWeapon.MatModifier;
            var effectiveRat = selectedUnit.Definition.Stats.rangedAttack + selectedWeapon.RatModifier;
            GUILayout.Label($"Effective MAT: {effectiveMat}  |  Effective RAT: {effectiveRat}");

            GUILayout.EndArea();
            DrawActionBar();
            DrawHoveredEnemyHealth();
        }

        private void DrawActionBar()
        {
            if (selectedUnit == null || activeTurnSide != TurnSide.Player)
            {
                return;
            }

            GUILayout.BeginArea(GetActionBarRect(), string.Empty, GUI.skin.window);
            GUILayout.BeginHorizontal();

            var canMove = selectedUnit.RemainingMovementThisTurn > MovementBudgetEpsilon
                && !selectedUnit.MoveTarget.HasValue
                && (!selectedUnit.HasActedThisTurn || selectedUnit.HasRunActionThisTurn);
            GUI.enabled = canMove;
            var moveLabel = currentPlayerMode == UnitActionMode.Move ? "[ Move ]" : "Move";
            if (GUILayout.Button(moveLabel, GUILayout.Height(30f)))
            {
                if (!WasUiCancelTriggeredThisFrame())
                {
                    SetCurrentMode(currentPlayerMode == UnitActionMode.Move ? UnitActionMode.None : UnitActionMode.Move);
                }
            }

            var canAttack = !selectedUnit.HasActedThisTurn && !selectedUnit.HasRunActionThisTurn;
            GUI.enabled = canAttack;
            var attackLabel = currentPlayerMode == UnitActionMode.Attack ? "[ Attack ]" : "Attack";
            if (GUILayout.Button(attackLabel, GUILayout.Height(30f)))
            {
                if (!WasUiCancelTriggeredThisFrame())
                {
                    SetCurrentMode(currentPlayerMode == UnitActionMode.Attack ? UnitActionMode.None : UnitActionMode.Attack);
                }
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (currentPlayerMode == UnitActionMode.Attack)
            {
                GUILayout.Space(6f);
                if (selectedUnit.Weapons != null && selectedUnit.Weapons.Length > 0)
                {
                    GUILayout.BeginHorizontal();
                    for (var i = 0; i < selectedUnit.Weapons.Length; i++)
                    {
                        var weapon = selectedUnit.Weapons[i];
                        var label = $"{weapon.DisplayName} ({weapon.Range:0.0}\")";
                        if (i == selectedAttackWeaponIndex)
                        {
                            label = $"[ {label} ]";
                        }

                        if (GUILayout.Button(label))
                        {
                            if (!WasUiCancelTriggeredThisFrame())
                            {
                                selectedAttackWeaponIndex = i;
                                RefreshAttackRangeRing();
                            }
                        }
                    }

                    GUILayout.EndHorizontal();
                }
            }
            else if (currentPlayerMode == UnitActionMode.Move)
            {
                GUILayout.Space(6f);
                GUILayout.BeginHorizontal();
                var runLocked = selectedUnit.HasRunActionThisTurn;
                if (runLocked)
                {
                    selectedMovementOption = MovementStepOption.Run;
                }

                var advanceBudget = selectedUnit.RemainingMovementThisTurn;
                var advanceCore = $"Advance ({advanceBudget:0.0}\")";
                var advanceLabel = selectedMovementOption == MovementStepOption.Advance ? $"[ {advanceCore} ]" : advanceCore;
                GUI.enabled = !runLocked;
                if (GUILayout.Button(advanceLabel))
                {
                    selectedMovementOption = MovementStepOption.Advance;
                }

                var runBudget = selectedUnit.HasRunActionThisTurn
                    ? selectedUnit.RemainingMovementThisTurn
                    : selectedUnit.RemainingMovementThisTurn * RunMovementMultiplier;
                var runCore = $"Run ({runBudget:0.0}\")";
                var runLabel = selectedMovementOption == MovementStepOption.Run ? $"[ {runCore} ]" : runCore;
                GUI.enabled = canMove;
                if (GUILayout.Button(runLabel))
                {
                    selectedMovementOption = MovementStepOption.Run;
                }

                var chargeBudget = selectedUnit.RemainingMovementThisTurn
                    + (GetSelectedAttackWeapon(selectedUnit).attackType == WeaponAttackType.Melee ? ChargeMovementBonus : 0f);
                var chargeCore = $"Charge ({chargeBudget:0.0}\")";
                var chargeLabel = selectedMovementOption == MovementStepOption.Charge ? $"[ {chargeCore} ]" : chargeCore;
                GUI.enabled = !runLocked;
                if (GUILayout.Button(chargeLabel))
                {
                    selectedMovementOption = MovementStepOption.Charge;
                }

                GUI.enabled = canMove && !selectedUnit.HasRunActionThisTurn;
                if (GUILayout.Button($"Aim (+{AimToHitBonus} to hit)"))
                {
                    if (!WasUiCancelTriggeredThisFrame())
                    {
                        ApplyAim(selectedUnit);
                    }
                }

                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }

        private void ApplyAim(RuntimeUnit unit)
        {
            if (unit == null || !unit.IsAlive || unit.HasActedThisTurn || unit.HasRunActionThisTurn)
            {
                return;
            }

            unit.MoveTarget = null;
            unit.PathWaypoints = null;
            unit.RemainingMovementThisTurn = 0f;
            unit.IsAimingThisTurn = true;
            AddCombatLogEntry($"{unit.Definition.DisplayName} aims (+{AimToHitBonus} to hit).");
            SetCurrentMode(UnitActionMode.None);
        }

        private static Rect GetActionBarRect()
        {
            var areaX = (Screen.width - ActionBarWidth) * 0.5f;
            var areaY = Screen.height - ActionBarHeight - ActionBarBottomMargin;
            return new Rect(areaX, areaY, ActionBarWidth, ActionBarHeight);
        }

        private static Rect GetSelectedUnitPanelRect()
        {
            var areaY = Screen.height - SelectedUnitPanelHeight - SelectedUnitPanelOffsetY;
            return new Rect(SelectedUnitPanelOffsetX, areaY, SelectedUnitPanelWidth, SelectedUnitPanelHeight);
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

        private float GetHoverPanelHeight()
        {
            var showHitChance = currentPlayerMode == UnitActionMode.Attack
                && selectedUnit != null && selectedUnit.IsAlive
                && activeTurnSide == TurnSide.Player;
            return showHitChance ? HoverPanelHeight + HoverPanelAttackExtraHeight : HoverPanelHeight;
        }

        private void DrawHoveredEnemyHealth()
        {
            if (hoveredEnemyUnit == null || !hoveredEnemyUnit.IsAlive)
            {
                return;
            }

            var mousePosition = Input.mousePosition;
            var panelHeight = GetHoverPanelHeight();
            var x = Mathf.Clamp(mousePosition.x + HoverPanelMouseOffset, HoverPanelScreenPadding, Screen.width - HoverPanelWidth - HoverPanelScreenPadding);
            var y = Mathf.Clamp(Screen.height - mousePosition.y + HoverPanelMouseOffset, HoverPanelScreenPadding, Screen.height - panelHeight - HoverPanelScreenPadding);

            GUILayout.BeginArea(new Rect(x, y, HoverPanelWidth, panelHeight), "Target", GUI.skin.window);
            GUILayout.Label(hoveredEnemyUnit.Definition.DisplayName);
            GUILayout.Label($"HP: {hoveredEnemyUnit.Health}/{hoveredEnemyUnit.Definition.Stats.health}");
            GUILayout.Label(BuildHealthBoxes(hoveredEnemyUnit.Health, hoveredEnemyUnit.Definition.Stats.health));
            GUILayout.Label($"DEF: {hoveredEnemyUnit.Definition.Stats.defense}  |  ARM: {hoveredEnemyUnit.Definition.Stats.armor}");
            if (currentPlayerMode == UnitActionMode.Attack && selectedUnit != null && selectedUnit.IsAlive && activeTurnSide == TurnSide.Player)
            {
                var weapon = GetSelectedAttackWeapon(selectedUnit);
                var inRange = IsTargetInRange(selectedUnit, hoveredEnemyUnit, weapon);
                if (inRange)
                {
                    var hitChance = CalculateHitChancePercent(selectedUnit, hoveredEnemyUnit, weapon);
                    GUILayout.Label($"Hit Chance: {hitChance:0}%");
                }
                else
                {
                    GUILayout.Label("Out of range");
                }
            }

            GUILayout.EndArea();
        }

        private void DrawCombatLog()
        {
            if (combatLog.Count == 0)
            {
                return;
            }

            var x = Screen.width - CombatLogPanelWidth - CombatLogPanelRightMargin;
            GUILayout.BeginArea(new Rect(x, CombatLogPanelTopMargin, CombatLogPanelWidth, CombatLogPanelHeight), "Combat Log", GUI.skin.window);
            combatLogScrollPosition = GUILayout.BeginScrollView(combatLogScrollPosition);
            foreach (var entry in combatLog)
            {
                GUILayout.Label(entry);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private sealed class RuntimeUnit
        {
            public RuntimeUnit(UnitTypeDefinition definition, bool isPlayerControlled, GameObject pawn)
            {
                Definition = definition;
                IsPlayerControlled = isPlayerControlled;
                Pawn = pawn;
                NavmeshCut = pawn != null ? pawn.GetComponent<NavmeshCut>() : null;
                Health = definition.Stats.health;
                definition.Stats.EnsureWeaponDefaults();
                if (definition.Stats.weapons == null || definition.Stats.weapons.Length == 0)
                {
                    Weapons = new[] { WeaponProfile.CreateDefault() };
                }
                else
                {
                    Weapons = definition.Stats.weapons;
                }
            }

            public UnitTypeDefinition Definition { get; }
            public bool IsPlayerControlled { get; }
            public GameObject Pawn { get; }
            public NavmeshCut NavmeshCut { get; }
            public WeaponProfile[] Weapons { get; }
            public int Health { get; set; }
            public float RemainingMovementThisTurn { get; set; }
            public bool HasActedThisTurn { get; set; }
            public bool HasRunActionThisTurn { get; set; }
            public bool IsAimingThisTurn { get; set; }
            public Vector3? MoveTarget { get; set; }
            public bool IsAlive => Health > 0;

            /// <summary>World-space waypoints for the current A* path (null when not path-following).</summary>
            public List<Vector3> PathWaypoints { get; set; }

            /// <summary>Index of the waypoint the unit is currently moving toward.</summary>
            public int PathWaypointIndex { get; set; }
        }
    }
}
