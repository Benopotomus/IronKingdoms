using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace IronKingdoms.Combat
{
    public class TestLevelUnitController : MonoBehaviour
    {
        private const float AiInRangeTolerance = 0.95f;
        private const float AiDesiredStopFactor = 0.85f;
        private const float AiMinimumStopDistance = 0.2f;
        private const float PositionArrivalTolerance = 0.05f;
        private const float MovementBudgetEpsilon = 0.001f;
        private const float VisualizerLineWidth = 0.06f;
        private const int AttackRingSegments = 48;
        private const float PawnYPosition = 1f;
        private const float GroundYPosition = 0f;
        private const float MinimumVectorSqrMagnitude = 0.0001f;
        private const float InputAxisDeadzone = 0.001f;
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
        private const float ActionBarHeight = 96f;
        private const float ActionBarBottomMargin = 12f;
        private const float CameraControlsPanelWidth = 460f;
        private const float CameraControlsPanelHeight = 54f;
        private const float CameraControlsPanelTopMargin = 12f;
        private const float HoverPanelWidth = 280f;
        private const float HoverPanelHeight = 86f;
        private const float HoverPanelScreenPadding = 4f;
        private const float HoverPanelMouseOffset = 14f;
        private const float CameraOrbitFallbackForwardDistance = 1f;
        private const float CameraOrbitMinimumDistance = 0.1f;
        private const float DoubleClickIntervalSeconds = 0.3f;
        private const float CameraFocusTransitionSpeed = 12f;
        private const float DefaultTargetRingRadius = 0.6f;
        private const float TargetRingScaleFactor = 0.6f;

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

        [SerializeField] private List<UnitTypeDefinition> playerUnits = new();
        [SerializeField] private List<UnitTypeDefinition> enemyUnits = new();
        [SerializeField] private Transform playerSpawnAnchor;
        [SerializeField] private Transform enemySpawnAnchor;
        [SerializeField, Min(0.5f)] private float spawnSpacing = 2f;
        [SerializeField, Min(0.1f)] private float aiThinkInterval = 0.5f;
        [SerializeField] private Camera selectionCamera;
        [SerializeField] private bool autoSpawnOnStart = true;
        [SerializeField, Min(1f)] private float cameraKeyboardPanSpeed = 10f;
        [SerializeField, Min(0.001f)] private float cameraDragPanSensitivity = 0.02f;
        [SerializeField, Min(0.01f)] private float cameraRotationSensitivity = 0.2f;
        [SerializeField, Range(5f, 89f)] private float cameraMinPitch = 25f;
        [SerializeField, Range(5f, 89f)] private float cameraMaxPitch = 75f;

        private readonly List<RuntimeUnit> playerRuntimeUnits = new();
        private readonly List<RuntimeUnit> enemyRuntimeUnits = new();
        private readonly List<RuntimeUnit> allRuntimeUnits = new();
        private readonly Plane boardPlane = new(Vector3.up, Vector3.zero);
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
        private int selectedAttackWeaponIndex;
        private LineRenderer movementPathLine;
        private readonly List<LineRenderer> attackTargetRings = new();
        private GameObject destinationMarkerObject;
        private Material visualizerMaterial;
        private bool isCameraDragging;
        private Vector3 lastCameraDragMousePosition;
        private bool cameraPitchInitialized;
        private float cameraPitchDegrees;
        private bool cameraOrbitPivotInitialized;
        private Vector3 cameraOrbitGroundPivot;
        private float cameraOrbitDistance;
        private RuntimeUnit lastClickedPlayerUnit;
        private float lastClickedPlayerUnitClickTime = float.NegativeInfinity;
        private bool isCameraFocusTransitioning;
        private Vector3 cameraFocusTransitionTarget;
        private int uiCancelFrame = -1;

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
            HandleCameraInput();
            if (activeTurnSide == TurnSide.Player)
            {
                HandlePlayerInput();
            }

            TickMovement(Time.deltaTime);
            TickEnemyAi(Time.deltaTime);
            UpdateMovementVisualizer();
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
            if (visualizerMaterial != null)
            {
                movementPathLine.material = visualizerMaterial;
            }

            movementPathLine.enabled = false;

            destinationMarkerObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            destinationMarkerObject.name = "DestinationMarker";
            destinationMarkerObject.transform.SetParent(transform);
            destinationMarkerObject.transform.localScale = new Vector3(0.6f, 0.01f, 0.6f);
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

            var activeCamera = selectionCamera != null ? selectionCamera : Camera.main;
            if (activeCamera == null)
            {
                movementPathLine.enabled = false;
                destinationMarkerObject.SetActive(false);
                return;
            }

            var ray = activeCamera.ScreenPointToRay(Input.mousePosition);
            if (!boardPlane.Raycast(ray, out var enter))
            {
                movementPathLine.enabled = false;
                destinationMarkerObject.SetActive(false);
                return;
            }

            var hoverPos = ray.GetPoint(enter);
            hoverPos.y = PawnYPosition;

            var unitPos = selectedUnit.Pawn.transform.position;
            var planarDelta = hoverPos - unitPos;
            planarDelta.y = 0f;
            var distToHover = planarDelta.magnitude;
            var remaining = selectedUnit.RemainingMovementThisTurn;

            var withinRange = distToHover <= remaining + PositionArrivalTolerance;
            Vector3 effectiveDest;
            if (withinRange)
            {
                effectiveDest = hoverPos;
            }
            else
            {
                effectiveDest = unitPos + planarDelta.normalized * remaining;
                effectiveDest.y = PawnYPosition;
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

            movementPathLine.enabled = true;
            movementPathLine.SetPosition(0, unitPos);
            movementPathLine.SetPosition(1, effectiveDest);
            movementPathLine.startColor = pathColor;
            movementPathLine.endColor = pathFadeColor;

            destinationMarkerObject.SetActive(true);
            var markerPos = effectiveDest;
            markerPos.y = 0.01f;
            destinationMarkerObject.transform.position = markerPos;
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

            var pawnScale = target.Pawn.transform.localScale;
            var scaledRadius = Mathf.Max(pawnScale.x, pawnScale.z) * TargetRingScaleFactor;
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

            if (mode != UnitActionMode.Move)
            {
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
            selectedUnit = FindFirstAlive(playerRuntimeUnits);
            StartPlayerTurn();
        }

        private void SpawnArmy(List<UnitTypeDefinition> units, Transform anchor, List<RuntimeUnit> destination, bool isPlayerControlled, Color color)
        {
            if (units == null)
            {
                return;
            }

            var origin = anchor == null ? Vector3.zero : anchor.position;
            for (var i = 0; i < units.Count; i++)
            {
                var unitDefinition = units[i];
                if (unitDefinition == null)
                {
                    continue;
                }

                unitDefinition.Stats.EnsureWeaponDefaults();
                var pawn = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                var pawnScale = unitDefinition.Stats.modelSize.GetPawnScale();
                pawn.name = $"{unitDefinition.DisplayName} ({(isPlayerControlled ? "Player" : "Enemy")})";
                pawn.transform.localScale = pawnScale;
                pawn.transform.SetPositionAndRotation(origin + new Vector3(i * spawnSpacing, pawnScale.y, 0f), Quaternion.identity);
                pawn.transform.SetParent(transform);
                var renderer = pawn.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = color;
                }

                var runtimeUnit = new RuntimeUnit(unitDefinition, isPlayerControlled, pawn);
                destination.Add(runtimeUnit);
                allRuntimeUnits.Add(runtimeUnit);
            }
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

            if (Input.GetKeyDown(KeyCode.Return))
            {
                EndPlayerTurn();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                SetCurrentMode(UnitActionMode.None);
                return;
            }

            if (Input.GetKeyDown(KeyCode.M) && selectedUnit != null && selectedUnit.IsAlive
                && selectedUnit.RemainingMovementThisTurn > MovementBudgetEpsilon
                && !selectedUnit.HasActedThisTurn)
            {
                SetCurrentMode(currentPlayerMode == UnitActionMode.Move ? UnitActionMode.None : UnitActionMode.Move);
            }

            if (Input.GetKeyDown(KeyCode.A) && selectedUnit != null && selectedUnit.IsAlive
                && !selectedUnit.HasActedThisTurn)
            {
                SetCurrentMode(currentPlayerMode == UnitActionMode.Attack ? UnitActionMode.None : UnitActionMode.Attack);
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
            for (var i = 0; i < Mathf.Min(9, playerRuntimeUnits.Count); i++)
            {
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)))
                {
                    SelectUnit(playerRuntimeUnits[i]);
                    return;
                }
            }

            if (!Input.GetMouseButtonDown(0))
            {
                return;
            }

            var activeCamera = selectionCamera != null ? selectionCamera : Camera.main;
            if (activeCamera == null)
            {
                return;
            }

            var ray = activeCamera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit))
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
            for (var i = 0; i < Mathf.Min(9, playerRuntimeUnits.Count); i++)
            {
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)))
                {
                    SelectUnit(playerRuntimeUnits[i]);
                    return;
                }
            }

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

            if (selectedUnit.HasActedThisTurn)
            {
                SetCurrentMode(UnitActionMode.None);
                return;
            }

            var activeCamera = selectionCamera != null ? selectionCamera : Camera.main;
            if (activeCamera == null)
            {
                return;
            }

            var ray = activeCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit))
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

            if (!boardPlane.Raycast(ray, out var enter))
            {
                return;
            }

            var destination = ray.GetPoint(enter);
            destination.y = PawnYPosition;
            IssueMoveOrder(selectedUnit, destination);
            SetCurrentMode(UnitActionMode.None);
        }

        private void HandleAttackModeInput()
        {
            for (var i = 0; i < Mathf.Min(9, playerRuntimeUnits.Count); i++)
            {
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)))
                {
                    SelectUnit(playerRuntimeUnits[i]);
                    return;
                }
            }

            if (TryCancelModeOnRightClick())
            {
                return;
            }

            if (!Input.GetMouseButtonDown(0))
            {
                return;
            }

            if (selectedUnit == null || !selectedUnit.IsAlive || selectedUnit.HasActedThisTurn)
            {
                return;
            }

            var attackWeapon = GetSelectedAttackWeapon(selectedUnit);

            var activeCamera = selectionCamera != null ? selectionCamera : Camera.main;
            if (activeCamera == null)
            {
                return;
            }

            var ray = activeCamera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit))
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
                var maxStepThisFrame = unit.Definition.Stats.speed * deltaTime;
                var allowedStep = Mathf.Min(maxStepThisFrame, unit.RemainingMovementThisTurn);
                if (allowedStep <= 0f)
                {
                    unit.MoveTarget = null;
                    continue;
                }

                var currentPosition = unit.Pawn.transform.position;
                var nextPosition = Vector3.MoveTowards(currentPosition, targetPosition, allowedStep);
                var movedDistance = Vector3.Distance(currentPosition, nextPosition);
                unit.Pawn.transform.position = nextPosition;
                unit.RemainingMovementThisTurn = Mathf.Max(0f, unit.RemainingMovementThisTurn - movedDistance);

                if (Vector3.Distance(nextPosition, targetPosition) <= PositionArrivalTolerance || unit.RemainingMovementThisTurn <= MovementBudgetEpsilon)
                {
                    unit.MoveTarget = null;
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
            var desiredRange = GetLongestWeaponRange(enemy);
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
            destination.y = PawnYPosition;
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
            var weapon = GetBestWeaponForDistance(unit, distance);
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

        private void IssueMoveOrder(RuntimeUnit unit, Vector3 destination)
        {
            if (unit == null || !unit.IsAlive || unit.Pawn == null || unit.HasActedThisTurn)
            {
                return;
            }

            var remaining = Mathf.Max(0f, unit.RemainingMovementThisTurn);
            if (remaining <= 0f)
            {
                unit.MoveTarget = null;
                return;
            }

            var current = unit.Pawn.transform.position;
            var planarDelta = destination - current;
            planarDelta.y = 0f;
            var distanceToDestination = planarDelta.magnitude;
            if (distanceToDestination <= PositionArrivalTolerance)
            {
                unit.MoveTarget = null;
                return;
            }

            var moveDistance = Mathf.Min(remaining, distanceToDestination);
            var clampedDestination = current + planarDelta.normalized * moveDistance;
            clampedDestination.y = PawnYPosition;
            unit.MoveTarget = clampedDestination;
        }

        private void StartPlayerTurn()
        {
            activeTurnSide = TurnSide.Player;
            ResetMovementForTurn(playerRuntimeUnits);
            selectedUnit = FindFirstAlive(playerRuntimeUnits);
            SetCurrentMode(UnitActionMode.None);
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
                unit.MoveTarget = null;
            }
        }

        private void ResolveAttack(RuntimeUnit attacker, RuntimeUnit defender, WeaponProfile weapon)
        {
            var isMeleeAttack = weapon.attackType == WeaponAttackType.Melee;
            var attackValue = isMeleeAttack ? attacker.Definition.Stats.meleeAttack : attacker.Definition.Stats.rangedAttack;
            var attackRoll = Roll2d6() + attackValue;
            if (attackRoll < defender.Definition.Stats.defense)
            {
                return;
            }

            var damageRoll = Roll2d6() + weapon.Power;
            var damage = Mathf.Max(0, damageRoll - defender.Definition.Stats.armor);
            defender.Health = Mathf.Max(0, defender.Health - damage);
            if (!defender.IsAlive)
            {
                defender.Pawn.SetActive(false);
                if (ReferenceEquals(defender, selectedUnit))
                {
                    selectedUnit = FindFirstAlive(playerRuntimeUnits);
                }
            }
        }

        private static int Roll2d6()
        {
            return Random.Range(1, 7) + Random.Range(1, 7);
        }

        private void SelectUnit(RuntimeUnit unit)
        {
            selectedUnit = unit != null && unit.IsAlive ? unit : null;
            selectedAttackWeaponIndex = 0;
            SetCurrentMode(UnitActionMode.None);
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

            var activeCamera = selectionCamera != null ? selectionCamera : Camera.main;
            if (activeCamera == null)
            {
                return;
            }

            if (!cameraPitchInitialized)
            {
                InitializeCameraPitch(activeCamera);
            }

            if (!cameraOrbitPivotInitialized)
            {
                InitializeCameraOrbitPivot(activeCamera);
            }

            var focusPoint = unit.Pawn.transform.position;
            focusPoint.y = GroundYPosition;
            if (cameraOrbitDistance < CameraOrbitMinimumDistance)
            {
                cameraOrbitDistance = Mathf.Max(CameraOrbitMinimumDistance, Vector3.Distance(activeCamera.transform.position, focusPoint));
            }

            cameraOrbitPivotInitialized = true;
            cameraFocusTransitionTarget = focusPoint;
            isCameraFocusTransitioning = true;
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
                return WeaponProfile.CreateDefault().Range;
            }

            var range = unit.Weapons[0].Range;
            for (var i = 1; i < unit.Weapons.Length; i++)
            {
                range = Mathf.Max(range, unit.Weapons[i].Range);
            }

            return range;
        }

        private static WeaponProfile GetBestWeaponForDistance(RuntimeUnit unit, float distance)
        {
            if (unit.Weapons == null || unit.Weapons.Length == 0)
            {
                return null;
            }

            WeaponProfile best = null;
            for (var i = 0; i < unit.Weapons.Length; i++)
            {
                var weapon = unit.Weapons[i];
                if (distance > weapon.Range)
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
            return distance <= weapon.Range + PositionArrivalTolerance;
        }

        private static float GetPlanarDistance(Vector3 from, Vector3 to)
        {
            var delta = to - from;
            delta.y = 0f;
            return delta.magnitude;
        }

        private void UpdateHoveredEnemy()
        {
            hoveredEnemyUnit = null;
            var activeCamera = selectionCamera != null ? selectionCamera : Camera.main;
            if (activeCamera == null)
            {
                return;
            }

            var ray = activeCamera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit))
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
            DrawCameraControlsPanel();

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
            GUILayout.Label($"Model Size: {selectedUnit.Definition.Stats.modelSize.DisplayName()}");
            var selectedWeapon = GetSelectedAttackWeapon(selectedUnit);
            GUILayout.Label($"Weapon: {selectedWeapon.DisplayName}");
            GUILayout.Label($"Type: {selectedWeapon.attackType}  |  Range: {selectedWeapon.Range:0.0}\"");
            GUILayout.Label($"Weapon Power: {selectedWeapon.Power}");

            GUILayout.EndArea();
            DrawActionBar();
            DrawHoveredEnemyHealth();
        }

        private void DrawCameraControlsPanel()
        {
            var areaX = (Screen.width - CameraControlsPanelWidth) * 0.5f;
            var areaY = CameraControlsPanelTopMargin;
            GUILayout.BeginArea(new Rect(areaX, areaY, CameraControlsPanelWidth, CameraControlsPanelHeight), "Camera Controls", GUI.skin.window);
            GUILayout.Label("WASD/Arrows: Pan | MMB Drag: Rotate | Shift+MMB Drag: Pan");
            GUILayout.EndArea();
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
                && !selectedUnit.HasActedThisTurn;
            GUI.enabled = canMove;
            var moveLabel = currentPlayerMode == UnitActionMode.Move ? "[ Move ]" : "Move";
            if (GUILayout.Button(moveLabel, GUILayout.Height(30f)))
            {
                if (!WasUiCancelTriggeredThisFrame())
                {
                    SetCurrentMode(currentPlayerMode == UnitActionMode.Move ? UnitActionMode.None : UnitActionMode.Move);
                }
            }

            var canAttack = !selectedUnit.HasActedThisTurn;
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

            GUILayout.EndArea();
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

        private void HandleCameraInput()
        {
            var activeCamera = selectionCamera != null ? selectionCamera : Camera.main;
            if (activeCamera == null)
            {
                return;
            }

            if (!cameraPitchInitialized)
            {
                InitializeCameraPitch(activeCamera);
                if (!cameraPitchInitialized)
                {
                    return;
                }
            }
            HandleKeyboardCameraPan(activeCamera);

            if (Input.GetMouseButtonDown(MiddleMouseButton))
            {
                isCameraDragging = !IsMouseOverGameplayUi();
                lastCameraDragMousePosition = Input.mousePosition;
            }

            if (Input.GetMouseButtonUp(MiddleMouseButton))
            {
                isCameraDragging = false;
            }

            TickCameraFocusTransition(activeCamera);

            if (!isCameraDragging || !Input.GetMouseButton(MiddleMouseButton))
            {
                return;
            }

            var mousePosition = Input.mousePosition;
            var delta = mousePosition - lastCameraDragMousePosition;
            lastCameraDragMousePosition = mousePosition;

            if (delta.sqrMagnitude < MinimumVectorSqrMagnitude)
            {
                return;
            }

            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                DragPanCamera(activeCamera, delta);
            }
            else
            {
                RotateCamera(activeCamera, delta);
            }
        }

        private void HandleKeyboardCameraPan(Camera activeCamera)
        {
            var horizontal = Input.GetAxisRaw("Horizontal");
            var vertical = Input.GetAxisRaw("Vertical");
            if (Mathf.Abs(horizontal) <= InputAxisDeadzone && Mathf.Abs(vertical) <= InputAxisDeadzone)
            {
                return;
            }

            isCameraFocusTransitioning = false;
            var forward = GetPlanarForward(activeCamera.transform.forward);
            var right = GetPlanarRight(forward);
            var delta = (right * horizontal + forward * vertical) * (cameraKeyboardPanSpeed * Time.deltaTime);
            TranslateCameraOrbit(activeCamera, delta);
        }

        private void DragPanCamera(Camera activeCamera, Vector3 delta)
        {
            isCameraFocusTransitioning = false;
            var forward = GetPlanarForward(activeCamera.transform.forward);
            var right = GetPlanarRight(forward);
            var pan = (-right * delta.x - forward * delta.y) * cameraDragPanSensitivity;
            TranslateCameraOrbit(activeCamera, pan);
        }

        private void RotateCamera(Camera activeCamera, Vector3 delta)
        {
            if (!cameraOrbitPivotInitialized)
            {
                InitializeCameraOrbitPivot(activeCamera);
                if (!cameraOrbitPivotInitialized)
                {
                    return;
                }
            }

            isCameraFocusTransitioning = false;
            var yaw = delta.x * cameraRotationSensitivity;
            cameraPitchDegrees = Mathf.Clamp(cameraPitchDegrees - (delta.y * cameraRotationSensitivity), cameraMinPitch, cameraMaxPitch);
            var euler = activeCamera.transform.rotation.eulerAngles;
            activeCamera.transform.rotation = Quaternion.Euler(cameraPitchDegrees, euler.y + yaw, 0f);
            var cameraForward = activeCamera.transform.forward;
            activeCamera.transform.position = cameraOrbitGroundPivot - (cameraForward * cameraOrbitDistance);
        }

        private void TickCameraFocusTransition(Camera activeCamera)
        {
            if (!isCameraFocusTransitioning)
            {
                return;
            }

            cameraOrbitGroundPivot = Vector3.MoveTowards(
                cameraOrbitGroundPivot,
                cameraFocusTransitionTarget,
                CameraFocusTransitionSpeed * Time.deltaTime);

            var cameraForward = activeCamera.transform.forward;
            activeCamera.transform.position = cameraOrbitGroundPivot - (cameraForward * cameraOrbitDistance);

            if (Vector3.Distance(cameraOrbitGroundPivot, cameraFocusTransitionTarget) < 0.001f)
            {
                isCameraFocusTransitioning = false;
            }
        }

        private void InitializeCameraPitch()
        {
            var activeCamera = selectionCamera != null ? selectionCamera : Camera.main;
            InitializeCameraPitch(activeCamera);
        }

        private void InitializeCameraPitch(Camera activeCamera)
        {
            if (cameraPitchInitialized || activeCamera == null)
            {
                return;
            }

            cameraPitchDegrees = Mathf.Clamp(NormalizeSignedAngle(activeCamera.transform.eulerAngles.x), cameraMinPitch, cameraMaxPitch);
            cameraPitchInitialized = true;
            InitializeCameraOrbitPivot(activeCamera);
        }

        private void InitializeCameraOrbitPivot(Camera activeCamera)
        {
            if (cameraOrbitPivotInitialized || activeCamera == null)
            {
                return;
            }

            if (!TryGetGroundPointFromScreenCenter(activeCamera, out cameraOrbitGroundPivot))
            {
                var planarForward = GetPlanarForward(activeCamera.transform.forward);
                cameraOrbitGroundPivot = activeCamera.transform.position + (planarForward * CameraOrbitFallbackForwardDistance);
                cameraOrbitGroundPivot.y = 0f;
            }

            cameraOrbitDistance = Vector3.Distance(activeCamera.transform.position, cameraOrbitGroundPivot);
            if (cameraOrbitDistance < CameraOrbitMinimumDistance)
            {
                cameraOrbitDistance = CameraOrbitMinimumDistance;
            }

            var cameraForward = activeCamera.transform.forward;
            activeCamera.transform.position = cameraOrbitGroundPivot - (cameraForward * cameraOrbitDistance);
            cameraOrbitPivotInitialized = true;
        }

        private void TranslateCameraOrbit(Camera activeCamera, Vector3 planarDelta)
        {
            if (!cameraOrbitPivotInitialized)
            {
                InitializeCameraOrbitPivot(activeCamera);
                if (!cameraOrbitPivotInitialized)
                {
                    return;
                }
            }

            cameraOrbitGroundPivot += planarDelta;
            var cameraForward = activeCamera.transform.forward;
            activeCamera.transform.position = cameraOrbitGroundPivot - (cameraForward * cameraOrbitDistance);
        }

        private bool TryGetGroundPointFromScreenCenter(Camera activeCamera, out Vector3 groundPoint)
        {
            var screenCenter = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
            var centerRay = activeCamera.ScreenPointToRay(screenCenter);
            if (boardPlane.Raycast(centerRay, out var enter))
            {
                groundPoint = centerRay.GetPoint(enter);
                groundPoint.y = 0f;
                return true;
            }

            groundPoint = Vector3.zero;
            return false;
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

            if (currentPlayerMode != UnitActionMode.None)
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

            if (hoveredEnemyUnit != null && hoveredEnemyUnit.IsAlive)
            {
                var mousePosition = Input.mousePosition;
                var x = Mathf.Clamp(mousePosition.x + HoverPanelMouseOffset, HoverPanelScreenPadding, Screen.width - HoverPanelWidth - HoverPanelScreenPadding);
                var y = Mathf.Clamp(Screen.height - mousePosition.y + HoverPanelMouseOffset, HoverPanelScreenPadding, Screen.height - HoverPanelHeight - HoverPanelScreenPadding);
                if (new Rect(x, y, HoverPanelWidth, HoverPanelHeight).Contains(mouseGuiPosition))
                {
                    return true;
                }
            }

            return false;
        }

        private static Vector2 GetMouseGuiPosition()
        {
            var mousePosition = Input.mousePosition;
            return new Vector2(mousePosition.x, Screen.height - mousePosition.y);
        }

        private static float NormalizeSignedAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f)
            {
                angle -= 360f;
            }

            return angle;
        }

        private static Vector3 GetPlanarForward(Vector3 forward)
        {
            var planarForward = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (planarForward.sqrMagnitude < MinimumVectorSqrMagnitude)
            {
                return Vector3.forward;
            }

            return planarForward.normalized;
        }

        private static Vector3 GetPlanarRight(Vector3 planarForward)
        {
            return Vector3.Cross(Vector3.up, planarForward).normalized;
        }

        private static bool IsAnyMouseButtonDown()
        {
            return Input.GetMouseButtonDown(LeftMouseButton)
                || Input.GetMouseButtonDown(RightMouseButton)
                || Input.GetMouseButtonDown(MiddleMouseButton);
        }

        private void DrawHoveredEnemyHealth()
        {
            if (hoveredEnemyUnit == null || !hoveredEnemyUnit.IsAlive)
            {
                return;
            }

            var mousePosition = Input.mousePosition;
            var x = Mathf.Clamp(mousePosition.x + HoverPanelMouseOffset, HoverPanelScreenPadding, Screen.width - HoverPanelWidth - HoverPanelScreenPadding);
            var y = Mathf.Clamp(Screen.height - mousePosition.y + HoverPanelMouseOffset, HoverPanelScreenPadding, Screen.height - HoverPanelHeight - HoverPanelScreenPadding);

            GUILayout.BeginArea(new Rect(x, y, HoverPanelWidth, HoverPanelHeight), "Target", GUI.skin.window);
            GUILayout.Label(hoveredEnemyUnit.Definition.DisplayName);
            GUILayout.Label($"HP: {hoveredEnemyUnit.Health}/{hoveredEnemyUnit.Definition.Stats.health}");
            GUILayout.Label(BuildHealthBoxes(hoveredEnemyUnit.Health, hoveredEnemyUnit.Definition.Stats.health));
            GUILayout.EndArea();
        }

        private sealed class RuntimeUnit
        {
            public RuntimeUnit(UnitTypeDefinition definition, bool isPlayerControlled, GameObject pawn)
            {
                Definition = definition;
                IsPlayerControlled = isPlayerControlled;
                Pawn = pawn;
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
            public WeaponProfile[] Weapons { get; }
            public int Health { get; set; }
            public float RemainingMovementThisTurn { get; set; }
            public bool HasActedThisTurn { get; set; }
            public Vector3? MoveTarget { get; set; }
            public bool IsAlive => Health > 0;
        }
    }
}
