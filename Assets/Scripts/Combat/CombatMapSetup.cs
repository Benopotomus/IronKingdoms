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
            if (targetController != null)
            {
                targetController.DisableAutoSpawn();
            }

            StartCoroutine(RunSetup(targetController));
        }

        private IEnumerator RunSetup(TestLevelUnitController targetController)
        {
            yield return CombatMatchSetup.RunFromSceneLoad(
                targetController,
                combatMapSceneName,
                logSetupPhases);
        }
    }
}
