using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace ARSpaceMemo
{
    public class ARStartupStatus : MonoBehaviour
    {
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private ARPlaneManager planeManager;

        private int detectedPlaneCount;
        private bool loggedRuntimeStatus;
        private ARCameraManager cameraManager;
        private int loggedCameraFrames;

        private void Awake()
        {
            DisableExtraCameras();

            if (planeManager == null)
            {
                planeManager = FindFirstObjectByType<ARPlaneManager>();
            }

            cameraManager = FindFirstObjectByType<ARCameraManager>();
        }

        private static void DisableExtraCameras()
        {
            Camera mainCamera = Camera.main;
            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (Camera camera in cameras)
            {
                if (camera == null || camera == mainCamera)
                {
                    continue;
                }

                if (camera.GetComponent<ARCameraManager>() != null || camera.CompareTag("MainCamera"))
                {
                    continue;
                }

                camera.enabled = false;
                AudioListener listener = camera.GetComponent<AudioListener>();
                if (listener != null)
                {
                    listener.enabled = false;
                }

                Debug.Log($"ARSpaceMemo disabled extra camera on {camera.gameObject.name}");
            }
        }

        private void Start()
        {
            RequestCameraPermissionIfNeeded();
            UpdateStatus();
        }

        private void OnEnable()
        {
            if (planeManager == null)
            {
                planeManager = FindFirstObjectByType<ARPlaneManager>();
            }

            if (planeManager != null)
            {
                planeManager.trackablesChanged.AddListener(OnPlanesChanged);
            }

            if (cameraManager == null)
            {
                cameraManager = FindFirstObjectByType<ARCameraManager>();
            }

            if (cameraManager != null)
            {
                cameraManager.frameReceived += OnCameraFrameReceived;
            }
        }

        private void OnDisable()
        {
            if (planeManager != null)
            {
                planeManager.trackablesChanged.RemoveListener(OnPlanesChanged);
            }

            if (cameraManager != null)
            {
                cameraManager.frameReceived -= OnCameraFrameReceived;
            }
        }

        private void Update()
        {
            UpdateStatus();
        }

        public void SetStatusText(TMP_Text text)
        {
            statusText = text;
        }

        private void RequestCameraPermissionIfNeeded()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                Permission.RequestUserPermission(Permission.Camera);
            }
#endif
        }

        private void UpdateStatus()
        {
            if (statusText == null)
            {
                return;
            }

            LogRuntimeStatusOnce();

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                statusText.text = "Allow camera permission";
                return;
            }
#endif

            string message = ARSession.state switch
            {
                ARSessionState.None => "AR waiting",
                ARSessionState.Unsupported => "ARCore unsupported",
                ARSessionState.CheckingAvailability => "Checking ARCore",
                ARSessionState.NeedsInstall => "Install Google Play Services for AR",
                ARSessionState.Installing => "Installing ARCore",
                ARSessionState.Ready => "AR ready",
                ARSessionState.SessionInitializing => "Move phone slowly",
                ARSessionState.SessionTracking => "Scan a table/floor, then tap to place",
                _ => $"AR state: {ARSession.state}"
            };

            statusText.text = $"{message}\nPlanes: {detectedPlaneCount}";
        }

        private void LogRuntimeStatusOnce()
        {
            if (loggedRuntimeStatus)
            {
                return;
            }

            ARCameraBackground cameraBackground = FindFirstObjectByType<ARCameraBackground>();
            cameraManager = cameraManager != null ? cameraManager : FindFirstObjectByType<ARCameraManager>();
            Camera mainCamera = Camera.main;
            Debug.Log(
                "ARSpaceMemo runtime status: " +
                $"graphics={SystemInfo.graphicsDeviceType}, " +
                $"renderPipeline={GraphicsSettings.currentRenderPipeline?.name ?? "Built-in"}, " +
                $"arCameraBackground={(cameraBackground != null ? cameraBackground.enabled : false)}, " +
                $"requestedBackgroundMode={(cameraManager != null ? cameraManager.requestedBackgroundRenderingMode.ToString() : "None")}, " +
                $"currentBackgroundMode={(cameraManager != null ? cameraManager.currentRenderingMode.ToString() : "None")}, " +
                $"cameraClearFlags={(mainCamera != null ? mainCamera.clearFlags.ToString() : "None")}");
            loggedRuntimeStatus = true;
        }

        private void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
        {
            if (loggedCameraFrames >= 3)
            {
                return;
            }

            loggedCameraFrames++;
            Debug.Log(
                "ARSpaceMemo camera frame: " +
                $"frame={loggedCameraFrames}, " +
                $"textures={eventArgs.textures?.Count ?? 0}, " +
                $"properties={eventArgs.propertyNameIds?.Count ?? 0}, " +
                $"currentBackgroundMode={(cameraManager != null ? cameraManager.currentRenderingMode.ToString() : "None")}, " +
                $"displayMatrix={eventArgs.displayMatrix.HasValue}, " +
                $"projectionMatrix={eventArgs.projectionMatrix.HasValue}");
        }

        private void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
        {
            detectedPlaneCount = 0;

            if (planeManager == null)
            {
                return;
            }

            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane.trackingState == TrackingState.Tracking)
                {
                    detectedPlaneCount++;
                }
            }
        }
    }
}
