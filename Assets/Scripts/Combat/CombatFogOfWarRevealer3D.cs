using System.Collections.Generic;
using FOW;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Unit revealer that keeps stock FOW wall raycasts and applies per-ray analytic forest depth
    /// clipping to phase-1 results. Forest rules are evaluated independently on each direction,
    /// so edge-straddling observers do not split into separate inside/outside visibility states.
    /// </summary>
    public class CombatFogOfWarRevealer3D : FogOfWarRevealer3D
    {
        [Header("Forest Debug")]
        [SerializeField] private bool drawForestClipDebug = true;
        [SerializeField] private bool drawForestClipInGameView = true;
        [SerializeField] private Color debugClipRayColor = new(0.1f, 1f, 0.2f, 1f);
        [SerializeField] private Color debugBridgeRayColor = new(1f, 0.85f, 0.1f, 1f);
        [SerializeField] private Color debugContourColor = new(0.2f, 0.9f, 1f, 1f);

        private CombatForestFogBlockerRing blockerRing;
        private bool ignoresForestForLineOfSight;

        private readonly List<Vector3> debugClipPointsWorld = new();
        private readonly List<Vector3> debugBridgePointsWorld = new();
        private readonly List<Vector3> debugContourPointsWorld = new();
        private readonly HashSet<int> debugBridgedRayIndices = new();
        private const int ForestLimitedAngularScanSteps = 96;
        private Vector3 debugEyeWorld;
        private bool hasForestDebugContour;

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
            base.LineOfSightPhase1();
        }

        public override void LineOfSightPhase2()
        {
            debugBridgedRayIndices.Clear();

            if (useOcclusion && ShouldApplyForestClip())
            {
                CompletePhaseOneBeforeForestClip();
                ApplyForestClipToFirstIteration();
                FillForestMissBridges();
                FirstIterationPointsAndConditionsJob.Run(FirstIterationStepCount);
                ForceForestContourViewPoints();
                ForceForestAdjacentOpenContourPoints();
                CaptureForestDebugContour();
                if (drawForestClipDebug)
                {
                    blockerRing?.RebuildForDebug();
                }
            }
            else if (drawForestClipDebug)
            {
                blockerRing?.DisableForFogCalculation();
                hasForestDebugContour = false;
            }

            base.LineOfSightPhase2();

            if (drawForestClipDebug && drawForestClipInGameView && hasForestDebugContour)
            {
                DrawForestDebugLines();
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

        private void ApplyForestClipToFirstIteration()
        {
            var depthWorld = GetForestDepthWorld();
            var eyeWorld = (Vector3)GetEyePosition();
            var projectedEye = Projection.Project((float3)eyeWorld);
            var maxRadius = TotalRevealerRadius;

            for (var i = 0; i < FirstIterationStepCount; i++)
            {
                var dir2 = FirstIteration.Directions[i];
                if (math.lengthsq(dir2) <= 1e-8f)
                {
                    continue;
                }

                dir2 = math.normalize(dir2);
                var dir3 = Direction2DToWorld(dir2);
                var forestClip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                    eyeWorld,
                    dir3,
                    maxRadius,
                    depthWorld);

                var physicsHit = FirstIteration.Hits[i];
                var physicsDist = physicsHit ? FirstIteration.Distances[i] : maxRadius;
                var finalDist = Mathf.Min(physicsDist, forestClip);

                if (finalDist >= maxRadius - 0.001f)
                {
                    FirstIteration.Hits[i] = false;
                    FirstIteration.Distances[i] = maxRadius;
                    FirstIteration.Points[i] = projectedEye + (dir2 * maxRadius);
                    FirstIteration.Normals[i] = -dir2;
                    continue;
                }

                var forestIsTighter = forestClip < physicsDist - 0.001f;
                FirstIteration.Hits[i] = true;
                FirstIteration.Distances[i] = finalDist;
                FirstIteration.Points[i] = projectedEye + (dir2 * finalDist);
                if (forestIsTighter || !physicsHit)
                {
                    FirstIteration.Normals[i] = -dir2;
                }
            }
        }

        /// <summary>
        /// Per-ray bridge: any sample the clipper would limit gets a hit so SortData does not
        /// draw a miss chord across it. Open rays (clipper returns max radius) stay misses.
        /// </summary>
        private void FillForestMissBridges()
        {
            var eyeWorld = (Vector3)GetEyePosition();
            var depthWorld = GetForestDepthWorld();
            var projectedEye = Projection.Project((float3)eyeWorld);
            var maxRadius = TotalRevealerRadius;
            var count = FirstIterationStepCount;

            for (var i = 0; i < count; i++)
            {
                if (FirstIteration.Hits[i])
                {
                    continue;
                }

                var dir2 = FirstIteration.Directions[i];
                if (math.lengthsq(dir2) <= 1e-8f)
                {
                    continue;
                }

                dir2 = math.normalize(dir2);
                var dir3 = Direction2DToWorld(dir2);
                var forestClip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                    eyeWorld,
                    dir3,
                    maxRadius,
                    depthWorld);
                if (forestClip >= maxRadius - 0.01f)
                {
                    continue;
                }

                BridgeForestRay(i, dir2, forestClip, projectedEye);
            }
        }

        private void BridgeForestRay(int index, float2 dir2, float bridgeDist, float2 projectedEye)
        {
            FirstIteration.Hits[index] = true;
            FirstIteration.Distances[index] = bridgeDist;
            FirstIteration.Points[index] = projectedEye + (dir2 * bridgeDist);
            FirstIteration.Normals[index] = -dir2;
            debugBridgedRayIndices.Add(index);
        }

        /// <summary>
        /// Stock FOW SortData can skip clipped rays; force every forest-limited sample into the contour.
        /// </summary>
        private void ForceForestContourViewPoints()
        {
            var maxRadius = TotalRevealerRadius;
            for (var i = 0; i < FirstIterationStepCount; i++)
            {
                if (!FirstIteration.Hits[i] || FirstIteration.Distances[i] >= maxRadius - 0.01f)
                {
                    continue;
                }

                FirstIterationConditions[i] = true;
            }
        }

        /// <summary>
        /// Genuinely open rays (clipper returns max radius) near the forest-depth arc must stay
        /// in the contour even when many consecutive arc samples sit between them and the arc.
        /// </summary>
        private void ForceForestAdjacentOpenContourPoints()
        {
            var eyeWorld = (Vector3)GetEyePosition();
            var maxRadius = TotalRevealerRadius;
            var depthWorld = GetForestDepthWorld();
            var count = FirstIterationStepCount;

            for (var i = 0; i < count; i++)
            {
                if (!IsOpenMissRay(i, maxRadius))
                {
                    continue;
                }

                var dir3 = Direction2DToWorld(math.normalize(FirstIteration.Directions[i]));
                var forestClip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                    eyeWorld,
                    dir3,
                    maxRadius,
                    depthWorld);
                if (forestClip < maxRadius - 0.01f)
                {
                    continue;
                }

                if (!ForestLimitedHitWithinAngularScan(i, maxRadius, count))
                {
                    continue;
                }

                FirstIterationConditions[i] = true;
            }
        }

        private bool ForestLimitedHitWithinAngularScan(int index, float maxRadius, int count)
        {
            for (var d = 1; d <= ForestLimitedAngularScanSteps; d++)
            {
                var prev = index - d;
                if (prev >= 0 && IsForestLimitedHit(prev, maxRadius))
                {
                    return true;
                }

                var next = index + d;
                if (next < count && IsForestLimitedHit(next, maxRadius))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsOpenMissRay(int index, float maxRadius)
        {
            return !FirstIteration.Hits[index] && FirstIteration.Distances[index] >= maxRadius - 0.01f;
        }

        private void CaptureForestDebugContour()
        {
            debugClipPointsWorld.Clear();
            debugBridgePointsWorld.Clear();
            debugContourPointsWorld.Clear();
            hasForestDebugContour = false;

            if (!drawForestClipDebug)
            {
                return;
            }

            debugEyeWorld = (Vector3)GetEyePosition();
            var maxRadius = TotalRevealerRadius;

            for (var i = 0; i < FirstIterationStepCount; i++)
            {
                if (!FirstIteration.Hits[i] || FirstIteration.Distances[i] >= maxRadius - 0.01f)
                {
                    continue;
                }

                var dir2 = math.normalize(FirstIteration.Directions[i]);
                var pointWorld = debugEyeWorld + (Direction2DToWorld(dir2) * FirstIteration.Distances[i]);
                debugContourPointsWorld.Add(pointWorld);
                hasForestDebugContour = true;

                if (debugBridgedRayIndices.Contains(i))
                {
                    debugBridgePointsWorld.Add(pointWorld);
                }
                else
                {
                    debugClipPointsWorld.Add(pointWorld);
                }
            }
        }

        private void DrawForestDebugLines()
        {
            for (var i = 0; i < debugClipPointsWorld.Count; i++)
            {
                Debug.DrawLine(debugEyeWorld, debugClipPointsWorld[i], debugClipRayColor, 0f, false);
            }

            for (var i = 0; i < debugBridgePointsWorld.Count; i++)
            {
                Debug.DrawLine(debugEyeWorld, debugBridgePointsWorld[i], debugBridgeRayColor, 0f, false);
            }

            for (var i = 1; i < debugContourPointsWorld.Count; i++)
            {
                Debug.DrawLine(debugContourPointsWorld[i - 1], debugContourPointsWorld[i], debugContourColor, 0f, false);
            }

            if (debugContourPointsWorld.Count > 1)
            {
                Debug.DrawLine(
                    debugContourPointsWorld[debugContourPointsWorld.Count - 1],
                    debugContourPointsWorld[0],
                    debugContourColor,
                    0f,
                    false);
            }
        }

        private void OnDrawGizmos()
        {
            if (!drawForestClipDebug || !Application.isPlaying || !hasForestDebugContour)
            {
                return;
            }

            Gizmos.color = debugClipRayColor;
            for (var i = 0; i < debugClipPointsWorld.Count; i++)
            {
                Gizmos.DrawLine(debugEyeWorld, debugClipPointsWorld[i]);
                Gizmos.DrawSphere(debugClipPointsWorld[i], 0.05f);
            }

            Gizmos.color = debugBridgeRayColor;
            for (var i = 0; i < debugBridgePointsWorld.Count; i++)
            {
                Gizmos.DrawLine(debugEyeWorld, debugBridgePointsWorld[i]);
                Gizmos.DrawSphere(debugBridgePointsWorld[i], 0.06f);
            }

            Gizmos.color = debugContourColor;
            for (var i = 1; i < debugContourPointsWorld.Count; i++)
            {
                Gizmos.DrawLine(debugContourPointsWorld[i - 1], debugContourPointsWorld[i]);
            }

            if (debugContourPointsWorld.Count > 1)
            {
                Gizmos.DrawLine(
                    debugContourPointsWorld[debugContourPointsWorld.Count - 1],
                    debugContourPointsWorld[0]);
            }

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(debugEyeWorld, 0.08f);
        }

        private static float GetForestDepthWorld()
        {
            var depthWorld = CombatForestFogClipper.GetStrictestLimitedDepthWorld();
            if (depthWorld <= 0.001f)
            {
                depthWorld = CombatScale.InchesToWorldUnits(3f);
            }

            return depthWorld;
        }

        private bool IsForestLimitedHit(int index, float maxRadius)
        {
            return FirstIteration.Hits[index] && FirstIteration.Distances[index] < maxRadius - 0.01f;
        }

        private Vector3 Direction2DToWorld(float2 dir2)
        {
            var dir3 = float3.zero;
            dir3[Projection.Axis0] = dir2.x;
            dir3[Projection.Axis1] = dir2.y;
            return (Vector3)math.normalizesafe(dir3);
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
