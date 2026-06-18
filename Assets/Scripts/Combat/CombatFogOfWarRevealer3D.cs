using FOW;
using Unity.Jobs;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Combat unit revealer that lets stock FOW calculate wall/occluder hits first, then applies
    /// combat forest pass-through depth to those phase-1 ray samples before stock contour sorting.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class CombatFogOfWarRevealer3D : FogOfWarRevealer3D
    {
        private const float StationaryEyeDistanceWorld = 0.02f;
        private const float StationaryEyeDistanceWorldSq = StationaryEyeDistanceWorld * StationaryEyeDistanceWorld;
        private const float StationaryYawDegrees = 0.5f;

        [Header("Forest Debug")]
        [SerializeField] private bool drawForestClipDebug = false;
        [SerializeField] private bool drawForestClipInGameView = true;
        [SerializeField] private Color debugClipRayColor = new(0.1f, 1f, 0.2f, 1f);
        [SerializeField] private Color debugBridgeRayColor = new(1f, 0.85f, 0.1f, 1f);
        [SerializeField] private Color debugContourColor = new(0.2f, 0.9f, 1f, 1f);

        private readonly CombatForestFogRayPostProcessor forestPostProcessor = new();
        private readonly CombatForestFogDebugContour forestDebugContour = new();

        private CombatForestFogBlockerRing blockerRing;
        private bool ignoresForestForLineOfSight;
        private float baseRadiusWorld;
        private Vector3 lastCalculatedEyeWorld;
        private float lastCalculatedEyeYaw;
        private bool hasCalculatedLineOfSightPose;
        private bool pendingLineOfSightRecalculation;
        private bool wantsLocalFogContribution = true;

        public bool ShouldContributeToLocalFog => wantsLocalFogContribution;

        public bool IsContributingToFog => wantsLocalFogContribution && IsRegistered;

        public bool IgnoresForestLineOfSightLimits => ignoresForestForLineOfSight;

        public bool MatchesUnitVisionRules(Unit unit)
        {
            if (unit?.Definition?.Stats == null)
            {
                return unit == null;
            }

            return ignoresForestForLineOfSight
                == CombatAbilitySolver.IgnoresForestWhenDeterminingLineOfSight(unit);
        }

        public bool IsContributionStateSatisfied()
        {
            return wantsLocalFogContribution == IsRegistered;
        }

        public void ConfigureForUnit(Unit unit)
        {
            if (unit?.Definition?.Stats == null)
            {
                ClearConfiguration();
                return;
            }

            var nextIgnoresForest = CombatAbilitySolver.IgnoresForestWhenDeterminingLineOfSight(unit);
            var forestRulesChanged = ignoresForestForLineOfSight != nextIgnoresForest;
            ignoresForestForLineOfSight = nextIgnoresForest;
            baseRadiusWorld = Mathf.Max(0f, unit.Definition.Stats.modelSize.BaseDiameterWorldUnits() * 0.5f);

            EnsureBlockerRing();
            blockerRing.ConfigureForUnit(unit);
            InvalidateLineOfSightPose();

            if (Application.isPlaying && forestRulesChanged)
            {
                RequestLineOfSightRecalculation();
            }
        }

        /// <summary>
        /// Controls whether this unit is registered with FogOfWarWorld. The component stays
        /// enabled; non-contributing revealers are deregistered only.
        /// </summary>
        public void SetLocalFogContribution(bool active)
        {
            if (wantsLocalFogContribution == active && IsContributionStateSatisfied())
            {
                return;
            }

            wantsLocalFogContribution = active;
            if (!active)
            {
                pendingLineOfSightRecalculation = false;
            }

            ApplyLocalFogContributionState();

            if (active)
            {
                TryProcessPendingLineOfSightRecalculation();
            }
        }

        private void OnEnable()
        {
            if (!wantsLocalFogContribution && IsRegistered)
            {
                DeregisterRevealer();
            }
        }

        private void ApplyLocalFogContributionState()
        {
            FogOfWarWorld.PendingRevealerRegister.Remove(this);

            if (!wantsLocalFogContribution)
            {
                if (IsRegistered)
                {
                    DeregisterRevealer();
                }

                return;
            }

            if (IsRegistered)
            {
                return;
            }

            var fow = FogOfWarWorld.instance;
            if (fow == null)
            {
                return;
            }

            if (fow.IsInPhasedUpdate)
            {
                if (!FogOfWarWorld.PendingRevealerRegister.Contains(this))
                {
                    FogOfWarWorld.PendingRevealerRegister.Add(this);
                }

                return;
            }

            RegisterRevealer();
        }

        /// <summary>
        /// Re-reads forest/ability vision rules from a live unit and schedules a safe FOW refresh.
        /// </summary>
        public void ApplyVisionRulesFromUnit(Unit unit)
        {
            ConfigureForUnit(unit);
            if (!Application.isPlaying)
            {
                return;
            }

            RequestLineOfSightRecalculation();
            TryProcessPendingLineOfSightRecalculation();
        }

        /// <summary>
        /// Marks this revealer dynamic and recalculates on the next safe frame boundary.
        /// Avoid calling ManualCalculateLineOfSight during FOW phased updates or revealer enable.
        /// </summary>
        public void RequestLineOfSightRecalculation()
        {
            SetRevealerAsStatic(false);
            InvalidateLineOfSightPose();
            pendingLineOfSightRecalculation = true;
        }

        public void ForceImmediateLineOfSightRecalculation()
        {
            RequestLineOfSightRecalculation();
        }

        private void LateUpdate()
        {
            if (IsRegistered && Application.isPlaying)
            {
                UpdateStationaryState();
            }

            TryProcessPendingLineOfSightRecalculation();
        }

        private void TryProcessPendingLineOfSightRecalculation()
        {
            if (!pendingLineOfSightRecalculation || !isActiveAndEnabled || !IsRegistered)
            {
                return;
            }

            var fow = FogOfWarWorld.instance;
            if (fow == null || fow.IsInPhasedUpdate)
            {
                return;
            }

            pendingLineOfSightRecalculation = false;
            ManualCalculateLineOfSight();
        }

        public void ConfigureForUnit(UnitTypeDefinition definition)
        {
            if (definition?.Stats == null)
            {
                ClearConfiguration();
                return;
            }

            ignoresForestForLineOfSight = CombatAbilitySolver.IgnoresForestWhenDeterminingLineOfSight(definition, null);
            baseRadiusWorld = Mathf.Max(0f, definition.Stats.modelSize.BaseDiameterWorldUnits() * 0.5f);

            EnsureBlockerRing();
            blockerRing.ConfigureForUnitDefinition(definition);
            InvalidateLineOfSightPose();
        }

        private void ClearConfiguration()
        {
            ignoresForestForLineOfSight = false;
            baseRadiusWorld = 0f;
            EnsureBlockerRing();
            blockerRing.ConfigureForUnitDefinition(null);
            InvalidateLineOfSightPose();
        }

        public override void LineOfSightPhase1()
        {
            EnsureBlockerRing();
            blockerRing?.DisableForFogCalculation();

            // Base phase 1 is where stock FOW raycasts against normal wall/fog occluder colliders.
            base.LineOfSightPhase1();
        }

        public override void LineOfSightPhase2()
        {
            forestPostProcessor.ClearDebugState();

            if (useOcclusion && ShouldApplyTerrainFeatureClip())
            {
                CompletePhaseOneBeforeForestClip();
                ApplyForestClipBeforeStockSorting();
            }
            else
            {
                ClearForestDebug();
            }

            base.LineOfSightPhase2();
            CaptureLineOfSightPose();

            if (drawForestClipDebug && drawForestClipInGameView && forestDebugContour.HasContour)
            {
                forestDebugContour.DrawRuntimeLines(debugClipRayColor, debugBridgeRayColor, debugContourColor);
            }
        }

        private void UpdateStationaryState()
        {
            if (!hasCalculatedLineOfSightPose)
            {
                return;
            }

            if (HasMovedSinceLastLineOfSightCalculation())
            {
                if (CurrentlyStaticRevealer)
                {
                    SetRevealerAsStatic(false);
                }

                return;
            }

            if (!CurrentlyStaticRevealer)
            {
                // Must run after FogOfWarWorld Phase2 (StartInUpdateFinishInLateUpdate). Marking static
                // between Phase1 and Phase2 skips Phase2 and leaves native job handles in flight.
                CompletePhaseOneBeforeForestClip();
                SetRevealerAsStatic(true);
            }
        }

        private bool HasMovedSinceLastLineOfSightCalculation()
        {
            var eyeWorld = (Vector3)GetEyePosition();
            var eyeYaw = transform.eulerAngles.y;
            return (eyeWorld - lastCalculatedEyeWorld).sqrMagnitude > StationaryEyeDistanceWorldSq
                || Mathf.Abs(Mathf.DeltaAngle(eyeYaw, lastCalculatedEyeYaw)) > StationaryYawDegrees;
        }

        private void CaptureLineOfSightPose()
        {
            lastCalculatedEyeWorld = (Vector3)GetEyePosition();
            lastCalculatedEyeYaw = transform.eulerAngles.y;
            hasCalculatedLineOfSightPose = true;
        }

        private void InvalidateLineOfSightPose()
        {
            hasCalculatedLineOfSightPose = false;
        }

        private void CompletePhaseOneBeforeForestClip()
        {
            PreReqJobHandle.Complete();
            FirstIterationPointsAndConditionsJobHandle.Complete();
        }

        private bool ShouldApplyTerrainFeatureClip()
        {
            CombatBlockingTerrainClipper.EnsureCache();
            if (CombatBlockingTerrainClipper.HasActiveZones)
            {
                return true;
            }

            if (ignoresForestForLineOfSight)
            {
                return false;
            }

            CombatForestFogClipper.EnsureCache();
            return CombatForestFogClipper.HasActiveZones;
        }

        private bool ShouldApplyForestClip()
        {
            return ShouldApplyTerrainFeatureClip();
        }

        private void ApplyForestClipBeforeStockSorting()
        {
            var eyeWorld = (Vector3)GetEyePosition();
            var baseIntersectsForest = CombatForestFogClipper.IsInsideLimitedDepthForest(eyeWorld, baseRadiusWorld);
            var baseIntersectsCloud = CombatBlockingTerrainClipper.IsInsideBlockingTerrain(eyeWorld, baseRadiusWorld);
            var collectDebugState = drawForestClipDebug && drawForestClipInGameView;

            forestPostProcessor.Apply(
                FirstIteration,
                FirstIterationStepCount,
                eyeWorld,
                TotalRevealerRadius,
                baseRadiusWorld,
                Projection,
                baseIntersectsForest,
                baseIntersectsCloud,
                collectDebugState);

            // Re-run the stock first-iteration conditions after forest has tightened ray distances.
            FirstIterationPointsAndConditionsJob.Run(FirstIterationStepCount);
            forestPostProcessor.ForceContourConditions(
                FirstIteration,
                FirstIterationConditions,
                FirstIterationStepCount,
                eyeWorld,
                TotalRevealerRadius,
                Projection,
                baseIntersectsForest);

            if (!drawForestClipDebug)
            {
                forestDebugContour.Clear();
                return;
            }

            forestDebugContour.Capture(
                FirstIteration,
                FirstIterationStepCount,
                eyeWorld,
                TotalRevealerRadius,
                Projection,
                forestPostProcessor.BridgedRayIndices);
            blockerRing?.RebuildForDebug();
        }

        private void ClearForestDebug()
        {
            forestDebugContour.Clear();
            if (drawForestClipDebug)
            {
                blockerRing?.DisableForFogCalculation();
            }
        }

        private void OnDrawGizmos()
        {
            if (!drawForestClipDebug || !Application.isPlaying || !forestDebugContour.HasContour)
            {
                return;
            }

            forestDebugContour.DrawGizmos(debugClipRayColor, debugBridgeRayColor, debugContourColor);
        }

        private void Reset()
        {
            EnsureBlockerRing();
        }

        private void OnValidate()
        {
            EnsureBlockerRing();
        }

        private void Awake()
        {
            EnsureBlockerRing();
        }

        private void OnDisable()
        {
            pendingLineOfSightRecalculation = false;
        }

        private void EnsureBlockerRing()
        {
            if (blockerRing == null)
            {
                blockerRing = GetComponent<CombatForestFogBlockerRing>();
            }

            if (blockerRing == null)
            {
                blockerRing = gameObject.AddComponent<CombatForestFogBlockerRing>();
            }
        }
    }
}
