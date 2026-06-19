namespace IronKingdoms.Combat
{
    /// <summary>
    /// Runtime switch between baseline stock FOW contours and combat forest clipping.
    /// </summary>
    public static class CombatForestFogPassSettings
    {
        public static bool UseForestPass { get; set; } = true;
    }
}
