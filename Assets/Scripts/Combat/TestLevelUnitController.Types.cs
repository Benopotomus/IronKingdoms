using UnityEngine;

namespace IronKingdoms.Combat
{
    public partial class TestLevelUnitController
    {
        private enum TurnSide
        {
            Player,
            Enemy
        }

        private enum UnitActionMode
        {
            None,
            Move,
            Attack
        }

        private struct FloatingDamageEntry
        {
            public Vector3 WorldPosition;
            public string Text;
            public float Age;
            public Color Color;
        }
    }
}
