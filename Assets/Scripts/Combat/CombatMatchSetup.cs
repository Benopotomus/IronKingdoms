using System;
using System.Collections;
using Pathfinding;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Staged orchestration for combat startup: map load, spawn anchors, army spawn,
    /// fog initialization, and the first player turn.
    /// </summary>
    public static class CombatMatchSetup
    {
        public static event Action<CombatMatchSetupPhase> PhaseChanged;

        public static CombatMatchSetupPhase CurrentPhase { get; private set; } = CombatMatchSetupPhase.None;

        public static bool LogPhases { get; set; } = true;

        public static IEnumerator RunFromSceneLoad(
            TestLevelUnitController controller,
            string combatMapSceneName,
            bool logPhases = true)
        {
            var previousLogging = LogPhases;
            LogPhases = logPhases;

            try
            {
                if (controller == null)
                {
                    SetPhase(CombatMatchSetupPhase.Failed);
                    Debug.LogError("Combat match setup failed: no unit controller assigned.");
                    yield break;
                }

                SetPhase(CombatMatchSetupPhase.LoadingMap);
                var mapScene = SceneManager.GetSceneByName(combatMapSceneName);
                if (!mapScene.IsValid() || !mapScene.isLoaded)
                {
                    if (string.IsNullOrWhiteSpace(combatMapSceneName))
                    {
                        SetPhase(CombatMatchSetupPhase.Failed);
                        Debug.LogError("Combat match setup failed: combat map scene name is not configured.");
                        yield break;
                    }

                    var loadOperation = SceneManager.LoadSceneAsync(combatMapSceneName, LoadSceneMode.Additive);
                    if (loadOperation == null)
                    {
                        SetPhase(CombatMatchSetupPhase.Failed);
                        Debug.LogError($"Combat match setup failed: could not load scene '{combatMapSceneName}'.");
                        yield break;
                    }

                    while (!loadOperation.isDone)
                    {
                        yield return null;
                    }

                    mapScene = SceneManager.GetSceneByName(combatMapSceneName);
                }

                SetPhase(CombatMatchSetupPhase.PreparingNavigation);
                EnsureNavigationReady(controller);

                SetPhase(CombatMatchSetupPhase.RegisteringMapScene);
                CombatMapSceneProvider.RegisterMapScene(mapScene);

                SetPhase(CombatMatchSetupPhase.ResolvingSpawnAnchors);
                ApplySpawnAnchors(mapScene, controller);

                yield return RunMatchPhases(controller);
            }
            finally
            {
                LogPhases = previousLogging;
            }
        }

        public static IEnumerator RunMatchPhases(TestLevelUnitController controller)
        {
            if (controller == null)
            {
                SetPhase(CombatMatchSetupPhase.Failed);
                Debug.LogError("Combat match setup failed: no unit controller assigned.");
                yield break;
            }

            SetPhase(CombatMatchSetupPhase.BuildingVisualizers);
            controller.PrepareMatchVisualizers();
            yield return null;

            SetPhase(CombatMatchSetupPhase.SpawningArmies);
            controller.SpawnArmies();
            yield return null;

            SetPhase(CombatMatchSetupPhase.InitializingVisibility);
            controller.InitializeMatchVisibility();
            yield return null;

            SetPhase(CombatMatchSetupPhase.BeginningMatch);
            controller.BeginMatch();

            SetPhase(CombatMatchSetupPhase.Ready);
        }

        public static void RunMatchPhasesImmediate(TestLevelUnitController controller)
        {
            if (controller == null)
            {
                SetPhase(CombatMatchSetupPhase.Failed);
                Debug.LogError("Combat match setup failed: no unit controller assigned.");
                return;
            }

            SetPhase(CombatMatchSetupPhase.BuildingVisualizers);
            controller.PrepareMatchVisualizers();

            SetPhase(CombatMatchSetupPhase.SpawningArmies);
            controller.SpawnArmies();

            SetPhase(CombatMatchSetupPhase.InitializingVisibility);
            controller.InitializeMatchVisibility();

            SetPhase(CombatMatchSetupPhase.BeginningMatch);
            controller.BeginMatch();

            SetPhase(CombatMatchSetupPhase.Ready);
        }

        private static void SetPhase(CombatMatchSetupPhase phase)
        {
            CurrentPhase = phase;
            if (LogPhases)
            {
                Debug.Log($"[CombatMatchSetup] {phase}");
            }

            PhaseChanged?.Invoke(phase);
        }

        private static void EnsureNavigationReady(MonoBehaviour context)
        {
            if (AstarPath.active == null)
            {
                Debug.LogWarning("Combat map loaded without an active AstarPath component; movement will use non-nav fallback positions.", context);
                return;
            }

            var graphs = AstarPath.active.data?.graphs;
            if (graphs == null || graphs.Length == 0)
            {
                Debug.LogWarning("AstarPath has no graphs configured after combat map load.", context);
                return;
            }

            for (var i = 0; i < graphs.Length; i++)
            {
                if (graphs[i] != null && graphs[i].isScanned)
                {
                    return;
                }
            }

            Debug.LogWarning("Combat map nav graphs are present but not scanned; running AstarPath.Scan() at runtime as fallback.", context);
            AstarPath.active.Scan();
        }

        private static void ApplySpawnAnchors(Scene mapScene, TestLevelUnitController controller)
        {
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
                controller.SetSpawnAnchors(playerSpawn, enemySpawn);
                return;
            }

            Debug.LogWarning("Combat map scene did not provide both player and enemy spawn points.");
        }
    }
}
