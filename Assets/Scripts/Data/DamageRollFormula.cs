using UnityEngine;

namespace IronKingdoms.Combat
{
    [CreateAssetMenu(menuName = "Iron Kingdoms/Combat/Damage Roll Formula", fileName = "DamageFormula")]
    public class DamageRollFormula : ScriptableObject
    {
        [Tooltip("Number of dice rolled for a standard (unboosted) damage roll.")]
        public int standardDice = 2;

        [Tooltip("Number of dice rolled for a boosted or charge-boosted damage roll.")]
        public int boostedDice = 3;

        public int GetDiceCount(bool boosted) => boosted ? boostedDice : standardDice;

        /// <summary>
        /// Calculates damage dealt after reducing by armor. Returns 0 when armor meets or exceeds the roll.
        /// </summary>
        /// <param name="diceTotal">The sum of all damage dice rolled.</param>
        /// <param name="weaponPower">The weapon's POW value.</param>
        /// <param name="targetArmor">The defender's ARM value.</param>
        public int EvaluateDamage(int diceTotal, int weaponPower, int targetArmor)
        {
            return Mathf.Max(0, diceTotal + weaponPower - targetArmor);
        }
    }
}
