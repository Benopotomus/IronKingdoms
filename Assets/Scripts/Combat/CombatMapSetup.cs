using System.Collections;
using Pathfinding;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Loads a dedicated combat map scene additively, applies map-authored spawn points,
    /// and scans an A* grid graph over the map geometry.
    /// </summary>
    public class CombatMapSetup : MonoBehaviour
    {
        private const int NavGridWidth = 44;
        private const int NavGridDepth = 36;
        private const float NavNodeSize = 0.5f;
        private const float NavMaxSlope = 45f;
        private const float NavBoundsPadding = 1f;

        [SerializeField] private string combatMapSceneName = "CombatMapScene";
        [SerializeField] private TestLevelUnitController unitController;

        private void Awake()
        {
            var targetController = unitController != null ? unitController : GetComponent<TestLevelUnitController>();
            if (targetController != null)
            {
                targetController.DisableAutoSpawn();
            }

            StartCoroutine(LoadAndSetup(targetController));
        }

        /// <summary>
        /// Async initialization sequence:
        /// 1. Waits for the combat map scene to finish loading additively.
        /// 2. Resolves player and enemy <see cref="CombatSpawnPoint"/> markers from the loaded scene.
        /// 3. Scans the A* navmesh over the map geometry.
        /// 4. Calls <see cref="TestLevelUnitController.SpawnUnits"/> so units are placed at the
        ///    correct spawn anchors instead of falling back to origin.
        /// </summary>
        private IEnumerator LoadAndSetup(TestLevelUnitController targetController)
        {
            var mapScene = SceneManager.GetSceneByName(combatMapSceneName);
            if (!mapScene.IsValid() || !mapScene.isLoaded)
            {
                if (string.IsNullOrWhiteSpace(combatMapSceneName))
                {
                    Debug.LogWarning("Combat map scene name is not configured.", this);
                }
                else
                {
                    yield return SceneManager.LoadSceneAsync(combatMapSceneName, LoadSceneMode.Additive);
                    mapScene = SceneManager.GetSceneByName(combatMapSceneName);
                }
            }

            ApplySpawnAnchors(mapScene, targetController);
            ScanNavmesh(mapScene);

            if (targetController != null)
            {
                targetController.SpawnUnits();
            }
        }

        private void ScanNavmesh(Scene mapScene)
        {
            var astar = AstarPath.active;
            if (astar == null)
            {
                var astarObject = new GameObject("A* Pathfinder");
                astar = astarObject.AddComponent<AstarPath>();
                astar.scanOnStartup = false;
            }

            var gg = astar.data.gridGraph ?? astar.data.AddGraph<GridGraph>();
            ConfigureGridGraphBounds(gg, mapScene);
            gg.maxSlope = NavMaxSlope;

            astar.Scan();
        }

        private static void ConfigureGridGraphBounds(GridGraph gridGraph, Scene mapScene)
        {
            if (gridGraph == null)
            {
                return;
            }

            var hasBounds = TryGetMapBounds(mapScene, out var mapBounds);
            if (!hasBounds)
            {
                mapBounds = new Bounds(
                    Vector3.zero,
                    new Vector3(NavGridWidth * NavNodeSize, 1f, NavGridDepth * NavNodeSize));
            }

            var widthWorldSize = Mathf.Max(NavGridWidth * NavNodeSize, mapBounds.size.x + (NavBoundsPadding * 2f));
            var depthWorldSize = Mathf.Max(NavGridDepth * NavNodeSize, mapBounds.size.z + (NavBoundsPadding * 2f));
            var gridWidth = Mathf.Max(1, Mathf.CeilToInt(widthWorldSize / NavNodeSize));
            var gridDepth = Mathf.Max(1, Mathf.CeilToInt(depthWorldSize / NavNodeSize));

            gridGraph.center = mapBounds.center;
            gridGraph.SetDimensions(gridWidth, gridDepth, NavNodeSize);
        }

        private static bool TryGetMapBounds(Scene mapScene, out Bounds bounds)
        {
            bounds = default;
            if (!mapScene.IsValid() || !mapScene.isLoaded)
            {
                return false;
            }

            var hasBounds = false;
            var roots = mapScene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var colliders = roots[i].GetComponentsInChildren<Collider>(true);
                for (var j = 0; j < colliders.Length; j++)
                {
                    var collider = colliders[j];
                    if (collider == null || !collider.enabled || collider.isTrigger)
                    {
                        continue;
                    }

                    if (!hasBounds)
                    {
                        bounds = collider.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(collider.bounds);
                    }
                }
            }

            return hasBounds;
        }

        private void ApplySpawnAnchors(Scene mapScene, TestLevelUnitController targetController)
        {
            if (targetController == null)
            {
                return;
            }

            Transform playerSpawn = null;
            Transform enemySpawn = null;
            if (mapScene.IsValid() && mapScene.isLoaded)
            {
                var roots = mapScene.GetRootGameObjects();
                for (var i = 0; i < roots.Length; i++)
                {
                    var spawnPoints = roots[i].GetComponentsInChildren<CombatSpawnPoint>(true);
                    for (var j = 0; j < spawnPoints.Length; j++)
                    {
                        var spawnPoint = spawnPoints[j];
                        if (spawnPoint.Side == CombatSpawnSide.Player && playerSpawn == null)
                        {
                            playerSpawn = spawnPoint.transform;
                        }
                        else if (spawnPoint.Side == CombatSpawnSide.Enemy && enemySpawn == null)
                        {
                            enemySpawn = spawnPoint.transform;
                        }

                        if (playerSpawn != null && enemySpawn != null)
                        {
                            break;
                        }
                    }

                    if (playerSpawn != null && enemySpawn != null)
                    {
                        break;
                    }
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

    }
}
