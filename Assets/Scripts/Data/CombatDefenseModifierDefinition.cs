using UnityEngine;

namespace IronKingdoms.Combat
{
    public enum CombatDefenseModifierCategory
    {
        Other = 0,
        Concealment = 1,
        Cover = 2
    }

    public enum CombatDefenseModifierApplication
    {
        UnitCompletelyInsideTerrainZone = 0,
        UnitWithinOneInchOfFeature = 1,
        AlwaysWhileTagged = 2
    }

    [CreateAssetMenu(menuName = "Iron Kingdoms/Combat/Defense Modifier", fileName = "DefenseModifier")]
    public class CombatDefenseModifierDefinition : ScriptableObject
    {
        [SerializeField] private string modifierId;
        [SerializeField] private string displayName;
        [TextArea] [SerializeField] private string description;
        [SerializeField] private CombatDefenseModifierCategory category = CombatDefenseModifierCategory.Concealment;
        [SerializeField] private CombatDefenseModifierApplication application = CombatDefenseModifierApplication.UnitCompletelyInsideTerrainZone;
        [SerializeField] private int defenseBonus = 2;
        [SerializeField] private bool appliesToRangedAndArcane = true;
        [SerializeField] private bool appliesToMelee;
        [SerializeField] private bool ignoredBySprayAttacks = true;
        [SerializeField, Min(0f)] private float proximityInches = 1f;

        public string ModifierId => string.IsNullOrWhiteSpace(modifierId) ? name : modifierId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string Description => description ?? string.Empty;
        public CombatDefenseModifierCategory Category => category;
        public CombatDefenseModifierApplication Application => application;
        public int DefenseBonus => defenseBonus;
        public bool AppliesToRangedAndArcane => appliesToRangedAndArcane;
        public bool AppliesToMelee => appliesToMelee;
        public bool IgnoredBySprayAttacks => ignoredBySprayAttacks;
        public float ProximityInches => proximityInches;

        public bool AppliesToWeapon(WeaponProfile weapon, bool isSprayAttack = false)
        {
            if (weapon == null)
            {
                return false;
            }

            if (isSprayAttack && ignoredBySprayAttacks)
            {
                return false;
            }

            return weapon.AttackType == WeaponAttackType.Melee
                ? appliesToMelee
                : appliesToRangedAndArcane;
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(modifierId))
            {
                modifierId = name;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = name;
            }
        }
    }
}
