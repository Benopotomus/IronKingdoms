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

        /// <summary>
        /// Builds a regular polygon on the XZ tabletop centered at local origin.
        /// <paramref name="diameterInches"/> is the flat-to-flat diameter on the board plane.
        /// </summary>
        public static List<Vector2> BuildRegularPolygonLocalVertices(
            float diameterInches,
            int segmentCount,
            float startAngleDegrees = 0f)
        {
            var vertices = new List<Vector2>(Mathf.Max(3, segmentCount));
            if (segmentCount < 3 || diameterInches <= 0f)
            {
                return vertices;
            }

            var radiusWorld = CombatScale.InchesToWorldUnits(diameterInches * 0.5f);
            var startRadians = startAngleDegrees * Mathf.Deg2Rad;
            var stepRadians = Mathf.PI * 2f / segmentCount;
            for (var i = 0; i < segmentCount; i++)
            {
                var angle = startRadians + stepRadians * i;
                vertices.Add(new Vector2(Mathf.Cos(angle) * radiusWorld, Mathf.Sin(angle) * radiusWorld));
            }

            return vertices;
        }

        public static bool ContainsPointLocal(Vector2 localPoint, IReadOnlyList<Vector2> localVertices)
        {
            if (!IsValidFootprint(localVertices))
            {
                return false;
            }

            var inside = false;
            for (int i = 0, j = localVertices.Count - 1; i < localVertices.Count; j = i++)
            {
                var a = localVertices[i];
                var b = localVertices[j];
                var intersects = (a.y > localPoint.y) != (b.y > localPoint.y)
                    && localPoint.x
                    < (b.x - a.x) * (localPoint.y - a.y) / (b.y - a.y + 1e-12f) + a.x;
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
            if (!IsValidFootprint(localVertices) || direction.sqrMagnitude <= 1e-12f)
            {
                return false;
            }

            direction.Normalize();
            var originInside = ContainsPointLocal(origin, localVertices);
            var bestEnter = float.MaxValue;
            var bestExit = float.MaxValue;

            for (var i = 0; i < localVertices.Count; i++)
            {
                var a = localVertices[i];
                var b = localVertices[(i + 1) % localVertices.Count];
                if (!CombatFogPlanarGeometry.TryRaySegmentHit(origin, direction, a, b, out var hitT))
                {
                    continue;
                }

                var edge = b - a;
                var cross = edge.x * direction.y - edge.y * direction.x;
                if (cross > 0f)
                {
                    if (hitT < bestEnter)
                    {
                        bestEnter = hitT;
                    }
                }
                else if (cross < 0f)
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
            var area = 0f;
            for (var i = 0; i < localVertices.Count; i++)
            {
                var a = localVertices[i];
                var b = localVertices[(i + 1) % localVertices.Count];
                area += a.x * b.y - b.x * a.y;
            }

            return area * 0.5f;
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
