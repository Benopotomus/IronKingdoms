using UnityEngine;

namespace IronKingdoms.Combat
{
    [CreateAssetMenu(menuName = "Iron Kingdoms/Combat/Test Scenario", fileName = "TestCombatScenario")]
    public class TestCombatScenarioAsset : ScriptableObject
    {
        [SerializeField] private UnitTypeDefinition attacker;
        [SerializeField] private UnitTypeDefinition defender;
        [SerializeField, Min(0f)] private float startingDistance = 10f;
        [SerializeField, Min(1)] private int maxRounds = 5;
        [SerializeField] private int randomSeed = 42;

        public UnitTypeDefinition Attacker => attacker;
        public UnitTypeDefinition Defender => defender;
        public float StartingDistance => startingDistance;
        public int MaxRounds => maxRounds;
        public int RandomSeed => randomSeed;
    }
}
