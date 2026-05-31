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
    }
}
