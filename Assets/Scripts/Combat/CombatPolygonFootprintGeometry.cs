using System.Collections.Generic;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// XZ polygon helpers for combat zone footprints (point tests, ray intervals, triangulation).
    /// </summary>
    public static class CombatPolygonFootprintGeometry
    {
        public static bool IsValidFootprint(IReadOnlyList<Vector2> localVertices)
        {
            return localVertices != null && localVertices.Count >= 3;
        }

        public static bool ContainsPointLocal(Vector2 localPoint, IReadOnlyList<Vector2> localVertices)
        {
            return localVertices != null
                && ContainsPointLocal(localPoint, localVertices, 0, localVertices.Count);
        }

        /// <summary>Thread-safe slice into a shared vertex buffer (parallel fog clip).</summary>
        public static bool ContainsPointLocal(
            Vector2 localPoint,
            IReadOnlyList<Vector2> vertices,
            int start,
            int count)
        {
            if (vertices == null || count < 3 || start < 0 || start + count > vertices.Count)
            {
                return false;
            }

            var inside = false;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                var a = vertices[start + i];
                var b = vertices[start + j];
                var dy = b.y - a.y;
                if (Mathf.Abs(dy) <= 1e-8f)
                {
                    continue;
                }

                var intersects = (a.y > localPoint.y) != (b.y > localPoint.y)
                    && localPoint.x < (b.x - a.x) * (localPoint.y - a.y) / dy + a.x;
                if (intersects)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        public static bool TryRayPolygonIntervalLocal(
            Vector2 origin,
            Vector2 direction,
            IReadOnlyList<Vector2> localVertices,
            out float enter,
            out float exit)
        {
            enter = -1f;
            exit = -1f;
            return localVertices != null
                && TryRayPolygonIntervalLocal(origin, direction, localVertices, 0, localVertices.Count, out enter, out exit);
        }

        /// <summary>Thread-safe slice into a shared vertex buffer (parallel fog clip).</summary>
        public static bool TryRayPolygonIntervalLocal(
            Vector2 origin,
            Vector2 direction,
            IReadOnlyList<Vector2> vertices,
            int start,
            int count,
            out float enter,
            out float exit)
        {
            enter = -1f;
            exit = -1f;
            if (vertices == null || count < 3 || start < 0 || start + count > vertices.Count
                || direction.sqrMagnitude <= 1e-12f)
            {
                return false;
            }

            var dirLen = direction.magnitude;
            if (dirLen <= 1e-8f)
            {
                return false;
            }

            var dirX = direction.x / dirLen;
            var dirY = direction.y / dirLen;
            var isCounterClockwise = SignedAreaLocal(vertices, start, count) > 0f;
            var originInside = ContainsPointLocal(origin, vertices, start, count);
            var bestEnter = float.MaxValue;
            var bestExit = float.MaxValue;

            for (var i = 0; i < count; i++)
            {
                var a = vertices[start + i];
                var b = vertices[start + (i + 1 < count ? i + 1 : 0)];
                if (!CombatFogPlanarGeometry.TryRaySegmentHit(
                        origin,
                        new Vector2(dirX, dirY),
                        a,
                        b,
                        out var hitT))
                {
                    continue;
                }

                var edge = b - a;
                var cross = edge.x * dirY - edge.y * dirX;
                if (Mathf.Approximately(cross, 0f))
                {
                    continue;
                }

                var crossingEntering = cross > 0f;
                if (!isCounterClockwise)
                {
                    crossingEntering = !crossingEntering;
                }

                if (crossingEntering)
                {
                    if (hitT < bestEnter)
                    {
                        bestEnter = hitT;
                    }
                }
                else
                {
                    if (hitT < bestExit)
                    {
                        bestExit = hitT;
                    }
                }
            }

            if (originInside)
            {
                enter = 0f;
                exit = bestExit < float.MaxValue ? bestExit : -1f;
                return exit >= 0f;
            }

            enter = bestEnter < float.MaxValue ? bestEnter : -1f;
            exit = bestExit < float.MaxValue ? bestExit : -1f;
            return enter >= 0f || exit >= 0f;
        }

        public static bool TryTriangulateSimplePolygonLocal(
            IReadOnlyList<Vector2> localVertices,
            List<int> triangleIndices)
        {
            triangleIndices.Clear();
            if (!IsValidFootprint(localVertices))
            {
                return false;
            }

            var remaining = new List<int>(localVertices.Count);
            for (var i = 0; i < localVertices.Count; i++)
            {
                remaining.Add(i);
            }

            var signedArea = SignedAreaLocal(localVertices);
            var isCounterClockwise = signedArea > 0f;
            var guard = 0;
            while (remaining.Count > 3 && guard++ < localVertices.Count * localVertices.Count)
            {
                var earFound = false;
                for (var i = 0; i < remaining.Count; i++)
                {
                    var prev = remaining[(i - 1 + remaining.Count) % remaining.Count];
                    var curr = remaining[i];
                    var next = remaining[(i + 1) % remaining.Count];
                    if (!IsConvexEar(prev, curr, next, localVertices, isCounterClockwise))
                    {
                        continue;
                    }

                    if (EarContainsAnyOtherVertex(prev, curr, next, localVertices, remaining))
                    {
                        continue;
                    }

                    triangleIndices.Add(prev);
                    triangleIndices.Add(curr);
                    triangleIndices.Add(next);
                    remaining.RemoveAt(i);
                    earFound = true;
                    break;
                }

                if (!earFound)
                {
                    return false;
                }
            }

            if (remaining.Count == 3)
            {
                triangleIndices.Add(remaining[0]);
                triangleIndices.Add(remaining[1]);
                triangleIndices.Add(remaining[2]);
            }

            return triangleIndices.Count >= 3;
        }

        public static float SignedAreaLocal(IReadOnlyList<Vector2> localVertices)
        {
            return localVertices == null ? 0f : SignedAreaLocal(localVertices, 0, localVertices.Count);
        }

        public static float SignedAreaLocal(IReadOnlyList<Vector2> vertices, int start, int count)
        {
            if (vertices == null || count < 3 || start < 0 || start + count > vertices.Count)
            {
                return 0f;
            }

            var area = 0f;
            for (var i = 0; i < count; i++)
            {
                var a = vertices[start + i];
                var b = vertices[start + (i + 1 < count ? i + 1 : 0)];
                area += a.x * b.y - b.x * a.y;
            }

            return area * 0.5f;
        }

        public static bool IsConvexFootprintLocal(IReadOnlyList<Vector2> localVertices)
        {
            if (!IsValidFootprint(localVertices))
            {
                return false;
            }

            var sign = 0f;
            for (var i = 0; i < localVertices.Count; i++)
            {
                var a = localVertices[i];
                var b = localVertices[(i + 1) % localVertices.Count];
                var c = localVertices[(i + 2) % localVertices.Count];
                var cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                if (Mathf.Abs(cross) <= 1e-6f)
                {
                    continue;
                }

                var crossSign = Mathf.Sign(cross);
                if (Mathf.Approximately(sign, 0f))
                {
                    sign = crossSign;
                }
                else if (!Mathf.Approximately(crossSign, sign))
                {
                    return false;
                }
            }

            return !Mathf.Approximately(sign, 0f);
        }

        private static bool IsConvexEar(
            int prev,
            int curr,
            int next,
            IReadOnlyList<Vector2> vertices,
            bool isCounterClockwise)
        {
            var a = vertices[prev];
            var b = vertices[curr];
            var c = vertices[next];
            var cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            return isCounterClockwise ? cross > 1e-8f : cross < -1e-8f;
        }

        private static bool EarContainsAnyOtherVertex(
            int prev,
            int curr,
            int next,
            IReadOnlyList<Vector2> vertices,
            List<int> remaining)
        {
            var a = vertices[prev];
            var b = vertices[curr];
            var c = vertices[next];
            for (var i = 0; i < remaining.Count; i++)
            {
                var index = remaining[i];
                if (index == prev || index == curr || index == next)
                {
                    continue;
                }

                if (ContainsPointInTriangle(vertices[index], a, b, c))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsPointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            var area = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
            if (area <= 1e-8f)
            {
                return false;
            }

            var w1 = ((c.x - b.x) * (point.y - b.y) - (c.y - b.y) * (point.x - b.x)) / area;
            var w2 = ((a.x - c.x) * (point.y - c.y) - (a.y - c.y) * (point.x - c.x)) / area;
            var w3 = 1f - w1 - w2;
            const float margin = -1e-4f;
            return w1 > margin && w2 > margin && w3 > margin;
        }
    }
}
