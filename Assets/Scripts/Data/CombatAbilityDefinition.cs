using UnityEngine;

namespace IronKingdoms.Combat
{
    [CreateAssetMenu(menuName = "Iron Kingdoms/Combat/Ability Definition", fileName = "Ability")]
    public class CombatAbilityDefinition : ScriptableObject
    {
        [SerializeField] private string abilityId;
        [SerializeField] private string displayName;
        [TextArea] [SerializeField] private string description;

        [Header("Line of Sight")]
        [SerializeField] private bool ignoresForestForLineOfSight;

        [Header("Defense While In Terrain")]
        [SerializeField] private string requiredTerrainFeatureId = "Forest";
        [SerializeField] private int meleeDefenseBonusWhileCompletelyInside;
        [SerializeField] private int rangedDefenseBonusWhileCompletelyInside;

        public string AbilityId => string.IsNullOrWhiteSpace(abilityId) ? name : abilityId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string Description => description ?? string.Empty;
        public bool IgnoresForestForLineOfSight => ignoresForestForLineOfSight;
        public string RequiredTerrainFeatureId => string.IsNullOrWhiteSpace(requiredTerrainFeatureId) ? "Forest" : requiredTerrainFeatureId;
        public int MeleeDefenseBonusWhileCompletelyInside => meleeDefenseBonusWhileCompletelyInside;
        public int RangedDefenseBonusWhileCompletelyInside => rangedDefenseBonusWhileCompletelyInside;

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(abilityId))
            {
                abilityId = name;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = name;
            }
        }
    }
}
