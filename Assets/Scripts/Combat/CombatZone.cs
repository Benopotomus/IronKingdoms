using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace IronKingdoms.Combat
{
    [DisallowMultipleComponent]
    public class CombatZone : MonoBehaviour
    {
        private static readonly List<CombatZone> ActiveZoneRegistry = new();
        private const float PointContainmentThresholdSqr = 0.0001f;
        private const int UnitContainmentSampleCount = 8;
        private const int SphereFootprintCornerCount = 16;

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
            ResolveCollider();
            EnsureLegacyTerrainFeatureResolved();
        }

        private void OnEnable()
        {
            zoneCollider = null;
            EnsureRegistered();
            CombatForestFogClipper.InvalidateCache();
        }

        /// <summary>
        /// Ensures this zone is present in <see cref="ActiveZones"/>.
        /// Editor batch tests may assign terrain features after component creation.
        /// </summary>
        public void EnsureRegistered()
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
            CombatForestFogClipper.InvalidateCache();
        }

        /// <summary>
        /// Footprint outline on the XZ tabletop for debug and analytic helpers.
        /// Supports oriented boxes, spheres, and falls back to bounds for other colliders.
        /// </summary>
        public void CollectFootprintCorners(List<Vector3> corners)
        {
            ResolveCollider();
            if (zoneCollider == null || !zoneCollider.enabled)
            {
                return;
            }

            var tabletopY = zoneCollider.bounds.center.y;
            if (zoneCollider is BoxCollider box)
            {
                AppendOrientedBoxFootprintCorners(box, tabletopY, corners);
                return;
            }

            if (zoneCollider is SphereCollider sphere)
            {
                AppendSphereFootprintCorners(sphere, tabletopY, corners);
                return;
            }

            var bounds = zoneCollider.bounds;
            corners.Add(new Vector3(bounds.min.x, tabletopY, bounds.min.z));
            corners.Add(new Vector3(bounds.max.x, tabletopY, bounds.min.z));
            corners.Add(new Vector3(bounds.max.x, tabletopY, bounds.max.z));
            corners.Add(new Vector3(bounds.min.x, tabletopY, bounds.max.z));
        }

        public bool ContainsPoint(Vector3 worldPoint)
        {
            if (!isActiveAndEnabled)
            {
                return false;
            }

            ResolveCollider();
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

            ResolveCollider();
            if (zoneCollider == null || !zoneCollider.enabled)
            {
                return false;
            }

            var closestPoint = zoneCollider.ClosestPoint(center);
            var dx = closestPoint.x - center.x;
            var dz = closestPoint.z - center.z;
            return dx * dx + dz * dz <= radius * radius;
        }

        public bool TryGetFootprintCollider(out Collider collider)
        {
            ResolveCollider();
            collider = zoneCollider;
            return collider != null && collider.enabled;
        }

        private void ResolveCollider()
        {
            if (zoneCollider != null)
            {
                zoneCollider.isTrigger = true;
                return;
            }

            zoneCollider = GetComponent<Collider>();
            if (zoneCollider == null)
            {
                zoneCollider = GetComponentInChildren<Collider>(true);
            }

            if (zoneCollider != null)
            {
                zoneCollider.isTrigger = true;
            }
        }

        private static void AppendOrientedBoxFootprintCorners(BoxCollider box, float tabletopY, List<Vector3> corners)
        {
            var t = box.transform;
            var half = box.size * 0.5f;
            var center = box.center;
            var localCorners = new[]
            {
                center + new Vector3(-half.x, 0f, -half.z),
                center + new Vector3(half.x, 0f, -half.z),
                center + new Vector3(half.x, 0f, half.z),
                center + new Vector3(-half.x, 0f, half.z)
            };

            for (var i = 0; i < localCorners.Length; i++)
            {
                var world = t.TransformPoint(localCorners[i]);
                world.y = tabletopY;
                corners.Add(world);
            }
        }

        private static void AppendSphereFootprintCorners(SphereCollider sphere, float tabletopY, List<Vector3> corners)
        {
            var t = sphere.transform;
            var center = t.TransformPoint(sphere.center);
            center.y = tabletopY;
            var scale = t.lossyScale;
            var radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            for (var i = 0; i < SphereFootprintCornerCount; i++)
            {
                var angle = (Mathf.PI * 2f * i) / SphereFootprintCornerCount;
                corners.Add(center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
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
            ResolveCollider();
            if (terrainFeature == null && legacyZoneType != CombatZoneType.None)
            {
                EnsureLegacyTerrainFeatureResolved();
            }
        }
#endif
    }
}
