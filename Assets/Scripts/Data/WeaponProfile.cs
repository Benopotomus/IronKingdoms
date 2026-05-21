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

        [Tooltip("Formula ScriptableObject that governs attack dice count and hit evaluation. Uses standard 2d6 rules when unassigned.")]
        public AttackRollFormula attackFormula;

        [Tooltip("Formula ScriptableObject that governs damage dice count and damage calculation. Uses standard 2d6 rules when unassigned.")]
        public DamageRollFormula damageFormula;

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

        /// <summary>Returns the number of attack dice for a standard or boosted roll.</summary>
        public int GetAttackDiceCount(bool boosted)
        {
            return attackFormula != null ? attackFormula.GetDiceCount(boosted) : (boosted ? 3 : 2);
        }

        /// <summary>
        /// Evaluates whether a completed attack roll hits.
        /// die1 and die2 must be the first two dice rolled (used for auto-miss/hit detection).
        /// </summary>
        public bool EvaluateAttackHit(int die1, int die2, int totalRoll, int targetDefense)
        {
            if (attackFormula != null)
            {
                return attackFormula.EvaluateHit(die1, die2, totalRoll, targetDefense);
            }

            if (die1 == 1 && die2 == 1)
            {
                return false;
            }

            if (die1 == 6 && die2 == 6)
            {
                return true;
            }

            return totalRoll >= targetDefense;
        }

        /// <summary>Returns the number of damage dice for a standard or boosted roll.</summary>
        public int GetDamageDiceCount(bool boosted)
        {
            return damageFormula != null ? damageFormula.GetDiceCount(boosted) : (boosted ? 3 : 2);
        }

        /// <summary>Calculates damage applied to a target after subtracting ARM.</summary>
        public int EvaluateDamage(int diceTotal, int targetArmor)
        {
            return damageFormula != null
                ? damageFormula.EvaluateDamage(diceTotal, Power, targetArmor)
                : Mathf.Max(0, diceTotal + Power - targetArmor);
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
