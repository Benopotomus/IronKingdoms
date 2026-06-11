using FOW;
using Unity.Mathematics;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Small adapter between stock FOW's 2D ray buffers and combat's world-space tabletop rays.
    /// </summary>
    internal static class CombatFogProjection
    {
        public static Vector3 Direction2DToWorld(float2 direction, FogOfWarRevealer3D.PlaneProjection projection)
        {
            var world = float3.zero;
            world[projection.Axis0] = direction.x;
            world[projection.Axis1] = direction.y;
            return (Vector3)math.normalizesafe(world);
        }
    }
}
