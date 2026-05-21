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
            var mapScene = LoadMapScene();
            ApplySpawnAnchors(mapScene);
        }

        private Scene LoadMapScene()
        {
            if (string.IsNullOrWhiteSpace(combatMapSceneName))
            {
                Debug.LogWarning("Combat map scene name is not configured.", this);
                return default;
            }

            var mapScene = SceneManager.GetSceneByName(combatMapSceneName);
            if (!mapScene.IsValid() || !mapScene.isLoaded)
            {
                SceneManager.LoadScene(combatMapSceneName, LoadSceneMode.Additive);
                mapScene = SceneManager.GetSceneByName(combatMapSceneName);
            }

            return mapScene;
        }

        private void ApplySpawnAnchors(Scene mapScene)
        {
            var targetController = unitController != null ? unitController : GetComponent<TestLevelUnitController>();
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
