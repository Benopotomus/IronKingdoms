using System;
using UnityEngine;

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
        public int startingResource = 0;
        public WeaponProfile[] weapons = Array.Empty<WeaponProfile>();

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
                weapons[i].Sanitize();
            }
        }
    }
}
