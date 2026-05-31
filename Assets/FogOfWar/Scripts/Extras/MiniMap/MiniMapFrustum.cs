using UnityEngine;
using UnityEngine.UI;

namespace FOW
{
    public class MiniMapFrustum : MonoBehaviour
    {
        [Header("References")]
        public Collider MapCollider;
        public RawImage RawImageComponent;

        [Header("Raycasting")]
        public bool UseRaycast = true;
        public LayerMask RaycastLayers = ~0;
        public float RayDistance = 1000f;

        [Header("Fallback Plane")]
        [Tooltip("Offset along the fog-of-war up axis from this transform's position")]
        public Vector3 PlaneCenterOffset = Vector3.zero;
        public Vector3 PlaneNormal = Vector3.up;

        [Header("Line Settings")]
        public Color LineColor = Color.white;
        [Range(0.5f, 10f)] public float LineWidth = 2f;
        [Range(0.1f, 5f)] public float Softness = 1f;
        [SerializeField] private bool ClampToBounds = true;

        [Tooltip("Scale line width with canvas scale factor\nIgnored when Use Render Texture Pixel Size is enabled.")]
        [SerializeField] private bool ScaleWithCanvasSize = false;

        [Tooltip("Use the render textures resolution instead of the UI element's screen size for line width")]
        [SerializeField] private bool UseRenderTexturePixelSize = false;

#if UNITY_EDITOR
        [Header("Gizmos")]
        public bool ShowGizmo = true;
        public float GizmoSize = 500f;
#endif

        private Material material;
        private Vector2[] uvs = new Vector2[4];
        private Vector2[] clippedUVs = new Vector2[8];

        // Sutherland-Hodgman working arrays
        private Vector2[] clipA = new Vector2[8];
        private Vector2[] clipB = new Vector2[8];
        private int clipCount;

        private RectTransform rectTransform;
        private Canvas canvas;

        // Shader IDs
        private static readonly int LineColorID = Shader.PropertyToID("_LineColor");
        private static readonly int LineWidthID = Shader.PropertyToID("_LineWidth");
        private static readonly int SoftnessID = Shader.PropertyToID("_Softness");
        private static readonly int TexSizeOverrideID = Shader.PropertyToID("_TexSizeOverride");
        private static readonly int InsetXID = Shader.PropertyToID("_InsetX");
        private static readonly int InsetYID = Shader.PropertyToID("_InsetY");

        private static readonly int FrustumUV0ID = Shader.PropertyToID("_FrustumUV0");
        private static readonly int FrustumUV1ID = Shader.PropertyToID("_FrustumUV1");
        private static readonly int FrustumUV2ID = Shader.PropertyToID("_FrustumUV2");
        private static readonly int FrustumUV3ID = Shader.PropertyToID("_FrustumUV3");
        private static readonly int FrustumUV4ID = Shader.PropertyToID("_FrustumUV4");
        private static readonly int FrustumUV5ID = Shader.PropertyToID("_FrustumUV5");
        private static readonly int FrustumUV6ID = Shader.PropertyToID("_FrustumUV6");
        private static readonly int FrustumUV7ID = Shader.PropertyToID("_FrustumUV7");
        private static int[] FrustumShaderIds;
        private Vector3 PlaneCenter => transform.position + PlaneCenterOffset;

        private Plane FrustumPlane => new Plane(PlaneNormal.normalized, PlaneCenter);

        void Start()
        {
            uvs = new Vector2[4];
            clippedUVs = new Vector2[8];
            clipA = new Vector2[8];
            clipB = new Vector2[8];

            FrustumShaderIds = new int[] {FrustumUV0ID, FrustumUV1ID, FrustumUV2ID, FrustumUV3ID, FrustumUV4ID, FrustumUV5ID, FrustumUV6ID, FrustumUV7ID};
            material = RawImageComponent.material;
            material.EnableKeyword("FRUSTUM_ENABLED");

            RawImageComponent.TryGetComponent<RectTransform>(out rectTransform);
            canvas = RawImageComponent.GetComponentInParent<Canvas>();

            UpdateClampKeyword(ClampToBounds);
        }

        public void UpdateClampKeyword(bool clamp)
        {
            ClampToBounds = clamp;
            if (clamp)
                material.EnableKeyword("FRUSTUM_CLAMP");
            else
                material.DisableKeyword("FRUSTUM_CLAMP");
        }

        void Update()
        {
            Vector4 bounds = FogOfWarWorld.CachedFowShaderBounds;

            Vector2[] screenCorners = {
                new Vector2(0, 0),
                new Vector2(0, Screen.height),
                new Vector2(Screen.width, Screen.height),
                new Vector2(Screen.width, 0)
            };

            for (int i = 0; i < 4; i++)
            {
                Vector3 worldPos = GetWorldSpaceFrustumCorner(screenCorners[i]);
                Vector2 fogPos = FogOfWarWorld.instance.GetFowBoundsPositionFromWorldPosition(worldPos);

                uvs[i] = new Vector2(
                    ((fogPos.x - bounds.y) + (bounds.x * 0.5f)) / bounds.x,
                    ((fogPos.y - bounds.w) + (bounds.z * 0.5f)) / bounds.z
                );
            }

            Vector2 pixelSize = GetPixelSize();

            float insetX = (LineWidth * 0.5f) / pixelSize.x;
            float insetY = (LineWidth * 0.5f) / pixelSize.y;

            if (ClampToBounds)
            {
                ClipPolygonToUnitBox(insetX, insetY);
            }
            else
            {
                // No clipping - just duplicate corners into 8 slots
                for (int i = 0; i < 4; i++)
                {
                    clippedUVs[i * 2] = uvs[i];
                    clippedUVs[i * 2 + 1] = uvs[i];
                }
            }

            material.SetColor(LineColorID, LineColor);
            material.SetFloat(LineWidthID, LineWidth);
            material.SetFloat(SoftnessID, Softness);
            material.SetVector(TexSizeOverrideID, new Vector4(pixelSize.x, pixelSize.y, 0, 0));
            material.SetFloat(InsetXID, insetX);
            material.SetFloat(InsetYID, insetY);

            for (int i = 0; i < clippedUVs.Length; i++)
                material.SetVector(FrustumShaderIds[i], clippedUVs[i]);
        }

        Vector3 GetWorldSpaceFrustumCorner(Vector2 screenPos)
        {
            Ray ray = Camera.main.ScreenPointToRay(screenPos);

            if (UseRaycast && Physics.Raycast(ray, out RaycastHit hit, RayDistance, RaycastLayers))
                return hit.point;

            Plane plane = FrustumPlane;
            if (plane.Raycast(ray, out float enter) && enter > 0f)
                return ray.GetPoint(enter);

            // Fallback: project ray direction onto plane and extend
            Vector3 normal = PlaneNormal.normalized;
            Vector3 flatDir = (ray.direction - normal * Vector3.Dot(ray.direction, normal)).normalized;

            // Project camera position onto plane
            float camDist = plane.GetDistanceToPoint(ray.origin);
            Vector3 camOnPlane = ray.origin - normal * camDist;

            // Extend along flat direction
            return camOnPlane + flatDir * 1000;
        }

        private Vector2 GetPixelSize()
        {
            if (!UseRenderTexturePixelSize && rectTransform != null)
            {
                Vector2 size = rectTransform.rect.size;

                // Account for canvas scaling
                if (ScaleWithCanvasSize && canvas != null)
                    size *= canvas.scaleFactor;

                return new Vector2(Mathf.Max(1f, size.x), Mathf.Max(1f, size.y));
            }

            // Default: use texture resolution
            Texture tex = RawImageComponent.texture;
            if (tex != null)
                return new Vector2(tex.width, tex.height);

            return new Vector2(256, 256); // Fallback
        }

        #region Sutherland-Hodgman

        private void ClipPolygonToUnitBox(float insetX, float insetY)
        {
            float minX = insetX;
            float maxX = 1f - insetX;
            float minY = insetY;
            float maxY = 1f - insetY;

            clipA[0] = uvs[0];
            clipA[1] = uvs[1];
            clipA[2] = uvs[2];
            clipA[3] = uvs[3];
            clipCount = 4;

            ClipAgainstEdge(minX, true, true);   // Left
            ClipAgainstEdge(maxX, true, false);  // Right
            ClipAgainstEdge(minY, false, true);  // Bottom
            ClipAgainstEdge(maxY, false, false); // Top

            if (clipCount == 0 && IsPointInQuad(new Vector2(0.5f, 0.5f), uvs))
            {
                clipA[0] = new Vector2(minX, minY);
                clipA[1] = new Vector2(minX, maxY);
                clipA[2] = new Vector2(maxX, maxY);
                clipA[3] = new Vector2(maxX, minY);
                clipCount = 4;
            }

            if (clipCount == 0)
            {
                for (int i = 0; i < 8; i++)
                    clippedUVs[i] = Vector2.zero;
            }
            else
            {
                for (int i = 0; i < 8; i++)
                    clippedUVs[i] = clipA[Mathf.Min(i, clipCount - 1)];
            }
        }

        private void ClipAgainstEdge(float edgeVal, bool isX, bool keepGreater)
        {
            if (clipCount == 0) return;

            int outCount = 0;

            for (int i = 0; i < clipCount; i++)
            {
                Vector2 curr = clipA[i];
                Vector2 next = clipA[(i + 1) % clipCount];

                float currVal = isX ? curr.x : curr.y;
                float nextVal = isX ? next.x : next.y;

                bool currIn = keepGreater ? currVal >= edgeVal : currVal <= edgeVal;
                bool nextIn = keepGreater ? nextVal >= edgeVal : nextVal <= edgeVal;

                if (currIn)
                {
                    clipB[outCount++] = curr;
                    if (!nextIn)
                        clipB[outCount++] = Intersect(curr, next, edgeVal, isX);
                }
                else if (nextIn)
                {
                    clipB[outCount++] = Intersect(curr, next, edgeVal, isX);
                }
            }

            // Swap buffers
            var temp = clipA;
            clipA = clipB;
            clipB = temp;
            clipCount = outCount;
        }

        private Vector2 Intersect(Vector2 a, Vector2 b, float edge, bool isX)
        {
            float t = isX
                ? (edge - a.x) / (b.x - a.x)
                : (edge - a.y) / (b.y - a.y);
            return Vector2.Lerp(a, b, t);
        }

        private bool IsPointInQuad(Vector2 p, Vector2[] quad)
        {
            float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
            {
                return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
            }

            bool b1 = Sign(p, quad[0], quad[1]) < 0f;
            bool b2 = Sign(p, quad[1], quad[2]) < 0f;
            bool b3 = Sign(p, quad[2], quad[3]) < 0f;
            bool b4 = Sign(p, quad[3], quad[0]) < 0f;

            return (b1 == b2) && (b2 == b3) && (b3 == b4);
        }

        #endregion

#if UNITY_EDITOR

        private void OnValidate()
        {
            if (!Application.isPlaying)
                return;
            material = RawImageComponent.material;
            UpdateClampKeyword(ClampToBounds);
        }

        void OnDrawGizmosSelected()
        {
            if (!ShowGizmo)
                return;

            Vector3 up = PlaneNormal.normalized;
            Vector3 center = transform.position + PlaneCenterOffset;

            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, up);

            Gizmos.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.25f);
            Gizmos.DrawCube(Vector3.zero, new Vector3(GizmoSize, 0.01f, GizmoSize));

            Gizmos.matrix = Matrix4x4.identity;

            UnityEditor.Handles.Label(center, "Minimap Frustum Plane");
        }
#endif

    }
}