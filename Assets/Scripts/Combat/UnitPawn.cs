using System.Collections.Generic;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Prefab/scene component on a unit pawn root. Holds the authored <see cref="UnitTypeDefinition"/>
    /// and, after spawn, the match-local <see cref="Unit"/> runtime state.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Iron Kingdoms/Combat/Unit Pawn")]
    public sealed class UnitPawn : MonoBehaviour
    {
        [SerializeField] private UnitTypeDefinition unitDefinition;

        [Header("Additional Match Loadout")]
        [Tooltip("Extra abilities granted when this pawn spawns, in addition to the unit type definition.")]
        [SerializeField] private List<CombatAbilityDefinition> additionalAbilities = new();

        [Tooltip("Extra advantages granted when this pawn spawns, in addition to the unit type definition.")]
        [SerializeField] private List<CombatAdvantageDefinition> additionalAdvantages = new();

        public UnitTypeDefinition UnitDefinition => unitDefinition;
        public IReadOnlyList<CombatAbilityDefinition> AdditionalAbilities => additionalAbilities;
        public IReadOnlyList<CombatAdvantageDefinition> AdditionalAdvantages => additionalAdvantages;
        public Unit RuntimeUnit { get; private set; }
        public bool HasRuntimeUnit => RuntimeUnit != null;

        public Unit Bind(UnitTypeDefinition spawnDefinition, bool isPlayerControlled)
        {
            var definition = spawnDefinition != null ? spawnDefinition : unitDefinition;
            if (definition == null)
            {
                Debug.LogError($"UnitPawn on '{name}' has no unit definition assigned.", this);
                return null;
            }

            RuntimeUnit = new Unit(definition, isPlayerControlled, gameObject);
            ApplyAdditionalLoadoutTo(RuntimeUnit);
            return RuntimeUnit;
        }

        /// <summary>
        /// Re-applies serialized prefab loadout onto a runtime unit (e.g. before fog setup at spawn).
        /// </summary>
        public void ApplyAdditionalLoadoutTo(Unit unit)
        {
            SyncAdditionalLoadoutTo(unit, notifyVisionRulesChanged: false);
        }

        /// <summary>
        /// Mirrors inspector loadout onto the live runtime unit and optionally refreshes fog vision rules.
        /// </summary>
        public void SyncAdditionalLoadoutTo(Unit unit, bool notifyVisionRulesChanged = true)
        {
            unit?.SyncAdditionalLoadout(additionalAbilities, additionalAdvantages, notifyVisionRulesChanged);
        }

        public void ClearRuntimeUnit()
        {
            RuntimeUnit = null;
        }

        public static bool TryGetRuntimeUnit(GameObject pawn, out Unit unit)
        {
            unit = null;
            if (pawn == null)
            {
                return false;
            }

            var unitPawn = pawn.GetComponentInParent<UnitPawn>();
            if (unitPawn == null || !unitPawn.HasRuntimeUnit)
            {
                return false;
            }

            unit = unitPawn.RuntimeUnit;
            return true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            additionalAbilities ??= new List<CombatAbilityDefinition>();
            additionalAdvantages ??= new List<CombatAdvantageDefinition>();

            if (unitDefinition?.Stats == null)
            {
                return;
            }

            unitDefinition.Stats.EnsureAdvantageDefaults();
            unitDefinition.Stats.EnsureAbilityDefaults();
            unitDefinition.Stats.EnsureWeaponDefaults();

            if (Application.isPlaying && HasRuntimeUnit)
            {
                SyncAdditionalLoadoutTo(RuntimeUnit, notifyVisionRulesChanged: true);
            }
        }
#endif
    }
}
