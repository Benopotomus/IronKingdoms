using UnityEngine;

namespace IronKingdoms.Combat
{
    public enum WeaponAttackType
    {
        Melee = 0,
        Ranged = 1
    }

    [CreateAssetMenu(menuName = "Iron Kingdoms/Combat/Weapon", fileName = "Weapon")]
    public class WeaponProfile : ScriptableObject
    {
        public string displayName = "Primary Weapon";
        public WeaponAttackType attackType = WeaponAttackType.Melee;
        public int power = 5;
        public float range = 1.5f;
        public int matModifier;
        public int ratModifier;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "Weapon" : displayName;
        public WeaponAttackType AttackType => attackType;
        public int Power => Mathf.Max(1, power);
        public float Range => Mathf.Max(0.5f, range);
        public int MatModifier => matModifier;
        public int RatModifier => ratModifier;

        public int GetAttackModifier()
        {
            return attackType == WeaponAttackType.Melee ? matModifier : ratModifier;
        }

        private void OnValidate()
        {
            Sanitize();
        }

        public void Sanitize()
        {
            power = Power;
            range = Range;
            displayName = DisplayName;
        }

        public static WeaponProfile CreateDefault()
        {
            var profile = CreateInstance<WeaponProfile>();
            profile.name = "Default Weapon";
            profile.Sanitize();
            return profile;
        }
    }
}
