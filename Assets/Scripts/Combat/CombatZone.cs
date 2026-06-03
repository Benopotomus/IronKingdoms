using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace IronKingdoms.Combat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class CombatZone : MonoBehaviour
    {
        private static readonly List<CombatZone> ActiveZoneRegistry = new();
        private const float PointContainmentThresholdSqr = 0.0001f;
        private const int UnitContainmentSampleCount = 8;

        [SerializeField] private CombatTerrainFeatureDefinition terrainFeature;
        [FormerlySerializedAs("zoneType")]
        [SerializeField] private CombatZoneType legacyZoneType = CombatZoneType.None;

        private Collider zoneCollider;

        public static IReadOnlyList<CombatZone> ActiveZones => ActiveZoneRegistry;
        public CombatTerrainFeatureDefinition TerrainFeature => terrainFeature;
        public CombatZoneType LegacyZoneType => legacyZoneType;

        public bool IsRoughTerrain => terrainFeature != null
            ? terrainFeature.IsRoughTerrain
            : legacyZoneType == CombatZoneType.RoughTerrain;

        public float MovementSpeedMultiplier
        {
            get
            {
                if (terrainFeature != null)
                {
                    return terrainFeature.MovementSpeedMultiplier;
                }

                return legacyZoneType == CombatZoneType.RoughTerrain ? 0.5f : 1f;
            }
        }

        public bool IsMovementZone => IsRoughTerrain;

        private void Awake()
        {
            zoneCollider = GetComponent<Collider>();
            if (zoneCollider != null)
            {
                zoneCollider.isTrigger = true;
            }

            EnsureLegacyTerrainFeatureResolved();
        }

        private void OnEnable()
        {
            EnsureLegacyTerrainFeatureResolved();
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

            // Terrain zones are flat on the XZ table; project to tabletop height so samples from
            // fog rays, LOS checks, and model volumes above the zone still hit the footprint.
            var bounds = zoneCollider.bounds;
            var testPoint = worldPoint;
            testPoint.y = bounds.center.y;

            var closestPoint = zoneCollider.ClosestPoint(testPoint);
            return (closestPoint - testPoint).sqrMagnitude <= PointContainmentThresholdSqr;
        }

        public bool ContainsUnitCompletely(Vector3 center, float radius)
        {
            if (!ContainsPoint(center))
            {
                return false;
            }

            if (radius <= PointContainmentThresholdSqr)
            {
                return true;
            }

            for (var i = 0; i < UnitContainmentSampleCount; i++)
            {
                var angle = (Mathf.PI * 2f * i) / UnitContainmentSampleCount;
                var edgePoint = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                if (!ContainsPoint(edgePoint))
                {
                    return false;
                }
            }

            return true;
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
            return dx * dx + dz * dz <= radius * radius;
        }

        private void EnsureLegacyTerrainFeatureResolved()
        {
            if (terrainFeature != null || legacyZoneType == CombatZoneType.None)
            {
                return;
            }

            var catalog = CombatDefinitionCatalog.Instance;
            if (catalog == null)
            {
                return;
            }

            terrainFeature = legacyZoneType switch
            {
                CombatZoneType.RoughTerrain => catalog.FindTerrainFeature("RoughTerrain"),
                CombatZoneType.Forest => catalog.FindTerrainFeature("Forest"),
                _ => null
            };
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (terrainFeature == null && legacyZoneType != CombatZoneType.None)
            {
                EnsureLegacyTerrainFeatureResolved();
            }
        }
#endif
    }
}
