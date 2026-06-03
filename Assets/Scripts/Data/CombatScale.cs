namespace IronKingdoms.Combat
{
    /// <summary>
    /// Converts Warmachine-style inch measurements to Unity world units for the combat prototype.
    /// See Docs/VisibilityAndScale.md for the full scale reference.
    /// </summary>
    public static class CombatScale
    {
        public const float MillimetersPerInch = 25.4f;
        public const float MillimetersPerWorldUnit = 30f;
        public const float WorldUnitsPerInch = MillimetersPerInch / MillimetersPerWorldUnit;
        public const float InchesPerWorldUnit = MillimetersPerWorldUnit / MillimetersPerInch;

        /// <summary>
        /// Default fog-of-war reveal radius for player models (inches).
        /// </summary>
        public const float DefaultVisibilityRangeInches = 36f;

        public static float InchesToWorldUnits(float inches)
        {
            return inches * WorldUnitsPerInch;
        }

        public static float WorldUnitsToInches(float worldUnits)
        {
            return worldUnits * InchesPerWorldUnit;
        }
    }
}
