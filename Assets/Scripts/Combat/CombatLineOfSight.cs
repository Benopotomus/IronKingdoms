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
    }

    public static class CombatLineOfSight
    {
        private const float DistanceEpsilon = 0.001f;

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
