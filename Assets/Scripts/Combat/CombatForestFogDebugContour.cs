using System.Collections.Generic;
using FOW;
using Unity.Mathematics;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Captures and draws the forest-clipped contour after the ray post-processor has edited
    /// phase-1 FOW samples. This is debug-only state; it does not affect visibility.
    /// </summary>
    internal sealed class CombatForestFogDebugContour
    {
        private readonly List<Vector3> clipPointsWorld = new();
        private readonly List<Vector3> bridgePointsWorld = new();
        private readonly List<Vector3> contourPointsWorld = new();

        private Vector3 eyeWorld;

        public bool HasContour { get; private set; }

        public void Clear()
        {
            clipPointsWorld.Clear();
            bridgePointsWorld.Clear();
            contourPointsWorld.Clear();
            HasContour = false;
        }

        public void Capture(
            RaycastRevealer.SightIteration firstIteration,
            int stepCount,
            Vector3 sourceEyeWorld,
            float maxRadius,
            FogOfWarRevealer3D.PlaneProjection projection,
            HashSet<int> bridgedRayIndices)
        {
            Clear();
            eyeWorld = sourceEyeWorld;

            for (var i = 0; i < stepCount; i++)
            {
                if (!firstIteration.Hits[i] || firstIteration.Distances[i] >= maxRadius - 0.01f)
                {
                    continue;
                }

                var direction2D = firstIteration.Directions[i];
                if (math.lengthsq(direction2D) <= 1e-8f)
                {
                    continue;
                }

                direction2D = math.normalize(direction2D);
                var pointWorld = eyeWorld
                    + (CombatFogProjection.Direction2DToWorld(direction2D, projection) * firstIteration.Distances[i]);
                contourPointsWorld.Add(pointWorld);
                HasContour = true;

                if (bridgedRayIndices != null && bridgedRayIndices.Contains(i))
                {
                    bridgePointsWorld.Add(pointWorld);
                }
                else
                {
                    clipPointsWorld.Add(pointWorld);
                }
            }
        }

        public void DrawRuntimeLines(Color clipColor, Color bridgeColor, Color contourColor)
        {
            for (var i = 0; i < clipPointsWorld.Count; i++)
            {
                Debug.DrawLine(eyeWorld, clipPointsWorld[i], clipColor, 0f, false);
            }

            for (var i = 0; i < bridgePointsWorld.Count; i++)
            {
                Debug.DrawLine(eyeWorld, bridgePointsWorld[i], bridgeColor, 0f, false);
            }

            DrawContourLines(contourColor);
        }

        public void DrawGizmos(Color clipColor, Color bridgeColor, Color contourColor)
        {
            Gizmos.color = clipColor;
            for (var i = 0; i < clipPointsWorld.Count; i++)
            {
                Gizmos.DrawLine(eyeWorld, clipPointsWorld[i]);
                Gizmos.DrawSphere(clipPointsWorld[i], 0.05f);
            }

            Gizmos.color = bridgeColor;
            for (var i = 0; i < bridgePointsWorld.Count; i++)
            {
                Gizmos.DrawLine(eyeWorld, bridgePointsWorld[i]);
                Gizmos.DrawSphere(bridgePointsWorld[i], 0.06f);
            }

            Gizmos.color = contourColor;
            for (var i = 1; i < contourPointsWorld.Count; i++)
            {
                Gizmos.DrawLine(contourPointsWorld[i - 1], contourPointsWorld[i]);
            }

            if (contourPointsWorld.Count > 1)
            {
                Gizmos.DrawLine(
                    contourPointsWorld[contourPointsWorld.Count - 1],
                    contourPointsWorld[0]);
            }

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(eyeWorld, 0.08f);
        }

        private void DrawContourLines(Color contourColor)
        {
            for (var i = 1; i < contourPointsWorld.Count; i++)
            {
                Debug.DrawLine(contourPointsWorld[i - 1], contourPointsWorld[i], contourColor, 0f, false);
            }

            if (contourPointsWorld.Count > 1)
            {
                Debug.DrawLine(
                    contourPointsWorld[contourPointsWorld.Count - 1],
                    contourPointsWorld[0],
                    contourColor,
                    0f,
                    false);
            }
        }
    }
}
