using FOW;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Matches Hidden/FullScreen/FOW/SolidColor depth reconstruction for calibration probes.
    /// </summary>
    public static class CombatFogDisplaySampling
    {
        public static bool TrySampleGroundTruthFogSpace(
            Camera camera,
            Vector2 viewportUv,
            float groundY,
            out Vector2 fogSpacePos)
        {
            fogSpacePos = Vector2.zero;
            if (camera == null)
            {
                return false;
            }

            var groundPlane = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
            var ray = camera.ViewportPointToRay(new Vector3(viewportUv.x, viewportUv.y, 0f));
            if (!groundPlane.Raycast(ray, out var distance) || distance <= 0f)
            {
                return false;
            }

            var hit = ray.GetPoint(distance);
            fogSpacePos = new Vector2(hit.x, hit.z);
            return true;
        }

        public static bool TrySampleDisplayFogSpace(
            Camera camera,
            Vector2 viewportUv,
            float rawDepth01,
            float groundY,
            float maxFogDistance,
            out Vector2 fogSpacePos)
        {
            fogSpacePos = Vector2.zero;
            if (camera == null)
            {
                return false;
            }

            if (FogOfWarWorld.instance != null
                && FogOfWarWorld.instance.GamePlaneOrientation == FogOfWarWorld.GamePlane.XZ
                && !FogOfWarWorld.instance.is2D)
            {
                var ray = camera.ViewportPointToRay(new Vector3(viewportUv.x, viewportUv.y, 0f));
                var groundPlane = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
                if (!groundPlane.Raycast(ray, out var distance) || distance <= 0f)
                {
                    return false;
                }

                var hit = ray.GetPoint(distance);
                fogSpacePos = new Vector2(hit.x, hit.z);
                return true;
            }

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
            var p11 = projection.m00;
            var p22 = projection.m11;
            var p13 = projection.m02;
            var p23 = projection.m12;

            var clipX = viewportUv.x * 2f - 1f;
            var clipY = viewportUv.y * 2f - 1f;
            var vpos = new Vector3(
                (clipX - p13) / p11 * Mathf.Lerp(vz, 1f, isOrtho),
                (clipY - p23) / p22 * Mathf.Lerp(vz, 1f, isOrtho),
                -vz);

            var worldPos = camera.cameraToWorldMatrix.MultiplyPoint(vpos);

            if (vz >= maxFogDistance * 0.999f)
            {
                var camPos = camera.transform.position;
                var dir = (worldPos - camPos).normalized;
                if (Mathf.Abs(dir.y) > 1e-5f)
                {
                    var t = (groundY - camPos.y) / dir.y;
                    if (t > 0f)
                    {
                        worldPos = camPos + dir * t;
                    }
                }
            }

            fogSpacePos = new Vector2(worldPos.x, worldPos.z);
            return true;
        }

        public static float GetGroundY()
        {
            return FogOfWarWorld.instance != null
                ? FogOfWarWorld.instance.WorldBounds.center.y
                : 0f;
        }

        public static float GetMaxFogDistance()
        {
            return FogOfWarWorld.instance != null
                ? FogOfWarWorld.instance.MaxFogDistance
                : 10000f;
        }
    }
}
