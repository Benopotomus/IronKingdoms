using FOW;
using Unity.Jobs;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Combat unit revealer that lets stock FOW calculate wall/occluder hits first, then applies
    /// combat forest pass-through depth to those phase-1 ray samples before stock contour sorting.
    /// </summary>
    public class CombatFogOfWarRevealer3D : FogOfWarRevealer3D
    {
        [Header("Forest Debug")]
        [SerializeField] private bool drawForestClipDebug = true;
        [SerializeField] private bool drawForestClipInGameView = true;
        [SerializeField] private Color debugClipRayColor = new(0.1f, 1f, 0.2f, 1f);
        [SerializeField] private Color debugBridgeRayColor = new(1f, 0.85f, 0.1f, 1f);
        [SerializeField] private Color debugContourColor = new(0.2f, 0.9f, 1f, 1f);

        private readonly CombatForestFogRayPostProcessor forestPostProcessor = new();
        private readonly CombatForestFogDebugContour forestDebugContour = new();

        private CombatForestFogBlockerRing blockerRing;
        private bool ignoresForestForLineOfSight;

        public void ConfigureForUnit(UnitTypeDefinition definition)
        {
            ignoresForestForLineOfSight = definition != null
                && CombatAbilitySolver.IgnoresForestWhenDeterminingLineOfSight(definition, null);

            EnsureBlockerRing();
            blockerRing.ConfigureForUnit(definition);
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

            if (drawForestClipDebug && drawForestClipInGameView && forestDebugContour.HasContour)
            {
                forestDebugContour.DrawRuntimeLines(debugClipRayColor, debugBridgeRayColor, debugContourColor);
            }
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

            forestPostProcessor.Apply(
                FirstIteration,
                FirstIterationStepCount,
                eyeWorld,
                TotalRevealerRadius,
                Projection);

            // Re-run the stock first-iteration conditions after forest has tightened ray distances.
            FirstIterationPointsAndConditionsJob.Run(FirstIterationStepCount);
            forestPostProcessor.ForceContourConditions(
                FirstIteration,
                FirstIterationConditions,
                FirstIterationStepCount,
                eyeWorld,
                TotalRevealerRadius,
                Projection);

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
