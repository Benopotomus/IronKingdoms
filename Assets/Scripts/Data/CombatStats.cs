using System;

namespace IronKingdoms.Combat
{
    [Serializable]
    public class CombatStats
    {
        public float speed = 5f;
        public int meleeAttack = 5;
        public int rangedAttack = 4;
        public int defense = 12;
        public int armor = 14;
        public int health = 10;
        public int startingResource = 0;
        public int weaponPower = 5;
        public float weaponRange = 1.5f;
    }
}
