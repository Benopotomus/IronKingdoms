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
            ScanNavmesh();

            if (targetController != null)
            {
                targetController.SpawnUnits();
            }
        }

        private void ScanNavmesh()
        {
            if (AstarPath.active != null)
            {
                AstarPath.active.Scan();
                return;
            }

            var astarObject = new GameObject("A* Pathfinder");
            var astar = astarObject.AddComponent<AstarPath>();
            astar.scanOnStartup = false;

            var gg = astar.data.AddGraph<GridGraph>();
            gg.center = Vector3.zero;
            gg.SetDimensions(NavGridWidth, NavGridDepth, NavNodeSize);
            gg.maxSlope = NavMaxSlope;

            astar.Scan();
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
