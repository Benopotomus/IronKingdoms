using UnityEngine;

namespace IronKingdoms.Combat
{
    public enum CombatSpawnSide
    {
        Player,
        Enemy
    }

    public class CombatSpawnPoint : MonoBehaviour
    {
        [SerializeField] private CombatSpawnSide side;

        public CombatSpawnSide Side => side;
    }
}
