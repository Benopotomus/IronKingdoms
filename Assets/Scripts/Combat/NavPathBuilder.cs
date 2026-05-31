using System;
using System.Collections.Generic;
using Pathfinding;
using Pathfinding.Pooling;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// MonoBehaviour that builds funnel-smoothed navmesh paths between two world-space positions.
    /// Attach one instance to a scene GameObject (e.g. CombatFlowBootstrap) and reference it via
    /// <see cref="TestLevelUnitController.navPathBuilder"/>.
    /// Call <see cref="RequestAsync"/> to kick off a non-blocking path request (e.g. during
    /// movement preview) or <see cref="BuildSync"/> for an immediate result (e.g. on click-confirm).
    /// The start point is pinned exactly; the destination remains on the computed walkable path.
    /// </summary>
    public class NavPathBuilder : MonoBehaviour
    {
        // -----------------------------------------------------------------------------------------
        // Singleton access
        // -----------------------------------------------------------------------------------------

        public static NavPathBuilder instance;
        private static bool pendingNavmeshUpdate;
        private readonly HashSet<ModelSize> missingGraphWarnings = new();

        [SerializeField] private FunnelModifier _funnel;
        [Header("Per-base navmesh graph names")]
        [SerializeField] private string base30GraphName = "Base30mm";
        [SerializeField] private string base40GraphName = "Base40mm";
        [SerializeField] private string base50GraphName = "Base50mm";
        [SerializeField] private string base80GraphName = "Base80mm";
        [SerializeField] private string base120GraphName = "Base120mm";

        /// <summary>
        /// Returns the first <see cref="NavPathBuilder"/> found in the scene.
        /// Cached after the first lookup.
        /// </summary>
        public void Awake()
        {
            if (instance == null)
            {
                instance = this;
            }
           
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        public static void MarkNavmeshDirty()
        {
            pendingNavmeshUpdate = true;
        }

        // -----------------------------------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Begins an asynchronous A* path request.  The <paramref name="onComplete"/> callback is
        /// invoked on the main thread when the path is ready.  The returned list contains the
        /// funnel-smoothed world-space waypoints with <paramref name="from"/> pinned as the first
        /// point.
        /// Returns an empty list on error or when A* is unavailable.
        /// </summary>
        public void RequestAsync(Vector3 from, Vector3 to, Action<List<Vector3>> onComplete, GraphMask? graphMask = null)
        {
            if (AstarPath.active == null)
            {
                onComplete?.Invoke(new List<Vector3>());
                return;
            }

            var activeGraphMask = graphMask ?? GraphMask.everything;
            FlushPendingNavmeshUpdates();
            to = SnapToWalkablePosition(to, activeGraphMask);
            
            var path = ABPath.Construct(from, to, p =>
            {
                var result = Smooth(p as ABPath, from, to);
                onComplete?.Invoke(result.Count >= 2 ? result : null);
            });
            var traversalConstraint = path.traversalConstraint;
            traversalConstraint.graphMask = activeGraphMask;
            path.traversalConstraint = traversalConstraint;
            AstarPath.StartPath(path);
        }

        /// <summary>
        /// Computes a funnel-smoothed path synchronously.  Blocks until A* finishes.
        /// The first waypoint is pinned to <paramref name="from"/>.
        /// Returns an empty list on error or when A* is unavailable.
        /// </summary>
        public List<Vector3> BuildSync(Vector3 from, Vector3 to, GraphMask? graphMask = null)
        {
            if (AstarPath.active == null)
            {
                return new List<Vector3>();
            }

            var activeGraphMask = graphMask ?? GraphMask.everything;
            FlushPendingNavmeshUpdates();
            to = SnapToWalkablePosition(to, activeGraphMask);
            var path = ABPath.Construct(from, to);
            var traversalConstraint = path.traversalConstraint;
            traversalConstraint.graphMask = activeGraphMask;
            path.traversalConstraint = traversalConstraint;
            AstarPath.StartPath(path);
            AstarPath.BlockUntilCalculated(path);
            return Smooth(path, from, to);
        }

        public GraphMask GetGraphMaskForModelSizeOrDefault(ModelSize modelSize)
        {
            return TryGetGraphMaskForModelSize(modelSize, out var graphMask)
                ? graphMask
                : GraphMask.everything;
        }

        public bool TryGetGraphMaskForModelSize(ModelSize modelSize, out GraphMask graphMask)
        {
            graphMask = GraphMask.everything;
            if (AstarPath.active?.data == null)
            {
                return false;
            }

            var graphName = GetGraphNameForModelSize(modelSize);
            if (string.IsNullOrWhiteSpace(graphName))
            {
                return false;
            }

            var graph = AstarPath.active.data.FindGraph(g => g != null && string.Equals(g.name, graphName, StringComparison.Ordinal));
            if (graph == null)
            {
                if (missingGraphWarnings.Add(modelSize))
                {
                    Debug.LogWarning($"No nav graph named '{graphName}' for {modelSize}. Falling back to all graphs.");
                }
                return false;
            }

            graphMask = GraphMask.FromGraph(graph);
            return true;
        }

        // -----------------------------------------------------------------------------------------
        // Private helpers
        // -----------------------------------------------------------------------------------------

        private static List<Vector3> Smooth(ABPath path, Vector3 pinnedStart, Vector3 pinnedEnd)
        {
            if (path == null || path.error || path.vectorPath == null || path.vectorPath.Count < 2)
            {
                return new List<Vector3> {pinnedStart, pinnedEnd};
            }

            path.vectorPath[0] = pinnedStart;
            path.vectorPath.Add(pinnedEnd);
            var smoothed = instance.FunnelSmooth(path);
            
            smoothed[0] = pinnedStart;
            smoothed[smoothed.Count - 1] = pinnedEnd;
            
            return smoothed;
        }

        private List<Vector3> FunnelSmooth(ABPath path)
        {
            if (path.path == null || path.path.Count == 0
                || path.vectorPath == null || path.vectorPath.Count < 2)
            {
                return new List<Vector3>(path.vectorPath ?? new List<Vector3>());
            }
            _funnel.Apply(path);
            return path.vectorPath;

        }

        private static void FlushPendingNavmeshUpdates()
        {
            if (AstarPath.active == null || !pendingNavmeshUpdate)
            {
                return;
            }

            var navmeshUpdates = AstarPath.active.navmeshUpdates;
            if (navmeshUpdates != null)
            {
                navmeshUpdates.ForceUpdate();
            }

            AstarPath.active.FlushGraphUpdates();
            pendingNavmeshUpdate = false;
        }

        /// <summary>
        /// Snaps a world-space point to the nearest walkable navmesh point.
        /// Falls back to any nearest node when no walkable node is found, then
        /// returns the original position if no node is available at all.
        /// </summary>
        private static Vector3 SnapToWalkablePosition(Vector3 worldPosition, GraphMask graphMask)
        {
            if (AstarPath.active == null)
            {
                return worldPosition;
            }

            var walkableConstraint = NearestNodeConstraint.Walkable;
            walkableConstraint.graphMask = graphMask;
            var walkableNearest = AstarPath.active.GetNearest(worldPosition, walkableConstraint);
            if (walkableNearest.node != null)
            {
                return walkableNearest.position;
            }

            var nearestConstraint = NearestNodeConstraint.None;
            nearestConstraint.graphMask = graphMask;
            var nearest = AstarPath.active.GetNearest(worldPosition, nearestConstraint);
            return nearest.node != null ? nearest.position : worldPosition;
        }

        private string GetGraphNameForModelSize(ModelSize modelSize)
        {
            return modelSize switch
            {
                ModelSize.Base30mm => base30GraphName,
                ModelSize.Base40mm => base40GraphName,
                ModelSize.Base50mm => base50GraphName,
                ModelSize.Base80mm => base80GraphName,
                ModelSize.Base120mm => base120GraphName,
                _ => string.Empty
            };
        }
        
    }
}
