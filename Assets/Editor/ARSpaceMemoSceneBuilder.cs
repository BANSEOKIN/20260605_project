using System.Linq;
using System.IO;
using ARSpaceMemo;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;
using UnityEngine.XR.ARSubsystems;

namespace ARSpaceMemo.Editor
{
    public static class ARSpaceMemoSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string PrefabPath = "Assets/Prefabs/MemoCard.prefab";
        private const string MaterialPath = "Assets/Materials/MemoCard.mat";
        private const string PlanePrefabPath = "Assets/Prefabs/DetectedPlane.prefab";
        private const string PlaneMaterialPath = "Assets/Materials/DetectedPlane.mat";
        private static readonly Color MemoSelectionColor = new Color(0.12f, 0.38f, 1.00f, 0.24f);
        private static readonly Color PlaneDebugColor = new Color(0.00f, 0.68f, 1.00f, 0.28f);
        private const string MobilePipelinePath = "Assets/Settings/Mobile_RPAsset.asset";
        private const string MobileRendererPath = "Assets/Settings/Mobile_Renderer.asset";

        [MenuItem("Tools/AR Space Memo/Build Main Scene")]
        public static void BuildMainScene()
        {
            EnsureFolders();

            GameObject memoPrefab = CreateMemoCardPrefab();
            GameObject planePrefab = CreatePlanePrefab();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateArRig(planePrefab, out ARRaycastManager raycastManager, out ARStartupStatus startupStatus);
            CreateLighting();
            CreateUi(
                out MemoInputController inputController,
                out MemoManager memoManager,
                out TMP_Text statusText,
                out TMP_Text placementHintText);
            startupStatus.SetStatusText(statusText);
            CreatePlacementController(
                raycastManager,
                inputController,
                memoManager,
                memoPrefab,
                placementHintText);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("ARSpaceMemo Main scene and MemoCard prefab were generated.");
        }

        [MenuItem("Tools/AR Space Memo/Configure Android ARCore")]
        public static void ConfigureAndroidARCore()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            EnsureMobileRenderPipelineAssigned();
            EnsureArBackgroundRendererFeatures();

            XRGeneralSettingsPerBuildTarget buildTargetSettings = GetOrCreateXRSettingsPerBuildTarget();
            if (!buildTargetSettings.HasSettingsForBuildTarget(BuildTargetGroup.Android))
            {
                buildTargetSettings.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
            }

            if (!buildTargetSettings.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            {
                buildTargetSettings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            }

            XRManagerSettings managerSettings = buildTargetSettings.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            const string arCoreLoaderType = "UnityEngine.XR.ARCore.ARCoreLoader";

            bool alreadyAssigned = managerSettings.activeLoaders.Any(loader =>
                loader != null && loader.GetType().FullName == arCoreLoaderType);

            if (!alreadyAssigned && !XRPackageMetadataStore.AssignLoader(managerSettings, arCoreLoaderType, BuildTargetGroup.Android))
            {
                throw new System.InvalidOperationException("Failed to assign ARCore loader for Android.");
            }

            ConfigureArCoreSettings();

            EditorUtility.SetDirty(managerSettings);
            EditorUtility.SetDirty(buildTargetSettings);
            AssetDatabase.SaveAssets();
            Debug.Log("ARSpaceMemo Android ARCore loader configured.");
        }

        [MenuItem("Tools/AR Space Memo/Build Android APK")]
        public static void BuildAndroidApk()
        {
            BuildMainScene();
            ConfigureAndroidARCore();

            const string buildDirectory = "Builds/Android";
            const string apkPath = buildDirectory + "/ARSpaceMemo.apk";
            Directory.CreateDirectory(buildDirectory);

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = apkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                throw new System.InvalidOperationException(
                    $"Android APK build failed: {report.summary.result}");
            }

            Debug.Log($"ARSpaceMemo Android APK built at {apkPath}");
        }

        private static void ConfigureArCoreSettings()
        {
            const string settingsPath = "Assets/XR/Settings/ARCoreSettings.asset";
            Object settings = AssetDatabase.LoadAssetAtPath<Object>(settingsPath);
            if (settings == null)
            {
                return;
            }

            SerializedObject serializedSettings = new SerializedObject(settings);
            serializedSettings.FindProperty("m_Requirement").intValue = 0;
            serializedSettings.FindProperty("m_Depth").intValue = 0;
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
        }

        private static void EnsureArBackgroundRendererFeatures()
        {
            UniversalRendererData rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(MobileRendererPath);
            if (rendererData == null)
            {
                return;
            }

            EnsureRendererFeature<ARBackgroundRendererFeature>(rendererData);
            EnsureRendererFeature<ARCommandBufferSupportRendererFeature>(rendererData);
            SerializedObject serializedRenderer = new SerializedObject(rendererData);
            SerializedProperty nativeRenderPass = serializedRenderer.FindProperty("m_UseNativeRenderPass");
            if (nativeRenderPass != null)
            {
                if (nativeRenderPass.propertyType == SerializedPropertyType.Boolean)
                {
                    nativeRenderPass.boolValue = false;
                }
                else if (nativeRenderPass.propertyType == SerializedPropertyType.Integer)
                {
                    nativeRenderPass.intValue = 0;
                }

                serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
        }

        private static void EnsureMobileRenderPipelineAssigned()
        {
            UniversalRenderPipelineAsset pipelineAsset =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobilePipelinePath);
            UniversalRendererData rendererData =
                AssetDatabase.LoadAssetAtPath<UniversalRendererData>(MobileRendererPath);

            if (pipelineAsset == null || rendererData == null)
            {
                Debug.LogWarning("ARSpaceMemo could not find the Mobile URP asset or renderer data.");
                return;
            }

            SerializedObject serializedPipeline = new SerializedObject(pipelineAsset);
            SerializedProperty rendererDataProperty = serializedPipeline.FindProperty("m_RendererData");
            if (rendererDataProperty != null)
            {
                rendererDataProperty.objectReferenceValue = rendererData;
            }

            SerializedProperty rendererDataList = serializedPipeline.FindProperty("m_RendererDataList");
            if (rendererDataList != null)
            {
                rendererDataList.arraySize = 1;
                rendererDataList.GetArrayElementAtIndex(0).objectReferenceValue = rendererData;
            }

            SerializedProperty defaultRendererIndex = serializedPipeline.FindProperty("m_DefaultRendererIndex");
            if (defaultRendererIndex != null)
            {
                defaultRendererIndex.intValue = 0;
            }

            serializedPipeline.ApplyModifiedPropertiesWithoutUndo();
            GraphicsSettings.defaultRenderPipeline = pipelineAsset;
            QualitySettings.renderPipeline = pipelineAsset;
            QualitySettings.SetQualityLevel(0, true);
            EditorUtility.SetDirty(pipelineAsset);
        }

        private static void EnsureRendererFeature<T>(UniversalRendererData rendererData)
            where T : ScriptableRendererFeature
        {
            if (rendererData.rendererFeatures.Any(feature => feature is T))
            {
                return;
            }

            T feature = ScriptableObject.CreateInstance<T>();
            feature.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(feature, rendererData);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

            SerializedObject serializedRenderer = new SerializedObject(rendererData);
            SerializedProperty features = serializedRenderer.FindProperty("m_RendererFeatures");
            SerializedProperty featureMap = serializedRenderer.FindProperty("m_RendererFeatureMap");

            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;

            featureMap.arraySize++;
            featureMap.GetArrayElementAtIndex(featureMap.arraySize - 1).longValue = localId;

            serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
        }

        private static XRGeneralSettingsPerBuildTarget GetOrCreateXRSettingsPerBuildTarget()
        {
            var method = typeof(XRGeneralSettingsPerBuildTarget).GetMethod(
                "GetOrCreate",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

            return method?.Invoke(null, null) as XRGeneralSettingsPerBuildTarget
                ?? throw new System.InvalidOperationException("Unable to create XR settings per build target.");
        }

        private static void EnsureFolders()
        {
            CreateFolder("Assets", "Scenes");
            CreateFolder("Assets", "Prefabs");
            CreateFolder("Assets", "Materials");
            CreateFolder("Assets", "UI");
        }

        private static void CreateFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static GameObject CreateMemoCardPrefab()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                {
                    color = new Color(1f, 0.92f, 0.46f, 1f)
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.color = new Color(1f, 0.92f, 0.46f, 1f);
            material.SetFloat("_Cull", 0f);
            material.SetColor("_BaseColor", material.color);

            GameObject root = new GameObject("MemoCard");
            root.transform.localScale = Vector3.one;

            GameObject background = GameObject.CreatePrimitive(PrimitiveType.Quad);
            background.name = "Background";
            background.transform.SetParent(root.transform, false);
            background.transform.localScale = new Vector3(0.32f, 0.18f, 1f);
            background.GetComponent<MeshRenderer>().sharedMaterial = material;

            GameObject selectionArea = GameObject.CreatePrimitive(PrimitiveType.Quad);
            selectionArea.name = "SelectionArea";
            selectionArea.transform.SetParent(root.transform, false);
            selectionArea.transform.localPosition = new Vector3(0f, 0f, 0.006f);
            selectionArea.transform.localScale = new Vector3(0.42f, 0.28f, 1f);

            Material selectionMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            Color selectionColor = MemoSelectionColor;
            selectionMaterial.color = selectionColor;
            selectionMaterial.SetColor("_BaseColor", selectionColor);
            selectionMaterial.SetFloat("_Surface", 1f);
            selectionMaterial.SetFloat("_Blend", 0f);
            selectionMaterial.SetFloat("_Cull", 0f);
            selectionMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            selectionMaterial.renderQueue = 2995;
            selectionArea.GetComponent<MeshRenderer>().sharedMaterial = selectionMaterial;

            LineRenderer selectionOutline = root.AddComponent<LineRenderer>();
            selectionOutline.sharedMaterial = selectionMaterial;
            selectionOutline.useWorldSpace = false;
            selectionOutline.loop = true;
            selectionOutline.startWidth = 0.008f;
            selectionOutline.endWidth = 0.008f;
            selectionOutline.positionCount = 4;
            selectionOutline.SetPosition(0, new Vector3(-0.21f, -0.14f, 0.008f));
            selectionOutline.SetPosition(1, new Vector3(-0.21f, 0.14f, 0.008f));
            selectionOutline.SetPosition(2, new Vector3(0.21f, 0.14f, 0.008f));
            selectionOutline.SetPosition(3, new Vector3(0.21f, -0.14f, 0.008f));

            GameObject canvasObject = new GameObject("WorldCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(root.transform, false);
            canvasObject.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            canvasObject.transform.localRotation = Quaternion.identity;
            canvasObject.transform.localScale = Vector3.one * 0.001f;

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;
            canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(320f, 180f);

            TMP_Text text = CreateText(canvasObject.transform, "MemoText", "Memo", 28, TextAlignmentOptions.Center);
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18f, 18f);
            textRect.offsetMax = new Vector2(-18f, -18f);
            text.color = new Color(0.11f, 0.10f, 0.06f, 1f);

            MemoCard memoCard = root.AddComponent<MemoCard>();
            SetPrivateObjectReference(memoCard, "memoText", text);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        private static GameObject CreatePlanePrefab()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(PlaneMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, PlaneMaterialPath);
            }

            Color planeColor = PlaneDebugColor;
            material.color = planeColor;
            material.SetColor("_BaseColor", planeColor);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3000;

            GameObject root = new GameObject("DetectedPlane");
            root.AddComponent<ARPlane>();
            root.AddComponent<ARPlaneMeshVisualizer>();
            root.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = root.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;

            LineRenderer lineRenderer = root.AddComponent<LineRenderer>();
            lineRenderer.sharedMaterial = material;
            lineRenderer.startWidth = 0.025f;
            lineRenderer.endWidth = 0.025f;
            lineRenderer.loop = true;
            lineRenderer.useWorldSpace = false;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PlanePrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void CreateArRig(GameObject planePrefab, out ARRaycastManager raycastManager, out ARStartupStatus startupStatus)
        {
            GameObject session = new GameObject("AR Session");
            session.AddComponent<ARSession>();
            session.AddComponent<ARInputManager>();
            startupStatus = session.AddComponent<ARStartupStatus>();

            GameObject origin = new GameObject("XR Origin (AR)");
            XROrigin xrOrigin = origin.AddComponent<XROrigin>();
            RemoveUnexpectedCamera(origin);
            ARPlaneManager planeManager = origin.AddComponent<ARPlaneManager>();
            planeManager.planePrefab = planePrefab;
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            raycastManager = origin.AddComponent<ARRaycastManager>();
            origin.AddComponent<ARPointCloudManager>();
            origin.AddComponent<ARAnchorManager>();
            RemoveUnexpectedCamera(origin);

            GameObject cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(origin.transform, false);

            GameObject cameraObject = ObjectFactory.CreateGameObject(
                "AR Camera",
                typeof(Camera),
                typeof(AudioListener),
                typeof(ARCameraManager),
                typeof(ARCameraBackground),
                typeof(AROcclusionManager),
                typeof(TrackedPoseDriver));
            cameraObject.transform.SetParent(cameraOffset.transform, false);
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.GetComponent<Camera>();
            ARCameraManager cameraManager = cameraObject.GetComponent<ARCameraManager>();
            cameraManager.requestedBackgroundRenderingMode = CameraBackgroundRenderingMode.BeforeOpaques;
            SerializedObject serializedCameraManager = new SerializedObject(cameraManager);
            SerializedProperty renderMode = serializedCameraManager.FindProperty("m_RenderMode");
            if (renderMode != null)
            {
                renderMode.intValue = (int)CameraBackgroundRenderingMode.BeforeOpaques;
                serializedCameraManager.ApplyModifiedPropertiesWithoutUndo();
            }

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 20f;
            AROcclusionManager occlusionManager = cameraObject.GetComponent<AROcclusionManager>();
            occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Best;
            occlusionManager.environmentDepthTemporalSmoothingRequested = true;
            occlusionManager.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.NoOcclusion;
            ConfigureTrackedPoseDriver(cameraObject);

            xrOrigin.CameraFloorOffsetObject = cameraOffset;
            xrOrigin.Camera = camera;
        }

        private static void RemoveUnexpectedCamera(GameObject gameObject)
        {
            Camera extraCamera = gameObject.GetComponent<Camera>();
            if (extraCamera != null)
            {
                Object.DestroyImmediate(extraCamera);
            }

            AudioListener extraListener = gameObject.GetComponent<AudioListener>();
            if (extraListener != null)
            {
                Object.DestroyImmediate(extraListener);
            }
        }

        private static void ConfigureTrackedPoseDriver(GameObject cameraObject)
        {
            TrackedPoseDriver trackedPoseDriver = cameraObject.GetComponent<TrackedPoseDriver>();
            trackedPoseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            trackedPoseDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

            InputAction positionAction = new InputAction(
                "CenterEyePosition",
                InputActionType.Value,
                "<XRHMD>/centerEyePosition",
                expectedControlType: "Vector3");
            positionAction.AddBinding("<HandheldARInputDevice>/devicePosition");

            InputAction rotationAction = new InputAction(
                "CenterEyeRotation",
                InputActionType.Value,
                "<XRHMD>/centerEyeRotation",
                expectedControlType: "Quaternion");
            rotationAction.AddBinding("<HandheldARInputDevice>/deviceRotation");

            trackedPoseDriver.positionInput = new InputActionProperty(positionAction);
            trackedPoseDriver.rotationInput = new InputActionProperty(rotationAction);
        }

        private static void CreateLighting()
        {
            GameObject lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static void CreateUi(
            out MemoInputController inputController,
            out MemoManager memoManager,
            out TMP_Text statusText,
            out TMP_Text placementHintText)
        {
            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            GameObject canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject panel = CreateUiPanel(canvasObject.transform, "BottomPanel");
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(1f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.sizeDelta = new Vector2(0f, 320f);
            panelRect.anchoredPosition = Vector2.zero;

            GameObject statusPanel = CreateUiPanel(canvasObject.transform, "StatusPanel");
            RectTransform statusPanelRect = statusPanel.GetComponent<RectTransform>();
            statusPanelRect.anchorMin = new Vector2(0f, 1f);
            statusPanelRect.anchorMax = new Vector2(1f, 1f);
            statusPanelRect.pivot = new Vector2(0.5f, 1f);
            statusPanelRect.offsetMin = new Vector2(24f, -128f);
            statusPanelRect.offsetMax = new Vector2(-24f, -32f);

            statusText = CreateText(statusPanel.transform, "ARStatusText", "Starting AR", 32, TextAlignmentOptions.Center);
            statusText.color = Color.white;
            RectTransform statusRect = statusText.GetComponent<RectTransform>();
            SetStretchRect(statusRect, 20f, 8f, -20f, -8f);

            placementHintText = CreateText(panel.transform, "HintText", "Slowly scan a wall, desk, or floor.", 34, TextAlignmentOptions.Center);
            SetBottomStretchRect(placementHintText.GetComponent<RectTransform>(), 36f, 228f, -36f, 56f);
            placementHintText.color = Color.white;

            TMP_InputField inputField = CreateInputField(panel.transform);
            SetBottomStretchRect(inputField.GetComponent<RectTransform>(), 36f, 128f, -36f, 76f);

            Button saveButton = CreateButton(panel.transform, "SaveButton", "SAVE");
            SetBottomLeftRect(saveButton.GetComponent<RectTransform>(), 36f, 24f, 200f, 72f);

            Button deleteButton = CreateButton(panel.transform, "DeleteButton", "DELETE");
            SetBottomLeftRect(deleteButton.GetComponent<RectTransform>(), 260f, 24f, 220f, 72f);

            Button clearButton = CreateButton(panel.transform, "ClearAllButton", "CLEAR");
            SetBottomLeftRect(clearButton.GetComponent<RectTransform>(), 504f, 24f, 200f, 72f);

            TMP_Text countText = CreateText(panel.transform, "MemoCountText", "Memo 0", 30, TextAlignmentOptions.Right);
            SetBottomStretchRect(countText.GetComponent<RectTransform>(), 728f, 24f, -36f, 72f);
            countText.color = Color.white;

            GameObject inputObject = new GameObject("MemoInputController");
            inputController = inputObject.AddComponent<MemoInputController>();
            inputController.SetInputField(inputField);

            GameObject managerObject = new GameObject("MemoManager");
            memoManager = managerObject.AddComponent<MemoManager>();
            memoManager.SetCountText(countText);

            _ = eventSystem;
            _ = saveButton;
            _ = deleteButton;
            _ = clearButton;
        }

        private static ARPlacementController CreatePlacementController(
            ARRaycastManager raycastManager,
            MemoInputController inputController,
            MemoManager memoManager,
            GameObject memoPrefab,
            TMP_Text placementHintText)
        {
            GameObject placementObject = new GameObject("ARPlacementController");
            ARPlacementController placementController = placementObject.AddComponent<ARPlacementController>();
            SetPrivateObjectReference(placementController, "raycastManager", raycastManager);
            SetPrivateObjectReference(placementController, "memoInputController", inputController);
            SetPrivateObjectReference(placementController, "memoManager", memoManager);
            SetPrivateObjectReference(placementController, "memoCardPrefab", memoPrefab);
            placementController.SetPlacementHintText(placementHintText);
            return placementController;
        }

        private static GameObject CreateUiPanel(Transform parent, string name)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            Image image = panel.GetComponent<Image>();
            image.color = new Color(0.06f, 0.07f, 0.08f, 0.72f);
            image.raycastTarget = false;
            return panel;
        }

        private static TMP_InputField CreateInputField(Transform parent)
        {
            GameObject root = new GameObject("MemoInputField", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            root.transform.SetParent(parent, false);
            root.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.96f);

            TMP_Text text = CreateText(root.transform, "Text", string.Empty, 30, TextAlignmentOptions.Left);
            SetStretchRect(text.GetComponent<RectTransform>(), 24f, 0f, -24f, 0f);
            text.color = new Color(0.09f, 0.10f, 0.12f, 1f);

            TMP_Text placeholder = CreateText(root.transform, "Placeholder", "Memo text", 30, TextAlignmentOptions.Left);
            SetStretchRect(placeholder.GetComponent<RectTransform>(), 24f, 0f, -24f, 0f);
            placeholder.color = new Color(0.45f, 0.47f, 0.50f, 1f);

            TMP_InputField inputField = root.GetComponent<TMP_InputField>();
            inputField.textComponent = text;
            inputField.placeholder = placeholder;
            return inputField;
        }

        private static Button CreateButton(Transform parent, string name, string label)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            root.transform.SetParent(parent, false);
            root.GetComponent<Image>().color = new Color(0.92f, 0.25f, 0.20f, 1f);

            TMP_Text text = CreateText(root.transform, "Label", label, 28, TextAlignmentOptions.Center);
            SetStretchRect(text.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            text.color = Color.white;
            return root.GetComponent<Button>();
        }

        private static TMP_Text CreateText(Transform parent, string name, string textValue, float fontSize, TextAlignmentOptions alignment)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            TMP_Text text = textObject.GetComponent<TMP_Text>();
            text.text = textValue;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            return text;
        }

        private static void SetStretchRect(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(right, top);
        }

        private static void SetBottomStretchRect(RectTransform rect, float left, float bottom, float right, float height)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(right, bottom + height);
        }

        private static void SetBottomLeftRect(RectTransform rect, float left, float bottom, float width, float height)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(left, bottom);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void SetPrivateObjectReference(Object target, string propertyName, Object value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            serializedObject.FindProperty(propertyName).objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
