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
        private const float MillimetersPerWorldUnit = 30f;
        private const float MillimetersPerInch = 25.4f;

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

        public static float BaseDiameterWorldUnits(this ModelSize modelSize)
        {
            return modelSize.BaseDiameterMillimeters() / MillimetersPerWorldUnit;
        }

        public static float VolumeHeightInches(this ModelSize modelSize)
        {
            return modelSize switch
            {
                ModelSize.Base30mm => 1.75f,
                ModelSize.Base40mm => 2.25f,
                ModelSize.Base50mm => 2.75f,
                ModelSize.Base80mm => 3.25f,
                ModelSize.Base120mm => 5f,
                _ => 1.75f
            };
        }

        public static float VolumeHeightWorldUnits(this ModelSize modelSize)
        {
            return modelSize.VolumeHeightInches() * MillimetersPerInch / MillimetersPerWorldUnit;
        }

        public static string DisplayName(this ModelSize modelSize)
        {
            return $"{modelSize.BaseDiameterMillimeters():0}mm / {modelSize.VolumeHeightInches():0.##}\"";
        }

        public static Vector3 GetPawnScale(this ModelSize modelSize)
        {
            var diameter = modelSize.BaseDiameterWorldUnits();
            var halfHeight = modelSize.VolumeHeightWorldUnits() * 0.5f;
            return new Vector3(diameter, halfHeight, diameter);
        }
    }
}
