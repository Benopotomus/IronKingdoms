using Unity.Mathematics;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Precomputed unit directions for the 720-bin forest fog LUT (avoids per-bin sin/cos).
    /// </summary>
    internal static class CombatForestFogAngularTables
    {
        public static readonly float2[] Directions2D;
        public static readonly Vector3[] DirectionsWorldXZ;

        static CombatForestFogAngularTables()
        {
            var count = CombatForestFogAngularClipperLut.SampleCount;
            Directions2D = new float2[count];
            DirectionsWorldXZ = new Vector3[count];

            for (var i = 0; i < count; i++)
            {
                var angle = (i / (float)count) * math.PI * 2f;
                var dir2 = new float2(math.cos(angle), math.sin(angle));
                Directions2D[i] = dir2;
                DirectionsWorldXZ[i] = new Vector3(dir2.x, 0f, dir2.y);
            }
        }

        public static float2 GetDirection2D(int index, int sampleCount)
        {
            if (sampleCount == Directions2D.Length)
            {
                return Directions2D[index];
            }

            var angle = (index / (float)sampleCount) * math.PI * 2f;
            return new float2(math.cos(angle), math.sin(angle));
        }

        public static Vector3 GetDirectionWorldXZ(int index, int sampleCount)
        {
            if (sampleCount == DirectionsWorldXZ.Length)
            {
                return DirectionsWorldXZ[index];
            }

            var dir2 = GetDirection2D(index, sampleCount);
            return new Vector3(dir2.x, 0f, dir2.y);
        }
    }
}
