using UnityEngine;

namespace IronKingdoms.Combat
{
    public class CombatCameraManager : MonoBehaviour
    {
        private const float CameraControlsPanelWidth = 460f;
        private const float CameraControlsPanelHeight = 54f;
        private const float CameraControlsPanelTopMargin = 12f;
        private const float CameraOrbitFallbackForwardDistance = 1f;
        private const float CameraOrbitMinimumDistance = 0.1f;
        private const float MinimumVectorSqrMagnitude = 0.0001f;
        private const float InputAxisDeadzone = 0.001f;
        private const int MiddleMouseButton = 2;

        [SerializeField] private Camera targetCamera;
        [SerializeField, Min(1f)] private float cameraKeyboardPanSpeed = 10f;
        [SerializeField, Min(0.001f)] private float cameraDragPanSensitivity = 0.02f;
        [SerializeField, Min(0.01f)] private float cameraRotationSensitivity = 0.2f;
        [SerializeField, Range(5f, 89f)] private float cameraMinPitch = 25f;
        [SerializeField, Range(5f, 89f)] private float cameraMaxPitch = 75f;
        [SerializeField, Min(0.1f)] private float cameraFocusTransitionSpeed = 12f;

        private readonly Plane boardPlane = new(Vector3.up, Vector3.zero);
        private bool isCameraDragging;
        private Vector3 lastCameraDragMousePosition;
        private bool cameraPitchInitialized;
        private float cameraPitchDegrees;
        private bool cameraOrbitPivotInitialized;
        private Vector3 cameraOrbitGroundPivot;
        private float cameraOrbitDistance;
        private bool isCameraFocusTransitioning;
        private Vector3 cameraFocusTransitionTarget;

        public Camera ActiveCamera => targetCamera != null ? targetCamera : Camera.main;

        public void Tick(bool isMouseOverUi)
        {
            var activeCamera = ActiveCamera;
            if (activeCamera == null)
            {
                return;
            }

            if (!cameraPitchInitialized)
            {
                InitializeCameraPitch(activeCamera);
                if (!cameraPitchInitialized)
                {
                    return;
                }
            }

            HandleKeyboardCameraPan(activeCamera);

            if (Input.GetMouseButtonDown(MiddleMouseButton))
            {
                isCameraDragging = !isMouseOverUi;
                lastCameraDragMousePosition = Input.mousePosition;
            }

            if (Input.GetMouseButtonUp(MiddleMouseButton))
            {
                isCameraDragging = false;
            }

            TickCameraFocusTransition(activeCamera);

            if (!isCameraDragging || !Input.GetMouseButton(MiddleMouseButton))
            {
                return;
            }

            var mousePosition = Input.mousePosition;
            var delta = mousePosition - lastCameraDragMousePosition;
            lastCameraDragMousePosition = mousePosition;

            if (delta.sqrMagnitude < MinimumVectorSqrMagnitude)
            {
                return;
            }

            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                DragPanCamera(activeCamera, delta);
            }
            else
            {
                RotateCamera(activeCamera, delta);
            }
        }

        public void DrawGui()
        {
            var areaX = (Screen.width - CameraControlsPanelWidth) * 0.5f;
            var areaY = CameraControlsPanelTopMargin;
            GUILayout.BeginArea(new Rect(areaX, areaY, CameraControlsPanelWidth, CameraControlsPanelHeight), "Camera Controls", GUI.skin.window);
            GUILayout.Label("WASD/Arrows: Pan | MMB Drag: Rotate | Shift+MMB Drag: Pan");
            GUILayout.EndArea();
        }

        public void FocusOnPoint(Vector3 worldPoint)
        {
            var activeCamera = ActiveCamera;
            if (activeCamera == null)
            {
                return;
            }

            if (!cameraPitchInitialized)
            {
                InitializeCameraPitch(activeCamera);
            }

            if (!cameraOrbitPivotInitialized)
            {
                InitializeCameraOrbitPivot(activeCamera);
            }

            if (cameraOrbitDistance < CameraOrbitMinimumDistance)
            {
                cameraOrbitDistance = Mathf.Max(CameraOrbitMinimumDistance, Vector3.Distance(activeCamera.transform.position, worldPoint));
            }

            cameraOrbitPivotInitialized = true;
            cameraFocusTransitionTarget = worldPoint;
            isCameraFocusTransitioning = true;
        }

        private void HandleKeyboardCameraPan(Camera activeCamera)
        {
            var horizontal = Input.GetAxisRaw("Horizontal");
            var vertical = Input.GetAxisRaw("Vertical");
            if (Mathf.Abs(horizontal) <= InputAxisDeadzone && Mathf.Abs(vertical) <= InputAxisDeadzone)
            {
                return;
            }

            isCameraFocusTransitioning = false;
            var forward = GetPlanarForward(activeCamera.transform.forward);
            var right = GetPlanarRight(forward);
            var delta = (right * horizontal + forward * vertical) * (cameraKeyboardPanSpeed * Time.deltaTime);
            TranslateCameraOrbit(activeCamera, delta);
        }

        private void DragPanCamera(Camera activeCamera, Vector3 delta)
        {
            isCameraFocusTransitioning = false;
            var forward = GetPlanarForward(activeCamera.transform.forward);
            var right = GetPlanarRight(forward);
            var pan = (-right * delta.x - forward * delta.y) * cameraDragPanSensitivity;
            TranslateCameraOrbit(activeCamera, pan);
        }

        private void RotateCamera(Camera activeCamera, Vector3 delta)
        {
            if (!cameraOrbitPivotInitialized)
            {
                InitializeCameraOrbitPivot(activeCamera);
                if (!cameraOrbitPivotInitialized)
                {
                    return;
                }
            }

            isCameraFocusTransitioning = false;
            var yaw = delta.x * cameraRotationSensitivity;
            cameraPitchDegrees = Mathf.Clamp(cameraPitchDegrees - (delta.y * cameraRotationSensitivity), cameraMinPitch, cameraMaxPitch);
            var euler = activeCamera.transform.rotation.eulerAngles;
            activeCamera.transform.rotation = Quaternion.Euler(cameraPitchDegrees, euler.y + yaw, 0f);
            var cameraForward = activeCamera.transform.forward;
            activeCamera.transform.position = cameraOrbitGroundPivot - (cameraForward * cameraOrbitDistance);
        }

        private void TickCameraFocusTransition(Camera activeCamera)
        {
            if (!isCameraFocusTransitioning)
            {
                return;
            }

            cameraOrbitGroundPivot = Vector3.MoveTowards(
                cameraOrbitGroundPivot,
                cameraFocusTransitionTarget,
                cameraFocusTransitionSpeed * Time.deltaTime);

            var cameraForward = activeCamera.transform.forward;
            activeCamera.transform.position = cameraOrbitGroundPivot - (cameraForward * cameraOrbitDistance);

            if (Vector3.Distance(cameraOrbitGroundPivot, cameraFocusTransitionTarget) < 0.001f)
            {
                isCameraFocusTransitioning = false;
            }
        }

        private void InitializeCameraPitch(Camera activeCamera)
        {
            if (cameraPitchInitialized || activeCamera == null)
            {
                return;
            }

            cameraPitchDegrees = Mathf.Clamp(NormalizeSignedAngle(activeCamera.transform.eulerAngles.x), cameraMinPitch, cameraMaxPitch);
            cameraPitchInitialized = true;
            InitializeCameraOrbitPivot(activeCamera);
        }

        private void InitializeCameraOrbitPivot(Camera activeCamera)
        {
            if (cameraOrbitPivotInitialized || activeCamera == null)
            {
                return;
            }

            if (!TryGetGroundPointFromScreenCenter(activeCamera, out cameraOrbitGroundPivot))
            {
                var planarForward = GetPlanarForward(activeCamera.transform.forward);
                cameraOrbitGroundPivot = activeCamera.transform.position + (planarForward * CameraOrbitFallbackForwardDistance);
                cameraOrbitGroundPivot.y = 0f;
            }

            cameraOrbitDistance = Vector3.Distance(activeCamera.transform.position, cameraOrbitGroundPivot);
            if (cameraOrbitDistance < CameraOrbitMinimumDistance)
            {
                cameraOrbitDistance = CameraOrbitMinimumDistance;
            }

            var cameraForward = activeCamera.transform.forward;
            activeCamera.transform.position = cameraOrbitGroundPivot - (cameraForward * cameraOrbitDistance);
            cameraOrbitPivotInitialized = true;
        }

        private void TranslateCameraOrbit(Camera activeCamera, Vector3 planarDelta)
        {
            if (!cameraOrbitPivotInitialized)
            {
                InitializeCameraOrbitPivot(activeCamera);
                if (!cameraOrbitPivotInitialized)
                {
                    return;
                }
            }

            cameraOrbitGroundPivot += planarDelta;
            var cameraForward = activeCamera.transform.forward;
            activeCamera.transform.position = cameraOrbitGroundPivot - (cameraForward * cameraOrbitDistance);
        }

        private bool TryGetGroundPointFromScreenCenter(Camera activeCamera, out Vector3 groundPoint)
        {
            var screenCenter = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
            var centerRay = activeCamera.ScreenPointToRay(screenCenter);
            if (boardPlane.Raycast(centerRay, out var enter))
            {
                groundPoint = centerRay.GetPoint(enter);
                groundPoint.y = 0f;
                return true;
            }

            groundPoint = Vector3.zero;
            return false;
        }

        private static float NormalizeSignedAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f)
            {
                angle -= 360f;
            }

            return angle;
        }

        private static Vector3 GetPlanarForward(Vector3 forward)
        {
            var planarForward = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (planarForward.sqrMagnitude < MinimumVectorSqrMagnitude)
            {
                return Vector3.forward;
            }

            return planarForward.normalized;
        }

        private static Vector3 GetPlanarRight(Vector3 planarForward)
        {
            return Vector3.Cross(Vector3.up, planarForward).normalized;
        }
    }
}
