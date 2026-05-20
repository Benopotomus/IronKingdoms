using System.Collections.Generic;
using UnityEngine;

namespace IronKingdoms.Combat
{
    public class TestLevelUnitController : MonoBehaviour
    {
        private const float AiInRangeTolerance = 0.95f;
        private const float AiDesiredStopFactor = 0.85f;
        private const float AiMinimumStopDistance = 0.2f;

        [SerializeField] private List<UnitTypeDefinition> playerUnits = new();
        [SerializeField] private List<UnitTypeDefinition> enemyUnits = new();
        [SerializeField] private Transform playerSpawnAnchor;
        [SerializeField] private Transform enemySpawnAnchor;
        [SerializeField, Min(0.5f)] private float spawnSpacing = 2f;
        [SerializeField, Min(0.1f)] private float aiThinkInterval = 0.5f;
        [SerializeField, Min(0.1f)] private float attackCooldownSeconds = 1.2f;
        [SerializeField] private Camera selectionCamera;
        [SerializeField] private bool autoSpawnOnStart = true;

        private readonly List<RuntimeUnit> playerRuntimeUnits = new();
        private readonly List<RuntimeUnit> enemyRuntimeUnits = new();
        private readonly List<RuntimeUnit> allRuntimeUnits = new();
        private readonly Plane boardPlane = new(Vector3.up, Vector3.zero);
        private RuntimeUnit selectedUnit;
        private float aiThinkTimer;

        private void Start()
        {
            if (autoSpawnOnStart)
            {
                SpawnUnits();
            }
        }

        private void Update()
        {
            HandlePlayerSelectionInput();
            HandlePlayerMoveInput();
            TickMovement(Time.deltaTime);
            TickEnemyAi(Time.deltaTime);
            TickCombat(Time.deltaTime);
        }

        [ContextMenu("Spawn Units")]
        public void SpawnUnits()
        {
            ClearSpawnedUnits();
            SpawnArmy(playerUnits, playerSpawnAnchor, playerRuntimeUnits, true, new Color(0.2f, 0.5f, 1f));
            SpawnArmy(enemyUnits, enemySpawnAnchor, enemyRuntimeUnits, false, new Color(1f, 0.3f, 0.3f));
            aiThinkTimer = aiThinkInterval;
            selectedUnit = FindFirstAlive(playerRuntimeUnits);
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

        private void HandlePlayerSelectionInput()
        {
            for (var i = 0; i < Mathf.Min(9, playerRuntimeUnits.Count); i++)
            {
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)))
                {
                    SelectUnit(playerRuntimeUnits[i]);
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

        private void HandlePlayerMoveInput()
        {
            if (selectedUnit == null || !selectedUnit.IsAlive || !Input.GetMouseButtonDown(1))
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
            selectedUnit.MoveTarget = destination;
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
                var nextPosition = Vector3.MoveTowards(unit.Pawn.transform.position, targetPosition, unit.Definition.Stats.speed * deltaTime);
                unit.Pawn.transform.position = nextPosition;

                if (Vector3.Distance(nextPosition, targetPosition) <= 0.05f)
                {
                    unit.MoveTarget = null;
                }
            }
        }

        private void TickEnemyAi(float deltaTime)
        {
            aiThinkTimer -= deltaTime;
            if (aiThinkTimer > 0f)
            {
                return;
            }

            aiThinkTimer = aiThinkInterval;
            for (var i = 0; i < enemyRuntimeUnits.Count; i++)
            {
                var enemy = enemyRuntimeUnits[i];
                if (!enemy.IsAlive)
                {
                    continue;
                }

                var target = FindNearestAliveUnit(enemy, playerRuntimeUnits);
                if (target == null)
                {
                    enemy.MoveTarget = null;
                    continue;
                }

                var targetPosition = target.Pawn.transform.position;
                var distance = Vector3.Distance(enemy.Pawn.transform.position, targetPosition);
                if (distance <= enemy.Weapon.Range * AiInRangeTolerance)
                {
                    enemy.MoveTarget = null;
                }
                else
                {
                    var direction = (targetPosition - enemy.Pawn.transform.position).normalized;
                    var stopDistance = Mathf.Max(AiMinimumStopDistance, enemy.Weapon.Range * AiDesiredStopFactor);
                    enemy.MoveTarget = targetPosition - direction * stopDistance;
                }
            }
        }

        private void TickCombat(float deltaTime)
        {
            for (var i = 0; i < allRuntimeUnits.Count; i++)
            {
                var unit = allRuntimeUnits[i];
                if (!unit.IsAlive)
                {
                    continue;
                }

                unit.AttackCooldownRemaining = Mathf.Max(0f, unit.AttackCooldownRemaining - deltaTime);
                if (unit.AttackCooldownRemaining > 0f)
                {
                    continue;
                }

                var targets = unit.IsPlayerControlled ? enemyRuntimeUnits : playerRuntimeUnits;
                var target = FindNearestAliveUnit(unit, targets);
                if (target == null)
                {
                    continue;
                }

                var distance = Vector3.Distance(unit.Pawn.transform.position, target.Pawn.transform.position);
                if (distance > unit.Weapon.Range)
                {
                    continue;
                }

                ResolveAttack(unit, target);
                unit.AttackCooldownRemaining = attackCooldownSeconds;
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
            GUILayout.Label("Left Click / 1-9: Select");
            GUILayout.Label("Right Click: Move selected");
            GUILayout.EndArea();

            if (selectedUnit == null)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(Screen.width - 292f, 12f, 280f, 250f), "Selected Unit", GUI.skin.window);
            GUILayout.Label(selectedUnit.Definition.DisplayName);
            GUILayout.Label($"Role: {selectedUnit.Definition.Role}");
            GUILayout.Label($"HP: {selectedUnit.Health}/{selectedUnit.Definition.Stats.health}");
            GUILayout.Label($"Speed: {selectedUnit.Definition.Stats.speed:0.0}");
            GUILayout.Label($"Weapon: {selectedUnit.Weapon.DisplayName}");
            GUILayout.Label($"Type: {selectedUnit.Weapon.attackType}");
            GUILayout.Label($"Range: {selectedUnit.Weapon.Range:0.0}\"");
            GUILayout.Label($"Power: {selectedUnit.Weapon.Power}");
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
            public float AttackCooldownRemaining { get; set; }
            public Vector3? MoveTarget { get; set; }
            public bool IsAlive => Health > 0;
        }
    }
}
