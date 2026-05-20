using UnityEngine;

namespace IronKingdoms.Combat
{
    public class CombatFlowBootstrap : MonoBehaviour
    {
        [SerializeField] private TestCombatScenarioAsset scenario;
        [SerializeField] private bool autoRunOnStart = true;
        [SerializeField] private bool logToConsole = true;
        [SerializeField, TextArea(12, 24)] private string lastReportPreview = string.Empty;

        public string LastReportPreview => lastReportPreview;

        private void Start()
        {
            if (autoRunOnStart)
            {
                RunScenario();
            }
        }

        [ContextMenu("Run Scenario")]
        public void RunScenario()
        {
            var result = CombatSimulator.Simulate(scenario);
            lastReportPreview = result.ToString();

            if (logToConsole)
            {
                if (string.IsNullOrEmpty(lastReportPreview))
                {
                    Debug.LogWarning("Combat scenario produced no output.", this);
                }
                else
                {
                    Debug.Log(lastReportPreview, this);
                }
            }
        }
    }
}
