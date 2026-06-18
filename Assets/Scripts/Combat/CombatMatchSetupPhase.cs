namespace IronKingdoms.Combat
{
    /// <summary>
    /// Ordered phases for bringing a combat encounter from scene load to playable state.
    /// </summary>
    public enum CombatMatchSetupPhase
    {
        None = 0,
        LoadingMap,
        PreparingNavigation,
        RegisteringMapScene,
        ResolvingSpawnAnchors,
        BuildingVisualizers,
        SpawningArmies,
        InitializingVisibility,
        BeginningMatch,
        Ready,
        Failed
    }
}
