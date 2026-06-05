using NUnit.Framework;
using UnityEngine;
using System;
using System.Reflection;

namespace IronKingdoms.Combat.Tests
{
    public class ForestFogClipperTests
    {
        private static readonly Type ForestClipperType =
            Type.GetType("IronKingdoms.Combat.CombatForestFogClipper, IronKingdoms.Runtime");

        private static readonly MethodInfo EnsureCacheMethod =
            ForestClipperType?.GetMethod("EnsureCache", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo PreciseClipMethod =
            ForestClipperType?.GetMethod(
                "GetClipDistanceWorldPrecise",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        private GameObject zoneObject;
        private CombatZone zone;
        private CombatTerrainFeatureDefinition forestFeature;

        [SetUp]
        public void SetUp()
        {
            forestFeature = ScriptableObject.CreateInstance<CombatTerrainFeatureDefinition>();
            SetPrivateField(forestFeature, "featureId", "TestForest");
            SetPrivateField(forestFeature, "displayName", "Test Forest");
            SetPrivateField(forestFeature, "lineOfSightMode", CombatTerrainLineOfSightMode.LimitedDepth);
            SetPrivateField(forestFeature, "lineOfSightPassThroughDepthInches", 3f);

            zoneObject = new GameObject("TestForestZone");
            var collider = zoneObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(40f, 2f, 40f);
            collider.center = new Vector3(0f, 1f, 0f);

            zone = zoneObject.AddComponent<CombatZone>();
            SetPrivateField(zone, "terrainFeature", forestFeature);

            // Ensure component lifecycle hooks register the zone.
            zoneObject.SetActive(false);
            zoneObject.SetActive(true);
            Assert.IsNotNull(ForestClipperType, "Could not resolve CombatForestFogClipper type.");
            Assert.IsNotNull(EnsureCacheMethod, "Could not resolve EnsureCache method.");
            Assert.IsNotNull(PreciseClipMethod, "Could not resolve GetClipDistanceWorldPrecise method.");
            EnsureCacheMethod.Invoke(null, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (zoneObject != null)
            {
                UnityEngine.Object.DestroyImmediate(zoneObject);
            }

            if (forestFeature != null)
            {
                UnityEngine.Object.DestroyImmediate(forestFeature);
            }
        }

        [Test]
        public void PreciseClip_AtEdge_SeesThreeInchesIntoForest()
        {
            var depthWorld = CombatScale.InchesToWorldUnits(3f);
            var origin = new Vector3(-20f, 0f, 0f); // exactly at left edge of a 40 wide forest centered at 0
            var direction = Vector3.right;
            var maxDistance = CombatScale.InchesToWorldUnits(20f);

            var clip = InvokePreciseClip(origin, direction, maxDistance);
            Assert.That(clip, Is.EqualTo(depthWorld).Within(CombatScale.InchesToWorldUnits(0.15f)));
        }

        [Test]
        public void PreciseClip_TenInchesBack_StillSeesThreeInchesIntoForest()
        {
            var tenInchesWorld = CombatScale.InchesToWorldUnits(10f);
            var depthWorld = CombatScale.InchesToWorldUnits(3f);
            var origin = new Vector3(-20f - tenInchesWorld, 0f, 0f);
            var direction = Vector3.right;
            var maxDistance = CombatScale.InchesToWorldUnits(30f);

            var clip = InvokePreciseClip(origin, direction, maxDistance);
            Assert.That(clip, Is.EqualTo(tenInchesWorld + depthWorld).Within(CombatScale.InchesToWorldUnits(0.2f)));
        }

        [Test]
        public void PreciseClip_WhenNoForestAhead_DoesNotClip()
        {
            var origin = new Vector3(-20f, 0f, 0f);
            var direction = Vector3.left; // away from forest
            var maxDistance = CombatScale.InchesToWorldUnits(15f);

            var clip = InvokePreciseClip(origin, direction, maxDistance);
            Assert.That(clip, Is.EqualTo(maxDistance).Within(0.001f));
        }

        private static float InvokePreciseClip(Vector3 origin, Vector3 direction, float maxDistance)
        {
            var result = PreciseClipMethod.Invoke(null, new object[] { origin, direction, maxDistance });
            return result is float f ? f : throw new InvalidOperationException("Unexpected clip result type.");
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            var field = type.GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Missing field {fieldName} on {type.Name}");
            field.SetValue(target, value);
        }
    }
}
