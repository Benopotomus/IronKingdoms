using System.Collections.Generic;
using UnityEngine;

namespace IronKingdoms.Combat
{
    public readonly struct CombatUnitTerrainState
    {
        public CombatUnitTerrainState(
            bool isInRoughTerrain,
            bool isInForest,
            CombatTerrainFeatureDefinition forestFeature,
            bool isInCloud,
            CombatTerrainFeatureDefinition cloudFeature)
        {
            IsInRoughTerrain = isInRoughTerrain;
            IsInForest = isInForest;
            ForestFeature = forestFeature;
            IsInCloud = isInCloud;
            CloudFeature = cloudFeature;
        }

        public bool IsInRoughTerrain { get; }
        public bool IsInForest { get; }
        public CombatTerrainFeatureDefinition ForestFeature { get; }
        public bool IsInCloud { get; }
        public CombatTerrainFeatureDefinition CloudFeature { get; }

        public string ForestStatusLabel => IsInForest ? "Inside" : "Outside";

        public string CloudStatusLabel => IsInCloud ? "Inside" : "Outside";
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
        public const string CloudTerrainFeatureId = "Cloud";

        public static CombatUnitTerrainState ResolveTerrainState(UnitTypeDefinition unitDefinition, GameObject pawn)
        {
            if (unitDefinition?.Stats == null || pawn == null)
            {
                return default;
            }

            var center = pawn.transform.position;
            var radius = GetUnitPlanarRadiusWorld(unitDefinition, pawn);
            var forestFeature = ResolveForestFeature();
            var cloudFeature = ResolveCloudFeature();
            var isInRoughTerrain = false;
            var isInForest = false;
            var isInCloud = false;

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

                if (forestFeature != null && feature == forestFeature && zone.IntersectsDisc(center, radius))
                {
                    isInForest = true;
                }

                if (cloudFeature != null && feature == cloudFeature && zone.IntersectsDisc(center, radius))
                {
                    isInCloud = true;
                }
            }

            return new CombatUnitTerrainState(
                isInRoughTerrain,
                isInForest,
                forestFeature,
                isInCloud,
                cloudFeature);
        }

        public static bool IgnoresForestWhenDeterminingLineOfSight(Unit observer)
        {
            if (observer?.Definition?.Stats == null)
            {
                return false;
            }

            if (observer.IgnoresForestLineOfSightLimits())
            {
                return true;
            }

            observer.Definition.Stats.EnsureAbilityDefaults();
            if (AnyAbilityIgnoresForest(observer.Definition.Stats.abilities))
            {
                return true;
            }

            return AnyAbilityIgnoresForest(observer.RuntimeAbilities);
        }

        public static bool IgnoresForestWhenDeterminingLineOfSight(UnitTypeDefinition observerDefinition, GameObject observerPawn)
        {
            if (observerPawn != null && UnitPawn.TryGetRuntimeUnit(observerPawn, out var unit))
            {
                return IgnoresForestWhenDeterminingLineOfSight(unit);
            }

            if (observerDefinition?.Stats == null)
            {
                return false;
            }

            var stats = observerDefinition.Stats;
            stats.EnsureAdvantageDefaults();
            stats.EnsureAbilityDefaults();
            if (stats.IgnoresForestLineOfSightLimits())
            {
                return true;
            }

            return AnyAbilityIgnoresForest(stats.abilities);
        }

        public static int GetAbilityDefenseBonus(
            Unit defender,
            WeaponProfile weapon)
        {
            if (defender?.Definition?.Stats == null || defender.Pawn == null || weapon == null)
            {
                return 0;
            }

            defender.Definition.Stats.EnsureAbilityDefaults();
            var terrainState = ResolveTerrainState(defender.Definition, defender.Pawn);
            var bonus = 0;

            AccumulateAbilityDefenseBonus(defender.Definition.Stats.abilities, weapon, terrainState, ref bonus);
            AccumulateAbilityDefenseBonus(defender.RuntimeAbilities, weapon, terrainState, ref bonus);
            return bonus;
        }

        public static int GetAbilityDefenseBonus(
            UnitTypeDefinition defenderDefinition,
            GameObject defenderPawn,
            WeaponProfile weapon)
        {
            if (defenderPawn != null && UnitPawn.TryGetRuntimeUnit(defenderPawn, out var defender))
            {
                return GetAbilityDefenseBonus(defender, weapon);
            }

            if (defenderDefinition?.Stats == null || defenderPawn == null || weapon == null)
            {
                return 0;
            }

            defenderDefinition.Stats.EnsureAbilityDefaults();
            var terrainState = ResolveTerrainState(defenderDefinition, defenderPawn);
            var bonus = 0;
            AccumulateAbilityDefenseBonus(defenderDefinition.Stats.abilities, weapon, terrainState, ref bonus);
            return bonus;
        }

        private static void AccumulateAbilityDefenseBonus(
            IReadOnlyList<CombatAbilityDefinition> abilities,
            WeaponProfile weapon,
            CombatUnitTerrainState terrainState,
            ref int bonus)
        {
            for (var i = 0; i < abilities.Count; i++)
            {
                var ability = abilities[i];
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
        }

        public static List<CombatActiveAbilityPassive> DescribeAbilityPassives(Unit unit)
        {
            if (unit?.Definition?.Stats == null)
            {
                return new List<CombatActiveAbilityPassive>();
            }

            unit.Definition.Stats.EnsureAbilityDefaults();
            var terrainState = unit.Pawn != null
                ? ResolveTerrainState(unit.Definition, unit.Pawn)
                : default;
            var results = new List<CombatActiveAbilityPassive>();
            AppendAbilityPassives(unit.Definition.Stats.abilities, terrainState, results);
            AppendAbilityPassives(unit.RuntimeAbilities, terrainState, results);
            return results;
        }

        public static List<CombatActiveAbilityPassive> DescribeAbilityPassives(
            UnitTypeDefinition unitDefinition,
            GameObject pawn)
        {
            if (pawn != null && UnitPawn.TryGetRuntimeUnit(pawn, out var unit)
                && unit.Definition == unitDefinition)
            {
                return DescribeAbilityPassives(unit);
            }

            var results = new List<CombatActiveAbilityPassive>();
            if (unitDefinition?.Stats == null)
            {
                return results;
            }

            unitDefinition.Stats.EnsureAbilityDefaults();
            var terrainState = pawn != null ? ResolveTerrainState(unitDefinition, pawn) : default;
            AppendAbilityPassives(unitDefinition.Stats.abilities, terrainState, results);
            return results;
        }

        private static void AppendAbilityPassives(
            IReadOnlyList<CombatAbilityDefinition> abilities,
            CombatUnitTerrainState terrainState,
            List<CombatActiveAbilityPassive> results)
        {
            for (var i = 0; i < abilities.Count; i++)
            {
                var ability = abilities[i];
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
                        $"+{ability.MeleeDefenseBonusWhileCompletelyInside} DEF vs melee attack rolls while in a forest",
                        inForest));
                }

                if (ability.RangedDefenseBonusWhileCompletelyInside > 0)
                {
                    var inForest = IsAbilityTerrainRequirementMet(ability, terrainState);
                    results.Add(new CombatActiveAbilityPassive(
                        ability,
                        $"+{ability.RangedDefenseBonusWhileCompletelyInside} DEF vs ranged attack rolls while in a forest",
                        inForest));
                }
            }
        }

        private static bool AnyAbilityIgnoresForest(IReadOnlyList<CombatAbilityDefinition> abilities)
        {
            if (abilities == null)
            {
                return false;
            }

            for (var i = 0; i < abilities.Count; i++)
            {
                var ability = abilities[i];
                if (ability != null && ability.IgnoresForestForLineOfSight)
                {
                    return true;
                }
            }

            return false;
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
                return terrainState.IsInForest;
            }

            if (terrainState.ForestFeature != null && requiredFeature == terrainState.ForestFeature)
            {
                return terrainState.IsInForest;
            }

            return terrainState.IsInForest;
        }

        private static CombatTerrainFeatureDefinition ResolveForestFeature()
        {
            return ResolveTerrainFeature(ForestTerrainFeatureId);
        }

        private static CombatTerrainFeatureDefinition ResolveCloudFeature()
        {
            return ResolveTerrainFeature(CloudTerrainFeatureId);
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
