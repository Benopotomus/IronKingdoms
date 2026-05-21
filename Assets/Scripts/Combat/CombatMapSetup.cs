using Pathfinding;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Loads a dedicated combat map scene additively, applies map-authored spawn points,
    /// and initialises an A* RecastGraph that covers the play area.
    /// </summary>
    public class CombatMapSetup : MonoBehaviour
    {
        // ── Navmesh configuration ────────────────────────────────────────────
        private const float NavmeshBoundsSize = 26f;
        [SerializeField] private string combatMapSceneName = "CombatMapScene";
        [SerializeField] private TestLevelUnitController unitController;

        private void Awake()
        {
            LoadMapScene();
            ApplySpawnAnchors();
            SetupPathfinding();
        }

        private void LoadMapScene()
        {
            if (string.IsNullOrWhiteSpace(combatMapSceneName))
            {
                Debug.LogWarning("Combat map scene name is not configured.", this);
                return;
            }

            var mapScene = SceneManager.GetSceneByName(combatMapSceneName);
            if (!mapScene.IsValid() || !mapScene.isLoaded)
            {
                SceneManager.LoadScene(combatMapSceneName, LoadSceneMode.Additive);
            }
        }

        private void ApplySpawnAnchors()
        {
            var targetController = unitController != null ? unitController : GetComponent<TestLevelUnitController>();
            if (targetController == null)
            {
                return;
            }

            var spawnPoints = FindObjectsByType<CombatSpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Transform playerSpawn = null;
            Transform enemySpawn = null;

            for (var i = 0; i < spawnPoints.Length; i++)
            {
                var spawnPoint = spawnPoints[i];
                if (spawnPoint.Side == CombatSpawnSide.Player && playerSpawn == null)
                {
                    playerSpawn = spawnPoint.transform;
                }
                else if (spawnPoint.Side == CombatSpawnSide.Enemy && enemySpawn == null)
                {
                    enemySpawn = spawnPoint.transform;
                }
            }

            if (playerSpawn != null && enemySpawn != null)
            {
                targetController.SetSpawnAnchors(playerSpawn, enemySpawn);
            }
            else
            {
                Debug.LogWarning("Combat map scene did not provide both player and enemy spawn points.", this);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  A* Pathfinding setup
        // ─────────────────────────────────────────────────────────────────────

        private void SetupPathfinding()
        {
            // Create AstarPath component if one isn't already present in the scene.
            var astar = AstarPath.active;
            if (astar == null)
            {
                var astarGo = new GameObject("AstarPath");
                astar = astarGo.AddComponent<AstarPath>();
            }

            // Clear any existing graphs and add a fresh RecastGraph navmesh.
            astar.data.ClearGraphs();
            var recast = astar.data.AddGraph<RecastGraph>();
            recast.forcedBoundsCenter = Vector3.up * 1.5f;
            recast.forcedBoundsSize = new Vector3(NavmeshBoundsSize, 6f, NavmeshBoundsSize);
            recast.cellSize = 0.2f;
            recast.walkableHeight = 1.5f;
            recast.walkableClimb = 0.55f;
            recast.characterRadius = 0.45f;
            recast.maxSlope = 50f;

            // Scan the graph now that the geometry is in place.
            astar.Scan();
        }
    }
}
