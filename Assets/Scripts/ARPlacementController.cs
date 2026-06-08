using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ARSpaceMemo
{
    public class ARPlacementController : MonoBehaviour
    {
        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private AROcclusionManager occlusionManager;
        [SerializeField] private MemoInputController memoInputController;
        [SerializeField] private MemoManager memoManager;
        [SerializeField] private GameObject memoCardPrefab;
        private ARPlaneManager planeManager;
        private TMP_Text placementHintText;
        private TMP_Text coordinateText;

        private static readonly List<ARRaycastHit> Hits = new();
        private static readonly List<ARRaycastHit> NeighborHits = new();
        private const TrackableType SurfaceHitTypes =
            TrackableType.Depth |
            TrackableType.PlaneWithinPolygon |
            TrackableType.FeaturePoint;
        private const TrackableType PreviewHitTypes =
            TrackableType.Depth |
            TrackableType.PlaneWithinPolygon |
            TrackableType.FeaturePoint;
        private const float MinPlacementDistance = 0.35f;
        private const float MaxPlacementDistance = 1.8f;
        private const float SurfaceOffsetMeters = 0.012f;
        private const float DepthSurfaceSamplePixels = 42f;
        private const float MaxDepthSurfaceNeighborDelta = 0.35f;
        private const float MinDepthSurfaceNormalSqrMagnitude = 0.00000001f;
        private const int DepthPatchGridHalfSize = 2;
        private const float DepthPatchStepPixels = 14f;
        private const int MinDepthPatchPoints = 10;
        private const int MinDepthPatchAxisPoints = 2;
        private const float MaxDepthPatchPointDelta = 0.22f;
        private const float MaxDepthPatchAveragePlaneError = 0.02f;
        private const float MinDepthMeters = 0.35f;
        private const float MaxDepthMeters = 1.8f;
        private const float RequiredStableDepthSeconds = 0.5f;
        private const float StablePosePositionTolerance = 0.05f;
        private const float StablePoseAngleTolerance = 18f;
        private const float VirtualWallDistanceMeters = 1.5f;
        private const float MinScanSecondsForFallback = 3f;
        private const int MinScanReadinessForFallback = 35;
        private const float ReticleVisualOffsetMeters = 0.006f;
        private const float ReticleSmoothingSpeed = 14f;
        private const float ReticleSnapDistance = 0.25f;
        private const float PreviewSuppressAfterSaveSeconds = 1.5f;
        private const float DragStartThresholdPixels = 18f;
        private const float MemoSelectionCastRadius = 0.035f;
        private const float SelectedMemoScreenPickRadiusPixels = 220f;
        private const float MaxUnmeasuredDragDistance = 1.05f;
        private static readonly Rect SaveButtonScreenRect = new Rect(36f, 24f, 200f, 72f);
        private const float SaveFallbackCooldownSeconds = 0.35f;
        private static readonly Color SurfaceReticleColor = new Color(0.00f, 0.82f, 1.00f, 0.95f);
        private static readonly Color DepthReticleColor = new Color(0.30f, 1.00f, 0.18f, 0.95f);
        private static readonly Color EstimatedReticleColor = new Color(1.00f, 0.55f, 0.05f, 0.95f);
        private static readonly Color MemoSelectionColor = new Color(0.12f, 0.38f, 1.00f, 0.24f);

        private int nextMemoId = 1;
        private string lastPlacementMode = "None";
        private GameObject placementReticle;
        private Material reticleMaterial;
        private bool hasPendingPlacement;
        private bool pendingPlacementIsSurface;
        private bool pendingPlacementIsVirtualWall;
        private bool pendingPlacementCanSave;
        private Pose pendingPlacementPose;
        private Vector2 pendingPlacementScreenPosition;
        private float pendingPlacementStableSince;
        private bool pendingPlacementNeedsStability;
        private bool hasSmoothedReticlePose;
        private Pose smoothedReticlePose;
        private bool uiButtonsWired;
        private float nextDepthSkipLogTime;
        private float nextDepthAcceptLogTime;
        private float scanStartedAt;
        private float lastReliableSurfaceAt = -100f;
        private float suppressPreviewUntil;
        private float lastPlacementDistanceMeters;
        private bool lastPlacementDistanceIsMeasured;
        private float lastSaveRequestAt = -10f;
        private int lastScanReadinessScore;
        private int lastTrackingPlaneCount;
        private MemoCard draggedMemo;
        private Vector2 dragStartScreenPosition;
        private float dragDistanceMeters;
        private bool isDraggingMemo;
        private bool hasDraggedMemo;
        private bool dragStartedOnSelectedMemo;
        private string dragPlacementMode = "None";

        private void Awake()
        {
            if (raycastManager == null)
            {
                raycastManager = FindAnyObjectByType<ARRaycastManager>();
            }

            if (occlusionManager == null)
            {
                occlusionManager = FindAnyObjectByType<AROcclusionManager>();
            }
            ConfigureOcclusionManager();

            if (planeManager == null)
            {
                planeManager = FindAnyObjectByType<ARPlaneManager>();
            }

            scanStartedAt = Time.unscaledTime;
            ResolvePlacementHintText();
            ResolveCoordinateText();
            CreatePlacementReticle();
            WireUiButtons();
            Debug.Log("ARSpaceMemo placement controller ready");
        }

        private void ConfigureOcclusionManager()
        {
            if (occlusionManager == null)
            {
                return;
            }

            occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Best;
            occlusionManager.environmentDepthTemporalSmoothingRequested = true;
            occlusionManager.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.NoOcclusion;
        }

        private void OnEnable()
        {
            EnhancedTouchSupport.Enable();
        }

        private void OnDisable()
        {
            EnhancedTouchSupport.Disable();
        }

        private void Update()
        {
            Camera arCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            Transform cameraTransform = arCamera != null ? arCamera.transform : null;

            if (HandleMemoDrag(arCamera, cameraTransform))
            {
                return;
            }

            UpdatePlacementPreview(arCamera, cameraTransform);

            if (!TryGetPlacementScreenPosition(out Vector2 screenPosition))
            {
                return;
            }

            if (IsPointerOverUi(screenPosition))
            {
                if (TryHandleUiFallback(screenPosition))
                {
                    return;
                }

                Debug.Log($"ARSpaceMemo placement blocked by UI at {screenPosition}");
                return;
            }

            if (TrySelectMemo(screenPosition, arCamera))
            {
                return;
            }

            TrySelectPlacement(screenPosition, arCamera, cameraTransform);
        }

        private static bool TryGetPlacementScreenPosition(out Vector2 screenPosition)
        {
            foreach (var touch in UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches)
            {
                if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began)
                {
                    screenPosition = touch.screenPosition;
                    Debug.Log($"ARSpaceMemo touch detected by EnhancedTouch at {screenPosition}");
                    return true;
                }
            }

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                screenPosition = Touchscreen.current.primaryTouch.position.ReadValue();
                Debug.Log($"ARSpaceMemo touch detected by Touchscreen at {screenPosition}");
                return true;
            }

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount > 0 && Input.GetTouch(0).phase == UnityEngine.TouchPhase.Began)
            {
                screenPosition = Input.GetTouch(0).position;
                Debug.Log($"ARSpaceMemo touch detected by legacy Input at {screenPosition}");
                return true;
            }
#endif

#if UNITY_EDITOR
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                screenPosition = Mouse.current.position.ReadValue();
                Debug.Log($"ARSpaceMemo mouse detected at {screenPosition}");
                return true;
            }
#endif

            screenPosition = default;
            return false;
        }

        private bool HandleMemoDrag(Camera arCamera, Transform cameraTransform)
        {
            if (arCamera == null || cameraTransform == null || memoManager == null)
            {
                return false;
            }

            if (!TryGetDragPointer(out Vector2 screenPosition, out UnityEngine.InputSystem.TouchPhase phase))
            {
                if (isDraggingMemo)
                {
                    EndMemoDrag();
                    return true;
                }

                return isDraggingMemo;
            }

            if (!isDraggingMemo && phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                if (IsPointerOverUi(screenPosition))
                {
                    return false;
                }

                if (!TryGetMemoAtScreenPosition(screenPosition, arCamera, out MemoCard memoCard) &&
                    !TryGetSelectedMemoNearScreenPosition(screenPosition, arCamera, out memoCard))
                {
                    return false;
                }

                BeginMemoDrag(memoCard, screenPosition, arCamera.transform.position);
                return true;
            }

            if (!isDraggingMemo)
            {
                return false;
            }

            if (phase == UnityEngine.InputSystem.TouchPhase.Moved ||
                phase == UnityEngine.InputSystem.TouchPhase.Stationary)
            {
                float dragDelta = Vector2.Distance(screenPosition, dragStartScreenPosition);
                if (hasDraggedMemo || dragDelta >= DragStartThresholdPixels)
                {
                    hasDraggedMemo = true;
                    MoveDraggedMemo(screenPosition, arCamera, cameraTransform);
                }

                return true;
            }

            if (phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                phase == UnityEngine.InputSystem.TouchPhase.Canceled)
            {
                EndMemoDrag();
                return true;
            }

            return true;
        }

        private static bool TryGetDragPointer(out Vector2 screenPosition, out UnityEngine.InputSystem.TouchPhase phase)
        {
            foreach (var touch in UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches)
            {
                screenPosition = touch.screenPosition;
                phase = touch.phase;
                return true;
            }

            if (Touchscreen.current != null)
            {
                var primaryTouch = Touchscreen.current.primaryTouch;
                screenPosition = primaryTouch.position.ReadValue();

                if (primaryTouch.press.wasPressedThisFrame)
                {
                    phase = UnityEngine.InputSystem.TouchPhase.Began;
                    return true;
                }

                if (primaryTouch.press.isPressed)
                {
                    phase = primaryTouch.delta.ReadValue().sqrMagnitude > 0.01f
                        ? UnityEngine.InputSystem.TouchPhase.Moved
                        : UnityEngine.InputSystem.TouchPhase.Stationary;
                    return true;
                }

                if (primaryTouch.press.wasReleasedThisFrame)
                {
                    phase = UnityEngine.InputSystem.TouchPhase.Ended;
                    return true;
                }
            }

#if UNITY_EDITOR
            if (Mouse.current != null)
            {
                screenPosition = Mouse.current.position.ReadValue();

                if (Mouse.current.leftButton.wasPressedThisFrame)
                {
                    phase = UnityEngine.InputSystem.TouchPhase.Began;
                    return true;
                }

                if (Mouse.current.leftButton.isPressed)
                {
                    phase = Mouse.current.delta.ReadValue().sqrMagnitude > 0.01f
                        ? UnityEngine.InputSystem.TouchPhase.Moved
                        : UnityEngine.InputSystem.TouchPhase.Stationary;
                    return true;
                }

                if (Mouse.current.leftButton.wasReleasedThisFrame)
                {
                    phase = UnityEngine.InputSystem.TouchPhase.Ended;
                    return true;
                }
            }
#endif

            screenPosition = default;
            phase = UnityEngine.InputSystem.TouchPhase.Canceled;
            return false;
        }

        private void BeginMemoDrag(MemoCard memoCard, Vector2 screenPosition, Vector3 cameraPosition)
        {
            draggedMemo = memoCard;
            isDraggingMemo = true;
            hasDraggedMemo = false;
            dragStartedOnSelectedMemo = memoManager.SelectedMemo == memoCard;
            dragStartScreenPosition = screenPosition;
            dragDistanceMeters = Mathf.Clamp(
                Vector3.Distance(cameraPosition, memoCard.transform.position),
                MinPlacementDistance,
                MaxPlacementDistance);

            RemoveMemoAnchor(memoCard);
            memoManager.Select(memoCard);
            hasPendingPlacement = false;
            if (memoInputController != null)
            {
                memoInputController.SetCurrentMemoText(memoCard.Text);
            }

            SetPlacementHint("Memo selected. Drag to move it.");
            Debug.Log($"ARSpaceMemo memo drag started {memoCard.Id}");
        }

        private void MoveDraggedMemo(Vector2 screenPosition, Camera arCamera, Transform cameraTransform)
        {
            if (draggedMemo == null)
            {
                EndMemoDrag();
                return;
            }

            Pose targetPose;
            if (TryGetWorldPlacement(screenPosition, arCamera, cameraTransform, SurfaceHitTypes, out targetPose) &&
                IsMeasuredMode(lastPlacementMode))
            {
                dragPlacementMode = lastPlacementMode;
            }
            else
            {
                targetPose = CreateScreenDistancePose(screenPosition, arCamera, cameraTransform, dragDistanceMeters);
                dragPlacementMode = "ScreenDistance";
                lastPlacementDistanceMeters = dragDistanceMeters;
                lastPlacementDistanceIsMeasured = false;
            }

            draggedMemo.transform.SetPositionAndRotation(targetPose.position, targetPose.rotation);
            SetReticleVisible(false);

            SetPlacementHint(IsMeasuredMode(dragPlacementMode)
                ? $"Moving memo: measured {lastPlacementDistanceMeters:0.00}m."
                : $"Moving memo: estimated {lastPlacementDistanceMeters:0.00}m.");
        }

        private void EndMemoDrag()
        {
            if (draggedMemo == null)
            {
                isDraggingMemo = false;
                hasDraggedMemo = false;
                return;
            }

            SetReticleVisible(false);
            if (!hasDraggedMemo && dragStartedOnSelectedMemo)
            {
                draggedMemo.ToggleCollapsed();
                SetPlacementHint(draggedMemo.IsCollapsed
                    ? "Memo collapsed. Tap again to expand, or drag to move."
                    : "Memo expanded. Tap again to collapse, or drag to move.");
            }
            else
            {
                if (hasDraggedMemo)
                {
                    RefreshMemoAnchor(draggedMemo);
                }

                SetPlacementHint(hasDraggedMemo
                    ? IsMeasuredMode(dragPlacementMode)
                        ? "Memo moved to measured position."
                        : "Memo moved using estimated screen distance."
                    : "Memo selected. Tap again to collapse, drag to move, edit, or delete.");
            }

            Debug.Log($"ARSpaceMemo memo drag ended {draggedMemo.Id}, moved={hasDraggedMemo}, mode={dragPlacementMode}");
            draggedMemo = null;
            isDraggingMemo = false;
            hasDraggedMemo = false;
            dragStartedOnSelectedMemo = false;
            dragPlacementMode = "None";
        }

        private static Pose CreateScreenDistancePose(
            Vector2 screenPosition,
            Camera arCamera,
            Transform cameraTransform,
            float distanceMeters)
        {
            Ray ray = arCamera.ScreenPointToRay(screenPosition);
            float visibleDistance = Mathf.Clamp(distanceMeters, MinPlacementDistance, MaxUnmeasuredDragDistance);
            Vector3 position = ray.origin + ray.direction.normalized * visibleDistance;
            Quaternion rotation = CreateCameraFacingRotation(cameraTransform, position);
            return new Pose(position, rotation);
        }

        private static void RefreshMemoAnchor(MemoCard memoCard)
        {
            if (memoCard == null)
            {
                return;
            }

            RemoveMemoAnchor(memoCard);
            ARAnchor anchor = memoCard.gameObject.AddComponent<ARAnchor>();
            Debug.Log($"ARSpaceMemo memo anchor refreshed {memoCard.Id}, anchorEnabled={anchor.enabled}");
        }

        private static void RemoveMemoAnchor(MemoCard memoCard)
        {
            if (memoCard == null)
            {
                return;
            }

            ARAnchor anchor = memoCard.GetComponent<ARAnchor>();
            if (anchor != null)
            {
                anchor.enabled = false;
                Destroy(anchor);
            }
        }

        public void SaveMemoFromInput()
        {
            lastSaveRequestAt = Time.unscaledTime;
            string currentText = memoInputController != null ? memoInputController.GetCurrentMemoText() : string.Empty;
            Debug.Log(
                $"ARSpaceMemo save requested: hasPending={hasPendingPlacement}, canSave={pendingPlacementCanSave}, " +
                $"selected={memoManager?.SelectedMemo != null}, mode={lastPlacementMode}, textLength={currentText.Length}");

            if (hasPendingPlacement)
            {
                if (!pendingPlacementCanSave)
                {
                    SetPlacementHint("Move within 0.35-1.8m and choose a cyan, green, or orange marker.");
                    Debug.Log($"ARSpaceMemo save skipped: pending placement is {lastPlacementMode}, not saveable.");
                    return;
                }

                CreateMemoAtPendingPlacement();
                return;
            }

            if (memoManager != null && memoManager.SaveSelectedFromInput(memoInputController))
            {
                SetPlacementHint("Memo updated.");
                return;
            }

            SetPlacementHint("Scan first, then tap a surface or virtual wall position.");
            Debug.Log("ARSpaceMemo save skipped: no selected memo or pending placement.");
        }

        private void TrySelectPlacement(Vector2 screenPosition, Camera arCamera, Transform cameraTransform)
        {
            Debug.Log($"ARSpaceMemo placement selection requested at {screenPosition}");

            if (arCamera == null || cameraTransform == null)
            {
                lastPlacementMode = "No camera";
                Debug.LogWarning("ARSpaceMemo placement selection failed: no camera found");
                return;
            }

            if (!TryGetWorldPlacement(screenPosition, arCamera, cameraTransform, out Pose placementPose))
            {
                SetPlacementHint("Scan the wall or object until the cyan marker appears.");
                Debug.Log("ARSpaceMemo placement selection skipped: no depth, plane, or feature point hit.");
                return;
            }

            pendingPlacementPose = placementPose;
            hasPendingPlacement = true;
            pendingPlacementIsSurface = lastPlacementMode == "Plane";
            pendingPlacementIsVirtualWall = lastPlacementMode == "VirtualWall";
            bool pendingPlacementIsDepthSurface = IsDepthSurfaceMode(lastPlacementMode);
            pendingPlacementNeedsStability = false;
            pendingPlacementCanSave = pendingPlacementIsSurface || pendingPlacementIsVirtualWall || pendingPlacementIsDepthSurface;
            pendingPlacementScreenPosition = screenPosition;
            pendingPlacementStableSince = 0f;
            memoManager?.Select(null);
            SetPlacementReticlePose(pendingPlacementPose, false);
            SetReticleVisible(true);
            SetReticleColor(lastPlacementMode);
            SetPlacementHint(pendingPlacementIsSurface
                    ? $"Measured plane selected ({lastPlacementDistanceMeters:0.00}m). Enter text, then press SAVE."
                : pendingPlacementIsVirtualWall
                    ? $"Estimated virtual point selected ({VirtualWallDistanceMeters:0.0}m). Enter text, then press SAVE."
                : pendingPlacementIsDepthSurface
                    ? $"Measured depth selected ({lastPlacementDistanceMeters:0.00}m). Enter text, then press SAVE."
                    : pendingPlacementNeedsStability
                        ? "Hold close and steady until the depth marker stabilizes."
                        : "Estimated point selected. Scan a surface before saving.");
            Debug.Log($"ARSpaceMemo selected placement using {lastPlacementMode} at {pendingPlacementPose.position}");
        }

        private void CreateMemoAtPendingPlacement()
        {
            MemoCard memoCard = CreateFallbackMemoCard(pendingPlacementPose.position, pendingPlacementPose.rotation);

            string memoText = memoInputController != null ? memoInputController.GetCurrentMemoText() : "Memo";
            memoCard.SetFaceCamera(true);

            memoCard.Initialize($"memo-{nextMemoId++}", memoText);
            RefreshMemoAnchor(memoCard);
            memoManager?.Register(memoCard);
            memoManager?.Select(memoCard);
            bool wasVirtualWall = pendingPlacementIsVirtualWall;
            hasPendingPlacement = false;
            pendingPlacementIsSurface = false;
            pendingPlacementIsVirtualWall = false;
            pendingPlacementCanSave = false;
            pendingPlacementNeedsStability = false;
            suppressPreviewUntil = Time.unscaledTime + PreviewSuppressAfterSaveSeconds;
            hasSmoothedReticlePose = false;
            SetReticleVisible(false);
            SetPlacementHint(wasVirtualWall
                ? "Memo attached to the virtual wall for this session."
                : "Memo attached to the scanned space.");
            Vector3 screenPosition = Camera.main != null
                ? Camera.main.WorldToScreenPoint(memoCard.transform.position)
                : Vector3.zero;
            LogMemoRenderers(memoCard);
            Debug.Log(
                $"ARSpaceMemo placed memo using {lastPlacementMode} at {pendingPlacementPose.position}, " +
                $"screen={screenPosition}, text='{memoText}'");
        }

        private static void LogMemoRenderers(MemoCard memoCard)
        {
            if (memoCard == null)
            {
                Debug.LogWarning("ARSpaceMemo memo renderer check skipped: memo is null.");
                return;
            }

            Renderer[] renderers = memoCard.GetComponentsInChildren<Renderer>(true);
            Debug.Log($"ARSpaceMemo memo renderer count={renderers.Length}");

            foreach (Renderer renderer in renderers)
            {
                Material material = renderer.sharedMaterial;
                string shaderName = material != null && material.shader != null ? material.shader.name : "none";
                int renderQueue = material != null ? material.renderQueue : -1;
                Debug.Log(
                    $"ARSpaceMemo memo renderer {renderer.name}: enabled={renderer.enabled}, " +
                    $"forceOff={renderer.forceRenderingOff}, queue={renderQueue}, shader={shaderName}, " +
                    $"boundsCenter={renderer.bounds.center}, boundsSize={renderer.bounds.size}");
            }
        }

        public void SetPlacementHintText(TMP_Text text)
        {
            placementHintText = text;
        }

        private void WireUiButtons()
        {
            if (uiButtonsWired)
            {
                return;
            }

            Button saveButton = FindButton("SaveButton");
            if (saveButton != null)
            {
                saveButton.onClick.AddListener(SaveMemoFromInput);
                Debug.Log("ARSpaceMemo SaveButton listener wired.");
            }
            else
            {
                Debug.LogWarning("ARSpaceMemo SaveButton not found.");
            }

            Button deleteButton = FindButton("DeleteButton");
            if (deleteButton != null)
            {
                deleteButton.onClick.AddListener(() => memoManager?.DeleteSelected());
                Debug.Log("ARSpaceMemo DeleteButton listener wired.");
            }
            else
            {
                Debug.LogWarning("ARSpaceMemo DeleteButton not found.");
            }

            Button clearButton = FindButton("ClearAllButton");
            if (clearButton != null)
            {
                clearButton.onClick.AddListener(() => memoManager?.ClearAll());
                Debug.Log("ARSpaceMemo ClearAllButton listener wired.");
            }
            else
            {
                Debug.LogWarning("ARSpaceMemo ClearAllButton not found.");
            }

            uiButtonsWired = true;
        }

        private bool TryHandleUiFallback(Vector2 screenPosition)
        {
            if (!SaveButtonScreenRect.Contains(screenPosition))
            {
                return false;
            }

            if (Time.unscaledTime - lastSaveRequestAt < SaveFallbackCooldownSeconds)
            {
                Debug.Log($"ARSpaceMemo SaveButton fallback skipped after UnityEvent at {screenPosition}");
                return true;
            }

            Debug.Log($"ARSpaceMemo SaveButton fallback triggered at {screenPosition}");
            SaveMemoFromInput();
            return true;
        }

        private static Button FindButton(string objectName)
        {
            GameObject buttonObject = GameObject.Find(objectName);
            return buttonObject != null ? buttonObject.GetComponent<Button>() : null;
        }

        private void ResolvePlacementHintText()
        {
            if (placementHintText != null)
            {
                return;
            }

            GameObject hintObject = GameObject.Find("HintText");
            if (hintObject != null)
            {
                placementHintText = hintObject.GetComponent<TMP_Text>();
            }
        }

        private void ResolveCoordinateText()
        {
            if (coordinateText != null)
            {
                return;
            }

            GameObject existingObject = GameObject.Find("CoordinateText");
            if (existingObject != null)
            {
                coordinateText = existingObject.GetComponent<TMP_Text>();
                return;
            }

            Canvas canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                return;
            }

            GameObject textObject = new GameObject("CoordinateText", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(canvas.transform, false);
            coordinateText = textObject.GetComponent<TMP_Text>();
            coordinateText.text = "XYZ --";
            coordinateText.fontSize = 26f;
            coordinateText.alignment = TextAlignmentOptions.TopRight;
            coordinateText.color = Color.white;
            coordinateText.textWrappingMode = TextWrappingModes.NoWrap;
            coordinateText.raycastTarget = false;

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-32f, -142f);
            rect.sizeDelta = new Vector2(520f, 150f);
        }

        private void SetCoordinateText(Pose? pose, string placementMode)
        {
            if (coordinateText == null)
            {
                ResolveCoordinateText();
            }

            if (coordinateText == null)
            {
                return;
            }

            if (!pose.HasValue)
            {
                coordinateText.text = $"Mode: {placementMode}\nXYZ: --\nDist: --";
                return;
            }

            Vector3 position = pose.Value.position;
            coordinateText.text =
                $"Mode: {placementMode}\n" +
                $"X {position.x:0.00}  Y {position.y:0.00}  Z {position.z:0.00}\n" +
                $"Dist {lastPlacementDistanceMeters:0.00}m";
        }

        private void UpdatePlacementPreview(Camera arCamera, Transform cameraTransform)
        {
            UpdateScanReadiness();

            if (arCamera == null || cameraTransform == null)
            {
                SetReticleVisible(false);
                return;
            }

            if (Time.unscaledTime < suppressPreviewUntil)
            {
                SetReticleVisible(false);
                return;
            }

            if (hasPendingPlacement)
            {
                UpdatePendingPlacementStability(arCamera, cameraTransform);
                SetPlacementReticlePose(pendingPlacementPose, false);
                SetReticleVisible(true);
                SetReticleColor(lastPlacementMode);
                SetCoordinateText(pendingPlacementPose, lastPlacementMode);
                SetPlacementHint(pendingPlacementIsSurface
                    ? $"Measured plane selected ({lastPlacementDistanceMeters:0.00}m). Enter text, then press SAVE."
                    : pendingPlacementIsVirtualWall
                        ? $"Estimated virtual point selected ({VirtualWallDistanceMeters:0.0}m). Enter text, then press SAVE."
                    : pendingPlacementCanSave
                        ? $"Measured depth selected ({lastPlacementDistanceMeters:0.00}m). Enter text, then press SAVE."
                        : pendingPlacementNeedsStability
                            ? "Hold close and steady until the depth marker stabilizes."
                            : "Estimated point selected. Scan a surface before saving.");
                return;
            }

            Vector2 screenCenter = new Vector2(arCamera.pixelWidth * 0.5f, arCamera.pixelHeight * 0.5f);
            if (TryGetWorldPlacement(screenCenter, arCamera, cameraTransform, PreviewHitTypes, out Pose placementPose))
            {
                SetPlacementReticlePose(placementPose, true);
                SetReticleVisible(true);
                SetReticleColor(lastPlacementMode);
                SetCoordinateText(placementPose, lastPlacementMode);
                SetPlacementHint(lastPlacementMode == "Plane"
                    ? $"Cyan plane marker: measured {lastPlacementDistanceMeters:0.00}m. Tap to choose."
                    : IsDepthSurfaceMode(lastPlacementMode)
                        ? $"Green depth marker: measured {lastPlacementDistanceMeters:0.00}m. Hold steady, then tap."
                        : GetScanReadinessHint());
                return;
            }

            if (TryCreateVirtualWallPose(arCamera, cameraTransform, out Pose virtualPose))
            {
                SetPlacementReticlePose(virtualPose, true);
                SetReticleVisible(true);
                SetReticleColor("VirtualWall");
                SetCoordinateText(virtualPose, "VirtualWall");
                SetPlacementHint($"Orange virtual marker: estimated {VirtualWallDistanceMeters:0.0}m, not wall-measured. Tap to choose.");
                return;
            }

            SetReticleVisible(false);
            hasSmoothedReticlePose = false;
            SetCoordinateText(null, lastPlacementMode);
            SetPlacementHint(GetScanReadinessHint());
        }

        private bool TryGetWorldPlacement(Vector2 screenPosition, Camera arCamera, Transform cameraTransform, out Pose placementPose)
        {
            placementPose = default;

            if (raycastManager == null)
            {
                lastPlacementMode = "No raycast manager";
                return false;
            }

            return TryGetWorldPlacement(screenPosition, arCamera, cameraTransform, SurfaceHitTypes, out placementPose);
        }

        private bool TryGetWorldPlacement(
            Vector2 screenPosition,
            Camera arCamera,
            Transform cameraTransform,
            TrackableType hitTypes,
            out Pose placementPose)
        {
            placementPose = default;
            lastPlacementDistanceMeters = 0f;
            lastPlacementDistanceIsMeasured = false;

            if (raycastManager == null)
            {
                lastPlacementMode = "No raycast manager";
                return false;
            }

            if (!raycastManager.Raycast(screenPosition, Hits, hitTypes))
            {
                if (TryCreateDepthPatchSurfacePose(screenPosition, arCamera, cameraTransform, out placementPose))
                {
                    lastPlacementMode = "DepthPatchSurface";
                    lastPlacementDistanceMeters = Vector3.Distance(cameraTransform.position, placementPose.position);
                    lastPlacementDistanceIsMeasured = true;
                    lastReliableSurfaceAt = Time.unscaledTime;
                    return true;
                }

                if (TryCreateVirtualWallPose(arCamera, cameraTransform, out placementPose))
                {
                    lastPlacementMode = "VirtualWall";
                    lastPlacementDistanceMeters = VirtualWallDistanceMeters;
                    lastPlacementDistanceIsMeasured = false;
                    return true;
                }

                lastPlacementMode = "No surface hit";
                return false;
            }

            ARRaycastHit hit = SelectBestPlacementHit(Hits);
            Vector3 position = hit.pose.position;
            float distanceFromCamera = Vector3.Distance(cameraTransform.position, position);
            lastPlacementDistanceMeters = distanceFromCamera;
            lastPlacementDistanceIsMeasured = true;
            if (distanceFromCamera < MinPlacementDistance || distanceFromCamera > MaxPlacementDistance)
            {
                lastPlacementMode = $"{hit.hitType} outside range ({distanceFromCamera:0.00}m)";
                return false;
            }

            Quaternion rotation;

            if ((hit.hitType & TrackableType.Depth) != 0)
            {
                if (TryCreateDepthSurfacePose(screenPosition, hit, arCamera, cameraTransform, out placementPose))
                {
                    lastPlacementDistanceMeters = Vector3.Distance(cameraTransform.position, placementPose.position);
                    lastPlacementDistanceIsMeasured = true;
                    lastReliableSurfaceAt = Time.unscaledTime;
                    return true;
                }

                if (TryCreateDepthPatchSurfacePose(screenPosition, arCamera, cameraTransform, out placementPose))
                {
                    lastPlacementMode = "DepthPatchSurface";
                    lastPlacementDistanceMeters = Vector3.Distance(cameraTransform.position, placementPose.position);
                    lastPlacementDistanceIsMeasured = true;
                    lastReliableSurfaceAt = Time.unscaledTime;
                    return true;
                }

                rotation = CreateCameraFacingRotation(cameraTransform, position);
                lastPlacementMode = "Depth";
            }
            else if ((hit.hitType & TrackableType.Planes) != 0)
            {
                Vector3 surfaceNormal = hit.pose.up;
                Vector3 directionToCamera = cameraTransform.position - position;
                if (Vector3.Dot(surfaceNormal, directionToCamera) < 0f)
                {
                    surfaceNormal = -surfaceNormal;
                }

                position += surfaceNormal * SurfaceOffsetMeters;
                rotation = CreateSurfaceRotation(surfaceNormal, cameraTransform);
                lastPlacementMode = "Plane";
                lastReliableSurfaceAt = Time.unscaledTime;
            }
            else if ((hit.hitType & TrackableType.FeaturePoint) != 0)
            {
                rotation = CreateCameraFacingRotation(cameraTransform, position);
                lastPlacementMode = "FeaturePoint";
            }
            else
            {
                rotation = CreateCameraFacingRotation(cameraTransform, position);
                lastPlacementMode = hit.hitType.ToString();
            }

            placementPose = new Pose(position, rotation);
            return true;
        }

        private bool TryCreateDepthSurfacePose(
            Vector2 screenPosition,
            ARRaycastHit centerHit,
            Camera arCamera,
            Transform cameraTransform,
            out Pose placementPose)
        {
            placementPose = default;

            if (TryCreateDepthPatchSurfacePose(screenPosition, centerHit, arCamera, cameraTransform, out placementPose))
            {
                lastPlacementMode = "DepthPatchSurface";
                return true;
            }

            if (!TryGetDepthPoint(screenPosition + Vector2.left * DepthSurfaceSamplePixels, out Vector3 left) ||
                !TryGetDepthPoint(screenPosition + Vector2.right * DepthSurfaceSamplePixels, out Vector3 right) ||
                !TryGetDepthPoint(screenPosition + Vector2.up * DepthSurfaceSamplePixels, out Vector3 up) ||
                !TryGetDepthPoint(screenPosition + Vector2.down * DepthSurfaceSamplePixels, out Vector3 down))
            {
                LogDepthSkip("ARSpaceMemo depth surface skipped: missing neighbor depth sample.");
                return false;
            }

            Vector3 center = centerHit.pose.position;
            if (Vector3.Distance(center, left) > MaxDepthSurfaceNeighborDelta ||
                Vector3.Distance(center, right) > MaxDepthSurfaceNeighborDelta ||
                Vector3.Distance(center, up) > MaxDepthSurfaceNeighborDelta ||
                Vector3.Distance(center, down) > MaxDepthSurfaceNeighborDelta)
            {
                LogDepthSkip("ARSpaceMemo depth surface skipped: neighbor depth samples are too uneven.");
                return false;
            }

            Vector3 horizontal = right - left;
            Vector3 vertical = up - down;
            Vector3 surfaceNormal = Vector3.Cross(horizontal, vertical);
            if (surfaceNormal.sqrMagnitude < MinDepthSurfaceNormalSqrMagnitude)
            {
                LogDepthSkip("ARSpaceMemo depth surface skipped: normal magnitude is too small.");
                return false;
            }

            surfaceNormal.Normalize();
            Vector3 directionToCamera = cameraTransform.position - center;
            if (Vector3.Dot(surfaceNormal, directionToCamera) < 0f)
            {
                surfaceNormal = -surfaceNormal;
            }

            Vector3 position = center + surfaceNormal * SurfaceOffsetMeters;
            Quaternion rotation = CreateSurfaceRotation(surfaceNormal, cameraTransform);
            placementPose = new Pose(position, rotation);
            lastPlacementMode = "DepthSurface";
            Debug.Log($"ARSpaceMemo depth surface normal={surfaceNormal}, center={center}, position={position}");
            return true;
        }

        private static bool IsDepthSurfaceMode(string placementMode)
        {
            return placementMode == "DepthSurface" || placementMode == "DepthPatchSurface";
        }

        private static bool IsMeasuredMode(string placementMode)
        {
            return placementMode == "Plane" || IsDepthSurfaceMode(placementMode);
        }

        private void UpdateScanReadiness()
        {
            int score = 0;
            if (ARSession.state == ARSessionState.SessionTracking)
            {
                score += 25;
            }

            float scanSeconds = Time.unscaledTime - scanStartedAt;
            if (scanSeconds >= MinScanSecondsForFallback)
            {
                score += 15;
            }

            lastTrackingPlaneCount = CountTrackingPlanes();
            if (lastTrackingPlaneCount > 0)
            {
                score += 35;
            }

            if (Time.unscaledTime - lastReliableSurfaceAt <= 2f)
            {
                score += 25;
            }

            lastScanReadinessScore = Mathf.Clamp(score, 0, 100);
        }

        private int CountTrackingPlanes()
        {
            if (planeManager == null)
            {
                planeManager = FindAnyObjectByType<ARPlaneManager>();
            }

            if (planeManager == null)
            {
                return 0;
            }

            int count = 0;
            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane != null && plane.trackingState == TrackingState.Tracking)
                {
                    count++;
                }
            }

            return count;
        }

        private string GetScanReadinessHint()
        {
            if (lastPlacementMode == "VirtualWall")
            {
                return $"Orange virtual marker: estimated {VirtualWallDistanceMeters:0.0}m, not wall-measured.";
            }

            if (lastScanReadinessScore < MinScanReadinessForFallback)
            {
                return $"Scan room edges slowly. Readiness {lastScanReadinessScore}/100.";
            }

            if (lastTrackingPlaneCount == 0)
            {
                return $"No plane yet. Virtual wall fallback ready after scan. Readiness {lastScanReadinessScore}/100.";
            }

            return $"Scan ready {lastScanReadinessScore}/100. Cyan/green is measured; orange is estimated.";
        }

        private bool TryCreateVirtualWallPose(Camera arCamera, Transform cameraTransform, out Pose placementPose)
        {
            placementPose = default;
            if (arCamera == null ||
                cameraTransform == null ||
                ARSession.state != ARSessionState.SessionTracking ||
                Time.unscaledTime - scanStartedAt < MinScanSecondsForFallback ||
                lastScanReadinessScore < MinScanReadinessForFallback)
            {
                return false;
            }

            Vector3 position = cameraTransform.position + cameraTransform.forward * VirtualWallDistanceMeters;
            Quaternion rotation = CreateCameraFacingRotation(cameraTransform, position);
            placementPose = new Pose(position, rotation);
            return true;
        }

        private void UpdatePendingPlacementStability(Camera arCamera, Transform cameraTransform)
        {
            if (!pendingPlacementNeedsStability || arCamera == null || cameraTransform == null)
            {
                return;
            }

            string previousMode = lastPlacementMode;
            Pose previousPose = pendingPlacementPose;

            if (!TryGetWorldPlacement(
                    pendingPlacementScreenPosition,
                    arCamera,
                    cameraTransform,
                    SurfaceHitTypes,
                    out Pose currentPose) ||
                !IsDepthSurfaceMode(lastPlacementMode))
            {
                pendingPlacementCanSave = false;
                pendingPlacementStableSince = Time.unscaledTime;
                lastPlacementMode = previousMode;
                return;
            }

            float positionDelta = Vector3.Distance(previousPose.position, currentPose.position);
            float angleDelta = Quaternion.Angle(previousPose.rotation, currentPose.rotation);
            if (positionDelta > StablePosePositionTolerance || angleDelta > StablePoseAngleTolerance)
            {
                pendingPlacementCanSave = false;
                pendingPlacementStableSince = Time.unscaledTime;
            }
            else if (Time.unscaledTime - pendingPlacementStableSince >= RequiredStableDepthSeconds)
            {
                pendingPlacementCanSave = true;
            }

            pendingPlacementPose = currentPose;
        }

        private void LogDepthSkip(string message)
        {
            if (Time.unscaledTime < nextDepthSkipLogTime)
            {
                return;
            }

            nextDepthSkipLogTime = Time.unscaledTime + 2f;
            Debug.Log(message);
        }

        private void LogDepthAccept(string message)
        {
            if (Time.unscaledTime < nextDepthAcceptLogTime)
            {
                return;
            }

            nextDepthAcceptLogTime = Time.unscaledTime + 1f;
            Debug.Log(message);
        }

        private bool TryCreateDepthPatchSurfacePose(
            Vector2 screenPosition,
            ARRaycastHit centerHit,
            Camera arCamera,
            Transform cameraTransform,
            out Pose placementPose)
        {
            placementPose = default;

            if (occlusionManager == null)
            {
                LogDepthSkip("ARSpaceMemo depth patch skipped: no occlusion manager.");
                return false;
            }

            if (!TryAcquireDepthImage(out XRCpuImage depthImage))
            {
                LogDepthSkip("ARSpaceMemo depth patch skipped: no environment depth CPU image.");
                return false;
            }

            using (depthImage)
            {
                if (depthImage.format != XRCpuImage.Format.DepthUint16 || depthImage.planeCount == 0)
                {
                    LogDepthSkip($"ARSpaceMemo depth patch skipped: unsupported image format {depthImage.format}.");
                    return false;
                }

                XRCpuImage.Plane depthPlane = depthImage.GetPlane(0);
                if (depthPlane.pixelStride < 2)
                {
                    LogDepthSkip($"ARSpaceMemo depth patch skipped: unsupported pixel stride {depthPlane.pixelStride}.");
                    return false;
                }

                if (!TryChooseDepthImageTransform(
                        screenPosition,
                        centerHit.pose.position,
                        arCamera,
                        depthImage,
                        depthPlane,
                        out DepthImageTransform imageTransform))
                {
                    LogDepthSkip("ARSpaceMemo depth patch skipped: could not align depth image to screen.");
                    return false;
                }

                Vector3 center = centerHit.pose.position;
                return TryCreateDepthPatchSurfacePoseFromImage(
                    screenPosition,
                    center,
                    true,
                    arCamera,
                    cameraTransform,
                    depthImage,
                    depthPlane,
                    imageTransform,
                    out placementPose);
            }
        }

        private bool TryCreateDepthPatchSurfacePose(
            Vector2 screenPosition,
            Camera arCamera,
            Transform cameraTransform,
            out Pose placementPose)
        {
            placementPose = default;

            if (occlusionManager == null)
            {
                LogDepthSkip("ARSpaceMemo depth-only patch skipped: no occlusion manager.");
                return false;
            }

            if (!TryAcquireDepthImage(out XRCpuImage depthImage))
            {
                LogDepthSkip("ARSpaceMemo depth-only patch skipped: no environment depth CPU image.");
                return false;
            }

            using (depthImage)
            {
                if (depthImage.format != XRCpuImage.Format.DepthUint16 || depthImage.planeCount == 0)
                {
                    LogDepthSkip($"ARSpaceMemo depth-only patch skipped: unsupported image format {depthImage.format}.");
                    return false;
                }

                XRCpuImage.Plane depthPlane = depthImage.GetPlane(0);
                if (depthPlane.pixelStride < 2)
                {
                    LogDepthSkip($"ARSpaceMemo depth-only patch skipped: unsupported pixel stride {depthPlane.pixelStride}.");
                    return false;
                }

                bool hasBestPose = false;
                Pose bestPose = default;
                float bestScore = float.PositiveInfinity;

                for (int i = 0; i <= (int)DepthImageTransform.MirrorY; i++)
                {
                    DepthImageTransform imageTransform = (DepthImageTransform)i;
                    if (!TryCreateDepthPatchSurfacePoseFromImage(
                            screenPosition,
                            Vector3.zero,
                            false,
                            arCamera,
                            cameraTransform,
                            depthImage,
                            depthPlane,
                            imageTransform,
                            out Pose candidatePose,
                            out float candidateScore))
                    {
                        continue;
                    }

                    if (candidateScore < bestScore)
                    {
                        bestScore = candidateScore;
                        bestPose = candidatePose;
                        hasBestPose = true;
                    }
                }

                if (!hasBestPose)
                {
                    LogDepthSkip("ARSpaceMemo depth-only patch skipped: no transform produced a stable surface.");
                    return false;
                }

                placementPose = bestPose;
                LogDepthAccept($"ARSpaceMemo depth-only patch surface accepted score={bestScore:0.000}, position={placementPose.position}");
                return true;
            }
        }

        private bool TryCreateDepthPatchSurfacePoseFromImage(
            Vector2 screenPosition,
            Vector3 expectedCenter,
            bool constrainToExpectedCenter,
            Camera arCamera,
            Transform cameraTransform,
            XRCpuImage depthImage,
            XRCpuImage.Plane depthPlane,
            DepthImageTransform imageTransform,
            out Pose placementPose)
        {
            return TryCreateDepthPatchSurfacePoseFromImage(
                screenPosition,
                expectedCenter,
                constrainToExpectedCenter,
                arCamera,
                cameraTransform,
                depthImage,
                depthPlane,
                imageTransform,
                out placementPose,
                out _);
        }

        private bool TryCreateDepthPatchSurfacePoseFromImage(
            Vector2 screenPosition,
            Vector3 expectedCenter,
            bool constrainToExpectedCenter,
            Camera arCamera,
            Transform cameraTransform,
            XRCpuImage depthImage,
            XRCpuImage.Plane depthPlane,
            DepthImageTransform imageTransform,
            out Pose placementPose,
            out float score)
        {
            placementPose = default;
            score = float.PositiveInfinity;

            if (!TryGetDepthImagePoint(screenPosition, arCamera, depthImage, depthPlane, imageTransform, out Vector3 center))
            {
                LogDepthSkip($"ARSpaceMemo depth patch skipped: no center depth for {imageTransform}.");
                return false;
            }

            if (constrainToExpectedCenter && Vector3.Distance(expectedCenter, center) > MaxDepthPatchPointDelta)
            {
                LogDepthSkip($"ARSpaceMemo depth patch skipped: center mismatch for {imageTransform}.");
                return false;
            }

            Vector3 sum = Vector3.zero;
            Vector3 leftSum = Vector3.zero;
            Vector3 rightSum = Vector3.zero;
            Vector3 upSum = Vector3.zero;
            Vector3 downSum = Vector3.zero;
            int validCount = 0;
            int leftCount = 0;
            int rightCount = 0;
            int upCount = 0;
            int downCount = 0;
            Vector3[] sampledPoints = new Vector3[(DepthPatchGridHalfSize * 2 + 1) * (DepthPatchGridHalfSize * 2 + 1)];

            for (int y = -DepthPatchGridHalfSize; y <= DepthPatchGridHalfSize; y++)
            {
                for (int x = -DepthPatchGridHalfSize; x <= DepthPatchGridHalfSize; x++)
                {
                    Vector2 sampleScreen = screenPosition + new Vector2(x * DepthPatchStepPixels, y * DepthPatchStepPixels);
                    if (!TryGetDepthImagePoint(sampleScreen, arCamera, depthImage, depthPlane, imageTransform, out Vector3 point))
                    {
                        continue;
                    }

                    if (Vector3.Distance(center, point) > MaxDepthPatchPointDelta)
                    {
                        continue;
                    }

                    sampledPoints[validCount++] = point;
                    sum += point;

                    if (x < 0)
                    {
                        leftSum += point;
                        leftCount++;
                    }
                    else if (x > 0)
                    {
                        rightSum += point;
                        rightCount++;
                    }

                    if (y < 0)
                    {
                        downSum += point;
                        downCount++;
                    }
                    else if (y > 0)
                    {
                        upSum += point;
                        upCount++;
                    }
                }
            }

            if (validCount < MinDepthPatchPoints ||
                leftCount < MinDepthPatchAxisPoints ||
                rightCount < MinDepthPatchAxisPoints ||
                upCount < MinDepthPatchAxisPoints ||
                downCount < MinDepthPatchAxisPoints)
            {
                LogDepthSkip(
                    $"ARSpaceMemo depth patch skipped: insufficient stable samples for {imageTransform} " +
                    $"total={validCount}, left={leftCount}, right={rightCount}, up={upCount}, down={downCount}.");
                return false;
            }

            Vector3 patchCenter = sum / validCount;
            Vector3 horizontal = rightSum / rightCount - leftSum / leftCount;
            Vector3 vertical = upSum / upCount - downSum / downCount;
            Vector3 surfaceNormal = Vector3.Cross(horizontal, vertical);
            if (surfaceNormal.sqrMagnitude < MinDepthSurfaceNormalSqrMagnitude)
            {
                LogDepthSkip($"ARSpaceMemo depth patch skipped: normal magnitude is too small for {imageTransform}.");
                return false;
            }

            surfaceNormal.Normalize();
            Vector3 directionToCamera = cameraTransform.position - patchCenter;
            if (Vector3.Dot(surfaceNormal, directionToCamera) < 0f)
            {
                surfaceNormal = -surfaceNormal;
            }

            float totalPlaneError = 0f;
            for (int i = 0; i < validCount; i++)
            {
                totalPlaneError += Mathf.Abs(Vector3.Dot(sampledPoints[i] - patchCenter, surfaceNormal));
            }

            float averagePlaneError = totalPlaneError / validCount;
            if (averagePlaneError > MaxDepthPatchAveragePlaneError)
            {
                LogDepthSkip($"ARSpaceMemo depth patch skipped: plane error {averagePlaneError:0.000}m is too high for {imageTransform}.");
                return false;
            }

            float facingScore = 1f - Mathf.Abs(Vector3.Dot(surfaceNormal, cameraTransform.forward));
            score = averagePlaneError + facingScore * 0.02f;
            Vector3 position = patchCenter + surfaceNormal * SurfaceOffsetMeters;
            Quaternion rotation = CreateSurfaceRotation(surfaceNormal, cameraTransform);
            placementPose = new Pose(position, rotation);
            LogDepthAccept(
                "ARSpaceMemo depth patch surface accepted " +
                $"transform={imageTransform}, samples={validCount}, error={averagePlaneError:0.000}m, normal={surfaceNormal}, position={position}");
            return true;
        }

        private bool TryAcquireDepthImage(out XRCpuImage depthImage)
        {
            if (occlusionManager.TryAcquireSmoothedEnvironmentDepthCpuImage(out depthImage))
            {
                return true;
            }

            return occlusionManager.TryAcquireEnvironmentDepthCpuImage(out depthImage);
        }

        private enum DepthImageTransform
        {
            Identity = 0,
            Rotate90 = 1,
            Rotate180 = 2,
            Rotate270 = 3,
            MirrorX = 4,
            MirrorY = 5,
        }

        private bool TryChooseDepthImageTransform(
            Vector2 screenPosition,
            Vector3 expectedWorldPoint,
            Camera arCamera,
            XRCpuImage depthImage,
            XRCpuImage.Plane depthPlane,
            out DepthImageTransform bestTransform)
        {
            bestTransform = DepthImageTransform.Identity;
            float bestError = float.PositiveInfinity;

            for (int i = 0; i <= (int)DepthImageTransform.MirrorY; i++)
            {
                DepthImageTransform candidate = (DepthImageTransform)i;
                if (!TryGetDepthImagePoint(screenPosition, arCamera, depthImage, depthPlane, candidate, out Vector3 point))
                {
                    continue;
                }

                float error = Vector3.Distance(expectedWorldPoint, point);
                if (error < bestError)
                {
                    bestError = error;
                    bestTransform = candidate;
                }
            }

            return bestError < MaxDepthPatchPointDelta;
        }

        private bool TryGetDepthImagePoint(
            Vector2 screenPosition,
            Camera arCamera,
            XRCpuImage depthImage,
            XRCpuImage.Plane depthPlane,
            DepthImageTransform imageTransform,
            out Vector3 point)
        {
            point = default;

            if (!TryGetDepthMeters(screenPosition, arCamera, depthImage, depthPlane, imageTransform, out float depthMeters))
            {
                return false;
            }

            Ray ray = arCamera.ScreenPointToRay(screenPosition);
            Vector3 rayDirection = ray.direction.normalized;
            float forwardDot = Vector3.Dot(rayDirection, arCamera.transform.forward);
            if (forwardDot < 0.15f)
            {
                return false;
            }

            float rayDistance = depthMeters / forwardDot;
            point = ray.origin + rayDirection * rayDistance;
            return true;
        }

        private bool TryGetDepthMeters(
            Vector2 screenPosition,
            Camera arCamera,
            XRCpuImage depthImage,
            XRCpuImage.Plane depthPlane,
            DepthImageTransform imageTransform,
            out float depthMeters)
        {
            depthMeters = 0f;

            if (!TryMapScreenToDepthPixel(screenPosition, arCamera, depthImage, imageTransform, out int x, out int y))
            {
                return false;
            }

            int offset = y * depthPlane.rowStride + x * depthPlane.pixelStride;
            if (offset < 0 || offset + 1 >= depthPlane.data.Length)
            {
                return false;
            }

            ushort depthMillimeters = (ushort)(depthPlane.data[offset] | (depthPlane.data[offset + 1] << 8));
            depthMeters = depthMillimeters * 0.001f;
            return depthMeters >= MinDepthMeters && depthMeters <= MaxDepthMeters;
        }

        private static bool TryMapScreenToDepthPixel(
            Vector2 screenPosition,
            Camera arCamera,
            XRCpuImage depthImage,
            DepthImageTransform imageTransform,
            out int pixelX,
            out int pixelY)
        {
            pixelX = 0;
            pixelY = 0;

            if (arCamera.pixelWidth <= 0 || arCamera.pixelHeight <= 0 || depthImage.width <= 0 || depthImage.height <= 0)
            {
                return false;
            }

            float screenU = Mathf.Clamp01(screenPosition.x / arCamera.pixelWidth);
            float screenV = Mathf.Clamp01(screenPosition.y / arCamera.pixelHeight);
            float imageU = screenU;
            float imageV = 1f - screenV;

            switch (imageTransform)
            {
                case DepthImageTransform.Rotate90:
                    (imageU, imageV) = (imageV, 1f - imageU);
                    break;
                case DepthImageTransform.Rotate180:
                    imageU = 1f - imageU;
                    imageV = 1f - imageV;
                    break;
                case DepthImageTransform.Rotate270:
                    (imageU, imageV) = (1f - imageV, imageU);
                    break;
                case DepthImageTransform.MirrorX:
                    imageU = 1f - imageU;
                    break;
                case DepthImageTransform.MirrorY:
                    imageV = 1f - imageV;
                    break;
            }

            pixelX = Mathf.Clamp(Mathf.RoundToInt(imageU * (depthImage.width - 1)), 0, depthImage.width - 1);
            pixelY = Mathf.Clamp(Mathf.RoundToInt(imageV * (depthImage.height - 1)), 0, depthImage.height - 1);
            return true;
        }

        private bool TryGetDepthPoint(Vector2 screenPosition, out Vector3 point)
        {
            point = default;

            if (raycastManager == null ||
                !raycastManager.Raycast(screenPosition, NeighborHits, TrackableType.Depth))
            {
                return false;
            }

            foreach (ARRaycastHit hit in NeighborHits)
            {
                if ((hit.hitType & TrackableType.Depth) != 0)
                {
                    point = hit.pose.position;
                    return true;
                }
            }

            return false;
        }

        private static ARRaycastHit SelectBestPlacementHit(List<ARRaycastHit> hits)
        {
            foreach (ARRaycastHit hit in hits)
            {
                if ((hit.hitType & TrackableType.PlaneWithinPolygon) != 0)
                {
                    return hit;
                }
            }

            foreach (ARRaycastHit hit in hits)
            {
                if ((hit.hitType & TrackableType.Depth) != 0)
                {
                    return hit;
                }
            }

            foreach (ARRaycastHit hit in hits)
            {
                if ((hit.hitType & TrackableType.FeaturePoint) != 0)
                {
                    return hit;
                }
            }

            return hits[0];
        }

        private static MemoCard CreateFallbackMemoCard(Vector3 position, Quaternion rotation)
        {
            GameObject root = new GameObject("RuntimeMemoCard");
            root.transform.SetPositionAndRotation(position, rotation);

            GameObject background = GameObject.CreatePrimitive(PrimitiveType.Cube);
            background.name = "FallbackBackground";
            background.transform.SetParent(root.transform, false);
            background.transform.localPosition = Vector3.zero;
            background.transform.localRotation = Quaternion.identity;
            background.transform.localScale = new Vector3(0.46f, 0.30f, 0.018f);

            Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.color = new Color(1f, 0.92f, 0.18f, 1f);
            material.SetColor("_BaseColor", material.color);
            material.SetFloat("_Cull", 0f);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_ZTest"))
            {
                material.SetFloat("_ZTest", (float)CompareFunction.Always);
            }
            if (material.HasProperty("_ZTestMode"))
            {
                material.SetFloat("_ZTestMode", (float)CompareFunction.Always);
            }
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 4990;
            MeshRenderer backgroundRenderer = background.GetComponent<MeshRenderer>();
            backgroundRenderer.enabled = true;
            backgroundRenderer.forceRenderingOff = false;
            backgroundRenderer.shadowCastingMode = ShadowCastingMode.Off;
            backgroundRenderer.receiveShadows = false;
            backgroundRenderer.sharedMaterial = material;

            GameObject selectionArea = GameObject.CreatePrimitive(PrimitiveType.Quad);
            selectionArea.name = "FallbackSelectionArea";
            selectionArea.transform.SetParent(root.transform, false);
            selectionArea.transform.localPosition = new Vector3(0f, 0f, -0.014f);
            selectionArea.transform.localScale = new Vector3(0.52f, 0.34f, 1f);

            Material selectionMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            selectionMaterial.color = MemoSelectionColor;
            selectionMaterial.SetColor("_BaseColor", selectionMaterial.color);
            selectionMaterial.SetFloat("_Surface", 1f);
            selectionMaterial.SetFloat("_Blend", 0f);
            selectionMaterial.SetFloat("_Cull", 0f);
            if (selectionMaterial.HasProperty("_ZTest"))
            {
                selectionMaterial.SetFloat("_ZTest", (float)CompareFunction.Always);
            }
            if (selectionMaterial.HasProperty("_ZTestMode"))
            {
                selectionMaterial.SetFloat("_ZTestMode", (float)CompareFunction.Always);
            }
            selectionMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            selectionMaterial.renderQueue = 4985;
            MeshRenderer selectionRenderer = selectionArea.GetComponent<MeshRenderer>();
            selectionRenderer.enabled = true;
            selectionRenderer.forceRenderingOff = false;
            selectionRenderer.shadowCastingMode = ShadowCastingMode.Off;
            selectionRenderer.receiveShadows = false;
            selectionRenderer.sharedMaterial = selectionMaterial;

            LineRenderer selectionOutline = root.AddComponent<LineRenderer>();
            selectionOutline.sharedMaterial = selectionMaterial;
            selectionOutline.useWorldSpace = false;
            selectionOutline.loop = true;
            selectionOutline.startWidth = 0.008f;
            selectionOutline.endWidth = 0.008f;
            selectionOutline.positionCount = 4;
            selectionOutline.SetPosition(0, new Vector3(-0.26f, -0.17f, 0.008f));
            selectionOutline.SetPosition(1, new Vector3(-0.26f, 0.17f, 0.008f));
            selectionOutline.SetPosition(2, new Vector3(0.26f, 0.17f, 0.008f));
            selectionOutline.SetPosition(3, new Vector3(0.26f, -0.17f, 0.008f));

            GameObject textObject = new GameObject("FallbackText", typeof(TextMeshPro));
            textObject.transform.SetParent(root.transform, false);
            textObject.transform.localPosition = new Vector3(0f, 0f, -0.026f);
            textObject.transform.localRotation = Quaternion.identity;
            textObject.transform.localScale = Vector3.one * 0.08f;

            TextMeshPro text = textObject.GetComponent<TextMeshPro>();
            text.text = "Memo";
            text.fontSize = 1.2f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.08f, 0.07f, 0.03f, 1f);
            text.rectTransform.sizeDelta = new Vector2(4.4f, 2.4f);
            if (text.fontMaterial != null)
            {
                if (text.fontMaterial.HasProperty("_ZTest"))
                {
                    text.fontMaterial.SetFloat("_ZTest", (float)CompareFunction.Always);
                }
                if (text.fontMaterial.HasProperty("_ZTestMode"))
                {
                    text.fontMaterial.SetFloat("_ZTestMode", (float)CompareFunction.Always);
                }
                text.fontMaterial.renderQueue = 5000;
            }

            MemoCard memoCard = root.AddComponent<MemoCard>();
            memoCard.SetTextTarget(text);
            memoCard.SetFaceCamera(false);
            return memoCard;
        }

        private void CreatePlacementReticle()
        {
            placementReticle = new GameObject("PlacementReticle");
            placementReticle.name = "PlacementReticle";
            placementReticle.transform.localScale = Vector3.one;

            reticleMaterial = CreateReticleMaterial();

            CreateReticleRing(placementReticle.transform, "OuterRing", 0.12f, 0.01f);
            CreateReticleRing(placementReticle.transform, "InnerRing", 0.05f, 0.007f);
            CreateReticleLine("VerticalPin", new Vector3(0f, 0.05f, 0.01f), new Vector3(0f, 0.2f, 0.01f), 0.01f);
            CreateReticleCenterDot();

            SetReticleVisible(false);
        }

        private Material CreateReticleMaterial()
        {
            Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.color = SurfaceReticleColor;
            material.SetColor("_BaseColor", material.color);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_Cull", 0f);
            material.SetInt("_ZTest", (int)CompareFunction.Always);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 5000;
            return material;
        }

        private void SetReticleColor(string placementMode)
        {
            if (reticleMaterial == null)
            {
                return;
            }

            Color color = placementMode == "Plane"
                ? SurfaceReticleColor
                : IsDepthSurfaceMode(placementMode)
                    ? DepthReticleColor
                    : EstimatedReticleColor;
            reticleMaterial.color = color;
            reticleMaterial.SetColor("_BaseColor", color);
        }

        private void CreateReticleRing(Transform parent, string ringName, float radius, float width)
        {
            GameObject ringObject = new GameObject(ringName);
            ringObject.transform.SetParent(parent, false);

            LineRenderer ring = ringObject.AddComponent<LineRenderer>();
            ring.sharedMaterial = reticleMaterial;
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.startWidth = width;
            ring.endWidth = width;
            ring.positionCount = 48;

            for (int i = 0; i < ring.positionCount; i++)
            {
                float angle = i / (float)ring.positionCount * Mathf.PI * 2f;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0.01f));
            }
        }

        private void CreateReticleLine(string lineName, Vector3 start, Vector3 end, float width)
        {
            GameObject lineObject = new GameObject(lineName);
            lineObject.transform.SetParent(placementReticle.transform, false);

            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.sharedMaterial = reticleMaterial;
            line.useWorldSpace = false;
            line.startWidth = width;
            line.endWidth = width;
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
        }

        private void CreateReticleCenterDot()
        {
            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Quad);
            dot.name = "CenterDot";
            dot.transform.SetParent(placementReticle.transform, false);
            dot.transform.localPosition = new Vector3(0f, 0f, 0.014f);
            dot.transform.localScale = new Vector3(0.04f, 0.04f, 1f);

            Collider collider = dot.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            MeshRenderer renderer = dot.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = reticleMaterial;
        }

        private void SetReticleVisible(bool visible)
        {
            if (placementReticle != null && placementReticle.activeSelf != visible)
            {
                placementReticle.SetActive(visible);
            }
        }

        private void SetPlacementReticlePose(Pose placementPose, bool smooth)
        {
            if (placementReticle == null)
            {
                return;
            }

            Vector3 visualPosition = placementPose.position + placementPose.rotation * Vector3.forward * ReticleVisualOffsetMeters;
            Pose visualPose = new Pose(visualPosition, placementPose.rotation);

            if (!smooth || !hasSmoothedReticlePose)
            {
                smoothedReticlePose = visualPose;
                hasSmoothedReticlePose = true;
            }
            else
            {
                float distance = Vector3.Distance(smoothedReticlePose.position, visualPose.position);
                if (distance > ReticleSnapDistance)
                {
                    smoothedReticlePose = visualPose;
                }
                else
                {
                    float t = 1f - Mathf.Exp(-ReticleSmoothingSpeed * Time.deltaTime);
                    smoothedReticlePose = new Pose(
                        Vector3.Lerp(smoothedReticlePose.position, visualPose.position, t),
                        Quaternion.Slerp(smoothedReticlePose.rotation, visualPose.rotation, t));
                }
            }

            placementReticle.transform.SetPositionAndRotation(smoothedReticlePose.position, smoothedReticlePose.rotation);
        }

        private void SetPlacementHint(string message)
        {
            if (placementHintText != null && placementHintText.text != message)
            {
                placementHintText.text = message;
            }
        }

        private static Quaternion CreateCameraFacingRotation(Transform cameraTransform, Vector3 position)
        {
            Vector3 directionToCamera = cameraTransform.position - position;

            if (directionToCamera.sqrMagnitude < 0.0001f)
            {
                directionToCamera = -cameraTransform.forward;
            }

            Vector3 up = cameraTransform.up;
            return CreateReadableSurfaceRotation(directionToCamera.normalized, up);
        }

        private static Quaternion CreateSurfaceRotation(Vector3 surfaceNormal, Transform cameraTransform)
        {
            Vector3 up = cameraTransform != null ? cameraTransform.up : Vector3.up;
            return CreateReadableSurfaceRotation(surfaceNormal, up);
        }

        private static Quaternion CreateReadableSurfaceRotation(Vector3 forward, Vector3 preferredUp)
        {
            forward = forward.normalized;
            Vector3 up = Vector3.ProjectOnPlane(preferredUp, forward).normalized;
            if (up.sqrMagnitude < 0.001f)
            {
                up = Vector3.ProjectOnPlane(Vector3.up, forward).normalized;
            }

            if (up.sqrMagnitude < 0.001f)
            {
                up = Vector3.ProjectOnPlane(Vector3.right, forward).normalized;
            }

            Quaternion rotation = Quaternion.LookRotation(forward, up);
            if (Vector3.Dot(rotation * Vector3.up, preferredUp) < 0f)
            {
                rotation *= Quaternion.AngleAxis(180f, Vector3.forward);
            }

            return rotation;
        }

        private static bool IsPointerOverUi(Vector2 screenPosition)
        {
            if (EventSystem.current == null)
            {
                return false;
            }

            var eventData = new PointerEventData(EventSystem.current)
            {
                position = screenPosition
            };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);
            return results.Count > 0;
        }

        private bool TrySelectMemo(Vector2 screenPosition, Camera arCamera)
        {
            if (arCamera == null || memoManager == null)
            {
                return false;
            }

            if (!TryGetMemoAtScreenPosition(screenPosition, arCamera, out MemoCard memoCard))
            {
                return false;
            }

            memoManager.Select(memoCard);
            hasPendingPlacement = false;
            if (memoInputController != null)
            {
                memoInputController.SetCurrentMemoText(memoCard.Text);
            }

            SetPlacementHint("Memo selected. Edit text, save, or delete.");
            Debug.Log($"ARSpaceMemo selected memo {memoCard.Id}");
            return true;
        }

        private bool TryGetSelectedMemoNearScreenPosition(Vector2 screenPosition, Camera arCamera, out MemoCard memoCard)
        {
            memoCard = null;
            if (memoManager == null || memoManager.SelectedMemo == null || arCamera == null)
            {
                return false;
            }

            Vector3 memoScreenPosition = arCamera.WorldToScreenPoint(memoManager.SelectedMemo.transform.position);
            if (memoScreenPosition.z <= 0f)
            {
                return false;
            }

            Vector2 memoPoint = new Vector2(memoScreenPosition.x, memoScreenPosition.y);
            float distancePixels = Vector2.Distance(screenPosition, memoPoint);
            if (distancePixels > SelectedMemoScreenPickRadiusPixels)
            {
                return false;
            }

            memoCard = memoManager.SelectedMemo;
            Debug.Log($"ARSpaceMemo selected memo picked by screen proximity: {distancePixels:0}px");
            return true;
        }

        private static bool TryGetMemoAtScreenPosition(Vector2 screenPosition, Camera arCamera, out MemoCard memoCard)
        {
            memoCard = null;
            if (arCamera == null)
            {
                return false;
            }

            Ray ray = arCamera.ScreenPointToRay(screenPosition);
            RaycastHit[] hits = Physics.SphereCastAll(ray, MemoSelectionCastRadius, 20f);
            if (hits.Length == 0)
            {
                hits = Physics.RaycastAll(ray, 20f);
            }

            System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

            foreach (RaycastHit hit in hits)
            {
                memoCard = hit.collider.GetComponentInParent<MemoCard>();
                if (memoCard != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
