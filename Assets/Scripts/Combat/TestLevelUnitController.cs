using System.Collections.Generic;
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

        private readonly List<RuntimeUnit> playerRuntimeUnits = new();
        private readonly List<RuntimeUnit> enemyRuntimeUnits = new();
        private readonly List<RuntimeUnit> allRuntimeUnits = new();
        private readonly Plane boardPlane = new(Vector3.up, Vector3.zero);
        private RuntimeUnit selectedUnit;
        private TurnSide activeTurnSide = TurnSide.Player;
        private float aiThinkTimer;
        private RuntimeUnit activeEnemyUnit;
        private int enemyActivationIndex;
        private bool enemyIssuedMoveForActiveUnit;
        private bool enemyResolvedActionForActiveUnit;

        private UnitActionMode currentPlayerMode = UnitActionMode.None;
        private LineRenderer movementPathLine;
        private LineRenderer attackRangeRing;
        private GameObject destinationMarkerObject;
        private Material visualizerMaterial;

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
            if (activeTurnSide == TurnSide.Player)
            {
                HandlePlayerInput();
            }

            TickMovement(Time.deltaTime);
            TickEnemyAi(Time.deltaTime);
            UpdateMovementVisualizer();
        }

        private void BuildVisualizers()
        {
            if (movementPathLine != null)
            {
                return;
            }

            visualizerMaterial = new Material(Shader.Find("Sprites/Default"));

            var lineObj = new GameObject("MovementPathLine");
            lineObj.transform.SetParent(transform);
            movementPathLine = lineObj.AddComponent<LineRenderer>();
            movementPathLine.widthMultiplier = VisualizerLineWidth;
            movementPathLine.positionCount = 2;
            movementPathLine.useWorldSpace = true;
            movementPathLine.material = visualizerMaterial;
            movementPathLine.enabled = false;

            var ringObj = new GameObject("AttackRangeRing");
            ringObj.transform.SetParent(transform);
            attackRangeRing = ringObj.AddComponent<LineRenderer>();
            attackRangeRing.widthMultiplier = VisualizerLineWidth;
            attackRangeRing.positionCount = AttackRingSegments + 1;
            attackRangeRing.useWorldSpace = true;
            attackRangeRing.loop = false;
            attackRangeRing.material = visualizerMaterial;
            attackRangeRing.enabled = false;

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
            if (markerRenderer != null)
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
            hoverPos.y = 1f;

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
                effectiveDest.y = 1f;
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
            if (attackRangeRing == null || selectedUnit == null || !selectedUnit.IsAlive || currentPlayerMode != UnitActionMode.Attack)
            {
                if (attackRangeRing != null)
                {
                    attackRangeRing.enabled = false;
                }

                return;
            }

            attackRangeRing.enabled = true;
            var center = selectedUnit.Pawn.transform.position;
            var radius = selectedUnit.Weapon.Range;
            var ringColor = new Color(0.95f, 0.55f, 0.1f, 0.75f);
            attackRangeRing.startColor = ringColor;
            attackRangeRing.endColor = ringColor;
            for (var i = 0; i <= AttackRingSegments; i++)
            {
                var angle = (float)i / AttackRingSegments * Mathf.PI * 2f;
                attackRangeRing.SetPosition(i, new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    0.05f,
                    center.z + Mathf.Sin(angle) * radius));
            }
        }

        private void SetCurrentMode(UnitActionMode mode)
        {
            currentPlayerMode = mode;

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
            else if (attackRangeRing != null)
            {
                attackRangeRing.enabled = false;
            }
        }

        private void HideAllVisualizers()
        {
            if (movementPathLine != null)
            {
                movementPathLine.enabled = false;
            }

            if (attackRangeRing != null)
            {
                attackRangeRing.enabled = false;
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
                pawn.name = $"{unitDefinition.DisplayName} ({(isPlayerControlled ? "Player" : "Enemy")})";
                pawn.transform.SetPositionAndRotation(origin + new Vector3(i * spawnSpacing, 1f, 0f), Quaternion.identity);
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
                && selectedUnit.RemainingMovementThisTurn > MovementBudgetEpsilon)
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
                    SelectUnit(playerRuntimeUnits[i]);
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

            if (!Input.GetMouseButtonDown(1))
            {
                return;
            }

            if (selectedUnit == null || !selectedUnit.IsAlive)
            {
                return;
            }

            var activeCamera = selectionCamera != null ? selectionCamera : Camera.main;
            if (activeCamera == null)
            {
                return;
            }

            var ray = activeCamera.ScreenPointToRay(Input.mousePosition);
            if (!boardPlane.Raycast(ray, out var enter))
            {
                return;
            }

            var destination = ray.GetPoint(enter);
            destination.y = 1f;
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

            if (!Input.GetMouseButtonDown(0))
            {
                return;
            }

            if (selectedUnit == null || !selectedUnit.IsAlive || selectedUnit.HasActedThisTurn)
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
                    SelectUnit(playerRuntimeUnits[i]);
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

                var dist = Vector3.Distance(selectedUnit.Pawn.transform.position, enemy.Pawn.transform.position);
                if (dist <= selectedUnit.Weapon.Range)
                {
                    ResolveAttack(selectedUnit, enemy);
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

            if (!enemyIssuedMoveForActiveUnit)
            {
                enemyIssuedMoveForActiveUnit = true;
                ResolveEnemyMovement(activeEnemyUnit);
                return;
            }

            if (activeEnemyUnit.MoveTarget.HasValue)
            {
                return;
            }

            if (!enemyResolvedActionForActiveUnit)
            {
                enemyResolvedActionForActiveUnit = true;
                ResolveUnitAction(activeEnemyUnit, playerRuntimeUnits);
                return;
            }

            CompleteEnemyActivation();
        }

        private void ResolveEnemyMovement(RuntimeUnit enemy)
        {
            var target = FindNearestAliveUnit(enemy, playerRuntimeUnits);
            if (target == null)
            {
                enemy.MoveTarget = null;
                return;
            }

            var targetPosition = target.Pawn.transform.position;
            var distance = Vector3.Distance(enemy.Pawn.transform.position, targetPosition);
            if (distance <= enemy.Weapon.Range * AiInRangeTolerance)
            {
                enemy.MoveTarget = null;
                return;
            }

            var direction = (targetPosition - enemy.Pawn.transform.position).normalized;
            var stopDistance = Mathf.Max(AiMinimumStopDistance, enemy.Weapon.Range * AiDesiredStopFactor);
            var destination = targetPosition - direction * stopDistance;
            destination.y = 1f;
            IssueMoveOrder(enemy, destination);
        }

        private bool ResolveUnitAction(RuntimeUnit unit, List<RuntimeUnit> targets)
        {
            var target = FindNearestAliveUnit(unit, targets);
            if (target == null)
            {
                return false;
            }

            var distance = Vector3.Distance(unit.Pawn.transform.position, target.Pawn.transform.position);
            if (distance > unit.Weapon.Range)
            {
                return false;
            }

            ResolveAttack(unit, target);
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
                enemyIssuedMoveForActiveUnit = false;
                enemyResolvedActionForActiveUnit = false;
                return true;
            }

            return false;
        }

        private void CompleteEnemyActivation()
        {
            activeEnemyUnit = null;
            enemyIssuedMoveForActiveUnit = false;
            enemyResolvedActionForActiveUnit = false;
        }

        private void IssueMoveOrder(RuntimeUnit unit, Vector3 destination)
        {
            if (unit == null || !unit.IsAlive || unit.Pawn == null)
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
            clampedDestination.y = 1f;
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

        private void ResolveAttack(RuntimeUnit attacker, RuntimeUnit defender)
        {
            var isMeleeAttack = attacker.Weapon.attackType == WeaponAttackType.Melee;
            var attackValue = isMeleeAttack ? attacker.Definition.Stats.meleeAttack : attacker.Definition.Stats.rangedAttack;
            var attackRoll = Roll2d6() + attackValue;
            if (attackRoll < defender.Definition.Stats.defense)
            {
                return;
            }

            var damageRoll = Roll2d6() + attacker.Weapon.Power;
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
            SetCurrentMode(UnitActionMode.None);
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

                var distance = Vector3.Distance(source.Pawn.transform.position, candidate.Pawn.transform.position);
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
            GUILayout.BeginArea(new Rect(12f, 12f, 320f, 300f), "Player-Controlled Units", GUI.skin.window);
            GUILayout.Label($"Active Turn: {activeTurnSide}");
            if (activeTurnSide == TurnSide.Player && GUILayout.Button("End Turn"))
            {
                EndPlayerTurn();
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
                        SelectUnit(unit);
                    }
                }
            }

            GUILayout.Space(8f);
            GUILayout.Label("Left Click / 1-9: Select unit");
            GUILayout.Label("M: Toggle Move  |  A: Toggle Attack");
            GUILayout.Label("Enter / End Turn: End player turn");
            GUILayout.Label("Esc: Cancel current mode");
            GUILayout.EndArea();

            if (selectedUnit == null)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(Screen.width - 292f, 12f, 280f, 310f), "Selected Unit", GUI.skin.window);
            GUILayout.Label(selectedUnit.Definition.DisplayName);
            GUILayout.Label($"Role: {selectedUnit.Definition.Role}");
            GUILayout.Label($"HP: {selectedUnit.Health}/{selectedUnit.Definition.Stats.health}");
            GUILayout.Label($"Speed: {selectedUnit.Definition.Stats.speed:0.0}  |  Move left: {selectedUnit.RemainingMovementThisTurn:0.0}\"");
            GUILayout.Label($"Weapon: {selectedUnit.Weapon.DisplayName}");
            GUILayout.Label($"Type: {selectedUnit.Weapon.attackType}  |  Range: {selectedUnit.Weapon.Range:0.0}\"");
            GUILayout.Label($"Power: {selectedUnit.Weapon.Power}");

            if (activeTurnSide == TurnSide.Player)
            {
                GUILayout.Space(6f);
                GUILayout.BeginHorizontal();

                var canMove = selectedUnit.RemainingMovementThisTurn > MovementBudgetEpsilon && !selectedUnit.MoveTarget.HasValue;
                GUI.enabled = canMove;
                var moveLabel = currentPlayerMode == UnitActionMode.Move ? "[ Move ]" : "Move";
                if (GUILayout.Button(moveLabel))
                {
                    SetCurrentMode(currentPlayerMode == UnitActionMode.Move ? UnitActionMode.None : UnitActionMode.Move);
                }

                var canAttack = !selectedUnit.HasActedThisTurn;
                GUI.enabled = canAttack;
                var attackLabel = currentPlayerMode == UnitActionMode.Attack ? "[ Attack ]" : "Attack";
                if (GUILayout.Button(attackLabel))
                {
                    SetCurrentMode(currentPlayerMode == UnitActionMode.Attack ? UnitActionMode.None : UnitActionMode.Attack);
                }

                GUI.enabled = true;
                GUILayout.EndHorizontal();

                GUILayout.Space(4f);
                switch (currentPlayerMode)
                {
                    case UnitActionMode.Move:
                        GUILayout.Label("Right-click to set destination");
                        break;
                    case UnitActionMode.Attack:
                        GUILayout.Label("Click an enemy within range to attack");
                        break;
                }
            }

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
                Weapon = definition.Stats.GetPrimaryWeapon();
            }

            public UnitTypeDefinition Definition { get; }
            public bool IsPlayerControlled { get; }
            public GameObject Pawn { get; }
            public WeaponProfile Weapon { get; }
            public int Health { get; set; }
            public float RemainingMovementThisTurn { get; set; }
            public bool HasActedThisTurn { get; set; }
            public Vector3? MoveTarget { get; set; }
            public bool IsAlive => Health > 0;
        }
    }
}
