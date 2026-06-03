using UnityEngine;

namespace IronKingdoms.Combat
{
    [CreateAssetMenu(menuName = "Iron Kingdoms/Combat/Advantage Definition", fileName = "Advantage")]
    public class CombatAdvantageDefinition : ScriptableObject
    {
        [SerializeField] private string advantageId;
        [SerializeField] private string displayName;
        [TextArea] [SerializeField] private string description;

        [Header("Rules (Mk4-inspired)")]
        [SerializeField] private bool ignoresConcealmentAndStealth;
        [SerializeField] private bool treatsRoughTerrainAsOpenWhileAdvancing;
        [SerializeField] private bool ignoresForestLineOfSightLimits;

        public string AdvantageId => string.IsNullOrWhiteSpace(advantageId) ? name : advantageId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string Description => description ?? string.Empty;
        public bool IgnoresConcealmentAndStealth => ignoresConcealmentAndStealth;
        public bool TreatsRoughTerrainAsOpenWhileAdvancing => treatsRoughTerrainAsOpenWhileAdvancing;
        public bool IgnoresForestLineOfSightLimits => ignoresForestLineOfSightLimits;

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(advantageId))
            {
                advantageId = name;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = name;
            }
        }
    }
}
