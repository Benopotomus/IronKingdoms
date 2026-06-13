using System.Collections.Generic;
using Pathfinding;
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

        private enum MovementStepOption
        {
            None,
            Advance,
            Run,
            Charge
        }

        private struct FloatingDamageEntry
        {
            public Vector3 WorldPosition;
            public string Text;
            public float Age;
            public Color Color;
        }

        /// <summary>
        /// Runtime combat state for a spawned model. Definitions remain immutable data assets;
        /// this object tracks the match-local pawn, health, turn flags, visibility, and path state.
        /// </summary>
        private sealed class RuntimeUnit
        {
            public RuntimeUnit(UnitTypeDefinition definition, bool isPlayerControlled, GameObject pawn)
            {
                Definition = definition;
                IsPlayerControlled = isPlayerControlled;
                Pawn = pawn;
                NavmeshCut = pawn != null ? pawn.GetComponent<NavmeshCut>() : null;
                Renderers = pawn != null ? pawn.GetComponentsInChildren<Renderer>(true) : null;
                Health = definition.Stats.health;
                definition.Stats.EnsureAdvantageDefaults();
                definition.Stats.EnsureAbilityDefaults();
                definition.Stats.EnsureWeaponDefaults();
                Weapons = definition.Stats.weapons == null || definition.Stats.weapons.Length == 0
                    ? new[] { WeaponProfile.CreateDefault() }
                    : definition.Stats.weapons;
            }

            public UnitTypeDefinition Definition { get; }
            public bool IsPlayerControlled { get; }
            public GameObject Pawn { get; }
            public NavmeshCut NavmeshCut { get; }
            public Renderer[] Renderers { get; }
            public WeaponProfile[] Weapons { get; }
            public int Health { get; set; }
            public float RemainingMovementThisTurn { get; set; }
            public bool HasActedThisTurn { get; set; }
            public bool HasRunActionThisTurn { get; set; }
            public bool HasAdvancedThisTurn { get; set; }
            public bool HasChargedThisTurn { get; set; }
            public bool IsAimingThisTurn { get; set; }
            public bool IsVisibleToPlayer { get; set; } = true;
            public Vector3? MoveTarget { get; set; }
            public bool IsAlive => Health > 0;

            /// <summary>World-space waypoints for the current A* path, or null when not path-following.</summary>
            public List<Vector3> PathWaypoints { get; set; }

            /// <summary>Index of the waypoint the unit is currently moving toward.</summary>
            public int PathWaypointIndex { get; set; }

            /// <summary>Advance, run, or charge step that issued the current move.</summary>
            public MovementStepOption ActiveMovementStep { get; set; }
        }
    }
}
