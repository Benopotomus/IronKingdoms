using System.Collections.Generic;
using UnityEngine;

namespace IronKingdoms.Combat
{
    public readonly struct CombatLineOfSightVolume
    {
        public CombatLineOfSightVolume(Vector3 position, float radius, float height, float baseDiameterMillimeters)
        {
            Position = position;
            Radius = Mathf.Max(0f, radius);
            Height = Mathf.Max(0f, height);
            BaseDiameterMillimeters = Mathf.Max(0f, baseDiameterMillimeters);
        }

        public Vector3 Position { get; }
        public float Radius { get; }
        public float Height { get; }
        public float BaseDiameterMillimeters { get; }
        public Vector3 SightPoint => Position + Vector3.up * (Height * 0.5f);

        public Vector3 GetPlanarEdgeToward(Vector3 targetCenter)
        {
            var delta = targetCenter - Position;
            delta.y = 0f;
            if (delta.sqrMagnitude <= 0.001f)
            {
                return Position;
            }

            return Position + delta.normalized * Radius;
        }
    }

    public static class CombatLineOfSight
    {
        private const float DistanceEpsilon = 0.001f;

        public static void GetPlanarBaseEdgePoints(
            CombatLineOfSightVolume origin,
            CombatLineOfSightVolume target,
            out Vector3 originEdge,
            out Vector3 targetEdge)
        {
            originEdge = origin.GetPlanarEdgeToward(target.Position);
            targetEdge = target.GetPlanarEdgeToward(origin.Position);
        }

        public static float GetPlanarEdgeToEdgeDistanceWorld(CombatLineOfSightVolume origin, CombatLineOfSightVolume target)
        {
            var delta = target.Position - origin.Position;
            delta.y = 0f;
            return Mathf.Max(0f, delta.magnitude - origin.Radius - target.Radius);
        }

        public static float GetPlanarEdgeToEdgeDistanceInches(CombatLineOfSightVolume origin, CombatLineOfSightVolume target)
        {
            return CombatScale.WorldUnitsToInches(GetPlanarEdgeToEdgeDistanceWorld(origin, target));
        }

        public static Vector3 GetSightPointAtPlanarEdgeToward(CombatLineOfSightVolume origin, Vector3 targetCenter)
        {
            var edge = origin.GetPlanarEdgeToward(targetCenter);
            return new Vector3(edge.x, origin.SightPoint.y, edge.z);
        }

        public static CombatLineOfSightVolume CreateVolume(Vector3 basePosition, ModelSize modelSize)
        {
            return new CombatLineOfSightVolume(
                basePosition,
                modelSize.BaseDiameterWorldUnits() * 0.5f,
                modelSize.VolumeHeightWorldUnits(),
                modelSize.BaseDiameterMillimeters());
        }

        public static bool IsBlockedByInterveningModel(
            CombatLineOfSightVolume origin,
            CombatLineOfSightVolume target,
            CombatLineOfSightVolume candidate)
        {
            if (candidate.BaseDiameterMillimeters + DistanceEpsilon < target.BaseDiameterMillimeters)
            {
                return false;
            }

            return SegmentIntersectsCircle(origin.Position, target.Position, candidate.Position, candidate.Radius + DistanceEpsilon);
        }

        public static bool HasLineOfSight(
            CombatLineOfSightVolume origin,
            CombatLineOfSightVolume target,
            IReadOnlyList<CombatLineOfSightVolume> interveningModels)
        {
            if (interveningModels == null)
            {
                return true;
            }

            for (var i = 0; i < interveningModels.Count; i++)
            {
                if (IsBlockedByInterveningModel(origin, target, interveningModels[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool SegmentIntersectsCircle(Vector3 start, Vector3 end, Vector3 center, float radius)
        {
            var start2 = new Vector2(start.x, start.z);
            var end2 = new Vector2(end.x, end.z);
            var center2 = new Vector2(center.x, center.z);
            var delta = end2 - start2;
            var lengthSquared = delta.sqrMagnitude;
            if (lengthSquared <= DistanceEpsilon)
            {
                return (center2 - start2).sqrMagnitude <= radius * radius;
            }

            var t = Mathf.Clamp01(Vector2.Dot(center2 - start2, delta) / lengthSquared);
            var nearest = start2 + delta * t;
            return (center2 - nearest).sqrMagnitude <= radius * radius;
        }
    }
}
