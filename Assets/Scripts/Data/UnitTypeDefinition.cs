using UnityEngine;

namespace IronKingdoms.Combat
{
    [CreateAssetMenu(menuName = "Iron Kingdoms/Combat/Unit Type", fileName = "UnitType")]
    public class UnitTypeDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = "New Unit";
        [SerializeField] private UnitRole role = UnitRole.Infantry;
        [SerializeField] private CombatStats stats = new CombatStats();
        [SerializeField, TextArea] private string designNotes = string.Empty;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public UnitRole Role => role;
        public CombatStats Stats => stats;
        public string DesignNotes => designNotes;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (stats == null)
            {
                stats = new CombatStats();
            }

            stats.EnsureWeaponDefaults();
        }
#endif
    }
}
