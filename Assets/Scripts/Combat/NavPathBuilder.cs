using System;
using System.Collections.Generic;
using Pathfinding;
using Pathfinding.Pooling;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Builds funnel-smoothed navmesh paths between two world-space positions.
    /// Call <see cref="RequestAsync"/> to kick off a non-blocking path request (e.g. during
    /// movement preview) or <see cref="BuildSync"/> for an immediate result (e.g. on click-confirm).
    /// Neither method modifies the positions — no snapping is applied.
    /// </summary>
    public static class NavPathBuilder
    {
        /// <summary>
        /// Begins an asynchronous A* path request.  The <paramref name="onComplete"/> callback is
        /// invoked on the main thread when the path is ready.  The returned list contains the
        /// funnel-smoothed world-space waypoints with <paramref name="from"/> pinned as the first
        /// point.  Returns an empty list on error or when A* is unavailable.
        /// </summary>
        public static void RequestAsync(Vector3 from, Vector3 to, Action<List<Vector3>> onComplete)
        {
            if (AstarPath.active == null)
            {
                onComplete?.Invoke(new List<Vector3>());
                return;
            }

            var path = ABPath.Construct(from, to, p =>
            {
                var result = Smooth(p as ABPath, from);
                onComplete?.Invoke(result.Count >= 2 ? result : null);
            });
            AstarPath.StartPath(path);
        }

        /// <summary>
        /// Computes a funnel-smoothed path synchronously.  Blocks until A* finishes.
        /// Returns an empty list on error or when A* is unavailable.
        /// </summary>
        public static List<Vector3> BuildSync(Vector3 from, Vector3 to)
        {
            if (AstarPath.active == null)
            {
                return new List<Vector3>();
            }

            var path = ABPath.Construct(from, to);
            AstarPath.StartPath(path);
            AstarPath.BlockUntilCalculated(path);
            return Smooth(path, from);
        }

        // -----------------------------------------------------------------------------------------
        // Private helpers
        // -----------------------------------------------------------------------------------------

        private static List<Vector3> Smooth(ABPath path, Vector3 pinnedStart)
        {
            if (path == null || path.error || path.vectorPath == null || path.vectorPath.Count < 2)
            {
                return new List<Vector3>();
            }

            var smoothed = FunnelSmooth(path);
            if (smoothed.Count > 0)
            {
                smoothed[0] = pinnedStart;
            }

            return smoothed;
        }

        private static List<Vector3> FunnelSmooth(ABPath path)
        {
            if (path.path == null || path.path.Count == 0
                || path.vectorPath == null || path.vectorPath.Count < 2)
            {
                return new List<Vector3>(path.vectorPath ?? new List<Vector3>());
            }

            var parts = Funnel.SplitIntoParts(path);
            if (parts.Count == 0)
            {
                return new List<Vector3>(path.vectorPath);
            }

            var smoothed = new List<Vector3>();
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.type == Funnel.PartType.NodeSequence)
                {
                    var portals = Funnel.ConstructFunnelPortals(path.path, part);
                    var result = Funnel.Calculate(portals, splitAtEveryPortal: false);
                    smoothed.AddRange(result);
                    ListPool<Vector3>.Release(ref portals.left);
                    ListPool<Vector3>.Release(ref portals.right);
                    ListPool<Vector3>.Release(ref result);
                }
                else
                {
                    if (i == 0 || parts[i - 1].type == Funnel.PartType.OffMeshLink)
                    {
                        smoothed.Add(part.startPoint);
                    }

                    if (i == parts.Count - 1 || parts[i + 1].type == Funnel.PartType.OffMeshLink)
                    {
                        smoothed.Add(part.endPoint);
                    }
                }
            }

            ListPool<Funnel.PathPart>.Release(ref parts);
            return smoothed.Count >= 2 ? smoothed : new List<Vector3>(path.vectorPath);
        }
    }
}
