using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Shared Unity layers for combat line-of-sight and fog-of-war occlusion.
    /// </summary>
    public static class CombatLayers
    {
        public const string FogOccluderLayerName = "FogOccluder";

        public static int FogOccluderMask
        {
            get
            {
                var mask = LayerMask.GetMask(FogOccluderLayerName);
                return mask != 0 ? mask : 1 << 6;
            }
        }

        public static int LineOfSightBlockerMask => FogOccluderMask;
    }
}
