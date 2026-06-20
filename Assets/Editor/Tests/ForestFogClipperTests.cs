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
        private GameObject extraZoneObject;
        private GameObject auxiliaryZoneObject;
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
            if (extraZoneObject != null)
            {
                UnityEngine.Object.DestroyImmediate(extraZoneObject);
            }

            if (auxiliaryZoneObject != null)
            {
                UnityEngine.Object.DestroyImmediate(auxiliaryZoneObject);
            }

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

        [Test]
        public void CircularFootprint_AabbCornerOutsideCircle_IsNotInsideForest()
        {
            extraZoneObject = new GameObject("CircularForestZone");
            var sphereCollider = extraZoneObject.AddComponent<SphereCollider>();
            sphereCollider.isTrigger = true;
            sphereCollider.radius = 2f;
            sphereCollider.center = new Vector3(0f, 1.27f, 0f);
            var circleZone = extraZoneObject.AddComponent<CombatZone>();
            SetPrivateField(circleZone, "terrainFeature", forestFeature);
            extraZoneObject.SetActive(false);
            extraZoneObject.SetActive(true);
            Physics.SyncTransforms();
            CombatForestFogClipper.InvalidateCache();
            EnsureCacheMethod.Invoke(null, null);

            var bounds = sphereCollider.bounds;
            var aabbCorner = new Vector3(bounds.max.x, bounds.center.y, bounds.max.z);
            Assert.IsFalse(CombatForestFogClipper.IsInsideLimitedDepthForest(aabbCorner));
        }

        [Test]
        public void CircularFootprint_EdgeApproach_SeesThreeInchesIntoForest()
        {
            extraZoneObject = new GameObject("CircularForestZone");
            var sphereCollider = extraZoneObject.AddComponent<SphereCollider>();
            sphereCollider.isTrigger = true;
            sphereCollider.radius = 2f;
            sphereCollider.center = new Vector3(0f, 1.27f, 0f);
            var circleZone = extraZoneObject.AddComponent<CombatZone>();
            SetPrivateField(circleZone, "terrainFeature", forestFeature);
            extraZoneObject.SetActive(false);
            extraZoneObject.SetActive(true);
            Physics.SyncTransforms();
            CombatForestFogClipper.InvalidateCache();
            EnsureCacheMethod.Invoke(null, null);

            var depthWorld = CombatScale.InchesToWorldUnits(3f);
            var origin = new Vector3(-2f, 0f, 0f);
            var direction = Vector3.right;
            var maxDistance = CombatScale.InchesToWorldUnits(20f);

            var clip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                origin,
                direction,
                maxDistance,
                depthWorld);
            Assert.That(clip, Is.EqualTo(depthWorld).Within(CombatScale.InchesToWorldUnits(0.2f)));
        }

        [Test]
        public void OutsideThinForest_ClipsAtForestExitInsteadOfRevealingBeyondIt()
        {
            var depthWorld = CombatScale.InchesToWorldUnits(3f);
            var approachWorld = CombatScale.InchesToWorldUnits(10f);
            var thinForestWidthWorld = CombatScale.InchesToWorldUnits(1f);
            var maxDistance = CombatScale.InchesToWorldUnits(30f);

            var collider = zoneObject.GetComponent<BoxCollider>();
            collider.size = new Vector3(thinForestWidthWorld, 2f, CombatScale.InchesToWorldUnits(8f));
            collider.center = new Vector3(0f, 1f, 0f);
            Physics.SyncTransforms();
            CombatForestFogClipper.InvalidateCache();
            EnsureCacheMethod.Invoke(null, null);

            var origin = new Vector3((-thinForestWidthWorld * 0.5f) - approachWorld, 0f, 0f);
            var direction = Vector3.right;

            var clip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                origin,
                direction,
                maxDistance,
                depthWorld);

            Assert.That(clip, Is.EqualTo(approachWorld + thinForestWidthWorld).Within(CombatScale.InchesToWorldUnits(0.25f)));
        }

        [Test]
        public void InsideBoxForest_StillClipsAtSeparateCircularForest()
        {
            zoneObject.SetActive(false);
            auxiliaryZoneObject = new GameObject("SmallForestBox");
            var boxCollider = auxiliaryZoneObject.AddComponent<BoxCollider>();
            boxCollider.isTrigger = true;
            boxCollider.size = new Vector3(4f, 2f, 4f);
            boxCollider.center = new Vector3(0f, 1f, 0f);
            var boxZone = auxiliaryZoneObject.AddComponent<CombatZone>();
            SetPrivateField(boxZone, "terrainFeature", forestFeature);
            auxiliaryZoneObject.SetActive(false);
            auxiliaryZoneObject.SetActive(true);

            extraZoneObject = new GameObject("DistantCircularForest");
            var sphereCollider = extraZoneObject.AddComponent<SphereCollider>();
            sphereCollider.isTrigger = true;
            sphereCollider.radius = 2f;
            sphereCollider.center = new Vector3(0f, 1.27f, 0f);
            extraZoneObject.transform.position = new Vector3(12f, 0f, 0f);
            var circleZone = extraZoneObject.AddComponent<CombatZone>();
            SetPrivateField(circleZone, "terrainFeature", forestFeature);
            extraZoneObject.SetActive(false);
            extraZoneObject.SetActive(true);

            Physics.SyncTransforms();
            CombatForestFogClipper.InvalidateCache();
            EnsureCacheMethod.Invoke(null, null);

            var depthWorld = CombatScale.InchesToWorldUnits(3f);
            var maxDistance = CombatScale.InchesToWorldUnits(30f);
            var insideOrigin = new Vector3(0f, 0f, 0f);
            var direction = Vector3.right;

            var clip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                insideOrigin,
                direction,
                maxDistance,
                depthWorld);

            var nearEyeClip = CombatScale.InchesToWorldUnits(3f);
            Assert.That(clip, Is.GreaterThan(nearEyeClip + CombatScale.InchesToWorldUnits(1f)));
            Assert.That(clip, Is.LessThan(maxDistance - 0.001f));
        }

        [Test]
        public void InsideSmallCircularForestAtCenter_LookingAtDistantSecondForest_ClipsAtSecondEntryPlusDepth()
        {
            zoneObject.SetActive(false);
            auxiliaryZoneObject = new GameObject("SmallCircularForest");
            var mainSphere = auxiliaryZoneObject.AddComponent<SphereCollider>();
            mainSphere.isTrigger = true;
            mainSphere.radius = CombatScale.InchesToWorldUnits(2f);
            mainSphere.center = new Vector3(0f, 1f, 0f);
            var mainZone = auxiliaryZoneObject.AddComponent<CombatZone>();
            SetPrivateField(mainZone, "terrainFeature", forestFeature);
            auxiliaryZoneObject.SetActive(false);
            auxiliaryZoneObject.SetActive(true);

            extraZoneObject = new GameObject("DistantCircularForest");
            var farSphere = extraZoneObject.AddComponent<SphereCollider>();
            farSphere.isTrigger = true;
            farSphere.radius = CombatScale.InchesToWorldUnits(3f);
            farSphere.center = new Vector3(0f, 1f, 0f);
            extraZoneObject.transform.position = new Vector3(CombatScale.InchesToWorldUnits(10f), 0f, 0f);
            var farZone = extraZoneObject.AddComponent<CombatZone>();
            SetPrivateField(farZone, "terrainFeature", forestFeature);
            extraZoneObject.SetActive(false);
            extraZoneObject.SetActive(true);

            Physics.SyncTransforms();
            CombatForestFogClipper.InvalidateCache();
            EnsureCacheMethod.Invoke(null, null);

            var depthWorld = CombatScale.InchesToWorldUnits(3f);
            var maxDistance = CombatScale.InchesToWorldUnits(30f);
            var insideOrigin = new Vector3(0f, 0f, 0f);
            var direction = Vector3.right;

            var clip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                insideOrigin,
                direction,
                maxDistance,
                depthWorld);

            Assert.That(clip, Is.GreaterThan(CombatScale.InchesToWorldUnits(4f)));
            Assert.That(clip, Is.LessThan(maxDistance - 0.001f));
            Assert.That(clip, Is.LessThan(CombatScale.InchesToWorldUnits(14f)));
        }

        [Test]
        public void InsideSmallCircularForestAtCenter_OpenDirection_SeesOut()
        {
            zoneObject.SetActive(false);
            auxiliaryZoneObject = new GameObject("SmallCircularForest");
            var sphere = auxiliaryZoneObject.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = CombatScale.InchesToWorldUnits(2f);
            sphere.center = new Vector3(0f, 1f, 0f);
            var circleZone = auxiliaryZoneObject.AddComponent<CombatZone>();
            SetPrivateField(circleZone, "terrainFeature", forestFeature);
            auxiliaryZoneObject.SetActive(false);
            auxiliaryZoneObject.SetActive(true);

            Physics.SyncTransforms();
            CombatForestFogClipper.InvalidateCache();
            EnsureCacheMethod.Invoke(null, null);

            var depthWorld = CombatScale.InchesToWorldUnits(3f);
            var maxDistance = CombatScale.InchesToWorldUnits(30f);
            var insideOrigin = new Vector3(0f, 0f, 0f);

            foreach (var direction in new[] { Vector3.forward, Vector3.right, Vector3.left, Vector3.back })
            {
                var clip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                    insideOrigin,
                    direction,
                    maxDistance,
                    depthWorld);

                Assert.That(
                    clip,
                    Is.EqualTo(maxDistance).Within(0.001f),
                    () => $"Direction {direction} should see out past the small forest.");
            }
        }

        [Test]
        public void InsideCircularForestAtCenter_LargerThanDepthLimit_ClipsAtThreeInches()
        {
            zoneObject.SetActive(false);
            auxiliaryZoneObject = new GameObject("CircularForest");
            var sphere = auxiliaryZoneObject.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = CombatScale.InchesToWorldUnits(4f);
            sphere.center = new Vector3(0f, 1f, 0f);
            var circleZone = auxiliaryZoneObject.AddComponent<CombatZone>();
            SetPrivateField(circleZone, "terrainFeature", forestFeature);
            auxiliaryZoneObject.SetActive(false);
            auxiliaryZoneObject.SetActive(true);

            Physics.SyncTransforms();
            CombatForestFogClipper.InvalidateCache();
            EnsureCacheMethod.Invoke(null, null);

            var depthWorld = CombatScale.InchesToWorldUnits(3f);
            var maxDistance = CombatScale.InchesToWorldUnits(30f);
            var insideOrigin = new Vector3(0f, 0f, 0f);

            foreach (var direction in new[] { Vector3.forward, Vector3.right, Vector3.left, Vector3.back })
            {
                var clip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                    insideOrigin,
                    direction,
                    maxDistance,
                    depthWorld);

                Assert.That(
                    clip,
                    Is.EqualTo(depthWorld).Within(CombatScale.InchesToWorldUnits(0.35f)),
                    () => $"Direction {direction} should clip at the 3\" depth limit.");
            }
        }

        [Test]
        public void InsideLargeForest_LookingAcrossInterior_ClipsAtDepthLimit()
        {
            var depthWorld = CombatScale.InchesToWorldUnits(3f);
            var maxDistance = CombatScale.InchesToWorldUnits(20f);
            var insideOrigin = new Vector3(0f, 0f, 0f);
            var direction = Vector3.forward;

            var clip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                insideOrigin,
                direction,
                maxDistance,
                depthWorld);

            Assert.That(clip, Is.EqualTo(depthWorld).Within(CombatScale.InchesToWorldUnits(0.2f)));
        }

        [Test]
        public void InsideForest_SecondForestNearEdge_ClipsAtDepthLimitWhenExitEdgeBeyondThreeInches()
        {
            zoneObject.SetActive(false);
            auxiliaryZoneObject = new GameObject("NearForestBox");
            var nearBox = auxiliaryZoneObject.AddComponent<BoxCollider>();
            nearBox.isTrigger = true;
            nearBox.size = new Vector3(CombatScale.InchesToWorldUnits(8f), 2f, CombatScale.InchesToWorldUnits(8f));
            nearBox.center = new Vector3(0f, 1f, 0f);
            var nearZone = auxiliaryZoneObject.AddComponent<CombatZone>();
            SetPrivateField(nearZone, "terrainFeature", forestFeature);
            auxiliaryZoneObject.SetActive(false);
            auxiliaryZoneObject.SetActive(true);

            extraZoneObject = new GameObject("AdjacentForestBox");
            var farBox = extraZoneObject.AddComponent<BoxCollider>();
            farBox.isTrigger = true;
            farBox.size = new Vector3(CombatScale.InchesToWorldUnits(8f), 2f, CombatScale.InchesToWorldUnits(8f));
            farBox.center = new Vector3(0f, 1f, 0f);
            extraZoneObject.transform.position = new Vector3(CombatScale.InchesToWorldUnits(8f), 0f, 0f);
            var farZone = extraZoneObject.AddComponent<CombatZone>();
            SetPrivateField(farZone, "terrainFeature", forestFeature);
            extraZoneObject.SetActive(false);
            extraZoneObject.SetActive(true);

            Physics.SyncTransforms();
            CombatForestFogClipper.InvalidateCache();
            EnsureCacheMethod.Invoke(null, null);

            var depthWorld = CombatScale.InchesToWorldUnits(3f);
            var maxDistance = CombatScale.InchesToWorldUnits(30f);
            var insideOrigin = new Vector3(0f, 0f, 0f);
            var direction = Vector3.right;

            var clip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                insideOrigin,
                direction,
                maxDistance,
                depthWorld);

            Assert.That(clip, Is.EqualTo(depthWorld).Within(CombatScale.InchesToWorldUnits(0.35f)));
        }

        [Test]
        public void InsideCircularForestNearEdge_TowardOpenSky_SeesOutDespiteNearbySecondCircle()
        {
            zoneObject.SetActive(false);
            auxiliaryZoneObject = new GameObject("MainCircularForest");
            var mainSphere = auxiliaryZoneObject.AddComponent<SphereCollider>();
            mainSphere.isTrigger = true;
            mainSphere.radius = CombatScale.InchesToWorldUnits(4f);
            mainSphere.center = new Vector3(0f, 1f, 0f);
            var mainZone = auxiliaryZoneObject.AddComponent<CombatZone>();
            SetPrivateField(mainZone, "terrainFeature", forestFeature);
            auxiliaryZoneObject.SetActive(false);
            auxiliaryZoneObject.SetActive(true);

            extraZoneObject = new GameObject("NearbyCircularForest");
            var nearSphere = extraZoneObject.AddComponent<SphereCollider>();
            nearSphere.isTrigger = true;
            nearSphere.radius = CombatScale.InchesToWorldUnits(3f);
            nearSphere.center = new Vector3(0f, 1f, 0f);
            extraZoneObject.transform.position = new Vector3(CombatScale.InchesToWorldUnits(10f), 0f, 0f);
            var nearZone = extraZoneObject.AddComponent<CombatZone>();
            SetPrivateField(nearZone, "terrainFeature", forestFeature);
            extraZoneObject.SetActive(false);
            extraZoneObject.SetActive(true);

            Physics.SyncTransforms();
            CombatForestFogClipper.InvalidateCache();
            EnsureCacheMethod.Invoke(null, null);

            var depthWorld = CombatScale.InchesToWorldUnits(3f);
            var maxDistance = CombatScale.InchesToWorldUnits(30f);
            var insideNearEdge = new Vector3(0f, 0f, CombatScale.InchesToWorldUnits(3.5f));
            var directionOutOfForest = Vector3.forward;

            var clip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                insideNearEdge,
                directionOutOfForest,
                maxDistance,
                depthWorld);

            Assert.That(clip, Is.GreaterThan(depthWorld + CombatScale.InchesToWorldUnits(0.5f)));

            var smoothed = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorldSmoothed(
                insideNearEdge,
                directionOutOfForest,
                maxDistance,
                depthWorld,
                0f,
                Mathf.PI / 720f);
            Assert.That(smoothed, Is.GreaterThan(depthWorld + CombatScale.InchesToWorldUnits(0.5f)));
        }

        [Test]
        public void InsideForestNearEdge_AdjacentSecondForest_ClipsAtFirstExitNotIntoSecond()
        {
            zoneObject.SetActive(false);
            auxiliaryZoneObject = new GameObject("NearCircularForest");
            var nearSphere = auxiliaryZoneObject.AddComponent<SphereCollider>();
            nearSphere.isTrigger = true;
            nearSphere.radius = CombatScale.InchesToWorldUnits(2f);
            nearSphere.center = new Vector3(0f, 1f, 0f);
            var nearZone = auxiliaryZoneObject.AddComponent<CombatZone>();
            SetPrivateField(nearZone, "terrainFeature", forestFeature);
            auxiliaryZoneObject.SetActive(false);
            auxiliaryZoneObject.SetActive(true);

            extraZoneObject = new GameObject("AdjacentCircularForest");
            var farSphere = extraZoneObject.AddComponent<SphereCollider>();
            farSphere.isTrigger = true;
            farSphere.radius = CombatScale.InchesToWorldUnits(2f);
            farSphere.center = new Vector3(0f, 1f, 0f);
            extraZoneObject.transform.position = new Vector3(CombatScale.InchesToWorldUnits(4f), 0f, 0f);
            var farZone = extraZoneObject.AddComponent<CombatZone>();
            SetPrivateField(farZone, "terrainFeature", forestFeature);
            extraZoneObject.SetActive(false);
            extraZoneObject.SetActive(true);

            Physics.SyncTransforms();
            CombatForestFogClipper.InvalidateCache();
            EnsureCacheMethod.Invoke(null, null);

            var depthWorld = CombatScale.InchesToWorldUnits(3f);
            var maxDistance = CombatScale.InchesToWorldUnits(30f);
            var insideOrigin = new Vector3(0f, 0f, 0f);
            var direction = Vector3.right;

            var clip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                insideOrigin,
                direction,
                maxDistance,
                depthWorld);

            var expectedFirstExit = CombatScale.InchesToWorldUnits(2f);
            var maxIntoSecond = expectedFirstExit + CombatScale.InchesToWorldUnits(0.35f);
            Assert.That(clip, Is.LessThanOrEqualTo(maxIntoSecond));
            Assert.That(clip, Is.GreaterThan(expectedFirstExit - CombatScale.InchesToWorldUnits(0.35f)));
        }

        [Test]
        public void InsideForestNearEdge_CanSeeOutWhenExitWithinThreeInches()
        {
            var depthWorld = CombatScale.InchesToWorldUnits(3f);
            var maxDistance = CombatScale.InchesToWorldUnits(30f);
            var collider = zoneObject.GetComponent<BoxCollider>();
            collider.size = new Vector3(CombatScale.InchesToWorldUnits(4f), 2f, CombatScale.InchesToWorldUnits(8f));
            collider.center = new Vector3(0f, 1f, 0f);
            Physics.SyncTransforms();
            CombatForestFogClipper.InvalidateCache();
            EnsureCacheMethod.Invoke(null, null);

            var insideOrigin = new Vector3(CombatScale.InchesToWorldUnits(0f), 0f, 0f);
            var direction = Vector3.right;

            var clip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                insideOrigin,
                direction,
                maxDistance,
                depthWorld);

            Assert.That(clip, Is.GreaterThan(depthWorld + CombatScale.InchesToWorldUnits(0.5f)));
        }

        [Test]
        public void OutsideBoxForest_ClipsAtDepthLimitBeforeDiagonalCorner()
        {
            var depthWorld = CombatScale.InchesToWorldUnits(3f);
            var maxDistance = CombatScale.InchesToWorldUnits(30f);
            var collider = zoneObject.GetComponent<BoxCollider>();
            collider.size = new Vector3(CombatScale.InchesToWorldUnits(8f), 2f, CombatScale.InchesToWorldUnits(16f));
            collider.center = new Vector3(0f, 1f, 0f);
            zoneObject.transform.position = new Vector3(CombatScale.InchesToWorldUnits(12f), 0f, 0f);
            Physics.SyncTransforms();
            CombatForestFogClipper.InvalidateCache();
            EnsureCacheMethod.Invoke(null, null);

            var origin = new Vector3(0f, 0f, CombatScale.InchesToWorldUnits(-1f));
            var direction = new Vector3(1f, 0f, 0.15f).normalized;

            var clip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                origin,
                direction,
                maxDistance,
                depthWorld);

            // Near face ~8" out; must clip at ~3" depth, not carry to the far corner (~20"+).
            var nearFaceCap = CombatScale.InchesToWorldUnits(12f);
            Assert.That(clip, Is.LessThan(nearFaceCap));
        }

        [Test]
        public void OutsideEye_NearCircularForestBeforeFarBox_ClipsAtNearForestNotFarBox()
        {
            zoneObject.SetActive(false);
            auxiliaryZoneObject = new GameObject("NearCircularForest");
            var nearSphere = auxiliaryZoneObject.AddComponent<SphereCollider>();
            nearSphere.isTrigger = true;
            nearSphere.radius = CombatScale.InchesToWorldUnits(3f);
            nearSphere.center = new Vector3(0f, 1f, 0f);
            auxiliaryZoneObject.transform.position = new Vector3(CombatScale.InchesToWorldUnits(6f), 0f, 0f);
            var nearZone = auxiliaryZoneObject.AddComponent<CombatZone>();
            SetPrivateField(nearZone, "terrainFeature", forestFeature);
            auxiliaryZoneObject.SetActive(false);
            auxiliaryZoneObject.SetActive(true);

            extraZoneObject = new GameObject("FarForestBox");
            var farBox = extraZoneObject.AddComponent<BoxCollider>();
            farBox.isTrigger = true;
            farBox.size = new Vector3(CombatScale.InchesToWorldUnits(8f), 2f, CombatScale.InchesToWorldUnits(8f));
            farBox.center = new Vector3(0f, 1f, 0f);
            extraZoneObject.transform.position = new Vector3(CombatScale.InchesToWorldUnits(20f), 0f, 0f);
            var farZone = extraZoneObject.AddComponent<CombatZone>();
            SetPrivateField(farZone, "terrainFeature", forestFeature);
            extraZoneObject.SetActive(false);
            extraZoneObject.SetActive(true);

            Physics.SyncTransforms();
            CombatForestFogClipper.InvalidateCache();
            EnsureCacheMethod.Invoke(null, null);

            var depthWorld = CombatScale.InchesToWorldUnits(3f);
            var maxDistance = CombatScale.InchesToWorldUnits(30f);
            var eyeOutside = new Vector3(0f, 0f, 0f);
            var direction = Vector3.right;

            var clip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                eyeOutside,
                direction,
                maxDistance,
                depthWorld);

            // Near circle center x=6, r=3 → entry ~3", plus up to 3" depth ≈ 6" total — not ~20"+ into far box.
            var nearForestCap = CombatScale.InchesToWorldUnits(7f);
            Assert.That(clip, Is.LessThan(nearForestCap));
            Assert.That(clip, Is.GreaterThan(CombatScale.InchesToWorldUnits(2f)));
        }

        [Test]
        public void EyeOutsideForest_ClipUsesEyeOnlyNotBaseWidth()
        {
            var collider = zoneObject.GetComponent<BoxCollider>();
            collider.size = new Vector3(CombatScale.InchesToWorldUnits(8f), 2f, CombatScale.InchesToWorldUnits(8f));
            collider.center = new Vector3(0f, 1f, 0f);
            Physics.SyncTransforms();
            CombatForestFogClipper.InvalidateCache();
            EnsureCacheMethod.Invoke(null, null);

            var baseRadius = CombatScale.InchesToWorldUnits(1f);
            var eyeOutsideForest = new Vector3(CombatScale.InchesToWorldUnits(4.5f), 0f, 0f);

            Assert.That(
                CombatForestFogClipper.IsInsideLimitedDepthForest(eyeOutsideForest, 0f),
                Is.False);
            Assert.That(
                CombatForestFogClipper.IsInsideLimitedDepthForest(eyeOutsideForest, baseRadius),
                Is.True);

            var depthWorld = CombatScale.InchesToWorldUnits(3f);
            var maxDistance = CombatScale.InchesToWorldUnits(30f);

            var clipOpenGround = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                eyeOutsideForest,
                Vector3.right,
                maxDistance,
                depthWorld,
                baseRadius);
            var clipOpenGroundEyeOnly = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                eyeOutsideForest,
                Vector3.right,
                maxDistance,
                depthWorld,
                0f);
            var clipIntoForest = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                eyeOutsideForest,
                Vector3.left,
                maxDistance,
                depthWorld,
                baseRadius);
            var clipIntoForestEyeOnly = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                eyeOutsideForest,
                Vector3.left,
                maxDistance,
                depthWorld,
                0f);

            Assert.That(clipOpenGround, Is.EqualTo(clipOpenGroundEyeOnly).Within(CombatScale.InchesToWorldUnits(0.05f)));
            Assert.That(clipIntoForest, Is.EqualTo(clipIntoForestEyeOnly).Within(CombatScale.InchesToWorldUnits(0.05f)));
            Assert.That(clipOpenGround, Is.GreaterThan(maxDistance - CombatScale.InchesToWorldUnits(1f)));
            Assert.That(clipIntoForest, Is.LessThan(maxDistance - CombatScale.InchesToWorldUnits(1f)));
        }

        [Test]
        public void PolygonForestTriangle_UsesSameClipRulesAsBoxForest()
        {
            zoneObject.SetActive(false);
            auxiliaryZoneObject = new GameObject("PolygonForest");
            var polygonZone = auxiliaryZoneObject.AddComponent<CombatZone>();
            SetPrivateField(polygonZone, "terrainFeature", forestFeature);
            var footprint = auxiliaryZoneObject.AddComponent<CombatZonePolygonFootprint>();
            footprint.SetLocalVertices(new[]
            {
                new Vector2(-CombatScale.InchesToWorldUnits(2f), -CombatScale.InchesToWorldUnits(2f)),
                new Vector2(CombatScale.InchesToWorldUnits(2f), -CombatScale.InchesToWorldUnits(2f)),
                new Vector2(0f, CombatScale.InchesToWorldUnits(2f))
            });
            footprint.RegenerateGeometry();
            auxiliaryZoneObject.SetActive(false);
            auxiliaryZoneObject.SetActive(true);

            Physics.SyncTransforms();
            CombatForestFogClipper.InvalidateCache();
            EnsureCacheMethod.Invoke(null, null);

            var depthWorld = CombatScale.InchesToWorldUnits(3f);
            var maxDistance = CombatScale.InchesToWorldUnits(30f);
            var outsideOrigin = new Vector3(-CombatScale.InchesToWorldUnits(10f), 0f, 0f);
            var direction = Vector3.right;

            var clip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                outsideOrigin,
                direction,
                maxDistance,
                depthWorld);

            Assert.That(clip, Is.GreaterThan(CombatScale.InchesToWorldUnits(6f)));
            Assert.That(clip, Is.LessThan(maxDistance - 0.001f));
        }

        [Test]
        public void ClockwisePolygonForest_UsesSameDepthClipAsCounterClockwise()
        {
            zoneObject.SetActive(false);
            auxiliaryZoneObject = new GameObject("ClockwisePolygonForest");
            var polygonZone = auxiliaryZoneObject.AddComponent<CombatZone>();
            SetPrivateField(polygonZone, "terrainFeature", forestFeature);
            var footprint = auxiliaryZoneObject.AddComponent<CombatZonePolygonFootprint>();
            footprint.SetLocalVertices(new[]
            {
                new Vector2(0f, CombatScale.InchesToWorldUnits(2f)),
                new Vector2(CombatScale.InchesToWorldUnits(2f), -CombatScale.InchesToWorldUnits(2f)),
                new Vector2(-CombatScale.InchesToWorldUnits(2f), -CombatScale.InchesToWorldUnits(2f)),
            });
            footprint.RegenerateGeometry();
            auxiliaryZoneObject.SetActive(false);
            auxiliaryZoneObject.SetActive(true);

            Physics.SyncTransforms();
            CombatForestFogClipper.InvalidateCache();
            EnsureCacheMethod.Invoke(null, null);

            var depthWorld = CombatScale.InchesToWorldUnits(3f);
            var maxDistance = CombatScale.InchesToWorldUnits(30f);
            var outsideOrigin = new Vector3(-CombatScale.InchesToWorldUnits(10f), 0f, 0f);
            var direction = Vector3.right;

            var clip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                outsideOrigin,
                direction,
                maxDistance,
                depthWorld);

            Assert.That(clip, Is.GreaterThan(CombatScale.InchesToWorldUnits(6f)));
            Assert.That(clip, Is.LessThan(maxDistance - 0.001f));
        }

        [Test]
        public void EyeOutsideForestNearEdge_OpenDirectionAwayFromTrees_StaysFullyOpen()
        {
            var collider = zoneObject.GetComponent<BoxCollider>();
            collider.size = new Vector3(CombatScale.InchesToWorldUnits(8f), 2f, CombatScale.InchesToWorldUnits(8f));
            collider.center = new Vector3(0f, 1f, 0f);
            Physics.SyncTransforms();
            CombatForestFogClipper.InvalidateCache();
            EnsureCacheMethod.Invoke(null, null);

            var depthWorld = CombatScale.InchesToWorldUnits(3f);
            var maxDistance = CombatScale.InchesToWorldUnits(30f);
            var eyeOutsideForest = new Vector3(CombatScale.InchesToWorldUnits(5f), 0f, 0f);

            Assert.That(CombatForestFogClipper.IsInsideLimitedDepthForest(eyeOutsideForest, 0f), Is.False);

            var clipOpen = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                eyeOutsideForest,
                Vector3.right,
                maxDistance,
                depthWorld,
                0f);
            var clipTowardForest = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                eyeOutsideForest,
                Vector3.left,
                maxDistance,
                depthWorld,
                0f);

            Assert.That(clipOpen, Is.GreaterThan(maxDistance - CombatScale.InchesToWorldUnits(1f)));
            Assert.That(clipTowardForest, Is.LessThan(maxDistance - CombatScale.InchesToWorldUnits(1f)));
            Assert.That(clipOpen, Is.GreaterThan(depthWorld + CombatScale.InchesToWorldUnits(0.5f)));
        }

        [Test]
        public void CloudZone_IsNotInsideLimitedDepthForest()
        {
            var cloudFeature = CreateCloudFeature();
            extraZoneObject = CreateCloudBoxZone(
                cloudFeature,
                new Vector3(CombatScale.InchesToWorldUnits(20f), 2f, CombatScale.InchesToWorldUnits(8f)),
                new Vector3(CombatScale.InchesToWorldUnits(12f), 0f, 0f));

            Physics.SyncTransforms();
            CombatForestFogClipper.InvalidateCache();
            EnsureCacheMethod.Invoke(null, null);

            var insideCloud = new Vector3(CombatScale.InchesToWorldUnits(12f), 0f, 0f);
            Assert.That(CombatForestFogClipper.IsInsideLimitedDepthForest(insideCloud), Is.False);
            Assert.That(CombatForestFogClipper.IsInsideBlockingTerrainForClip(insideCloud), Is.True);
        }

        [Test]
        public void OutsideThickCloud_ClipsAtThreeInchesNotFullRadius()
        {
            var cloudFeature = CreateCloudFeature();
            extraZoneObject = CreateCloudBoxZone(
                cloudFeature,
                new Vector3(CombatScale.InchesToWorldUnits(20f), 2f, CombatScale.InchesToWorldUnits(8f)),
                new Vector3(CombatScale.InchesToWorldUnits(12f), 0f, 0f));

            Physics.SyncTransforms();
            CombatForestFogClipper.InvalidateCache();
            EnsureCacheMethod.Invoke(null, null);

            var depthWorld = CombatScale.InchesToWorldUnits(3f);
            var maxDistance = CombatScale.InchesToWorldUnits(30f);
            var origin = new Vector3(0f, 0f, 0f);
            var direction = Vector3.right;

            var clip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                origin,
                direction,
                maxDistance,
                depthWorld);

            var nearFace = CombatScale.InchesToWorldUnits(2f);
            var expectedClip = nearFace + depthWorld;
            Assert.That(clip, Is.EqualTo(expectedClip).Within(CombatScale.InchesToWorldUnits(0.35f)));
            Assert.That(clip, Is.LessThan(maxDistance - CombatScale.InchesToWorldUnits(1f)));
        }

        [Test]
        public void OutsideThinCloud_DoesNotRevealGroundBehindCloud()
        {
            var cloudFeature = CreateCloudFeature();
            extraZoneObject = CreateCloudBoxZone(
                cloudFeature,
                new Vector3(CombatScale.InchesToWorldUnits(2f), 2f, CombatScale.InchesToWorldUnits(8f)),
                new Vector3(CombatScale.InchesToWorldUnits(12f), 0f, 0f));

            Physics.SyncTransforms();
            CombatForestFogClipper.InvalidateCache();
            EnsureCacheMethod.Invoke(null, null);

            var depthWorld = CombatScale.InchesToWorldUnits(3f);
            var maxDistance = CombatScale.InchesToWorldUnits(30f);
            var origin = new Vector3(0f, 0f, 0f);
            var direction = Vector3.right;

            var clip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                origin,
                direction,
                maxDistance,
                depthWorld);

            var nearFace = CombatScale.InchesToWorldUnits(11f);
            var farFace = CombatScale.InchesToWorldUnits(13f);
            Assert.That(clip, Is.GreaterThanOrEqualTo(nearFace - CombatScale.InchesToWorldUnits(0.25f)));
            Assert.That(clip, Is.LessThanOrEqualTo(farFace + CombatScale.InchesToWorldUnits(0.25f)));
            Assert.That(clip, Is.LessThan(maxDistance - CombatScale.InchesToWorldUnits(1f)));
        }

        [Test]
        public void InsideCloudNearEdge_DoesNotFullyOpenPastCloud()
        {
            var cloudFeature = CreateCloudFeature();
            extraZoneObject = CreateCloudBoxZone(
                cloudFeature,
                new Vector3(CombatScale.InchesToWorldUnits(8f), 2f, CombatScale.InchesToWorldUnits(8f)),
                new Vector3(CombatScale.InchesToWorldUnits(12f), 0f, 0f));

            Physics.SyncTransforms();
            CombatForestFogClipper.InvalidateCache();
            EnsureCacheMethod.Invoke(null, null);

            var depthWorld = CombatScale.InchesToWorldUnits(3f);
            var maxDistance = CombatScale.InchesToWorldUnits(30f);
            var insideOrigin = new Vector3(CombatScale.InchesToWorldUnits(14f), 0f, 0f);
            var direction = Vector3.right;

            var clip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                insideOrigin,
                direction,
                maxDistance,
                depthWorld);

            var expectedExit = CombatScale.InchesToWorldUnits(16f) - CombatScale.InchesToWorldUnits(14f);
            Assert.That(clip, Is.LessThanOrEqualTo(expectedExit + CombatScale.InchesToWorldUnits(0.35f)));
            Assert.That(clip, Is.LessThan(maxDistance - CombatScale.InchesToWorldUnits(1f)));
        }

        private static CombatTerrainFeatureDefinition CreateCloudFeature()
        {
            var cloudFeature = ScriptableObject.CreateInstance<CombatTerrainFeatureDefinition>();
            SetPrivateField(cloudFeature, "featureId", "TestCloud");
            SetPrivateField(cloudFeature, "displayName", "Test Cloud");
            SetPrivateField(cloudFeature, "lineOfSightMode", CombatTerrainLineOfSightMode.BlocksCompletely);
            SetPrivateField(cloudFeature, "lineOfSightPassThroughDepthInches", 3f);
            return cloudFeature;
        }

        private static GameObject CreateCloudBoxZone(
            CombatTerrainFeatureDefinition cloudFeature,
            Vector3 size,
            Vector3 position)
        {
            var cloudObject = new GameObject("TestCloudZone");
            var collider = cloudObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = size;
            collider.center = new Vector3(0f, 1f, 0f);
            cloudObject.transform.position = position;
            var cloudZone = cloudObject.AddComponent<CombatZone>();
            SetPrivateField(cloudZone, "terrainFeature", cloudFeature);
            cloudObject.SetActive(false);
            cloudObject.SetActive(true);
            return cloudObject;
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
