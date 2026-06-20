using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// XZ planar ray/segment helpers for fog boundary geometry.
    /// </summary>
    internal static class CombatFogPlanarGeometry
    {
        public static bool TryRaySegmentHit(
            Vector2 origin,
            Vector2 direction,
            Vector2 segmentStart,
            Vector2 segmentEnd,
            out float distanceAlongRay)
        {
            distanceAlongRay = -1f;
            var segment = segmentEnd - segmentStart;
            var denom = direction.x * segment.y - direction.y * segment.x;
            if (Mathf.Abs(denom) <= 1e-8f)
            {
                return false;
            }

            var diff = segmentStart - origin;
            var segmentT = (diff.x * direction.y - diff.y * direction.x) / denom;
            if (segmentT < 0f || segmentT > 1f)
            {
                return false;
            }

            var rayT = (diff.x * segment.y - diff.y * segment.x) / denom;
            if (rayT < 0f)
            {
                return false;
            }

            distanceAlongRay = rayT;
            return true;
        }

        public static bool TryRayDiscInterval(
            Vector2 origin,
            Vector2 direction,
            Vector2 center,
            float radius,
            out float enter,
            out float exit)
        {
            enter = -1f;
            exit = -1f;
            if (radius <= 1e-6f)
            {
                return false;
            }

            var dirLen = direction.magnitude;
            if (dirLen <= 1e-8f)
            {
                return false;
            }

            var dir = direction / dirLen;
            var oc = origin - center;
            var b = 2f * Vector2.Dot(oc, dir);
            var c = oc.sqrMagnitude - radius * radius;
            var discriminant = b * b - 4f * c;
            if (discriminant < 0f)
            {
                return false;
            }

            var sqrt = Mathf.Sqrt(discriminant);
            var t0 = (-b - sqrt) * 0.5f;
            var t1 = (-b + sqrt) * 0.5f;
            if (t0 > t1)
            {
                (t0, t1) = (t1, t0);
            }

            enter = t0;
            exit = t1;
            return true;
        }

        public static bool TryRayAabbInterval(
            Vector3 origin,
            Vector3 direction,
            Vector3 min,
            Vector3 max,
            out float enter,
            out float exit)
        {
            enter = -1f;
            exit = -1f;
            if (direction.sqrMagnitude <= 1e-12f)
            {
                return false;
            }

            var tMin = 0f;
            var tMax = float.MaxValue;

            if (!SlabInterval(origin.x, direction.x, min.x, max.x, ref tMin, ref tMax)
                || !SlabInterval(origin.y, direction.y, min.y, max.y, ref tMin, ref tMax)
                || !SlabInterval(origin.z, direction.z, min.z, max.z, ref tMin, ref tMax))
            {
                return false;
            }

            enter = tMin;
            exit = tMax;
            return true;
        }

        public static bool TryRayBoxColliderInterval(
            Vector3 worldOrigin,
            Vector3 worldDirection,
            BoxCollider box,
            out float enter,
            out float exit)
        {
            return TryRayOrientedBoxFootprintInterval(worldOrigin, worldDirection, box, out enter, out exit);
        }

        /// <summary>
        /// XZ tabletop footprint interval for an oriented box — same plane as
        /// <see cref="CombatZone.CollectFootprintCorners"/>.
        /// </summary>
        public static bool TryRayOrientedBoxFootprintInterval(
            Vector3 worldOrigin,
            Vector3 worldDirection,
            BoxCollider box,
            out float enter,
            out float exit)
        {
            enter = -1f;
            exit = -1f;
            if (box == null || worldDirection.sqrMagnitude <= 1e-12f)
            {
                return false;
            }

            var transform = box.transform;
            var localOrigin = transform.InverseTransformPoint(worldOrigin);
            var localDirection = transform.InverseTransformDirection(worldDirection);
            localOrigin.y = 0f;
            localDirection.y = 0f;
            if (localDirection.sqrMagnitude <= 1e-12f)
            {
                return false;
            }

            var center = box.center;
            var half = box.size * 0.5f;
            var min = new Vector3(center.x - half.x, 0f, center.z - half.z);
            var max = new Vector3(center.x + half.x, 0f, center.z + half.z);
            return TryRayAabbInterval(localOrigin, localDirection, min, max, out enter, out exit);
        }

        private static bool SlabInterval(
            float origin,
            float direction,
            float min,
            float max,
            ref float tMin,
            ref float tMax)
        {
            if (Mathf.Abs(direction) <= 1e-8f)
            {
                return origin >= min && origin <= max;
            }

            var inverse = 1f / direction;
            var t1 = (min - origin) * inverse;
            var t2 = (max - origin) * inverse;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            return tMin <= tMax;
        }

        public static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
        {
            var segment = segmentEnd - segmentStart;
            var lengthSq = segment.sqrMagnitude;
            if (lengthSq <= 1e-8f)
            {
                return segmentStart;
            }

            var t = Mathf.Clamp01(Vector2.Dot(point - segmentStart, segment) / lengthSq);
            return segmentStart + segment * t;
        }

        /// <summary>
        /// Conservative slab test: true when a horizontal ray may hit the XZ AABB within maxDistance.
        /// </summary>
        public static bool RayMayHitHorizontalAabb(
            Vector2 origin,
            Vector2 direction,
            float maxDistance,
            float minX,
            float maxX,
            float minZ,
            float maxZ)
        {
            if (maxDistance <= 0f)
            {
                return false;
            }

            var dirX = direction.x;
            var dirZ = direction.y;
            var tMin = 0f;
            var tMax = maxDistance;

            if (!SlabAxis(origin.x, dirX, minX, maxX, ref tMin, ref tMax))
            {
                return false;
            }

            if (!SlabAxis(origin.y, dirZ, minZ, maxZ, ref tMin, ref tMax))
            {
                return false;
            }

            return tMax >= 0f && tMin <= maxDistance;
        }

        private static bool SlabAxis(float origin, float direction, float min, float max, ref float tMin, ref float tMax)
        {
            if (Mathf.Abs(direction) <= 1e-8f)
            {
                return origin >= min && origin <= max;
            }

            var inv = 1f / direction;
            var t1 = (min - origin) * inv;
            var t2 = (max - origin) * inv;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            return tMin <= tMax;
        }
    }
}
