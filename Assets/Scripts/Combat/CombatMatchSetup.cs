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

            if (controller == null)
            {
                SetPhase(CombatMatchSetupPhase.Failed);
                CombatStartupLog.LogError("RunFromSceneLoad failed: no unit controller assigned.");
                LogPhases = previousLogging;
                yield break;
            }

            CombatStartupLog.Log($"RunFromSceneLoad begin (map='{combatMapSceneName}', controller='{controller.name}').");

            SetPhase(CombatMatchSetupPhase.LoadingMap);
            var mapScene = SceneManager.GetSceneByName(combatMapSceneName);
            if (!mapScene.IsValid() || !mapScene.isLoaded)
            {
                if (string.IsNullOrWhiteSpace(combatMapSceneName))
                {
                    SetPhase(CombatMatchSetupPhase.Failed);
                    CombatStartupLog.LogError("RunFromSceneLoad failed: combat map scene name is not configured.");
                    LogPhases = previousLogging;
                    yield break;
                }

                var loadOperation = SceneManager.LoadSceneAsync(combatMapSceneName, LoadSceneMode.Additive);
                if (loadOperation == null)
                {
                    SetPhase(CombatMatchSetupPhase.Failed);
                    CombatStartupLog.LogError($"RunFromSceneLoad failed: could not load scene '{combatMapSceneName}'.");
                    LogPhases = previousLogging;
                    yield break;
                }

                while (!loadOperation.isDone)
                {
                    yield return null;
                }

                mapScene = SceneManager.GetSceneByName(combatMapSceneName);
            }

            CombatStartupLog.Log(
                $"Map scene loaded: valid={mapScene.IsValid()}, name='{mapScene.name}', rootCount={(mapScene.IsValid() ? mapScene.rootCount : 0)}.");

            var postLoadFailed = false;
            try
            {
                SetPhase(CombatMatchSetupPhase.PreparingNavigation);
                EnsureNavigationReady(controller);

                SetPhase(CombatMatchSetupPhase.RegisteringMapScene);
                CombatMapSceneProvider.RegisterMapScene(mapScene);
                LogActiveCombatZones("after map registration");

                SetPhase(CombatMatchSetupPhase.ResolvingSpawnAnchors);
                ApplySpawnAnchors(mapScene, controller);
            }
            catch (Exception ex)
            {
                SetPhase(CombatMatchSetupPhase.Failed);
                CombatStartupLog.LogException("RunFromSceneLoad", ex);
                postLoadFailed = true;
            }

            if (postLoadFailed)
            {
                LogPhases = previousLogging;
                yield break;
            }

            yield return RunMatchPhases(controller);

            LogPhases = previousLogging;
        }

        public static IEnumerator RunMatchPhases(TestLevelUnitController controller)
        {
            if (controller == null)
            {
                SetPhase(CombatMatchSetupPhase.Failed);
                CombatStartupLog.LogError("RunMatchPhases failed: no unit controller assigned.");
                yield break;
            }

            CombatStartupLog.Log("RunMatchPhases begin.");

            if (!TryRunPhase(CombatMatchSetupPhase.BuildingVisualizers, () => controller.PrepareMatchVisualizers()))
            {
                yield break;
            }

            yield return null;

            if (!TryRunPhase(CombatMatchSetupPhase.SpawningArmies, () =>
                {
                    controller.LogSpawnDiagnostics("before SpawnArmies");
                    controller.SpawnArmies();
                    controller.LogSpawnDiagnostics("after SpawnArmies");
                }))
            {
                yield break;
            }

            yield return null;

            if (!TryRunPhase(CombatMatchSetupPhase.InitializingVisibility, () => controller.InitializeMatchVisibility()))
            {
                yield break;
            }

            yield return null;

            if (!TryRunPhase(CombatMatchSetupPhase.BeginningMatch, () => controller.BeginMatch()))
            {
                yield break;
            }

            SetPhase(CombatMatchSetupPhase.Ready);
            CombatStartupLog.Log(
                $"RunMatchPhases complete. Player units={controller.PlayerRuntimeUnitCount}, enemy units={controller.EnemyRuntimeUnitCount}.");
        }

        public static void RunMatchPhasesImmediate(TestLevelUnitController controller)
        {
            if (controller == null)
            {
                SetPhase(CombatMatchSetupPhase.Failed);
                CombatStartupLog.LogError("RunMatchPhasesImmediate failed: no unit controller assigned.");
                return;
            }

            CombatStartupLog.Log("RunMatchPhasesImmediate begin.");

            if (!TryRunPhase(CombatMatchSetupPhase.BuildingVisualizers, () => controller.PrepareMatchVisualizers()))
            {
                return;
            }

            if (!TryRunPhase(CombatMatchSetupPhase.SpawningArmies, () =>
                {
                    controller.LogSpawnDiagnostics("before SpawnArmies");
                    controller.SpawnArmies();
                    controller.LogSpawnDiagnostics("after SpawnArmies");
                }))
            {
                return;
            }

            if (!TryRunPhase(CombatMatchSetupPhase.InitializingVisibility, () => controller.InitializeMatchVisibility()))
            {
                return;
            }

            if (!TryRunPhase(CombatMatchSetupPhase.BeginningMatch, () => controller.BeginMatch()))
            {
                return;
            }

            SetPhase(CombatMatchSetupPhase.Ready);
            CombatStartupLog.Log(
                $"RunMatchPhasesImmediate complete. Player units={controller.PlayerRuntimeUnitCount}, enemy units={controller.EnemyRuntimeUnitCount}.");
        }

        private static bool TryRunPhase(CombatMatchSetupPhase phase, Action action)
        {
            SetPhase(phase);
            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                SetPhase(CombatMatchSetupPhase.Failed);
                CombatStartupLog.LogException(phase.ToString(), ex);
                return false;
            }
        }

        private static void LogActiveCombatZones(string context)
        {
            var zones = CombatZone.ActiveZones;
            var limitedDepth = 0;
            var polygon = 0;
            for (var i = 0; i < zones.Count; i++)
            {
                var zone = zones[i];
                if (zone?.TerrainFeature?.LineOfSightMode == CombatTerrainLineOfSightMode.LimitedDepth)
                {
                    limitedDepth++;
                    if (zone.TryGetComponent<CombatZonePolygonFootprint>(out var footprint) && footprint.HasFootprint)
                    {
                        polygon++;
                    }
                }
            }

            CombatStartupLog.Log(
                $"{context}: active CombatZones={zones.Count}, limitedDepth={limitedDepth}, polygonFootprints={polygon}.");
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
                CombatStartupLog.Log(
                    $"Spawn anchors resolved: player='{playerSpawn.name}' @ {playerSpawn.position}, enemy='{enemySpawn.name}' @ {enemySpawn.position}.");
                return;
            }

            CombatStartupLog.LogWarning(
                $"Spawn anchors missing (player={(playerSpawn != null ? playerSpawn.name : "null")}, enemy={(enemySpawn != null ? enemySpawn.name : "null")}). "
                + "Using serialized anchors on the unit controller if present.");
        }
    }
}
