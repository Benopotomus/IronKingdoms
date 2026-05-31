using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.Profiling;
#endif

namespace FOW
{
    [RequireComponent(typeof(Camera))]
    [ImageEffectAllowedInSceneView]
    [ExecuteInEditMode]
    public abstract class FowImageEffectBase : MonoBehaviour
    {
        Camera cam;

        //public bool isGL;
        private void Awake()
        {
            //isGL = SystemInfo.graphicsDeviceVersion.Contains("OpenGL");
            SetCamera();
        }

        void SetCamera()
        {
            if (cam)
                return;
            cam = GetComponent<Camera>();
            cam.depthTextureMode = DepthTextureMode.Depth | DepthTextureMode.DepthNormals;
        }

        private void OnPreRender()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
            Profiler.BeginSample("Pre-Render Fog Of War");
            SetCamera();
#endif
            if (!FogOfWarWorld.instance)
                return;

            if (!FogOfWarWorld.instance.is2D)
            {
                Matrix4x4 camToWorldMatrix = cam.cameraToWorldMatrix;

                //Matrix4x4 projectionMatrix = renderingData.cameraData.camera.projectionMatrix;
                //Matrix4x4 inverseProjectionMatrix = GL.GetGPUProjectionMatrix(projectionMatrix, true).inverse;

                //inverseProjectionMatrix[1, 1] *= -1;

                FogOfWarWorld.instance.FogOfWarMaterial.SetMatrix("_camToWorldMatrix", camToWorldMatrix);
                //FogOfWarWorld.instance.fowMat.SetMatrix("_inverseProjectionMatrix", inverseProjectionMatrix);
            }
            else
            {
                FogOfWarWorld.instance.FogOfWarMaterial.SetFloat("_cameraSize", cam.orthographicSize);
                FogOfWarWorld.instance.FogOfWarMaterial.SetVector("_cameraPosition", cam.transform.position);
                FogOfWarWorld.instance.FogOfWarMaterial.SetFloat("_cameraRotation", Mathf.DeltaAngle(0, cam.transform.eulerAngles.z));
            }
#if UNITY_EDITOR
            Profiler.EndSample();
#endif
        }

        protected void RenderImage(RenderTexture src, RenderTexture dest)
        {
#if UNITY_EDITOR
            Graphics.Blit(src, dest);
            if (!Application.isPlaying)
                return;
            Profiler.BeginSample("Render Fog Of War");
#endif
            if (!FogOfWarWorld.instance || !FogOfWarWorld.instance.enabled)
            {
                Graphics.Blit(src, dest);
#if UNITY_EDITOR
                Profiler.EndSample();
#endif
                return;
            }

            FogOfWarWorld.OnPreRenderFog();

            if (FogOfWarWorld.instance.GetFowAppearance() == FogOfWarWorld.FogOfWarAppearance.None)
                Graphics.Blit(src, dest);
            else
                Graphics.Blit(src, dest, FogOfWarWorld.instance.FogOfWarMaterial);

#if UNITY_EDITOR
            Profiler.EndSample();
#endif
        }
    }
}
