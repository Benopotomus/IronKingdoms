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
            Attack,
            /// <summary>BG3-style hide preview: shows enemy LOS threat grid.</summary>
            Hide
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
