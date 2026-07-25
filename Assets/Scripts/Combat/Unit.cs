using System;
using System.Collections.Generic;
using Pathfinding;
using UnityEngine;

namespace IronKingdoms.Combat
{
    public enum MovementStepOption
    {
        None,
        Advance,
        Run,
        Charge
    }

    /// <summary>
    /// Runtime combat state for a spawned model. Definitions remain immutable data assets;
    /// this object tracks the match-local pawn, health, turn flags, visibility, and path state.
    /// </summary>
    public sealed class Unit
    {
        private const float DefaultCollisionRadius = 0.6f;
        private const float TargetRingScaleFactor = 0.6f;
        private const float RadiusToDiameterMultiplier = 2f;
        private const float MovementBudgetEpsilon = 0.001f;
        private const float TerrainCostSampleStepInches = 0.25f;
        private const float NavmeshContainmentTolerance = 0.02f;
        private const float PositionArrivalTolerance = 0.05f;
        private const int AimToHitBonus = 2;

        public Unit(UnitTypeDefinition definition, bool isPlayerControlled, GameObject pawn)
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
        /// <summary>
        /// Prototype hide state used to surface the BG3-style enemy LOS threat grid overlay.
        /// </summary>
        public bool IsHiding { get; set; }
        public Vector3? MoveTarget { get; set; }
        public bool IsAlive => Health > 0;

        private readonly List<CombatAbilityDefinition> runtimeAbilities = new();
        private readonly List<CombatAdvantageDefinition> runtimeAdvantages = new();
        private readonly HashSet<CombatAbilityDefinition> pawnSourcedAbilities = new();
        private readonly HashSet<CombatAdvantageDefinition> pawnSourcedAdvantages = new();

        /// <summary>Fired when runtime abilities or vision-affecting advantages change.</summary>
        public event Action<Unit> VisionRulesChanged;

        /// <summary>Abilities granted during this match, separate from the unit type asset list.</summary>
        public IReadOnlyList<CombatAbilityDefinition> RuntimeAbilities => runtimeAbilities;

        /// <summary>Advantages granted during this match, separate from the unit type asset list.</summary>
        public IReadOnlyList<CombatAdvantageDefinition> RuntimeAdvantages => runtimeAdvantages;

        public bool HasAdvantage(CombatAdvantageDefinition advantage)
        {
            if (advantage == null)
            {
                return false;
            }

            Definition.Stats.EnsureAdvantageDefaults();
            return Definition.Stats.advantages.Contains(advantage) || runtimeAdvantages.Contains(advantage);
        }

        public bool GrantAdvantage(CombatAdvantageDefinition advantage)
        {
            if (!TryAddRuntimeAdvantage(advantage))
            {
                return false;
            }

            NotifyVisionRulesChanged();
            return true;
        }

        public bool GrantAdvantage(string advantageId)
        {
            var catalog = CombatDefinitionCatalog.Instance;
            var advantage = catalog != null ? catalog.FindAdvantage(advantageId) : null;
            return advantage != null && GrantAdvantage(advantage);
        }

        public bool GrantAdvantage(UnitAdvantage legacyAdvantage)
        {
            var catalog = CombatDefinitionCatalog.Instance;
            var advantage = catalog != null ? catalog.FindAdvantage(legacyAdvantage) : null;
            return advantage != null && GrantAdvantage(advantage);
        }

        public bool RevokeRuntimeAdvantage(CombatAdvantageDefinition advantage)
        {
            if (advantage == null)
            {
                return false;
            }

            pawnSourcedAdvantages.Remove(advantage);
            if (!runtimeAdvantages.Remove(advantage))
            {
                return false;
            }

            NotifyVisionRulesChanged();
            return true;
        }

        public bool IgnoresForestLineOfSightLimits()
        {
            return HasAdvantageFlag(advantage => advantage.IgnoresForestLineOfSightLimits);
        }

        public bool IgnoresConcealmentAndStealth()
        {
            return HasAdvantageFlag(advantage => advantage.IgnoresConcealmentAndStealth);
        }

        public bool TreatsRoughTerrainAsOpenWhileAdvancing()
        {
            Definition.Stats.EnsureAdvantageDefaults();
            if (HasAdvantageFlag(advantage => advantage.TreatsRoughTerrainAsOpenWhileAdvancing))
            {
                return true;
            }

            return Definition.Stats.HasAdvantage(UnitAdvantage.Pathfinder);
        }

        private bool HasAdvantageFlag(Func<CombatAdvantageDefinition, bool> predicate)
        {
            Definition.Stats.EnsureAdvantageDefaults();
            if (AnyAdvantageMatches(Definition.Stats.advantages, predicate))
            {
                return true;
            }

            return AnyAdvantageMatches(runtimeAdvantages, predicate);
        }

        private static bool AnyAdvantageMatches(
            IReadOnlyList<CombatAdvantageDefinition> advantages,
            Func<CombatAdvantageDefinition, bool> predicate)
        {
            for (var i = 0; i < advantages.Count; i++)
            {
                var advantage = advantages[i];
                if (advantage != null && predicate(advantage))
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasAbility(CombatAbilityDefinition ability)
        {
            if (ability == null)
            {
                return false;
            }

            Definition.Stats.EnsureAbilityDefaults();
            return Definition.Stats.abilities.Contains(ability) || runtimeAbilities.Contains(ability);
        }

        public bool GrantAbility(CombatAbilityDefinition ability)
        {
            if (!TryAddRuntimeAbility(ability))
            {
                return false;
            }

            NotifyVisionRulesChanged();
            return true;
        }

        /// <summary>
        /// Applies prefab or scripted extras on top of the unit type definition's authored loadout.
        /// Only adds entries; use <see cref="SyncAdditionalLoadout"/> when the pawn list can shrink.
        /// </summary>
        public void ApplyAdditionalLoadout(
            IReadOnlyList<CombatAbilityDefinition> abilities,
            IReadOnlyList<CombatAdvantageDefinition> advantages,
            bool notifyVisionRulesChanged = true)
        {
            SyncAdditionalLoadout(abilities, advantages, notifyVisionRulesChanged);
        }

        /// <summary>
        /// Mirrors the pawn's additional ability/advantage lists onto runtime state, removing
        /// pawn-sourced entries that were dropped while preserving match-granted extras.
        /// </summary>
        public void SyncAdditionalLoadout(
            IReadOnlyList<CombatAbilityDefinition> abilities,
            IReadOnlyList<CombatAdvantageDefinition> advantages,
            bool notifyVisionRulesChanged = true)
        {
            var ignoresForestBefore = CombatAbilitySolver.IgnoresForestWhenDeterminingLineOfSight(this);
            var ignoresConcealmentBefore = IgnoresConcealmentAndStealth();

            var changed = SyncPawnSourcedAbilities(abilities);
            changed |= SyncPawnSourcedAdvantages(advantages);

            var ignoresForestAfter = CombatAbilitySolver.IgnoresForestWhenDeterminingLineOfSight(this);
            var ignoresConcealmentAfter = IgnoresConcealmentAndStealth();
            var visionChanged = ignoresForestBefore != ignoresForestAfter
                || ignoresConcealmentBefore != ignoresConcealmentAfter;

            if ((changed || visionChanged) && notifyVisionRulesChanged)
            {
                NotifyVisionRulesChanged();
            }
        }

        private bool SyncPawnSourcedAbilities(IReadOnlyList<CombatAbilityDefinition> desiredAbilities)
        {
            var changed = false;
            var desired = CollectNonNullAbilitySet(desiredAbilities);

            if (pawnSourcedAbilities.Count > 0)
            {
                var toRemove = new List<CombatAbilityDefinition>();
                foreach (var ability in pawnSourcedAbilities)
                {
                    if (!desired.Contains(ability))
                    {
                        toRemove.Add(ability);
                    }
                }

                for (var i = 0; i < toRemove.Count; i++)
                {
                    var ability = toRemove[i];
                    pawnSourcedAbilities.Remove(ability);
                    if (runtimeAbilities.Remove(ability))
                    {
                        changed = true;
                    }
                }
            }

            Definition.Stats.EnsureAbilityDefaults();
            foreach (var ability in desired)
            {
                if (!pawnSourcedAbilities.Add(ability))
                {
                    continue;
                }

                if (Definition.Stats.abilities.Contains(ability) || runtimeAbilities.Contains(ability))
                {
                    continue;
                }

                runtimeAbilities.Add(ability);
                changed = true;
            }

            return changed;
        }

        private bool SyncPawnSourcedAdvantages(IReadOnlyList<CombatAdvantageDefinition> desiredAdvantages)
        {
            var changed = false;
            var desired = CollectNonNullAdvantageSet(desiredAdvantages);

            if (pawnSourcedAdvantages.Count > 0)
            {
                var toRemove = new List<CombatAdvantageDefinition>();
                foreach (var advantage in pawnSourcedAdvantages)
                {
                    if (!desired.Contains(advantage))
                    {
                        toRemove.Add(advantage);
                    }
                }

                for (var i = 0; i < toRemove.Count; i++)
                {
                    var advantage = toRemove[i];
                    pawnSourcedAdvantages.Remove(advantage);
                    if (runtimeAdvantages.Remove(advantage))
                    {
                        changed = true;
                    }
                }
            }

            Definition.Stats.EnsureAdvantageDefaults();
            foreach (var advantage in desired)
            {
                if (!pawnSourcedAdvantages.Add(advantage))
                {
                    continue;
                }

                if (Definition.Stats.advantages.Contains(advantage) || runtimeAdvantages.Contains(advantage))
                {
                    continue;
                }

                runtimeAdvantages.Add(advantage);
                changed = true;
            }

            return changed;
        }

        private static HashSet<CombatAbilityDefinition> CollectNonNullAbilitySet(
            IReadOnlyList<CombatAbilityDefinition> abilities)
        {
            var set = new HashSet<CombatAbilityDefinition>();
            if (abilities == null)
            {
                return set;
            }

            for (var i = 0; i < abilities.Count; i++)
            {
                var ability = abilities[i];
                if (ability != null)
                {
                    set.Add(ability);
                }
            }

            return set;
        }

        private static HashSet<CombatAdvantageDefinition> CollectNonNullAdvantageSet(
            IReadOnlyList<CombatAdvantageDefinition> advantages)
        {
            var set = new HashSet<CombatAdvantageDefinition>();
            if (advantages == null)
            {
                return set;
            }

            for (var i = 0; i < advantages.Count; i++)
            {
                var advantage = advantages[i];
                if (advantage != null)
                {
                    set.Add(advantage);
                }
            }

            return set;
        }

        private bool TryAddRuntimeAbility(CombatAbilityDefinition ability)
        {
            if (ability == null || HasAbility(ability))
            {
                return false;
            }

            runtimeAbilities.Add(ability);
            return true;
        }

        private bool TryAddRuntimeAdvantage(CombatAdvantageDefinition advantage)
        {
            if (advantage == null || HasAdvantage(advantage))
            {
                return false;
            }

            runtimeAdvantages.Add(advantage);
            return true;
        }

        public bool GrantAbility(string abilityId)
        {
            var catalog = CombatDefinitionCatalog.Instance;
            var ability = catalog != null ? catalog.FindAbility(abilityId) : null;
            return ability != null && GrantAbility(ability);
        }

        public bool RevokeRuntimeAbility(CombatAbilityDefinition ability)
        {
            if (ability == null)
            {
                return false;
            }

            pawnSourcedAbilities.Remove(ability);
            if (!runtimeAbilities.Remove(ability))
            {
                return false;
            }

            NotifyVisionRulesChanged();
            return true;
        }

        public void RefreshFogRevealerConfiguration()
        {
            if (Pawn == null)
            {
                return;
            }

            var revealer = Pawn.GetComponentInChildren<CombatFogOfWarRevealer3D>(true);
            if (revealer == null)
            {
                return;
            }

            revealer.ApplyVisionRulesFromUnit(this);
        }

        private void NotifyVisionRulesChanged()
        {
            RefreshFogRevealerConfiguration();
            VisionRulesChanged?.Invoke(this);
        }

        /// <summary>World-space waypoints for the current A* path, or null when not path-following.</summary>
        public List<Vector3> PathWaypoints { get; set; }

        /// <summary>Index of the waypoint the unit is currently moving toward.</summary>
        public int PathWaypointIndex { get; set; }

        /// <summary>Advance, run, or charge step that issued the current move.</summary>
        public MovementStepOption ActiveMovementStep { get; set; }

        public void ResetMovementForTurn()
        {
            RemainingMovementThisTurn = Definition.Stats.speed;
            HasActedThisTurn = false;
            HasRunActionThisTurn = false;
            HasAdvancedThisTurn = false;
            HasChargedThisTurn = false;
            IsAimingThisTurn = false;
            IsHiding = false;
            MoveTarget = null;
            PathWaypoints = null;
            ActiveMovementStep = MovementStepOption.None;
        }

        public void ApplyVisibility(bool isVisible)
        {
            if (IsVisibleToPlayer == isVisible)
            {
                return;
            }

            IsVisibleToPlayer = isVisible;
            if (Renderers == null)
            {
                return;
            }

            for (var i = 0; i < Renderers.Length; i++)
            {
                if (Renderers[i] != null)
                {
                    Renderers[i].enabled = isVisible;
                }
            }
        }

        public Vector3 GetFeetPosition()
        {
            return Pawn != null ? Pawn.transform.position : Vector3.zero;
        }

        public Vector3 GetCenterPosition()
        {
            if (Pawn == null)
            {
                return Vector3.zero;
            }

            var bodyHeight = Definition.Stats.modelSize.GetPawnScale().y;
            return Pawn.transform.position + Vector3.up * bodyHeight;
        }

        public float GetCollisionRadius()
        {
            if (Pawn == null)
            {
                return DefaultCollisionRadius;
            }

            var col = Pawn.GetComponent<CapsuleCollider>();
            return col != null ? Mathf.Max(0.1f, col.radius) : DefaultCollisionRadius;
        }

        public float GetRadiusInches()
        {
            return CombatScale.WorldUnitsToInches(GetCollisionRadius());
        }

        public float GetMovePreviewDiameter(float visualizerLineWidth)
        {
            if (Pawn != null)
            {
                var col = Pawn.GetComponent<CapsuleCollider>();
                if (col != null)
                {
                    return Mathf.Max(visualizerLineWidth, col.radius * RadiusToDiameterMultiplier);
                }
            }

            return Mathf.Max(visualizerLineWidth, Definition.Stats.modelSize.GetPawnScale().x);
        }

        public float GetTargetRingRadius()
        {
            if (Pawn == null)
            {
                return DefaultCollisionRadius;
            }

            var col = Pawn.GetComponent<CapsuleCollider>();
            if (col == null)
            {
                return DefaultCollisionRadius;
            }

            var scaledRadius = col.radius * RadiusToDiameterMultiplier * TargetRingScaleFactor;
            return Mathf.Max(DefaultCollisionRadius, scaledRadius);
        }

        public CombatLineOfSightVolume GetLineOfSightVolume()
        {
            return CombatLineOfSight.CreateVolume(GetFeetPosition(), Definition.Stats.modelSize);
        }

        public bool IsInRoughTerrain()
        {
            if (Pawn == null)
            {
                return false;
            }

            return CombatAbilitySolver.ResolveTerrainState(Definition, Pawn).IsInRoughTerrain;
        }

        public bool IsWithinVisibilityRangeOf(Unit target)
        {
            if (Pawn == null || target?.Pawn == null)
            {
                return false;
            }

            var distanceInches = CombatLineOfSight.GetPlanarEdgeToEdgeDistanceInches(
                GetLineOfSightVolume(),
                target.GetLineOfSightVolume());
            return distanceInches <= Definition.Stats.visibilityRange + CombatScale.WorldUnitsToInches(PositionArrivalTolerance);
        }

        public float GetPlanarDistanceTo(Unit other)
        {
            if (Pawn == null || other?.Pawn == null)
            {
                return float.MaxValue;
            }

            return GetPlanarDistance(Pawn.transform.position, other.Pawn.transform.position);
        }

        public float GetLongestWeaponRange()
        {
            if (Weapons == null || Weapons.Length == 0)
            {
                return 1.5f;
            }

            var range = Weapons[0].Range;
            for (var i = 1; i < Weapons.Length; i++)
            {
                range = Mathf.Max(range, Weapons[i].Range);
            }

            return range;
        }

        public WeaponProfile GetBestWeaponForDistance(Unit target, float distance)
        {
            if (Weapons == null || Weapons.Length == 0)
            {
                return null;
            }

            var combinedRadii = GetCombinedRadiiInches(this, target);
            WeaponProfile best = null;
            for (var i = 0; i < Weapons.Length; i++)
            {
                var weapon = Weapons[i];
                if (distance > weapon.Range + combinedRadii)
                {
                    continue;
                }

                if (best == null || weapon.Power > best.Power)
                {
                    best = weapon;
                }
            }

            return best;
        }

        public bool IsTargetInRange(Unit target, WeaponProfile weapon)
        {
            if (Pawn == null || target?.Pawn == null || weapon == null)
            {
                return false;
            }

            var distance = GetPlanarDistance(Pawn.transform.position, target.Pawn.transform.position);
            return distance <= weapon.Range + GetCombinedRadiiInches(this, target) + CombatScale.WorldUnitsToInches(PositionArrivalTolerance);
        }

        public int GetAttackStatForWeapon(WeaponProfile weapon)
        {
            var baseAttack = weapon.AttackType == WeaponAttackType.Melee
                ? Definition.Stats.meleeAttack
                : Definition.Stats.rangedAttack;
            return baseAttack + weapon.GetAttackModifier();
        }

        public int GetToHitModifier()
        {
            return IsAimingThisTurn ? AimToHitBonus : 0;
        }

        public int GetEffectiveDefense(Unit attacker, WeaponProfile weapon)
        {
            return CombatDefenseEvaluator.GetEffectiveDefense(
                Definition,
                Pawn,
                attacker,
                weapon);
        }

        public MovementStepOption ResolveRoughTerrainMovementStep(Unit playerSelectedUnit, MovementStepOption playerSelectedStep)
        {
            if (ActiveMovementStep != MovementStepOption.None)
            {
                return ActiveMovementStep;
            }

            return ReferenceEquals(this, playerSelectedUnit) ? playerSelectedStep : MovementStepOption.None;
        }

        public bool IgnoresRoughTerrainMovementCost(Unit playerSelectedUnit, MovementStepOption playerSelectedStep)
        {
            if (!TreatsRoughTerrainAsOpenWhileAdvancing())
            {
                return false;
            }

            return IsIntentionalAdvancingMovementStep(ResolveRoughTerrainMovementStep(playerSelectedUnit, playerSelectedStep));
        }

        public float GetMovementSpeedMultiplierAtPoint(Vector3 worldPoint, Unit playerSelectedUnit, MovementStepOption playerSelectedStep, float unitRadius = 0f)
        {
            if (IgnoresRoughTerrainMovementCost(playerSelectedUnit, playerSelectedStep))
            {
                return 1f;
            }

            var speedMultiplier = 1f;
            var activeZones = CombatZone.ActiveZones;
            for (var i = 0; i < activeZones.Count; i++)
            {
                var zone = activeZones[i];
                if (zone == null || !zone.IsMovementZone)
                {
                    continue;
                }

                if (!zone.IntersectsDisc(worldPoint, unitRadius))
                {
                    continue;
                }

                speedMultiplier = Mathf.Min(speedMultiplier, zone.MovementSpeedMultiplier);
                if (speedMultiplier <= MovementBudgetEpsilon)
                {
                    break;
                }
            }

            return Mathf.Max(MovementBudgetEpsilon, speedMultiplier);
        }

        public GraphMask GetPathGraphMask(NavPathBuilder navPathBuilder)
        {
            if (navPathBuilder == null)
            {
                return GraphMask.everything;
            }

            return navPathBuilder.GetGraphMaskForModelSizeOrDefault(Definition.Stats.modelSize);
        }

        public Vector3 GetNearestNavmeshPosition(Vector3 worldPosition, NavPathBuilder navPathBuilder)
        {
            if (AstarPath.active == null)
            {
                return worldPosition;
            }

            var nearestNodeConstraint = NearestNodeConstraint.Walkable;
            nearestNodeConstraint.graphMask = GetPathGraphMask(navPathBuilder);
            var nearest = AstarPath.active.GetNearest(worldPosition, nearestNodeConstraint);
            return nearest.node != null ? nearest.position : worldPosition;
        }

        public Vector3 GetGroundedPositionKeepingXZ(Vector3 worldPosition, NavPathBuilder navPathBuilder)
        {
            var groundedPosition = worldPosition;
            groundedPosition.y = GetNearestNavmeshPosition(worldPosition, navPathBuilder).y;
            return groundedPosition;
        }

        public void SnapToNavmesh(NavPathBuilder navPathBuilder)
        {
            if (Pawn == null)
            {
                return;
            }

            Pawn.transform.position = GetNearestNavmeshPosition(Pawn.transform.position, navPathBuilder);
        }

        public bool TryResolveChargePath(
            NavPathBuilder navPathBuilder,
            Vector3 clickPosition,
            List<Vector3> path,
            out Vector3 resolvedDestination)
        {
            resolvedDestination = clickPosition;
            if (path == null || navPathBuilder == null)
            {
                return false;
            }

            var from = GetFeetPosition();
            if (!navPathBuilder.TryResolveStraightLineChargeDestination(
                    from,
                    clickPosition,
                    GetPathGraphMask(navPathBuilder),
                    out resolvedDestination,
                    NavmeshContainmentTolerance))
            {
                return false;
            }

            BuildStraightLineChargePath(from, resolvedDestination, path);
            return true;
        }

        public List<Vector3> ClampPathToMovementBudget(
            IReadOnlyList<Vector3> waypoints,
            float budget,
            Unit playerSelectedUnit,
            MovementStepOption playerSelectedStep,
            float unitRadius = 0f)
        {
            var result = new List<Vector3>();
            if (waypoints == null || waypoints.Count == 0)
            {
                return result;
            }

            result.Add(waypoints[0]);
            if (budget <= MovementBudgetEpsilon)
            {
                return result;
            }

            var distanceCovered = 0f;
            for (var i = 1; i < waypoints.Count; i++)
            {
                if (TryGetSegmentStopPointAtMovementBudget(
                        waypoints[i - 1],
                        waypoints[i],
                        budget - distanceCovered,
                        playerSelectedUnit,
                        playerSelectedStep,
                        out var segmentStopPoint,
                        unitRadius))
                {
                    result.Add(segmentStopPoint);
                    break;
                }

                result.Add(waypoints[i]);
                distanceCovered += CalculateMovementCostForSegmentInInches(
                    waypoints[i - 1],
                    waypoints[i],
                    playerSelectedUnit,
                    playerSelectedStep,
                    unitRadius);
            }

            return result;
        }

        public bool TryGetPathStopPointAtMovementBudget(
            IReadOnlyList<Vector3> waypoints,
            float budget,
            Unit playerSelectedUnit,
            MovementStepOption playerSelectedStep,
            out Vector3 stopPoint,
            float unitRadius = 0f)
        {
            stopPoint = default;
            if (waypoints == null || waypoints.Count == 0)
            {
                return false;
            }

            budget = Mathf.Max(0f, budget);
            stopPoint = waypoints[0];
            var distanceCovered = 0f;
            for (var i = 1; i < waypoints.Count; i++)
            {
                if (TryGetSegmentStopPointAtMovementBudget(
                        waypoints[i - 1],
                        waypoints[i],
                        budget - distanceCovered,
                        playerSelectedUnit,
                        playerSelectedStep,
                        out stopPoint,
                        unitRadius))
                {
                    return true;
                }

                distanceCovered += CalculateMovementCostForSegmentInInches(
                    waypoints[i - 1],
                    waypoints[i],
                    playerSelectedUnit,
                    playerSelectedStep,
                    unitRadius);
                stopPoint = waypoints[i];
            }

            return true;
        }

        public float CalculatePathMovementCostInInches(
            IReadOnlyList<Vector3> waypoints,
            Unit playerSelectedUnit,
            MovementStepOption playerSelectedStep,
            float unitRadius = 0f)
        {
            if (waypoints == null || waypoints.Count < 2)
            {
                return 0f;
            }

            var movementCost = 0f;
            for (var i = 1; i < waypoints.Count; i++)
            {
                movementCost += CalculateMovementCostForSegmentInInches(
                    waypoints[i - 1],
                    waypoints[i],
                    playerSelectedUnit,
                    playerSelectedStep,
                    unitRadius);
            }

            return movementCost;
        }

        public float CalculatePathRoughTerrainPhysicalInches(
            IReadOnlyList<Vector3> waypoints,
            float budget,
            Unit playerSelectedUnit,
            MovementStepOption playerSelectedStep,
            float unitRadius = 0f)
        {
            if (IgnoresRoughTerrainMovementCost(playerSelectedUnit, playerSelectedStep)
                || waypoints == null
                || waypoints.Count < 2)
            {
                return 0f;
            }

            var roughInches = 0f;
            var costCovered = 0f;
            for (var i = 1; i < waypoints.Count; i++)
            {
                var remaining = budget - costCovered;
                if (remaining <= MovementBudgetEpsilon)
                {
                    break;
                }

                roughInches += CalculateSegmentRoughTerrainPhysicalInches(
                    waypoints[i - 1],
                    waypoints[i],
                    remaining,
                    playerSelectedUnit,
                    playerSelectedStep,
                    out var segmentCostConsumed,
                    unitRadius);
                costCovered += segmentCostConsumed;
            }

            return roughInches;
        }

        public float CalculateMovementCostForSegmentInInches(
            Vector3 from,
            Vector3 to,
            Unit playerSelectedUnit,
            MovementStepOption playerSelectedStep,
            float unitRadius = 0f)
        {
            var totalDistance = Vector3.Distance(from, to);
            if (totalDistance <= MovementBudgetEpsilon)
            {
                return 0f;
            }

            var sampleCount = GetTerrainCostSampleCount(totalDistance);
            var movementCost = 0f;
            for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                var segmentStartT = (float)sampleIndex / sampleCount;
                var segmentEndT = (float)(sampleIndex + 1) / sampleCount;
                var segmentStart = Vector3.Lerp(from, to, segmentStartT);
                var segmentEnd = Vector3.Lerp(from, to, segmentEndT);
                var samplePoint = Vector3.Lerp(segmentStart, segmentEnd, 0.5f);
                var segmentDistanceInches = CombatScale.WorldUnitsToInches(Vector3.Distance(segmentStart, segmentEnd));
                var speedMultiplier = GetMovementSpeedMultiplierAtPoint(
                    samplePoint,
                    playerSelectedUnit,
                    playerSelectedStep,
                    unitRadius);
                movementCost += segmentDistanceInches / speedMultiplier;
            }

            return movementCost;
        }

        public float GetAffordableWorldStepAlongSegment(
            Vector3 segmentStart,
            Vector3 segmentEnd,
            float maxPhysicalStep,
            Unit playerSelectedUnit,
            MovementStepOption playerSelectedStep,
            float unitRadius)
        {
            if (maxPhysicalStep <= MovementBudgetEpsilon)
            {
                return 0f;
            }

            var segmentLength = Vector3.Distance(segmentStart, segmentEnd);
            if (segmentLength <= MovementBudgetEpsilon)
            {
                return 0f;
            }

            var step = Mathf.Min(maxPhysicalStep, segmentLength);
            var trialEnd = segmentStart + (segmentEnd - segmentStart) * (step / segmentLength);
            var movementCost = CalculateMovementCostForSegmentInInches(
                segmentStart,
                trialEnd,
                playerSelectedUnit,
                playerSelectedStep,
                unitRadius);
            var remaining = RemainingMovementThisTurn;
            if (movementCost <= remaining + MovementBudgetEpsilon)
            {
                return step;
            }

            if (movementCost <= MovementBudgetEpsilon)
            {
                return step;
            }

            return step * Mathf.Clamp01(remaining / movementCost);
        }

        public void IssueMoveOrderFromPath(
            NavPathBuilder navPathBuilder,
            List<Vector3> smoothedPath,
            float movementBudget,
            Unit playerSelectedUnit,
            MovementStepOption playerSelectedStep)
        {
            if (!IsAlive || Pawn == null)
            {
                return;
            }

            if (ActiveMovementStep == MovementStepOption.None)
            {
                ActiveMovementStep = MovementStepOption.Advance;
            }

            var unitRadius = GetCollisionRadius();
            var waypoints = ClampPathToMovementBudget(
                smoothedPath,
                movementBudget,
                playerSelectedUnit,
                playerSelectedStep,
                unitRadius);
            if (waypoints.Count >= 2)
            {
                PathWaypoints = waypoints;
                PathWaypointIndex = 1;
                var firstTarget = GetGroundedPositionKeepingXZ(waypoints[1], navPathBuilder);
                MoveTarget = firstTarget;
            }
        }

        public static float CalculateHitChancePercent(Unit attacker, Unit defender, WeaponProfile weapon)
        {
            var attackStat = attacker.GetAttackStatForWeapon(weapon);
            var attackModifier = attacker.GetToHitModifier();
            var effectiveDefense = defender.GetEffectiveDefense(attacker, weapon);
            var hits = 0;
            for (var d1 = 1; d1 <= 6; d1++)
            {
                for (var d2 = 1; d2 <= 6; d2++)
                {
                    var attackRoll = d1 + d2 + attackStat + attackModifier;
                    if (weapon.EvaluateAttackHit(d1, d2, attackRoll, effectiveDefense))
                    {
                        hits++;
                    }
                }
            }

            return hits / 36f * 100f;
        }

        public static float GetCombinedRadiiInches(Unit first, Unit second)
        {
            return first.GetRadiusInches() + second.GetRadiusInches();
        }

        public static float GetPlanarDistance(Vector3 from, Vector3 to)
        {
            var delta = to - from;
            delta.y = 0f;
            return CombatScale.WorldUnitsToInches(delta.magnitude);
        }

        public static Unit FindFirstAlive(List<Unit> units)
        {
            for (var i = 0; i < units.Count; i++)
            {
                if (units[i].IsAlive)
                {
                    return units[i];
                }
            }

            return null;
        }

        public static Unit FindNearestAlive(Unit source, List<Unit> candidates)
        {
            Unit best = null;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (!candidate.IsAlive)
                {
                    continue;
                }

                var distance = source.GetPlanarDistanceTo(candidate);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        public static bool HasLineOfSight(
            Unit observer,
            Unit target,
            IReadOnlyList<Unit> allUnits,
            Func<CombatLineOfSightVolume, CombatLineOfSightVolume, bool> isTerrainBlocking)
        {
            if (observer?.Pawn == null || target?.Pawn == null || !observer.IsAlive || !target.IsAlive)
            {
                return false;
            }

            var observerVolume = observer.GetLineOfSightVolume();
            var targetVolume = target.GetLineOfSightVolume();
            if (isTerrainBlocking(observerVolume, targetVolume))
            {
                return false;
            }

            if (CombatTerrainLineOfSight.IsForestDepthBlockingLineOfSight(
                    observerVolume,
                    targetVolume,
                    target.Definition.Stats.modelSize,
                    observer.Definition,
                    observer.Pawn))
            {
                return false;
            }

            if (CombatBlockingTerrainClipper.IsObstructingLineOfSight(observerVolume, targetVolume))
            {
                return false;
            }

            var interveningVolumes = new List<CombatLineOfSightVolume>();
            for (var i = 0; i < allUnits.Count; i++)
            {
                var candidate = allUnits[i];
                if (candidate == null || !candidate.IsAlive || ReferenceEquals(candidate, observer) || ReferenceEquals(candidate, target))
                {
                    continue;
                }

                interveningVolumes.Add(candidate.GetLineOfSightVolume());
            }

            return CombatLineOfSight.HasLineOfSight(observerVolume, targetVolume, interveningVolumes);
        }

        public static void DrawAdvantageDebug(Unit unit)
        {
            if (unit?.Definition?.Stats == null)
            {
                return;
            }

            unit.Definition.Stats.EnsureAdvantageDefaults();
            var advantageNames = new List<string>();
            var authored = unit.Definition.Stats.advantages;
            for (var i = 0; i < authored.Count; i++)
            {
                if (authored[i] != null)
                {
                    advantageNames.Add(authored[i].DisplayName);
                }
            }

            for (var i = 0; i < unit.RuntimeAdvantages.Count; i++)
            {
                var runtimeAdvantage = unit.RuntimeAdvantages[i];
                if (runtimeAdvantage != null && !advantageNames.Contains(runtimeAdvantage.DisplayName))
                {
                    advantageNames.Add($"{runtimeAdvantage.DisplayName} (runtime)");
                }
            }

            GUILayout.Label(advantageNames.Count > 0 ? $"Advantages: {string.Join(", ", advantageNames)}" : "Advantages: none");
        }

        public static void DrawDefenseModifierDebug(Unit unit)
        {
            if (unit?.Definition == null || unit.Pawn == null)
            {
                return;
            }

            var modifiers = CombatDefenseEvaluator.CollectActiveDefenseModifiers(unit.Definition, unit.Pawn);
            if (modifiers.Count == 0)
            {
                GUILayout.Label("Defense Modifiers: none");
                return;
            }

            for (var i = 0; i < modifiers.Count; i++)
            {
                var modifier = modifiers[i];
                if (modifier.Definition == null)
                {
                    continue;
                }

                GUILayout.Label(
                    $"Terrain Modifier: {modifier.Definition.DisplayName} (+{modifier.Definition.DefenseBonus} DEF) via {modifier.SourceLabel}");
            }
        }

        public static void DrawTerrainStateDebug(Unit unit)
        {
            if (unit?.Definition == null || unit.Pawn == null)
            {
                GUILayout.Label("Terrain: unknown");
                return;
            }

            var terrainState = CombatAbilitySolver.ResolveTerrainState(unit.Definition, unit.Pawn);
            GUILayout.Label($"Rough Terrain: {(terrainState.IsInRoughTerrain ? "Yes" : "No")}");
            GUILayout.Label($"Forest: {terrainState.ForestStatusLabel}");
            GUILayout.Label($"Cloud: {terrainState.CloudStatusLabel}");
        }

        public static void DrawAbilityDebug(Unit unit)
        {
            if (unit?.Definition == null)
            {
                return;
            }

            unit.Definition.Stats.EnsureAbilityDefaults();
            var abilityNames = new List<string>();
            var authored = unit.Definition.Stats.abilities;
            for (var i = 0; i < authored.Count; i++)
            {
                if (authored[i] != null)
                {
                    abilityNames.Add(authored[i].DisplayName);
                }
            }

            for (var i = 0; i < unit.RuntimeAbilities.Count; i++)
            {
                var runtimeAbility = unit.RuntimeAbilities[i];
                if (runtimeAbility != null && !abilityNames.Contains(runtimeAbility.DisplayName))
                {
                    abilityNames.Add($"{runtimeAbility.DisplayName} (runtime)");
                }
            }

            GUILayout.Label(abilityNames.Count > 0 ? $"Abilities: {string.Join(", ", abilityNames)}" : "Abilities: none");

            var passives = CombatAbilitySolver.DescribeAbilityPassives(unit);
            for (var i = 0; i < passives.Count; i++)
            {
                var passive = passives[i];
                if (passive.Ability == null)
                {
                    continue;
                }

                var prefix = passive.IsActive ? "ACTIVE" : "inactive";
                GUILayout.Label($"  {prefix}: {passive.Ability.DisplayName} — {passive.EffectLabel}");
            }
        }

        private static void BuildStraightLineChargePath(Vector3 from, Vector3 to, List<Vector3> path)
        {
            path.Clear();
            var start = from;
            var end = to;
            end.y = start.y;
            path.Add(start);
            path.Add(end);
        }

        private static bool IsIntentionalAdvancingMovementStep(MovementStepOption step)
        {
            return step == MovementStepOption.Advance
                || step == MovementStepOption.Run
                || step == MovementStepOption.Charge;
        }

        private static int GetTerrainCostSampleCount(float segmentDistanceWorldUnits)
        {
            var sampleStep = CombatScale.InchesToWorldUnits(TerrainCostSampleStepInches);
            if (sampleStep <= MovementBudgetEpsilon)
            {
                return 1;
            }

            return Mathf.Max(1, Mathf.CeilToInt(segmentDistanceWorldUnits / sampleStep));
        }

        private static bool IsPointInRoughTerrain(Vector3 worldPoint, float unitRadius)
        {
            var activeZones = CombatZone.ActiveZones;
            for (var i = 0; i < activeZones.Count; i++)
            {
                var zone = activeZones[i];
                if (zone == null || !zone.IsMovementZone)
                {
                    continue;
                }

                if (zone.IntersectsDisc(worldPoint, unitRadius))
                {
                    return true;
                }
            }

            return false;
        }

        private float CalculateSegmentRoughTerrainPhysicalInches(
            Vector3 from,
            Vector3 to,
            float budgetRemaining,
            Unit playerSelectedUnit,
            MovementStepOption playerSelectedStep,
            out float costConsumed,
            float unitRadius = 0f)
        {
            costConsumed = 0f;
            var roughInches = 0f;
            var totalDistance = Vector3.Distance(from, to);
            if (totalDistance <= MovementBudgetEpsilon)
            {
                return 0f;
            }

            var sampleCount = GetTerrainCostSampleCount(totalDistance);
            for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                var subStartT = (float)sampleIndex / sampleCount;
                var subEndT = (float)(sampleIndex + 1) / sampleCount;
                var subStart = Vector3.Lerp(from, to, subStartT);
                var subEnd = Vector3.Lerp(from, to, subEndT);
                var samplePoint = Vector3.Lerp(subStart, subEnd, 0.5f);
                var subDistInches = CombatScale.WorldUnitsToInches(Vector3.Distance(subStart, subEnd));
                var speedMultiplier = GetMovementSpeedMultiplierAtPoint(
                    samplePoint,
                    playerSelectedUnit,
                    playerSelectedStep,
                    unitRadius);
                var subCost = subDistInches / speedMultiplier;
                var isRoughTerrain = IsPointInRoughTerrain(samplePoint, unitRadius);

                if (costConsumed + subCost > budgetRemaining + MovementBudgetEpsilon)
                {
                    var remaining = Mathf.Max(0f, budgetRemaining - costConsumed);
                    var fraction = subCost <= MovementBudgetEpsilon ? 0f : Mathf.Clamp01(remaining / subCost);
                    if (isRoughTerrain)
                    {
                        roughInches += subDistInches * fraction;
                    }

                    costConsumed += subCost * fraction;
                    break;
                }

                if (isRoughTerrain)
                {
                    roughInches += subDistInches;
                }

                costConsumed += subCost;
            }

            return roughInches;
        }

        private bool TryGetSegmentStopPointAtMovementBudget(
            Vector3 segmentStart,
            Vector3 segmentEnd,
            float budgetRemaining,
            Unit playerSelectedUnit,
            MovementStepOption playerSelectedStep,
            out Vector3 stopPoint,
            float unitRadius = 0f)
        {
            stopPoint = segmentStart;
            if (budgetRemaining <= MovementBudgetEpsilon)
            {
                return true;
            }

            var totalDistance = Vector3.Distance(segmentStart, segmentEnd);
            if (totalDistance <= MovementBudgetEpsilon)
            {
                return true;
            }

            var sampleCount = GetTerrainCostSampleCount(totalDistance);
            var movementCostCovered = 0f;
            for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                var subSegmentStartT = (float)sampleIndex / sampleCount;
                var subSegmentEndT = (float)(sampleIndex + 1) / sampleCount;
                var subSegmentStart = Vector3.Lerp(segmentStart, segmentEnd, subSegmentStartT);
                var subSegmentEnd = Vector3.Lerp(segmentStart, segmentEnd, subSegmentEndT);
                var samplePoint = Vector3.Lerp(subSegmentStart, subSegmentEnd, 0.5f);
                var subSegmentDistanceInches = CombatScale.WorldUnitsToInches(Vector3.Distance(subSegmentStart, subSegmentEnd));
                var speedMultiplier = GetMovementSpeedMultiplierAtPoint(
                    samplePoint,
                    playerSelectedUnit,
                    playerSelectedStep,
                    unitRadius);
                var subSegmentCost = subSegmentDistanceInches / speedMultiplier;

                if (movementCostCovered + subSegmentCost >= budgetRemaining - MovementBudgetEpsilon)
                {
                    var remainingCost = Mathf.Max(0f, budgetRemaining - movementCostCovered);
                    var t = subSegmentCost <= MovementBudgetEpsilon
                        ? 0f
                        : Mathf.Clamp01(remainingCost / subSegmentCost);
                    stopPoint = Vector3.Lerp(subSegmentStart, subSegmentEnd, t);
                    return true;
                }

                movementCostCovered += subSegmentCost;
                stopPoint = subSegmentEnd;
            }

            return false;
        }
    }
}
