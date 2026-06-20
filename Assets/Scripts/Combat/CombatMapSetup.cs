using System.Collections;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Loads a dedicated combat map scene additively, then runs the staged
    /// <see cref="CombatMatchSetup"/> sequence to spawn units and begin the match.
    ///
    /// The A* navmesh (RecastGraph) must be baked and saved inside CombatMapScene in the
    /// Unity editor. Use <b>Iron Kingdoms → Tools → Combat Navmesh → Scan Combat Map (High Quality)</b>,
    /// then save the scene. Set "Scan On Startup" to false on the AstarPath component so the
    /// cached graph is used directly at runtime.
    /// If no scanned data is present, the setup sequence performs a one-time runtime scan fallback.
    /// </summary>
    public class CombatMapSetup : MonoBehaviour
    {
        [SerializeField] private string combatMapSceneName = "CombatMapScene";
        [SerializeField] private TestLevelUnitController unitController;
        [SerializeField] private bool logSetupPhases = true;

        private void Awake()
        {
            var targetController = unitController != null ? unitController : GetComponent<TestLevelUnitController>();
            CombatStartupLog.Log(
                $"CombatMapSetup.Awake on '{name}': mapScene='{combatMapSceneName}', controller={(targetController != null ? targetController.name : "null")}.");
            if (targetController != null)
            {
                targetController.DisableAutoSpawn();
            }
            else
            {
                CombatStartupLog.LogError("CombatMapSetup has no TestLevelUnitController reference.");
            }

            StartCoroutine(RunSetup(targetController));
        }

        private IEnumerator RunSetup(TestLevelUnitController targetController)
        {
            CombatStartupLog.Log("CombatMapSetup.RunSetup coroutine started.");
            yield return CombatMatchSetup.RunFromSceneLoad(
                targetController,
                combatMapSceneName,
                logSetupPhases);
            CombatStartupLog.Log(
                $"CombatMapSetup.RunSetup finished. phase={CombatMatchSetup.CurrentPhase}, "
                + $"playerUnits={(targetController != null ? targetController.PlayerRuntimeUnitCount : 0)}, "
                + $"enemyUnits={(targetController != null ? targetController.EnemyRuntimeUnitCount : 0)}.");
        }
    }
}
