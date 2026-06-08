using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace ARSpaceMemo
{
    public class MemoCard : MonoBehaviour
    {
        [SerializeField] private TMP_Text memoText;
        [SerializeField] private bool faceCamera;

        private const float TouchWidth = 0.56f;
        private const float TouchHeight = 0.36f;
        private const float TouchDepth = 0.08f;
        private const float ExpandedWidth = 0.46f;
        private const float ExpandedHeight = 0.30f;
        private const float ExpandedSelectionWidth = 0.52f;
        private const float ExpandedSelectionHeight = 0.34f;
        private const float CollapsedWidth = 0.18f;
        private const float CollapsedHeight = 0.12f;
        private const float CollapsedSelectionWidth = 0.24f;
        private const float CollapsedSelectionHeight = 0.16f;
        private const int CollapsedPreviewMaxChars = 10;
        private const int MemoRenderQueue = 4990;
        private const int MemoTextRenderQueue = 5000;
        private Renderer backgroundRenderer;
        private Color defaultColor = new(1f, 0.92f, 0.46f, 1f);
        private readonly Color selectedColor = new(0.42f, 1f, 0.58f, 1f);
        private Transform backgroundTransform;
        private Transform selectionAreaTransform;
        private LineRenderer selectionOutline;
        private BoxCollider touchCollider;

        public string Id { get; private set; }
        public string Text { get; private set; }
        public float CreatedAt { get; private set; }
        public bool IsCollapsed { get; private set; }

        private void Awake()
        {
            EnsureTextTarget();
            EnsureTouchCollider();
            EnsureVisibleRendering();
        }

        public void Initialize(string id, string text)
        {
            Id = id;
            CreatedAt = Time.time;
            CacheBackgroundRenderer();
            EnsureTouchCollider();
            EnsureVisibleRendering();
            SetText(text);
        }

        public void SetText(string text)
        {
            Text = string.IsNullOrWhiteSpace(text) ? "Memo" : text.Trim();
            EnsureTextTarget();
            EnsureTextRendering();

            if (memoText != null)
            {
                memoText.text = IsCollapsed ? GetCollapsedPreview(Text) : Text;
                memoText.ForceMeshUpdate();
            }
        }

        public void SetTextTarget(TMP_Text text)
        {
            memoText = text;
            EnsureTextRendering();
        }

        public void SetFaceCamera(bool enabled)
        {
            faceCamera = enabled;
        }

        public void SetSelected(bool selected)
        {
            CacheBackgroundRenderer();
            EnsureTouchCollider();
            EnsureVisibleRendering();

            if (backgroundRenderer != null)
            {
                backgroundRenderer.material.color = selected ? selectedColor : defaultColor;
                ConfigureVisibleMaterial(backgroundRenderer.material, MemoRenderQueue);
            }
        }

        public void ToggleCollapsed()
        {
            SetCollapsed(!IsCollapsed);
        }

        public void SetCollapsed(bool collapsed)
        {
            IsCollapsed = collapsed;
            ApplyVisualState();
            SetText(Text);
        }

        private void EnsureTouchCollider()
        {
            BoxCollider boxCollider = GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                boxCollider = gameObject.AddComponent<BoxCollider>();
            }

            touchCollider = boxCollider;
            boxCollider.isTrigger = false;
            ApplyColliderState();
        }

        private void EnsureVisibleRendering()
        {
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = true;
                renderer.forceRenderingOff = false;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                foreach (Material material in renderer.materials)
                {
                    ConfigureVisibleMaterial(material, MemoRenderQueue);
                }
            }

            EnsureTextRendering();
        }

        private void EnsureTextTarget()
        {
            if (memoText == null)
            {
                memoText = GetComponentInChildren<TMP_Text>(true);
            }
        }

        private void EnsureTextRendering()
        {
            EnsureTextTarget();
            if (memoText == null)
            {
                return;
            }

            memoText.raycastTarget = false;
            memoText.alignment = TextAlignmentOptions.Center;
            memoText.textWrappingMode = TextWrappingModes.Normal;
            memoText.overflowMode = TextOverflowModes.Ellipsis;
            memoText.color = new Color(0.08f, 0.07f, 0.03f, 1f);

            Canvas worldCanvas = memoText.GetComponentInParent<Canvas>();
            if (worldCanvas != null && worldCanvas.renderMode == RenderMode.WorldSpace)
            {
                Transform canvasTransform = worldCanvas.transform;
                canvasTransform.localRotation = Quaternion.identity;
                canvasTransform.localPosition = new Vector3(0f, 0f, 0.02f);
                canvasTransform.localScale = Vector3.one * 0.001f;
                worldCanvas.overrideSorting = true;
                worldCanvas.sortingOrder = 100;
            }

            CanvasRenderer canvasRenderer = memoText.GetComponent<CanvasRenderer>();
            if (canvasRenderer != null)
            {
                canvasRenderer.cullTransparentMesh = false;
            }

            if (memoText.fontMaterial == null && memoText.fontSharedMaterial != null)
            {
                memoText.fontMaterial = new Material(memoText.fontSharedMaterial);
            }

            if (memoText.fontMaterial != null)
            {
                ConfigureVisibleMaterial(memoText.fontMaterial, MemoTextRenderQueue);
            }

            memoText.UpdateMeshPadding();
        }

        private static void ConfigureVisibleMaterial(Material material, int renderQueue)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", 0f);
            }

            if (material.HasProperty("_ZTest"))
            {
                material.SetFloat("_ZTest", (float)CompareFunction.Always);
            }

            if (material.HasProperty("_ZTestMode"))
            {
                material.SetFloat("_ZTestMode", (float)CompareFunction.Always);
            }

            material.renderQueue = renderQueue;
        }

        private void CacheBackgroundRenderer()
        {
            if (backgroundRenderer != null)
            {
                return;
            }

            Transform background = transform.Find("Background") ?? transform.Find("FallbackBackground");
            if (background != null && background.TryGetComponent(out Renderer renderer))
            {
                backgroundTransform = background;
                backgroundRenderer = renderer;
                defaultColor = renderer.material.color;
            }
        }

        private void CacheVisualTargets()
        {
            if (backgroundTransform == null)
            {
                backgroundTransform = transform.Find("Background") ?? transform.Find("FallbackBackground");
            }

            if (selectionAreaTransform == null)
            {
                selectionAreaTransform = transform.Find("FallbackSelectionArea");
            }

            if (selectionOutline == null)
            {
                selectionOutline = GetComponent<LineRenderer>();
            }
        }

        private void ApplyVisualState()
        {
            CacheVisualTargets();
            float cardWidth = IsCollapsed ? CollapsedWidth : ExpandedWidth;
            float cardHeight = IsCollapsed ? CollapsedHeight : ExpandedHeight;
            float selectionWidth = IsCollapsed ? CollapsedSelectionWidth : ExpandedSelectionWidth;
            float selectionHeight = IsCollapsed ? CollapsedSelectionHeight : ExpandedSelectionHeight;

            if (backgroundTransform != null)
            {
                backgroundTransform.localScale = new Vector3(cardWidth, cardHeight, 0.018f);
            }

            if (selectionAreaTransform != null)
            {
                selectionAreaTransform.localScale = new Vector3(selectionWidth, selectionHeight, 1f);
            }

            if (selectionOutline != null)
            {
                float halfWidth = selectionWidth * 0.5f;
                float halfHeight = selectionHeight * 0.5f;
                selectionOutline.SetPosition(0, new Vector3(-halfWidth, -halfHeight, 0.008f));
                selectionOutline.SetPosition(1, new Vector3(-halfWidth, halfHeight, 0.008f));
                selectionOutline.SetPosition(2, new Vector3(halfWidth, halfHeight, 0.008f));
                selectionOutline.SetPosition(3, new Vector3(halfWidth, -halfHeight, 0.008f));
            }

            if (memoText != null)
            {
                memoText.fontSize = IsCollapsed ? 1.0f : 1.2f;
                memoText.rectTransform.sizeDelta = IsCollapsed ? new Vector2(2.0f, 1.2f) : new Vector2(4.4f, 2.4f);
            }

            ApplyColliderState();
        }

        private void ApplyColliderState()
        {
            if (touchCollider == null)
            {
                touchCollider = GetComponent<BoxCollider>();
            }

            if (touchCollider == null)
            {
                return;
            }

            touchCollider.center = new Vector3(0f, 0f, 0.02f);
            touchCollider.size = IsCollapsed
                ? new Vector3(CollapsedSelectionWidth, CollapsedSelectionHeight, TouchDepth)
                : new Vector3(TouchWidth, TouchHeight, TouchDepth);
        }

        private static string GetCollapsedPreview(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "Memo";
            }

            string compactText = text.Trim().Replace('\n', ' ');
            return compactText.Length <= CollapsedPreviewMaxChars
                ? compactText
                : compactText.Substring(0, CollapsedPreviewMaxChars) + "...";
        }

        private void LateUpdate()
        {
            if (!faceCamera || Camera.main == null)
            {
                return;
            }

            Vector3 directionToCamera = Camera.main.transform.position - transform.position;

            if (directionToCamera.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(-directionToCamera.normalized, Vector3.up);
            }
        }
    }
}
