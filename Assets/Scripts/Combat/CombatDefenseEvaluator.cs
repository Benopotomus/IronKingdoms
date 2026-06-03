using System.Collections.Generic;
using UnityEngine;

namespace IronKingdoms.Combat
{
    public readonly struct CombatDefenseModifierInstance
    {
        public CombatDefenseModifierInstance(CombatDefenseModifierDefinition definition, string sourceLabel)
        {
            Definition = definition;
            SourceLabel = sourceLabel;
        }

        public CombatDefenseModifierDefinition Definition { get; }
        public string SourceLabel { get; }
    }

    public static class CombatDefenseEvaluator
    {
        public static int GetEffectiveDefense(
            UnitTypeDefinition defenderDefinition,
            GameObject defenderPawn,
            CombatStats attackerStats,
            WeaponProfile weapon,
            bool isSprayAttack = false)
        {
            if (defenderDefinition?.Stats == null)
            {
                return 0;
            }

            var stats = defenderDefinition.Stats;
            var baseDefense = stats.defense;
            var abilityBonus = CombatAbilitySolver.GetAbilityDefenseBonus(defenderDefinition, defenderPawn, weapon);

            if (!CanReceiveTerrainDefenseBonuses(stats.modelSize))
            {
                return baseDefense + abilityBonus;
            }

            if (AttackerIgnoresConcealment(attackerStats))
            {
                return baseDefense + abilityBonus;
            }

            var modifiers = CollectActiveDefenseModifiers(defenderDefinition, defenderPawn);
            if (modifiers.Count == 0)
            {
                return baseDefense + abilityBonus;
            }

            var bestConcealmentOrCoverBonus = 0;
            var otherBonus = 0;
            for (var i = 0; i < modifiers.Count; i++)
            {
                var modifier = modifiers[i].Definition;
                if (modifier == null || !modifier.AppliesToWeapon(weapon, isSprayAttack))
                {
                    continue;
                }

                if (modifier.Category == CombatDefenseModifierCategory.Concealment
                    || modifier.Category == CombatDefenseModifierCategory.Cover)
                {
                    bestConcealmentOrCoverBonus = Mathf.Max(bestConcealmentOrCoverBonus, modifier.DefenseBonus);
                }
                else
                {
                    otherBonus += modifier.DefenseBonus;
                }
            }

            return baseDefense + bestConcealmentOrCoverBonus + otherBonus + abilityBonus;
        }

        public static int GetAbilityDefenseBonus(
            UnitTypeDefinition defenderDefinition,
            GameObject defenderPawn,
            WeaponProfile weapon)
        {
            return CombatAbilitySolver.GetAbilityDefenseBonus(defenderDefinition, defenderPawn, weapon);
        }

        public static List<CombatDefenseModifierInstance> CollectActiveDefenseModifiers(
            UnitTypeDefinition unitDefinition,
            GameObject pawn)
        {
            var results = new List<CombatDefenseModifierInstance>();
            if (unitDefinition?.Stats == null || pawn == null)
            {
                return results;
            }

            if (!CanReceiveTerrainDefenseBonuses(unitDefinition.Stats.modelSize))
            {
                return results;
            }

            var center = pawn.transform.position;
            var radius = GetUnitPlanarRadiusWorld(unitDefinition, pawn);
            var activeZones = CombatZone.ActiveZones;
            for (var i = 0; i < activeZones.Count; i++)
            {
                var zone = activeZones[i];
                var feature = zone?.TerrainFeature;
                var modifier = feature?.DefenseModifierWhenInside;
                if (zone == null || feature == null || modifier == null)
                {
                    continue;
                }

                if (!ZoneGrantsDefenseModifier(zone, modifier, center, radius))
                {
                    continue;
                }

                results.Add(new CombatDefenseModifierInstance(modifier, feature.DisplayName));
            }

            return results;
        }

        public static bool IsUnitCompletelyInsideTerrain(
            UnitTypeDefinition unitDefinition,
            GameObject pawn,
            CombatTerrainFeatureDefinition feature)
        {
            if (unitDefinition?.Stats == null || pawn == null || feature == null)
            {
                return false;
            }

            var center = pawn.transform.position;
            var radius = GetUnitPlanarRadiusWorld(unitDefinition, pawn);
            var activeZones = CombatZone.ActiveZones;
            for (var i = 0; i < activeZones.Count; i++)
            {
                var zone = activeZones[i];
                if (zone != null && zone.TerrainFeature == feature && zone.ContainsUnitCompletely(center, radius))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ZoneGrantsDefenseModifier(
            CombatZone zone,
            CombatDefenseModifierDefinition modifier,
            Vector3 unitCenter,
            float unitRadius)
        {
            return modifier.Application switch
            {
                CombatDefenseModifierApplication.UnitCompletelyInsideTerrainZone =>
                    zone.ContainsUnitCompletely(unitCenter, unitRadius),
                CombatDefenseModifierApplication.UnitWithinOneInchOfFeature =>
                    zone.IntersectsDisc(unitCenter, unitRadius + CombatScale.InchesToWorldUnits(modifier.ProximityInches)),
                _ => zone.ContainsPoint(unitCenter)
            };
        }

        private static bool CanReceiveTerrainDefenseBonuses(ModelSize modelSize)
        {
            return !modelSize.IsExtraLargeOrHuge();
        }

        private static bool AttackerIgnoresConcealment(CombatStats attackerStats)
        {
            return attackerStats != null && attackerStats.IgnoresConcealmentAndStealth();
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
