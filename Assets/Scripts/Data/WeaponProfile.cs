using System;
using UnityEngine;

namespace IronKingdoms.Combat
{
    public enum WeaponAttackType
    {
        Melee = 0,
        Ranged = 1
    }

    [Serializable]
    public class WeaponProfile
    {
        public string displayName = "Primary Weapon";
        public WeaponAttackType attackType = WeaponAttackType.Melee;
        public int power = 5;
        public float range = 1.5f;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "Weapon" : displayName;
        public int Power => Mathf.Max(1, power);
        public float Range => Mathf.Max(0.5f, range);

        public void Sanitize()
        {
            power = Power;
            range = Range;
            displayName = DisplayName;
        }

        public static WeaponProfile CreateDefault()
        {
            var profile = new WeaponProfile();
            profile.Sanitize();
            return profile;
        }
    }
}
