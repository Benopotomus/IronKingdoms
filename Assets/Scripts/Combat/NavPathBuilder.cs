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

        /// <summary>
        /// Returns true when a straight segment from <paramref name="from"/> to <paramref name="to"/>
        /// lies entirely on walkable navmesh for <paramref name="graphMask"/> (Mk4 charge movement).
        /// </summary>
        public bool IsStraightLineFullyOnNavmesh(Vector3 from, Vector3 to, GraphMask graphMask, float endpointTolerance = 0.02f)
        {
            if (AstarPath.active == null)
            {
                return false;
            }

            FlushPendingNavmeshUpdates();

            var lineEnd = to;
            lineEnd.y = from.y;

            var walkableConstraint = NearestNodeConstraint.Walkable;
            walkableConstraint.graphMask = graphMask;

            var startNearest = AstarPath.active.GetNearest(from, walkableConstraint);
            var endNearest = AstarPath.active.GetNearest(lineEnd, walkableConstraint);
            if (startNearest.node == null || endNearest.node == null)
            {
                return false;
            }

            if (HorizontalDistanceXZ(from, startNearest.position) > endpointTolerance
                || HorizontalDistanceXZ(lineEnd, endNearest.position) > endpointTolerance)
            {
                return false;
            }

            var raycastGraph = GetRaycastableGraph(graphMask);
            if (raycastGraph == null)
            {
                return false;
            }

            var traversalConstraint = TraversalConstraint.None;
            traversalConstraint.graphMask = graphMask;

            var lineStart = startNearest.position;
            lineEnd = endNearest.position;
            lineEnd.y = lineStart.y;

            return !raycastGraph.Linecast(lineStart, lineEnd, out _, ref traversalConstraint, null);
        }

        /// <summary>
        /// Resolves a straight-line charge destination from <paramref name="from"/> toward
        /// <paramref name="clickPosition"/>. The click direction defines the ray; the result is
        /// the farthest point on that ray (up to the click distance) where the full segment stays
        /// on the navmesh, snapping the destination instead of rejecting off-mesh clicks.
        /// </summary>
        public bool TryResolveStraightLineChargeDestination(
            Vector3 from,
            Vector3 clickPosition,
            GraphMask graphMask,
            out Vector3 resolvedDestination,
            float endpointTolerance = 0.02f)
        {
            resolvedDestination = clickPosition;
            if (AstarPath.active == null)
            {
                return false;
            }

            FlushPendingNavmeshUpdates();

            var flatEnd = clickPosition;
            flatEnd.y = from.y;
            var deltaX = flatEnd.x - from.x;
            var deltaZ = flatEnd.z - from.z;
            var maxClickDistance = Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
            if (maxClickDistance <= endpointTolerance)
            {
                return false;
            }

            var dirX = deltaX / maxClickDistance;
            var dirZ = deltaZ / maxClickDistance;

            if (!TryFindFarthestValidStraightLineDistance(
                    from,
                    dirX,
                    dirZ,
                    maxClickDistance,
                    graphMask,
                    endpointTolerance,
                    out var validDistance))
            {
                return false;
            }

            resolvedDestination = new Vector3(
                from.x + dirX * validDistance,
                from.y,
                from.z + dirZ * validDistance);
            return true;
        }

        private static bool TryFindFarthestValidStraightLineDistance(
            Vector3 from,
            float dirX,
            float dirZ,
            float maxDistance,
            GraphMask graphMask,
            float endpointTolerance,
            out float validDistance)
        {
            validDistance = 0f;
            if (maxDistance <= endpointTolerance)
            {
                return false;
            }

            var instance = NavPathBuilder.instance;
            if (instance == null)
            {
                return false;
            }

            if (instance.IsStraightLineFullyOnNavmesh(
                    from,
                    PointOnChargeRay(from, dirX, dirZ, maxDistance),
                    graphMask,
                    endpointTolerance))
            {
                validDistance = maxDistance;
                return true;
            }

            var low = 0f;
            var high = maxDistance;
            for (var iteration = 0; iteration < 16; iteration++)
            {
                var mid = (low + high) * 0.5f;
                if (instance.IsStraightLineFullyOnNavmesh(
                        from,
                        PointOnChargeRay(from, dirX, dirZ, mid),
                        graphMask,
                        endpointTolerance))
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            if (low <= endpointTolerance)
            {
                return false;
            }

            validDistance = low;
            return true;
        }

        private static Vector3 PointOnChargeRay(Vector3 from, float dirX, float dirZ, float distance)
        {
            return new Vector3(from.x + dirX * distance, from.y, from.z + dirZ * distance);
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

        private static IRaycastableGraph GetRaycastableGraph(GraphMask graphMask)
        {
            var graphs = AstarPath.active?.data?.graphs;
            if (graphs == null)
            {
                return null;
            }

            for (var i = 0; i < graphs.Length; i++)
            {
                var graph = graphs[i];
                if (graph != null && graphMask.Contains(graph) && graph is IRaycastableGraph raycastable)
                {
                    return raycastable;
                }
            }

            return null;
        }

        private static float HorizontalDistanceXZ(Vector3 a, Vector3 b)
        {
            return new Vector2(a.x - b.x, a.z - b.z).magnitude;
        }
        
    }
}
