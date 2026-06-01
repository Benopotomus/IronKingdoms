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
        public ModelSize modelSize = ModelSize.Base30mm;
        public int meleeAttack = 5;
        public int rangedAttack = 4;
        public int defense = 12;
        public int armor = 14;
        public int health = 10;
        public List<UnitAdvantage> advantageList = new();
        [FormerlySerializedAs("advantages")]
        [SerializeField, HideInInspector] private UnitAdvantage legacyAdvantages = UnitAdvantage.None;
        public WeaponProfile[] weapons = Array.Empty<WeaponProfile>();
        [NonSerialized] private bool advantagesInitialized;

        public bool HasAdvantage(UnitAdvantage advantage)
        {
            if (advantage == UnitAdvantage.None)
            {
                return false;
            }

            EnsureAdvantageDefaults();
            return advantageList.Contains(advantage);
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

            advantageList ??= new List<UnitAdvantage>();

            if (legacyAdvantages != UnitAdvantage.None)
            {
                foreach (UnitAdvantage value in Enum.GetValues(typeof(UnitAdvantage)))
                {
                    if (value == UnitAdvantage.None)
                    {
                        continue;
                    }

                    if ((legacyAdvantages & value) == value && !advantageList.Contains(value))
                    {
                        advantageList.Add(value);
                    }
                }

                legacyAdvantages = UnitAdvantage.None;
            }

            advantageList.RemoveAll(value => value == UnitAdvantage.None);
            advantagesInitialized = true;
        }
    }
}
