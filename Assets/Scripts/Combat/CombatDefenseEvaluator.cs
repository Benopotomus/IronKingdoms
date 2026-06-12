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
        private const string WallTerrainFeatureId = "Wall";
        private const int WallCoverRaySampleCount = 8;
        // Slight Y lift so rays don't intersect the ground plane.
        private const float WallCoverRayHeight = 0.05f;

        private static readonly Collider[] WallCoverOverlapBuffer = new Collider[16];

        public static int GetEffectiveDefense(
            UnitTypeDefinition defenderDefinition,
            GameObject defenderPawn,
            CombatStats attackerStats,
            WeaponProfile weapon,
            GameObject attackerPawn = null,
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

            var modifiers = CollectActiveDefenseModifiers(defenderDefinition, defenderPawn, attackerPawn);
            if (modifiers.Count == 0)
            {
                return baseDefense + abilityBonus;
            }

            var attackerIgnoresConcealment = AttackerIgnoresConcealment(attackerStats);
            var bestConcealmentOrCoverBonus = 0;
            var otherBonus = 0;
            for (var i = 0; i < modifiers.Count; i++)
            {
                var modifier = modifiers[i].Definition;
                if (modifier == null || !modifier.AppliesToWeapon(weapon, isSprayAttack))
                {
                    continue;
                }

                if (modifier.Category == CombatDefenseModifierCategory.Concealment)
                {
                    // Eyeless Sight ignores concealment and Stealth, but NOT cover (Mk4 rules).
                    if (!attackerIgnoresConcealment)
                    {
                        bestConcealmentOrCoverBonus = Mathf.Max(bestConcealmentOrCoverBonus, modifier.DefenseBonus);
                    }
                }
                else if (modifier.Category == CombatDefenseModifierCategory.Cover)
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
            GameObject pawn,
            GameObject attackerPawn = null)
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

            // Check static scene geometry (non-trigger wall colliders on the FogOccluder layer).
            // Requires an attacker position to evaluate whether the wall partially covers the
            // defender from that direction.
            if (attackerPawn != null)
            {
                var wallCover = TryGetStaticWallCoverModifier(center, radius, attackerPawn.transform.position);
                if (wallCover.HasValue)
                {
                    results.Add(wallCover.Value);
                }
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

        /// <summary>
        /// Checks whether any static (non-trigger) wall collider on the FogOccluder layer is both
        /// within proximity of the defender's base disc AND partially blocks the line drawn from
        /// the attacker toward any edge point on that disc.  Returns the wall's Cover modifier
        /// instance when both conditions are met.
        /// </summary>
        private static CombatDefenseModifierInstance? TryGetStaticWallCoverModifier(
            Vector3 defenderCenter,
            float defenderRadius,
            Vector3 attackerCenter)
        {
            var modifier = GetWallCoverModifier();
            if (modifier == null)
            {
                return null;
            }

            var proximityWorld = CombatScale.InchesToWorldUnits(modifier.ProximityInches);
            var searchRadius = defenderRadius + proximityWorld;
            var layerMask = CombatLayers.LineOfSightBlockerMask;

            int count;
            if (CombatMapSceneProvider.TryGetMapPhysicsScene(out var mapScene))
            {
                count = mapScene.OverlapSphere(defenderCenter, searchRadius, WallCoverOverlapBuffer, layerMask, QueryTriggerInteraction.Ignore);
            }
            else
            {
                count = Physics.OverlapSphereNonAlloc(defenderCenter, searchRadius, WallCoverOverlapBuffer, layerMask, QueryTriggerInteraction.Ignore);
            }

            for (var i = 0; i < count; i++)
            {
                var wallCollider = WallCoverOverlapBuffer[i];
                if (wallCollider == null)
                {
                    continue;
                }

                if (!IsColliderWithinProximityOfDisc(wallCollider, defenderCenter, defenderRadius, proximityWorld))
                {
                    continue;
                }

                if (IsWallPartiallyBlockingView(wallCollider, defenderCenter, defenderRadius, attackerCenter))
                {
                    return new CombatDefenseModifierInstance(modifier, WallTerrainFeatureId);
                }
            }

            return null;
        }

        /// <summary>
        /// Returns true when the closest point on the collider (projected to XZ) is within
        /// <paramref name="discRadius"/> + <paramref name="proximityWorld"/> of <paramref name="discCenter"/>.
        /// </summary>
        private static bool IsColliderWithinProximityOfDisc(
            Collider col,
            Vector3 discCenter,
            float discRadius,
            float proximityWorld)
        {
            var closest = col.ClosestPoint(discCenter);
            var dx = closest.x - discCenter.x;
            var dz = closest.z - discCenter.z;
            var planarDist = Mathf.Sqrt(dx * dx + dz * dz);
            return planarDist <= discRadius + proximityWorld;
        }

        /// <summary>
        /// Casts rays from the attacker toward multiple evenly spaced points on the defender's
        /// base perimeter. Returns true if the wall collider intercepts any of those rays,
        /// meaning it partially covers the defender from the attacker's direction.
        /// </summary>
        private static bool IsWallPartiallyBlockingView(
            Collider wallCollider,
            Vector3 defenderCenter,
            float defenderRadius,
            Vector3 attackerCenter)
        {
            var attackerOrigin = new Vector3(attackerCenter.x, attackerCenter.y + WallCoverRayHeight, attackerCenter.z);

            for (var i = 0; i < WallCoverRaySampleCount; i++)
            {
                var angle = Mathf.PI * 2f * i / WallCoverRaySampleCount;
                var target = new Vector3(
                    defenderCenter.x + Mathf.Cos(angle) * defenderRadius,
                    defenderCenter.y + WallCoverRayHeight,
                    defenderCenter.z + Mathf.Sin(angle) * defenderRadius);

                var dir = target - attackerOrigin;
                var dist = dir.magnitude;
                if (dist < 0.01f)
                {
                    continue;
                }

                // Collider.Raycast tests against this specific collider only, bypassing
                // physics scene boundaries and avoiding broad-phase overhead.
                if (wallCollider.Raycast(new Ray(attackerOrigin, dir / dist), out _, dist))
                {
                    return true;
                }
            }

            return false;
        }

        private static CombatDefenseModifierDefinition GetWallCoverModifier()
        {
            var catalog = CombatDefinitionCatalog.Instance;
            if (catalog == null)
            {
                return null;
            }

            var wallFeature = catalog.FindTerrainFeature(WallTerrainFeatureId);
            return wallFeature?.DefenseModifierWhenInside;
        }
    }
}
