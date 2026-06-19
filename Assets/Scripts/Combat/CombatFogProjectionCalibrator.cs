using FOW;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Measures per-frame fog display error (depth reconstruction vs ground truth) and reports
    /// how much offset is needed. Attach to the same camera as FowImageEffectOpaque.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(-200)]
    [ExecuteAlways]
    public sealed class CombatFogProjectionCalibrator : MonoBehaviour
    {
        private const int DepthCaptureSize = 32;

        [Header("Calibration")]
        [Tooltip("Sample the depth buffer each frame and estimate the display offset.")]
        [SerializeField] private bool measureEachFrame = true;

        [Tooltip("Write the measured offset into CombatFogProjectionTuning every frame.")]
        [SerializeField] private bool autoApplySuggestedOffset;

        [Tooltip("Only average samples where depth hits near the ground plane.")]
        [SerializeField] private bool groundSamplesOnly = true;

        [SerializeField, Range(3, 11)] private int gridSize = 7;

        [SerializeField, Min(0.01f)] private float groundHitToleranceWorld = 0.15f;

        [Header("Debug HUD")]
        [SerializeField] private bool showDebugHud = true;

        [SerializeField] private bool probeAtMouseCursor = true;

        [Header("Latest estimate (inches)")]
        [SerializeField] private float suggestedOffsetXInches;
        [SerializeField] private float suggestedOffsetYInches;
        [SerializeField] private float meanErrorInches;
        [SerializeField] private int samplesUsed;

        [Header("Cursor probe (inches)")]
        [SerializeField] private float cursorOffsetXInches;
        [SerializeField] private float cursorOffsetYInches;

        private Camera cam;
        private Material copyDepthMaterial;
        private RenderTexture depthCaptureRt;
        private Texture2D depthReadback;
        private bool depthReady;
        private CombatFogProjectionTuning tuning;

        public float SuggestedOffsetXInches => suggestedOffsetXInches;
        public float SuggestedOffsetYInches => suggestedOffsetYInches;

        private void Awake()
        {
            EnsureCamera();
            EnsureResources();
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }

        private void OnEnable()
        {
            EnsureCamera();
            EnsureResources();
            ResolveTuning();
        }

        [ImageEffectOpaque]
        private void OnRenderImage(RenderTexture src, RenderTexture dest)
        {
            if (measureEachFrame && Application.isPlaying)
            {
                CaptureDepth();
            }

            Graphics.Blit(src, dest);
        }

        private void LateUpdate()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return;
            }
#endif
            if (!measureEachFrame || !depthReady)
            {
                if (probeAtMouseCursor)
                {
                    UpdateCursorProbe();
                }

                return;
            }

            ComputeSuggestedOffset();
            if (autoApplySuggestedOffset)
            {
                ApplySuggestedOffset();
            }

            if (probeAtMouseCursor)
            {
                UpdateCursorProbe();
            }
        }

        private void OnGUI()
        {
            if (!showDebugHud || !Application.isPlaying)
            {
                return;
            }

            const int width = 360;
            var rect = new Rect(12f, 12f, width, probeAtMouseCursor ? 150f : 118f);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(rect);
            GUILayout.Label("Fog projection calibrator");
            GUILayout.Label($"Suggested offset (in): X {suggestedOffsetXInches:F3}  Y {suggestedOffsetYInches:F3}");
            GUILayout.Label($"Mean error: {meanErrorInches:F3}\"  samples: {samplesUsed}");
            if (probeAtMouseCursor)
            {
                GUILayout.Label($"Cursor delta (in): X {cursorOffsetXInches:F3}  Y {cursorOffsetYInches:F3}");
            }

            GUILayout.Label("Auto-apply: " + (autoApplySuggestedOffset ? "ON" : "OFF"));
            GUILayout.EndArea();
        }

        public void ApplySuggestedOffset()
        {
            ResolveTuning();
            if (tuning == null)
            {
                return;
            }

            tuning.OffsetXInches = suggestedOffsetXInches;
            tuning.OffsetYInches = suggestedOffsetYInches;
        }

        private void ComputeSuggestedOffset()
        {
            EnsureCamera();
            if (cam == null || depthReadback == null)
            {
                return;
            }

            var groundY = CombatFogDisplaySampling.GetGroundY();
            var maxDistance = CombatFogDisplaySampling.GetMaxFogDistance();
            var sum = Vector2.zero;
            var count = 0;
            var errorSum = 0f;

            for (var y = 0; y < gridSize; y++)
            {
                for (var x = 0; x < gridSize; x++)
                {
                    var viewportUv = new Vector2(
                        (x + 0.5f) / gridSize,
                        (y + 0.5f) / gridSize);

                    var rawDepth = SampleCapturedDepth(viewportUv);
                    if (!CombatFogDisplaySampling.TrySampleDisplayFogSpace(
                            cam,
                            viewportUv,
                            rawDepth,
                            groundY,
                            maxDistance,
                            out var displayPos))
                    {
                        continue;
                    }

                    if (!CombatFogDisplaySampling.TrySampleGroundTruthFogSpace(
                            cam,
                            viewportUv,
                            groundY,
                            out var truthPos))
                    {
                        continue;
                    }

                    if (groundSamplesOnly)
                    {
                        var displayWorldY = ReconstructWorldY(cam, viewportUv, rawDepth);
                        if (Mathf.Abs(displayWorldY - groundY) > groundHitToleranceWorld)
                        {
                            continue;
                        }
                    }

                    var delta = truthPos - displayPos;
                    sum += delta;
                    errorSum += delta.magnitude;
                    count++;
                }
            }

            samplesUsed = count;
            if (count <= 0)
            {
                suggestedOffsetXInches = 0f;
                suggestedOffsetYInches = 0f;
                meanErrorInches = 0f;
                return;
            }

            var meanDelta = sum / count;
            suggestedOffsetXInches = CombatScale.WorldUnitsToInches(meanDelta.x);
            suggestedOffsetYInches = CombatScale.WorldUnitsToInches(meanDelta.y);
            meanErrorInches = CombatScale.WorldUnitsToInches(errorSum / count);
        }

        private void UpdateCursorProbe()
        {
            EnsureCamera();
            if (cam == null || !depthReady)
            {
                cursorOffsetXInches = 0f;
                cursorOffsetYInches = 0f;
                return;
            }

            var viewport = cam.ScreenToViewportPoint(Input.mousePosition);
            if (viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
            {
                return;
            }

            var viewportUv = new Vector2(viewport.x, viewport.y);
            var groundY = CombatFogDisplaySampling.GetGroundY();
            var maxDistance = CombatFogDisplaySampling.GetMaxFogDistance();
            var rawDepth = SampleCapturedDepth(viewportUv);
            if (!CombatFogDisplaySampling.TrySampleDisplayFogSpace(
                    cam,
                    viewportUv,
                    rawDepth,
                    groundY,
                    maxDistance,
                    out var displayPos)
                || !CombatFogDisplaySampling.TrySampleGroundTruthFogSpace(
                    cam,
                    viewportUv,
                    groundY,
                    out var truthPos))
            {
                return;
            }

            var delta = truthPos - displayPos;
            cursorOffsetXInches = CombatScale.WorldUnitsToInches(delta.x);
            cursorOffsetYInches = CombatScale.WorldUnitsToInches(delta.y);
        }

        private float SampleCapturedDepth(Vector2 viewportUv)
        {
            var x = Mathf.Clamp(Mathf.FloorToInt(viewportUv.x * DepthCaptureSize), 0, DepthCaptureSize - 1);
            var y = Mathf.Clamp(Mathf.FloorToInt(viewportUv.y * DepthCaptureSize), 0, DepthCaptureSize - 1);
            return depthReadback.GetPixel(x, y).r;
        }

        private static float ReconstructWorldY(Camera camera, Vector2 viewportUv, float rawDepth01)
        {
            var depth = rawDepth01;
            if (SystemInfo.usesReversedZBuffer)
            {
                depth = 1f - depth;
            }

            var near = camera.nearClipPlane;
            var far = camera.farClipPlane;
            var isOrtho = camera.orthographic ? 1f : 0f;
            var zOrtho = Mathf.Lerp(near, far, depth);
            var zPers = near * far / Mathf.Lerp(far, near, depth);
            var vz = Mathf.Lerp(zPers, zOrtho, isOrtho);

            var projection = camera.projectionMatrix;
            var vpos = new Vector3(
                (viewportUv.x * 2f - 1f - projection.m02) / projection.m00 * Mathf.Lerp(vz, 1f, isOrtho),
                (viewportUv.y * 2f - 1f - projection.m12) / projection.m11 * Mathf.Lerp(vz, 1f, isOrtho),
                -vz);

            return camera.cameraToWorldMatrix.MultiplyPoint(vpos).y;
        }

        private void CaptureDepth()
        {
            EnsureResources();
            var depthTexture = Shader.GetGlobalTexture("_CameraDepthTexture");
            if (depthTexture == null || copyDepthMaterial == null)
            {
                depthReady = false;
                return;
            }

            Graphics.Blit(depthTexture, depthCaptureRt, copyDepthMaterial);
            RenderTexture.active = depthCaptureRt;
            depthReadback.ReadPixels(new Rect(0, 0, DepthCaptureSize, DepthCaptureSize), 0, 0);
            depthReadback.Apply(false, false);
            RenderTexture.active = null;
            depthReady = true;
        }

        private void ResolveTuning()
        {
            if (tuning != null)
            {
                return;
            }

            var fogWorld = FogOfWarWorld.instance ?? FindFirstObjectByType<FogOfWarWorld>();
            tuning = fogWorld != null
                ? CombatFogProjectionTuning.EnsureOnFogWorld(fogWorld)
                : null;
        }

        private void EnsureCamera()
        {
            if (cam != null)
            {
                return;
            }

            cam = GetComponent<Camera>();
            if (cam != null)
            {
                cam.depthTextureMode |= DepthTextureMode.Depth;
            }
        }

        private void EnsureResources()
        {
            if (copyDepthMaterial == null)
            {
                var shader = Shader.Find("Hidden/Combat/CopyDepth");
                if (shader != null)
                {
                    copyDepthMaterial = new Material(shader);
                }
            }

            if (depthCaptureRt == null)
            {
                depthCaptureRt = new RenderTexture(DepthCaptureSize, DepthCaptureSize, 0, RenderTextureFormat.RFloat)
                {
                    name = "CombatFogDepthCapture",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            if (depthReadback == null)
            {
                depthReadback = new Texture2D(DepthCaptureSize, DepthCaptureSize, TextureFormat.RFloat, false, true)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
            }
        }

        private void ReleaseResources()
        {
            if (copyDepthMaterial != null)
            {
                Destroy(copyDepthMaterial);
                copyDepthMaterial = null;
            }

            if (depthCaptureRt != null)
            {
                depthCaptureRt.Release();
                Destroy(depthCaptureRt);
                depthCaptureRt = null;
            }

            if (depthReadback != null)
            {
                Destroy(depthReadback);
                depthReadback = null;
            }
        }

        public static CombatFogProjectionCalibrator EnsureOnCamera(Camera camera)
        {
            if (camera == null)
            {
                return null;
            }

            var calibrator = camera.GetComponent<CombatFogProjectionCalibrator>();
            if (calibrator == null)
            {
                calibrator = camera.gameObject.AddComponent<CombatFogProjectionCalibrator>();
            }

            return calibrator;
        }
    }
}
