using System.Collections.Generic;
using UnityEngine;

namespace IronKingdoms.Combat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class CombatZone : MonoBehaviour
    {
        private static readonly List<CombatZone> ActiveZoneRegistry = new();
        private const float PointContainmentThresholdSqr = 0.0001f;

        [SerializeField] private CombatZoneType zoneType = CombatZoneType.RoughTerrain;
        [SerializeField, Min(0.01f)] private float movementSpeedMultiplier = 0.5f;

        private Collider zoneCollider;

        public static IReadOnlyList<CombatZone> ActiveZones => ActiveZoneRegistry;
        public CombatZoneType ZoneType => zoneType;
        public bool IsMovementZone => zoneType == CombatZoneType.RoughTerrain;
        public float MovementSpeedMultiplier => zoneType == CombatZoneType.RoughTerrain ? movementSpeedMultiplier : 1f;

        private void Awake()
        {
            zoneCollider = GetComponent<Collider>();
            if (zoneCollider != null)
            {
                zoneCollider.isTrigger = true;
            }
        }

        private void OnEnable()
        {
            if (!ActiveZoneRegistry.Contains(this))
            {
                ActiveZoneRegistry.Add(this);
            }
        }

        private void OnDisable()
        {
            ActiveZoneRegistry.Remove(this);
        }

        public bool ContainsPoint(Vector3 worldPoint)
        {
            if (!isActiveAndEnabled)
            {
                return false;
            }

            if (zoneCollider == null || !zoneCollider.enabled)
            {
                return false;
            }

            var closestPoint = zoneCollider.ClosestPoint(worldPoint);
            return (closestPoint - worldPoint).sqrMagnitude <= PointContainmentThresholdSqr;
        }

        /// <summary>
        /// Returns true if a horizontal disc (XZ plane) centred at <paramref name="center"/>
        /// with the given <paramref name="radius"/> overlaps this zone.
        /// When <paramref name="radius"/> is zero this is equivalent to <see cref="ContainsPoint"/>.
        /// </summary>
        public bool IntersectsDisc(Vector3 center, float radius)
        {
            if (!isActiveAndEnabled)
            {
                return false;
            }

            if (zoneCollider == null || !zoneCollider.enabled)
            {
                return false;
            }

            var closestPoint = zoneCollider.ClosestPoint(center);
            var dx = closestPoint.x - center.x;
            var dz = closestPoint.z - center.z;
            return dx * dx + dz * dz <= radius * radius + PointContainmentThresholdSqr;
        }
    }
}
