using UnityEngine;

namespace IronKingdoms.Combat
{
    public enum CombatTerrainLineOfSightMode
    {
        None = 0,
        BlocksCompletely = 1,
        LimitedDepth = 2
    }

    [CreateAssetMenu(menuName = "Iron Kingdoms/Combat/Terrain Feature", fileName = "TerrainFeature")]
    public class CombatTerrainFeatureDefinition : ScriptableObject
    {
        [SerializeField] private string featureId;
        [SerializeField] private string displayName;
        [TextArea] [SerializeField] private string description;

        [Header("Movement")]
        [SerializeField] private bool isRoughTerrain;
        [SerializeField, Range(0.01f, 1f)] private float movementSpeedMultiplier = 0.5f;

        [Header("Defense")]
        [SerializeField] private CombatDefenseModifierDefinition defenseModifierWhenInside;

        [Header("Line of Sight")]
        [SerializeField] private CombatTerrainLineOfSightMode lineOfSightMode = CombatTerrainLineOfSightMode.None;
        [SerializeField, Min(0f)] private float lineOfSightPassThroughDepthInches = 3f;
        [SerializeField] private bool doesNotLimitLineOfSightToHugeBasedTargets = true;

        public string FeatureId => string.IsNullOrWhiteSpace(featureId) ? name : featureId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string Description => description ?? string.Empty;
        public bool IsRoughTerrain => isRoughTerrain;
        public float MovementSpeedMultiplier => isRoughTerrain ? movementSpeedMultiplier : 1f;
        public CombatDefenseModifierDefinition DefenseModifierWhenInside => defenseModifierWhenInside;
        public CombatTerrainLineOfSightMode LineOfSightMode => lineOfSightMode;
        public float LineOfSightPassThroughDepthInches => lineOfSightPassThroughDepthInches;
        public bool DoesNotLimitLineOfSightToHugeBasedTargets => doesNotLimitLineOfSightToHugeBasedTargets;

        public bool LimitsLineOfSightDepth => lineOfSightMode == CombatTerrainLineOfSightMode.LimitedDepth;

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(featureId))
            {
                featureId = name;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = name;
            }

            movementSpeedMultiplier = Mathf.Clamp(movementSpeedMultiplier, 0.01f, 1f);
            lineOfSightPassThroughDepthInches = Mathf.Max(0f, lineOfSightPassThroughDepthInches);
        }
    }
}
