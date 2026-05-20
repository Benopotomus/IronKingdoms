using UnityEngine;

namespace IronKingdoms.Combat
{
    public enum ModelSize
    {
        Base30mm = 0,
        Base40mm = 1,
        Base50mm = 2,
        Base80mm = 3,
        Base120mm = 4
    }

    public static class ModelSizeExtensions
    {
        public static float BaseDiameterMillimeters(this ModelSize modelSize)
        {
            return modelSize switch
            {
                ModelSize.Base30mm => 30f,
                ModelSize.Base40mm => 40f,
                ModelSize.Base50mm => 50f,
                ModelSize.Base80mm => 80f,
                ModelSize.Base120mm => 120f,
                _ => 30f
            };
        }

        public static float VolumeHeightInches(this ModelSize modelSize)
        {
            return modelSize switch
            {
                ModelSize.Base30mm => 1.75f,
                ModelSize.Base40mm => 2.25f,
                ModelSize.Base50mm => 2.75f,
                ModelSize.Base80mm => 5f,
                ModelSize.Base120mm => 5f,
                _ => 1.75f
            };
        }

        public static string DisplayName(this ModelSize modelSize)
        {
            return $"{modelSize.BaseDiameterMillimeters():0}mm / {modelSize.VolumeHeightInches():0.##}\"";
        }

        public static Vector3 GetPawnScale(this ModelSize modelSize)
        {
            var diameterScale = modelSize.BaseDiameterMillimeters() / 30f;
            var heightScale = modelSize.VolumeHeightInches() / 1.75f;
            return new Vector3(diameterScale, heightScale, diameterScale);
        }
    }
}
