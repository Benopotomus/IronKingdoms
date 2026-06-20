using System.Collections.Generic;
using FOW;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Draws the effective fog boundary (baseline + terrain upload) and forest footprints for debug.
    /// </summary>
    internal sealed class CombatFogTextureBoundaryDrawer
    {
        private const float MarkerYBoost = 0.09f;
        private const float EffectiveContourWidth = 0.06f;
        private const float ForestFootprintWidth = 0.04f;

        private readonly List<LineRenderer> footprintLinePool = new();
        private readonly List<(Vector3 start, Vector3 end)> footprintSegments = new();
        private readonly List<Vector3> effectiveContourPoints = new();

        private Transform lineParent;
        private LineRenderer effectiveContourLine;

        public void SetLineParent(Transform parent)
        {
            lineParent = parent;
            EnsureContourLine();
        }

        public void ClearGameViewLines()
        {
            if (effectiveContourLine != null)
            {
                effectiveContourLine.positionCount = 0;
                effectiveContourLine.enabled = false;
            }

            for (var i = 0; i < footprintLinePool.Count; i++)
            {
                if (footprintLinePool[i] != null)
                {
                    footprintLinePool[i].positionCount = 0;
                    footprintLinePool[i].enabled = false;
                }
            }
        }

        public void DrawEffectiveFogBoundaryAroundRevealer(
            CombatFogOfWarRevealer3D revealer,
            Color boundaryColor,
            bool drawForestFootprints = true)
        {
            if (lineParent == null || revealer == null)
            {
                ClearGameViewLines();
                return;
            }

            var maxSearchRadius = revealer.TotalRevealerRadius;
            if (maxSearchRadius <= 0f)
            {
                ClearGameViewLines();
                return;
            }

            EnsureContourLine();

            var eyeWorld = (Vector3)revealer.GetEyePosition();
            var groundY = FogOfWarWorld.instance != null
                ? FogOfWarWorld.instance.WorldBounds.center.y
                : eyeWorld.y;
            var drawY = groundY + MarkerYBoost;
            var originGround = new Vector3(eyeWorld.x, drawY, eyeWorld.z);

            CombatFogEffectiveBoundarySampler.BuildEffectiveFogBoundaryContour(
                revealer,
                eyeWorld,
                originGround,
                maxSearchRadius,
                0f,
                revealer.ShouldApplyForestClipThisFrame(),
                revealer.ShouldApplyBlockingTerrainClipThisFrame(),
                FogOfWarRevealer3D.Projection,
                effectiveContourPoints);

            ApplyContourLoop(effectiveContourLine, effectiveContourPoints, boundaryColor);
            if (drawForestFootprints)
            {
                BuildForestFootprintSegments(drawY, footprintSegments);
                ApplyFootprintSegments(new Color(0.2f, 0.85f, 1f, 0.9f));
            }
            else
            {
                for (var i = 0; i < footprintLinePool.Count; i++)
                {
                    if (footprintLinePool[i] != null)
                    {
                        footprintLinePool[i].positionCount = 0;
                        footprintLinePool[i].enabled = false;
                    }
                }
            }
        }

        private void BuildForestFootprintSegments(float drawY, List<(Vector3 start, Vector3 end)> segments)
        {
            segments.Clear();
            var activeZones = CombatZone.ActiveZones;
            for (var i = 0; i < activeZones.Count; i++)
            {
                var zone = activeZones[i];
                var feature = zone?.TerrainFeature;
                if (zone == null
                    || feature == null
                    || feature.LineOfSightMode != CombatTerrainLineOfSightMode.LimitedDepth)
                {
                    continue;
                }

                var corners = new List<Vector3>(8);
                zone.CollectFootprintCorners(corners);
                if (corners.Count < 2)
                {
                    continue;
                }

                for (var c = 0; c < corners.Count; c++)
                {
                    var start = corners[c];
                    var end = corners[(c + 1) % corners.Count];
                    start.y = drawY;
                    end.y = drawY;
                    segments.Add((start, end));
                }
            }
        }

        private void ApplyContourLoop(LineRenderer line, List<Vector3> points, Color color)
        {
            if (line == null)
            {
                return;
            }

            if (points.Count < 2)
            {
                line.positionCount = 0;
                line.enabled = false;
                return;
            }

            line.startColor = color;
            line.endColor = color;
            line.startWidth = EffectiveContourWidth;
            line.endWidth = EffectiveContourWidth;
            line.loop = true;
            line.positionCount = points.Count;
            for (var i = 0; i < points.Count; i++)
            {
                line.SetPosition(i, points[i]);
            }

            line.enabled = true;
        }

        private void ApplyFootprintSegments(Color footprintColor)
        {
            EnsureFootprintLinePool(footprintSegments.Count);

            for (var i = 0; i < footprintSegments.Count; i++)
            {
                var segment = footprintSegments[i];
                var line = footprintLinePool[i];
                line.startColor = footprintColor;
                line.endColor = footprintColor;
                line.startWidth = ForestFootprintWidth;
                line.endWidth = ForestFootprintWidth;
                line.positionCount = 2;
                line.SetPosition(0, segment.start);
                line.SetPosition(1, segment.end);
                line.enabled = true;
            }

            for (var i = footprintSegments.Count; i < footprintLinePool.Count; i++)
            {
                footprintLinePool[i].positionCount = 0;
                footprintLinePool[i].enabled = false;
            }
        }

        private void EnsureContourLine()
        {
            if (lineParent == null || effectiveContourLine != null)
            {
                return;
            }

            effectiveContourLine = CombatFogShaderUploadPolygonDrawer.CreateLoopLineRenderer(
                lineParent,
                "EffectiveFogBoundaryContour",
                Color.white,
                EffectiveContourWidth,
                loop: true);
            effectiveContourLine.enabled = false;
        }

        private void EnsureFootprintLinePool(int requiredCount)
        {
            while (footprintLinePool.Count < requiredCount)
            {
                var index = footprintLinePool.Count;
                var line = CombatFogShaderUploadPolygonDrawer.CreateLoopLineRenderer(
                    lineParent,
                    $"ForestFootprintEdge_{index}",
                    Color.cyan,
                    ForestFootprintWidth,
                    loop: false);
                line.positionCount = 0;
                line.enabled = false;
                footprintLinePool.Add(line);
            }
        }
    }
}
