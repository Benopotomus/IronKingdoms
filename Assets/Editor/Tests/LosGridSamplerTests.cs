using NUnit.Framework;
using UnityEngine;

namespace IronKingdoms.Combat.Tests
{
    public class LosGridSamplerTests
    {
        [Test]
        public void PackAndUnpackCellKey_RoundTrips()
        {
            CombatLosGridSampler.UnpackCellKey(
                CombatLosGridSampler.PackCellKey(-3, 12),
                out var cellX,
                out var cellZ);

            Assert.AreEqual(-3, cellX);
            Assert.AreEqual(12, cellZ);
        }

        [Test]
        public void SamplePolarDistance_InterpolatesBetweenNeighborRays()
        {
            var rays = new[] { 10f, 20f, 30f, 40f };
            // Angle halfway between ray 0 (0°) and ray 1 (90°) on a 4-ray circle.
            var distance = CombatLosGridSampler.SamplePolarDistance(rays, Mathf.PI * 0.25f);
            Assert.AreEqual(15f, distance, 0.001f);
        }

        [Test]
        public void SamplePolarDistance_WrapsAroundFullCircle()
        {
            var rays = new[] { 10f, 20f, 30f, 40f };
            // Just before 0 / after last ray should blend 40 -> 10.
            var distance = CombatLosGridSampler.SamplePolarDistance(rays, Mathf.PI * 2f * 0.875f);
            Assert.AreEqual(25f, distance, 0.001f);
        }

        [Test]
        public void CellCenterWorld_UsesCellSizeAndHalfOffset()
        {
            var center = CombatLosGridSampler.CellCenterWorld(2, -1, 1f, 0.5f);
            Assert.AreEqual(2.5f, center.x, 0.0001f);
            Assert.AreEqual(0.5f, center.y, 0.0001f);
            Assert.AreEqual(-0.5f, center.z, 0.0001f);
        }
    }
}
