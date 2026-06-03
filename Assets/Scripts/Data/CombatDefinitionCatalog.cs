using System.Collections.Generic;
using UnityEngine;

namespace IronKingdoms.Combat
{
    [CreateAssetMenu(menuName = "Iron Kingdoms/Combat/Definition Catalog", fileName = "CombatDefinitionCatalog")]
    public class CombatDefinitionCatalog : ScriptableObject
    {
        private static CombatDefinitionCatalog cachedInstance;

        [SerializeField] private CombatAdvantageDefinition[] advantages = System.Array.Empty<CombatAdvantageDefinition>();
        [SerializeField] private CombatWeaponAdvantageDefinition[] weaponAdvantages = System.Array.Empty<CombatWeaponAdvantageDefinition>();
        [SerializeField] private CombatAbilityDefinition[] abilities = System.Array.Empty<CombatAbilityDefinition>();
        [SerializeField] private CombatDefenseModifierDefinition[] defenseModifiers = System.Array.Empty<CombatDefenseModifierDefinition>();
        [SerializeField] private CombatTerrainFeatureDefinition[] terrainFeatures = System.Array.Empty<CombatTerrainFeatureDefinition>();

        public IReadOnlyList<CombatAdvantageDefinition> Advantages => advantages;
        public IReadOnlyList<CombatWeaponAdvantageDefinition> WeaponAdvantages => weaponAdvantages;
        public IReadOnlyList<CombatAbilityDefinition> Abilities => abilities;
        public IReadOnlyList<CombatDefenseModifierDefinition> DefenseModifiers => defenseModifiers;
        public IReadOnlyList<CombatTerrainFeatureDefinition> TerrainFeatures => terrainFeatures;

        public static CombatDefinitionCatalog Instance
        {
            get
            {
                if (cachedInstance != null)
                {
                    return cachedInstance;
                }

                var catalogs = Resources.FindObjectsOfTypeAll<CombatDefinitionCatalog>();
                if (catalogs.Length > 0)
                {
                    cachedInstance = catalogs[0];
                }

                return cachedInstance;
            }
        }

        public void RegisterAsActiveCatalog()
        {
            cachedInstance = this;
        }

        public CombatAdvantageDefinition FindAdvantage(string advantageId)
        {
            return FindById(advantages, advantageId);
        }

        public CombatWeaponAdvantageDefinition FindWeaponAdvantage(string advantageId)
        {
            return FindById(weaponAdvantages, advantageId);
        }

        public CombatWeaponAdvantageDefinition FindWeaponAdvantage(WeaponAdvantageKind kind)
        {
            if (kind == WeaponAdvantageKind.Other)
            {
                return null;
            }

            for (var i = 0; i < weaponAdvantages.Length; i++)
            {
                var advantage = weaponAdvantages[i];
                if (advantage != null && advantage.Kind == kind)
                {
                    return advantage;
                }
            }

            return null;
        }

        public CombatAdvantageDefinition FindAdvantage(UnitAdvantage legacyAdvantage)
        {
            if (legacyAdvantage == UnitAdvantage.None)
            {
                return null;
            }

            var legacyId = legacyAdvantage.ToString();
            for (var i = 0; i < advantages.Length; i++)
            {
                var advantage = advantages[i];
                if (advantage != null && advantage.AdvantageId == legacyId)
                {
                    return advantage;
                }
            }

            return null;
        }

        public CombatDefenseModifierDefinition FindDefenseModifier(string modifierId)
        {
            return FindById(defenseModifiers, modifierId);
        }

        public CombatTerrainFeatureDefinition FindTerrainFeature(string featureId)
        {
            return FindById(terrainFeatures, featureId);
        }

        public CombatAbilityDefinition FindAbility(string abilityId)
        {
            return FindById(abilities, abilityId);
        }

        private static TDefinition FindById<TDefinition>(TDefinition[] entries, string id)
            where TDefinition : ScriptableObject
        {
            if (string.IsNullOrWhiteSpace(id) || entries == null)
            {
                return null;
            }

            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry != null && entry.name == id)
                {
                    return entry;
                }
            }

            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry is CombatAdvantageDefinition advantage && advantage.AdvantageId == id)
                {
                    return entry;
                }

                if (entry is CombatWeaponAdvantageDefinition weaponAdvantage && weaponAdvantage.AdvantageId == id)
                {
                    return entry;
                }

                if (entry is CombatDefenseModifierDefinition defenseModifier && defenseModifier.ModifierId == id)
                {
                    return entry;
                }

                if (entry is CombatTerrainFeatureDefinition terrainFeature && terrainFeature.FeatureId == id)
                {
                    return entry;
                }

                if (entry is CombatAbilityDefinition ability && ability.AbilityId == id)
                {
                    return entry;
                }
            }

            return null;
        }

        private void OnEnable()
        {
            if (cachedInstance == null)
            {
                cachedInstance = this;
            }
        }
    }
}
