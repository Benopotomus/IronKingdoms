using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace IronKingdoms.Combat
{
    [Serializable]
    public class CombatStats
    {
        public float speed = 5f;
        [Tooltip("Radius in inches out to which this model reveals the map for its player (fog of war).")]
        public float visibilityRange = CombatScale.DefaultVisibilityRangeInches;
        public ModelSize modelSize = ModelSize.Base30mm;
        public int meleeAttack = 5;
        public int rangedAttack = 4;
        public int defense = 12;
        public int armor = 14;
        public int health = 10;
        public List<CombatAdvantageDefinition> advantages = new();
        public List<CombatAbilityDefinition> abilities = new();
        [FormerlySerializedAs("advantageList")]
        [SerializeField, HideInInspector] private List<UnitAdvantage> legacyAdvantageList = new();
        [FormerlySerializedAs("advantages")]
        [SerializeField, HideInInspector] private UnitAdvantage legacyAdvantages = UnitAdvantage.None;
        public WeaponProfile[] weapons = Array.Empty<WeaponProfile>();
        [NonSerialized] private bool advantagesInitialized;
        [NonSerialized] private bool abilitiesInitialized;

        public bool HasAbility(CombatAbilityDefinition ability)
        {
            if (ability == null)
            {
                return false;
            }

            EnsureAbilityDefaults();
            return abilities.Contains(ability);
        }

        public bool AddAbility(CombatAbilityDefinition ability)
        {
            EnsureAbilityDefaults();
            if (ability == null || abilities.Contains(ability))
            {
                return false;
            }

            abilities.Add(ability);
            return true;
        }

        public bool RemoveAbility(CombatAbilityDefinition ability)
        {
            EnsureAbilityDefaults();
            return ability != null && abilities.Remove(ability);
        }

        public bool HasAdvantage(CombatAdvantageDefinition advantage)
        {
            if (advantage == null)
            {
                return false;
            }

            EnsureAdvantageDefaults();
            return advantages.Contains(advantage);
        }

        public bool HasAdvantage(UnitAdvantage legacyAdvantage)
        {
            if (legacyAdvantage == UnitAdvantage.None)
            {
                return false;
            }

            EnsureAdvantageDefaults();
            var catalog = CombatDefinitionCatalog.Instance;
            var mapped = catalog != null ? catalog.FindAdvantage(legacyAdvantage) : null;
            return mapped != null && advantages.Contains(mapped);
        }

        public bool IgnoresConcealmentAndStealth()
        {
            EnsureAdvantageDefaults();
            for (var i = 0; i < advantages.Count; i++)
            {
                var advantage = advantages[i];
                if (advantage != null && advantage.IgnoresConcealmentAndStealth)
                {
                    return true;
                }
            }

            return false;
        }

        public bool IgnoresForestLineOfSightLimits()
        {
            EnsureAdvantageDefaults();
            for (var i = 0; i < advantages.Count; i++)
            {
                var advantage = advantages[i];
                if (advantage != null && advantage.IgnoresForestLineOfSightLimits)
                {
                    return true;
                }
            }

            return false;
        }

        public bool TreatsRoughTerrainAsOpenWhileAdvancing()
        {
            EnsureAdvantageDefaults();
            for (var i = 0; i < advantages.Count; i++)
            {
                var advantage = advantages[i];
                if (advantage != null && advantage.TreatsRoughTerrainAsOpenWhileAdvancing)
                {
                    return true;
                }
            }

            return HasAdvantage(UnitAdvantage.Pathfinder);
        }

        public WeaponProfile GetPrimaryWeapon()
        {
            EnsureWeaponDefaults();
            return weapons[0];
        }

        public void EnsureWeaponDefaults()
        {
            if (weapons == null || weapons.Length == 0 || weapons[0] == null)
            {
                weapons = new[]
                {
                    WeaponProfile.CreateDefault()
                };
                return;
            }

            for (var i = 0; i < weapons.Length; i++)
            {
                weapons[i] ??= WeaponProfile.CreateDefault();
            }
        }

        public void EnsureAdvantageDefaults()
        {
            if (advantagesInitialized)
            {
                return;
            }

            advantages ??= new List<CombatAdvantageDefinition>();
            var catalog = CombatDefinitionCatalog.Instance;

            if (legacyAdvantageList != null && legacyAdvantageList.Count > 0)
            {
                for (var i = 0; i < legacyAdvantageList.Count; i++)
                {
                    var legacy = legacyAdvantageList[i];
                    if (legacy == UnitAdvantage.None || catalog == null)
                    {
                        continue;
                    }

                    var mapped = catalog.FindAdvantage(legacy);
                    if (mapped != null && !advantages.Contains(mapped))
                    {
                        advantages.Add(mapped);
                    }
                }

                legacyAdvantageList.Clear();
            }

            if (legacyAdvantages != UnitAdvantage.None && catalog != null)
            {
                foreach (UnitAdvantage value in Enum.GetValues(typeof(UnitAdvantage)))
                {
                    if (value == UnitAdvantage.None)
                    {
                        continue;
                    }

                    if ((legacyAdvantages & value) == value)
                    {
                        var mapped = catalog.FindAdvantage(value);
                        if (mapped != null && !advantages.Contains(mapped))
                        {
                            advantages.Add(mapped);
                        }
                    }
                }

                legacyAdvantages = UnitAdvantage.None;
            }

            advantages.RemoveAll(value => value == null);
            advantagesInitialized = true;
        }

        public void EnsureAbilityDefaults()
        {
            if (abilitiesInitialized)
            {
                return;
            }

            abilities ??= new List<CombatAbilityDefinition>();
            abilities.RemoveAll(value => value == null);
            abilitiesInitialized = true;
        }
    }
}
