using FOW;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Live display-only fog projection tuning. Shifts/rotates how the fullscreen fog shader
    /// maps screen pixels to _FowRT samples. Does not change revealer rays or texture writes.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [DefaultExecutionOrder(50)]
    public sealed class CombatFogProjectionTuning : MonoBehaviour
    {
        public static readonly int DisplayTuningId = Shader.PropertyToID("_combatFogDisplayTuning");
        public static readonly int DisplayRotationId = Shader.PropertyToID("_combatFogDisplayRotation");
        public static readonly int TabletopGroundYId = Shader.PropertyToID("_combatTabletopGroundY");

        [Header("Offset (display only, inches)")]
        [Tooltip("East/west shift on the tabletop.")]
        [SerializeField] private float offsetXInches;

        [Tooltip("North/south shift on the tabletop (map Z).")]
        [SerializeField] private float offsetYInches;

        [Header("Rotation (display only)")]
        [Tooltip("Rotates fog sampling around the map center, in degrees.")]
        [SerializeField] private float rotationDegrees;

        [Header("Advanced")]
        [Tooltip("Scales fog lookup around the map center. 1 = unchanged.")]
        [SerializeField] private Vector2 sampleScale = Vector2.one;

        public float OffsetXInches
        {
            get => offsetXInches;
            set
            {
                offsetXInches = value;
                ApplyToShader();
            }
        }

        public float OffsetYInches
        {
            get => offsetYInches;
            set
            {
                offsetYInches = value;
                ApplyToShader();
            }
        }

        public float RotationDegrees
        {
            get => rotationDegrees;
            set
            {
                rotationDegrees = value;
                ApplyToShader();
            }
        }

        private void OnDisable()
        {
            Shader.SetGlobalVector(DisplayTuningId, Vector4.zero);
            Shader.SetGlobalFloat(DisplayRotationId, 0f);
            Shader.SetGlobalFloat(TabletopGroundYId, 0f);
        }

        private void OnDestroy()
        {
            OnDisable();
        }

        private void OnEnable()
        {
            ApplyToShader();
        }

        private void OnValidate()
        {
            sampleScale.x = Mathf.Max(sampleScale.x, 0.001f);
            sampleScale.y = Mathf.Max(sampleScale.y, 0.001f);
            ApplyToShader();
        }

        private void LateUpdate()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return;
            }
#endif
            ApplyToShader();
        }

        [ContextMenu("Reset Display Tuning")]
        public void ResetDisplayTuning()
        {
            offsetXInches = 0f;
            offsetYInches = 0f;
            rotationDegrees = 0f;
            sampleScale = Vector2.one;
            ApplyToShader();
        }

        public void ApplyToShader()
        {
            var worldOffset = new Vector2(
                CombatScale.InchesToWorldUnits(offsetXInches),
                CombatScale.InchesToWorldUnits(offsetYInches));

            Shader.SetGlobalVector(
                DisplayTuningId,
                new Vector4(
                    worldOffset.x,
                    worldOffset.y,
                    sampleScale.x,
                    sampleScale.y));

            Shader.SetGlobalFloat(DisplayRotationId, rotationDegrees);

            var groundY = FogOfWarWorld.instance != null
                ? FogOfWarWorld.instance.WorldBounds.center.y
                : 0f;
            Shader.SetGlobalFloat(TabletopGroundYId, groundY);
        }

        public static CombatFogProjectionTuning EnsureOnFogWorld(FogOfWarWorld fogWorld)
        {
            if (fogWorld == null)
            {
                return null;
            }

            var tuning = fogWorld.GetComponent<CombatFogProjectionTuning>();
            if (tuning == null)
            {
                tuning = fogWorld.gameObject.AddComponent<CombatFogProjectionTuning>();
            }

            return tuning;
        }
    }
}
