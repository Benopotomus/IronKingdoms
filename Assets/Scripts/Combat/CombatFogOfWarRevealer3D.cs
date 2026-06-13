using FOW;
using Unity.Jobs;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Combat unit revealer that lets stock FOW calculate wall/occluder hits first, then applies
    /// combat forest pass-through depth to those phase-1 ray samples before stock contour sorting.
    /// </summary>
    [DefaultExecutionOrder(-150)]
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

        public void ConfigureForUnit(UnitTypeDefinition definition)
        {
            ignoresForestForLineOfSight = definition != null
                && CombatAbilitySolver.IgnoresForestWhenDeterminingLineOfSight(definition, null);
            baseRadiusWorld = definition != null
                ? Mathf.Max(0f, definition.Stats.modelSize.BaseDiameterWorldUnits() * 0.5f)
                : 0f;

            EnsureBlockerRing();
            blockerRing.ConfigureForUnit(definition);
            InvalidateLineOfSightPose();
        }

        private void Update()
        {
            if (!IsRegistered || !Application.isPlaying)
            {
                return;
            }

            UpdateStationaryState();
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

            if (useOcclusion && ShouldApplyForestClip())
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

        private bool ShouldApplyForestClip()
        {
            if (ignoresForestForLineOfSight)
            {
                return false;
            }

            CombatForestFogClipper.EnsureCache();
            return CombatForestFogClipper.HasActiveZones;
        }

        private void ApplyForestClipBeforeStockSorting()
        {
            var eyeWorld = (Vector3)GetEyePosition();
            var baseIntersectsForest = CombatForestFogClipper.IsInsideLimitedDepthForest(eyeWorld, baseRadiusWorld);
            var collectDebugState = drawForestClipDebug && drawForestClipInGameView;

            forestPostProcessor.Apply(
                FirstIteration,
                FirstIterationStepCount,
                eyeWorld,
                TotalRevealerRadius,
                Projection,
                baseIntersectsForest,
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
