using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Loads a dedicated combat map scene additively, resolves spawn-point markers, then
    /// hands off to <see cref="TestLevelUnitController"/> to place units.
    ///
    /// The A* navmesh (RecastGraph) must be baked and saved inside CombatMapScene in the
    /// Unity editor (A* Inspector → Scan, then save the scene).  Set "Scan On Startup" to
    /// false on the AstarPath component so the cached graph is used directly at runtime —
    /// no runtime scanning takes place here.
    /// </summary>
    public class CombatMapSetup : MonoBehaviour
    {
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
        /// 3. Calls <see cref="TestLevelUnitController.SpawnUnits"/> so units are placed at the
        ///    correct spawn anchors.  The baked navmesh in CombatMapScene is already active by
        ///    this point (loaded with the scene).
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

            if (targetController != null)
            {
                targetController.SpawnUnits();
            }
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
