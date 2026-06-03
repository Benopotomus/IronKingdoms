using System.Collections.Generic;
using UnityEngine;

namespace IronKingdoms.Combat
{
    public readonly struct CombatUnitTerrainState
    {
        public CombatUnitTerrainState(
            bool isInRoughTerrain,
            bool isCompletelyInForest,
            bool isPartiallyInForest,
            CombatTerrainFeatureDefinition forestFeature)
        {
            IsInRoughTerrain = isInRoughTerrain;
            IsCompletelyInForest = isCompletelyInForest;
            IsPartiallyInForest = isPartiallyInForest;
            ForestFeature = forestFeature;
        }

        public bool IsInRoughTerrain { get; }
        public bool IsCompletelyInForest { get; }
        public bool IsPartiallyInForest { get; }
        public CombatTerrainFeatureDefinition ForestFeature { get; }

        public string ForestStatusLabel
        {
            get
            {
                if (IsCompletelyInForest)
                {
                    return "Completely inside";
                }

                if (IsPartiallyInForest)
                {
                    return "Partially inside";
                }

                return "Open";
            }
        }
    }

    public readonly struct CombatActiveAbilityPassive
    {
        public CombatActiveAbilityPassive(CombatAbilityDefinition ability, string effectLabel, bool isActive)
        {
            Ability = ability;
            EffectLabel = effectLabel;
            IsActive = isActive;
        }

        public CombatAbilityDefinition Ability { get; }
        public string EffectLabel { get; }
        public bool IsActive { get; }
    }

    /// <summary>
    /// Resolves model abilities against the current board state.
    /// </summary>
    public static class CombatAbilitySolver
    {
        public const string ForestTerrainFeatureId = "Forest";

        public static CombatUnitTerrainState ResolveTerrainState(UnitTypeDefinition unitDefinition, GameObject pawn)
        {
            if (unitDefinition?.Stats == null || pawn == null)
            {
                return default;
            }

            var center = pawn.transform.position;
            var radius = GetUnitPlanarRadiusWorld(unitDefinition, pawn);
            var forestFeature = ResolveForestFeature();
            var isInRoughTerrain = false;
            var isCompletelyInForest = false;
            var isPartiallyInForest = false;

            var activeZones = CombatZone.ActiveZones;
            for (var i = 0; i < activeZones.Count; i++)
            {
                var zone = activeZones[i];
                var feature = zone?.TerrainFeature;
                if (zone == null || feature == null)
                {
                    continue;
                }

                if (feature.IsRoughTerrain && zone.IntersectsDisc(center, radius))
                {
                    isInRoughTerrain = true;
                }

                if (forestFeature != null && feature == forestFeature)
                {
                    if (zone.ContainsUnitCompletely(center, radius))
                    {
                        isCompletelyInForest = true;
                    }
                    else if (zone.IntersectsDisc(center, radius))
                    {
                        isPartiallyInForest = true;
                    }
                }
            }

            return new CombatUnitTerrainState(isInRoughTerrain, isCompletelyInForest, isPartiallyInForest, forestFeature);
        }

        public static bool IgnoresForestWhenDeterminingLineOfSight(UnitTypeDefinition observerDefinition, GameObject observerPawn)
        {
            if (observerDefinition?.Stats == null)
            {
                return false;
            }

            var stats = observerDefinition.Stats;
            stats.EnsureAbilityDefaults();
            if (stats.IgnoresForestLineOfSightLimits())
            {
                return true;
            }

            for (var i = 0; i < stats.abilities.Count; i++)
            {
                var ability = stats.abilities[i];
                if (ability != null && ability.IgnoresForestForLineOfSight)
                {
                    return true;
                }
            }

            return false;
        }

        public static int GetAbilityDefenseBonus(
            UnitTypeDefinition defenderDefinition,
            GameObject defenderPawn,
            WeaponProfile weapon)
        {
            if (defenderDefinition?.Stats == null || defenderPawn == null || weapon == null)
            {
                return 0;
            }

            defenderDefinition.Stats.EnsureAbilityDefaults();
            var terrainState = ResolveTerrainState(defenderDefinition, defenderPawn);
            var bonus = 0;

            for (var i = 0; i < defenderDefinition.Stats.abilities.Count; i++)
            {
                var ability = defenderDefinition.Stats.abilities[i];
                if (ability == null || !IsAbilityTerrainRequirementMet(ability, terrainState))
                {
                    continue;
                }

                if (weapon.AttackType == WeaponAttackType.Melee)
                {
                    bonus += ability.MeleeDefenseBonusWhileCompletelyInside;
                }
                else
                {
                    bonus += ability.RangedDefenseBonusWhileCompletelyInside;
                }
            }

            return bonus;
        }

        public static List<CombatActiveAbilityPassive> DescribeAbilityPassives(
            UnitTypeDefinition unitDefinition,
            GameObject pawn)
        {
            var results = new List<CombatActiveAbilityPassive>();
            if (unitDefinition?.Stats == null)
            {
                return results;
            }

            unitDefinition.Stats.EnsureAbilityDefaults();
            var terrainState = pawn != null ? ResolveTerrainState(unitDefinition, pawn) : default;

            for (var i = 0; i < unitDefinition.Stats.abilities.Count; i++)
            {
                var ability = unitDefinition.Stats.abilities[i];
                if (ability == null)
                {
                    continue;
                }

                if (ability.IgnoresForestForLineOfSight)
                {
                    results.Add(new CombatActiveAbilityPassive(
                        ability,
                        "Ignores forests when determining line of sight",
                        true));
                }

                if (ability.MeleeDefenseBonusWhileCompletelyInside > 0)
                {
                    var inForest = IsAbilityTerrainRequirementMet(ability, terrainState);
                    results.Add(new CombatActiveAbilityPassive(
                        ability,
                        $"+{ability.MeleeDefenseBonusWhileCompletelyInside} DEF vs melee attack rolls while completely in a forest",
                        inForest));
                }

                if (ability.RangedDefenseBonusWhileCompletelyInside > 0)
                {
                    var inForest = IsAbilityTerrainRequirementMet(ability, terrainState);
                    results.Add(new CombatActiveAbilityPassive(
                        ability,
                        $"+{ability.RangedDefenseBonusWhileCompletelyInside} DEF vs ranged attack rolls while completely in a forest",
                        inForest));
                }
            }

            return results;
        }

        private static bool IsAbilityTerrainRequirementMet(
            CombatAbilityDefinition ability,
            CombatUnitTerrainState terrainState)
        {
            if (ability == null)
            {
                return false;
            }

            if (ability.MeleeDefenseBonusWhileCompletelyInside <= 0
                && ability.RangedDefenseBonusWhileCompletelyInside <= 0)
            {
                return true;
            }

            var requiredFeature = ResolveTerrainFeature(ability.RequiredTerrainFeatureId);
            if (requiredFeature == null)
            {
                return terrainState.IsCompletelyInForest;
            }

            if (terrainState.ForestFeature != null && requiredFeature == terrainState.ForestFeature)
            {
                return terrainState.IsCompletelyInForest;
            }

            return terrainState.IsCompletelyInForest;
        }

        private static CombatTerrainFeatureDefinition ResolveForestFeature()
        {
            return ResolveTerrainFeature(ForestTerrainFeatureId);
        }

        private static CombatTerrainFeatureDefinition ResolveTerrainFeature(string featureId)
        {
            var catalog = CombatDefinitionCatalog.Instance;
            return catalog != null ? catalog.FindTerrainFeature(featureId) : null;
        }

        private static float GetUnitPlanarRadiusWorld(UnitTypeDefinition unitDefinition, GameObject pawn)
        {
            var collider = pawn.GetComponent<CapsuleCollider>();
            if (collider != null)
            {
                return collider.radius;
            }

            return unitDefinition.Stats.modelSize.BaseDiameterWorldUnits() * 0.5f;
        }
    }
}
