using UnityEngine;

namespace IronKingdoms.Combat
{
    [CreateAssetMenu(menuName = "Iron Kingdoms/Combat/Attack Roll Formula", fileName = "AttackFormula")]
    public class AttackRollFormula : ScriptableObject
    {
        [Tooltip("Number of dice rolled on a standard (unboosted) attack.")]
        public int standardDice = 2;

        [Tooltip("Number of dice rolled on a boosted attack.")]
        public int boostedDice = 3;

        [Tooltip("A roll of 1,1 on the first two dice always misses, regardless of the total.")]
        public bool autoMissOnSnakeEyes = true;

        [Tooltip("A roll of 6,6 on the first two dice always hits, regardless of the total.")]
        public bool autoHitOnBoxcars = true;

        public int GetDiceCount(bool boosted) => boosted ? boostedDice : standardDice;

        /// <summary>
        /// Evaluates whether a completed attack roll hits the target.
        /// </summary>
        /// <param name="die1">The value of the first die (used for auto-miss/hit checks).</param>
        /// <param name="die2">The value of the second die (used for auto-miss/hit checks).</param>
        /// <param name="totalRoll">
        /// The full attack roll: sum of all dice + unit attack stat + weapon modifier + other bonuses.
        /// </param>
        /// <param name="targetDefense">The defender's DEF value.</param>
        public bool EvaluateHit(int die1, int die2, int totalRoll, int targetDefense)
        {
            if (autoMissOnSnakeEyes && die1 == 1 && die2 == 1)
            {
                return false;
            }

            if (autoHitOnBoxcars && die1 == 6 && die2 == 6)
            {
                return true;
            }

            return totalRoll >= targetDefense;
        }
    }
}
