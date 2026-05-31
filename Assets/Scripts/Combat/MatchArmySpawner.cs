using System.Collections.Generic;
using Pathfinding;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Computes start-of-match unit spawn positions that honor base footprint spacing
    /// and model-size navmesh graph constraints.
    /// </summary>
    public sealed class MatchArmySpawner
    {
        private const float MinimumSpawnRadius = 0.1f;
        private const float AdditionalBaseClearance = 0.05f;
        private const float SpawnLineSearchStep = 0.25f;
        private const int MaxPlacementAttempts = 64;

        private readonly NavPathBuilder navPathBuilder;

        public MatchArmySpawner(NavPathBuilder navPathBuilder)
        {
            this.navPathBuilder = navPathBuilder;
        }

        public List<SpawnPlacement> BuildPlacements(IReadOnlyList<UnitTypeDefinition> units, Transform anchor, float minimumCenterSpacing)
        {
            var placements = new List<SpawnPlacement>();
            if (units == null || units.Count == 0)
            {
                return placements;
            }

            var origin = anchor == null ? Vector3.zero : anchor.position;
            var lineDirection = anchor == null ? Vector3.right : anchor.right;
            if (lineDirection.sqrMagnitude < 0.0001f)
            {
                lineDirection = Vector3.right;
            }

            lineDirection.Normalize();

            var safeCenterSpacing = Mathf.Max(0f, minimumCenterSpacing);

            for (var i = 0; i < units.Count; i++)
            {
                var unitDefinition = units[i];
                if (unitDefinition == null)
                {
                    continue;
                }

                var unitRadius = GetSpawnRadius(unitDefinition);
                var startDistance = 0f;
                if (placements.Count > 0)
                {
                    var previous = placements[placements.Count - 1];
                    startDistance = previous.LineDistance + previous.Radius + unitRadius + AdditionalBaseClearance;
                }

                var placed = false;
                var candidateDistance = startDistance;
                for (var attempt = 0; attempt < MaxPlacementAttempts; attempt++)
                {
                    var candidate = origin + lineDirection * candidateDistance;
                    var snapped = SnapToSpawnGraph(candidate, unitDefinition.Stats.modelSize);
                    if (HasRequiredSpacing(snapped, unitRadius, placements, safeCenterSpacing))
                    {
                        placements.Add(new SpawnPlacement(unitDefinition, snapped, unitRadius, candidateDistance));
                        placed = true;
                        break;
                    }

                    candidateDistance += unitRadius + AdditionalBaseClearance;
                }

                if (!placed)
                {
                    var fallback = origin + lineDirection * candidateDistance;
                    var snappedFallback = SnapToSpawnGraph(fallback, unitDefinition.Stats.modelSize);
                    Debug.LogWarning(
                        $"Failed to find fully clear spawn slot for '{unitDefinition.DisplayName}' after {MaxPlacementAttempts} attempts; using best-effort placement.");
                    placements.Add(new SpawnPlacement(unitDefinition, snappedFallback, unitRadius, candidateDistance));
                }
            }

            return placements;
        }

        private bool HasRequiredSpacing(Vector3 position, float radius, IReadOnlyList<SpawnPlacement> placements, float minimumCenterSpacing)
        {
            for (var i = 0; i < placements.Count; i++)
            {
                var existing = placements[i];
                var requiredDistance = Mathf.Max(
                    minimumCenterSpacing,
                    existing.Radius + radius + AdditionalBaseClearance);
                var horizontalDistance = Vector2.Distance(
                    new Vector2(existing.Position.x, existing.Position.z),
                    new Vector2(position.x, position.z));
                if (horizontalDistance < requiredDistance)
                {
                    return false;
                }
            }

            return true;
        }

        private Vector3 SnapToSpawnGraph(Vector3 worldPosition, ModelSize modelSize)
        {
            if (AstarPath.active == null)
            {
                return worldPosition;
            }

            var graphMask = navPathBuilder != null
                ? navPathBuilder.GetGraphMaskForModelSizeOrDefault(modelSize)
                : GraphMask.everything;
            var walkableConstraint = NearestNodeConstraint.Walkable;
            walkableConstraint.graphMask = graphMask;
            var nearest = AstarPath.active.GetNearest(worldPosition, walkableConstraint);
            return nearest.node != null ? nearest.position : worldPosition;
        }

        private static float GetSpawnRadius(UnitTypeDefinition unitDefinition)
        {
            if (unitDefinition == null)
            {
                return MinimumSpawnRadius;
            }

            return Mathf.Max(
                MinimumSpawnRadius,
                unitDefinition.Stats.modelSize.GetPawnScale().x * 0.5f);
        }

        public readonly struct SpawnPlacement
        {
            public SpawnPlacement(UnitTypeDefinition unitDefinition, Vector3 position, float radius, float lineDistance)
            {
                UnitDefinition = unitDefinition;
                Position = position;
                Radius = radius;
                LineDistance = lineDistance;
            }

            public UnitTypeDefinition UnitDefinition { get; }
            public Vector3 Position { get; }
            public float Radius { get; }
            public float LineDistance { get; }
        }
    }
}
