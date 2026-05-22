using System;
using System.Collections.Generic;
using Pathfinding;
using Pathfinding.Pooling;
using UnityEngine;
using UnityEngine.Analytics;

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

        [SerializeField] private FunnelModifier _funnel;

        /// <summary>
        /// Returns the first <see cref="NavPathBuilder"/> found in the scene.
        /// Cached after the first lookup.
        /// </summary>
        public void Awake()
        {
            if (instance == null)
            {
                instance = FindObjectOfType<NavPathBuilder>();
                if (instance == null)
                {
                    Debug.LogWarning(
                        "[NavPathBuilder] No NavPathBuilder instance found in the scene. " +
                        "Callers should provide or create one before requesting paths.");
                }
            }
            
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
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
        public void RequestAsync(Vector3 from, Vector3 to, Action<List<Vector3>> onComplete)
        {
            if (AstarPath.active == null)
            {
                onComplete?.Invoke(new List<Vector3>());
                return;
            }

            to = AstarPath.active.GetNearest(to).position;
            
            var path = ABPath.Construct(from, to, p =>
            {
                var result = Smooth(p as ABPath, from, to);
                onComplete?.Invoke(result.Count >= 2 ? result : null);
            });
            AstarPath.StartPath(path);
        }

        /// <summary>
        /// Computes a funnel-smoothed path synchronously.  Blocks until A* finishes.
        /// The first waypoint is pinned to <paramref name="from"/>.
        /// Returns an empty list on error or when A* is unavailable.
        /// </summary>
        public List<Vector3> BuildSync(Vector3 from, Vector3 to)
        {
            if (AstarPath.active == null)
            {
                return new List<Vector3>();
            }

            var path = ABPath.Construct(from, to);
            AstarPath.StartPath(path);
            AstarPath.BlockUntilCalculated(path);
            return Smooth(path, from, to);
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
            
            var smoothed = instance.FunnelSmooth(path);
            
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
        
    }
}
