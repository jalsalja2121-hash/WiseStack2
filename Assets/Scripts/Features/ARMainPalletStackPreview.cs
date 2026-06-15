using System.Collections.Generic;
using ARLogistics.Data;
using ARLogistics.Detection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ARLogistics.Features
{
    /// <summary>ARMain-only virtual pallet placement and stacking preview.</summary>
    public sealed class ARMainPalletStackPreview : MonoBehaviour
    {
        private const string PlacementMessage = "바닥을 터치하여 팔레트를 배치하세요";
        private const string PalletCreatedMessage = "가상 팔레트 생성 완료";

        [Header("ARMain References")]
        [SerializeField] private YoloDetector yoloDetector;
        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private Camera arCamera;
        [Header("Prefabs")]
        [SerializeField] private GameObject palletPrefab;
        [SerializeField] private GameObject boxPreviewPrefab;
        [Header("UI")]
        [SerializeField] private Text statusText;
        [SerializeField] private Button placeButton;
        [SerializeField] private Button resetButton;
        [Header("Pallet Size (m)")]
        [SerializeField, Min(0.1f)] private float palletWidth = 1.1f;
        [SerializeField, Min(0.1f)] private float palletLength = 1.1f;
        [SerializeField, Min(0.01f)] private float palletHeight = 0.15f;
        [Header("Detection")]
        [SerializeField, Range(0f, 1f)] private float minimumConfidence = 0.45f;
        [Header("Stack Rules")]
        [SerializeField, Min(0.1f)] private float recommendedStackHeight = 1.8f;
        [SerializeField, Min(0.1f)] private float previewHeight = 2.1f;
        [SerializeField, Range(0f, 0.05f)] private float boxGap = 0.01f;
        [SerializeField] private Color safeColor = new(0.1f, 0.9f, 0.25f, 0.5f);
        [SerializeField] private Color overHeightColor = new(1f, 0.15f, 0.1f, 0.55f);

        public GameObject PalletAnchor => palletAnchor;
        public Transform PalletTransform => palletTransform;
        public int EstimatedCapacity { get; private set; }
        public float RemainingSpacePercent { get; private set; }

        private readonly List<ARRaycastHit> raycastHits = new();
        private readonly List<GameObject> spawnedBoxes = new();
        private GameObject palletAnchor;
        private Transform palletTransform;

        private BoxMeasurement currentMeasurement;
        private bool hasMeasurement;
        private DetectionResult? latestProductDetection;
        private bool placementEnabled = true;
        private int lastProcessedTouchFrame = -1;

        private void Awake() => ResolveReferences();

        private void OnEnable()
        {
            if (yoloDetector != null) yoloDetector.OnDetectionResultsUpdated += HandleDetections;
            placeButton?.onClick.AddListener(EnablePlacement);
            resetButton?.onClick.AddListener(ResetSimulation);
            ResetSimulation();
            TryLoadMeasurement(true);
        }

        private void OnDisable()
        {
            if (yoloDetector != null) yoloDetector.OnDetectionResultsUpdated -= HandleDetections;
            placeButton?.onClick.RemoveListener(EnablePlacement);
            resetButton?.onClick.RemoveListener(ResetSimulation);
            ClearRuntimeObjects();
        }

        private void Update()
        {
            if (!placementEnabled || palletAnchor != null) return;
            if (!TryGetPointerDown(out Vector2 screenPosition, out int pointerId)) return;
            if (lastProcessedTouchFrame == Time.frameCount) return;
            lastProcessedTouchFrame = Time.frameCount;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointerId)) return;
            TryPlacePallet(screenPosition);
        }

        private void ResolveReferences()
        {
            if (yoloDetector == null) yoloDetector = FindFirstObjectByType<YoloDetector>();
            if (raycastManager == null) raycastManager = FindFirstObjectByType<ARRaycastManager>();
            if (arCamera == null) arCamera = Camera.main;
            if (statusText == null) statusText = GameObject.Find("SP_StatusText")?.GetComponent<Text>();
            if (placeButton == null) placeButton = GameObject.Find("SP_PlaceBtn")?.GetComponent<Button>();


            if (ARLogistics.AppSettings.PalletWidth > 0.1f)
                palletWidth = ARLogistics.AppSettings.PalletWidth;
            if (ARLogistics.AppSettings.PalletLength > 0.1f)
                palletLength = ARLogistics.AppSettings.PalletLength;
            if (resetButton == null) resetButton = GameObject.Find("SP_ClearBtn")?.GetComponent<Button>();
        }

        private void HandleDetections(List<DetectionResult> detections)
        {
            DetectionResult? best = null;
            if (detections != null)
                foreach (DetectionResult detection in detections)
                {
                    if (detection.confidence < minimumConfidence) continue;
                    if (!best.HasValue || detection.confidence > best.Value.confidence) best = detection;
                }

            latestProductDetection = best;
        }

        public void EnablePlacement()
        {
            if (!hasMeasurement && !TryLoadMeasurement(true))
                return;

            if (palletAnchor != null)
            {
                GenerateStackPreview();
                return;
            }

            placementEnabled = true;
            SetStatus($"상자 크기 불러오기 완료\n{FormatMeasurement()}\n{PlacementMessage}");
        }

        public void ResetSimulation()
        {
            ClearRuntimeObjects();
            latestProductDetection = null;
            currentMeasurement = default;
            hasMeasurement = false;
            EstimatedCapacity = 0;
            RemainingSpacePercent = 0f;
            placementEnabled = true;
            SetStatus("상자 크기를 먼저 측정해주세요");
        }

        private void TryPlacePallet(Vector2 screenPosition)
        {
            if (raycastManager == null) { SetStatus("AR 평면 감지를 초기화할 수 없습니다"); return; }
            raycastHits.Clear();
            if (!raycastManager.Raycast(screenPosition, raycastHits, TrackableType.PlaneWithinPolygon))
            {
                SetStatus("감지된 바닥을 터치해 주세요");
                return;
            }
            Pose hitPose = raycastHits[0].pose;
            CreatePallet(new Pose(hitPose.position, GetHorizontalFacingRotation(hitPose.rotation)));
            placementEnabled = false;
            SetStatus(PalletCreatedMessage);
            GenerateStackPreview();
        }

        private void CreatePallet(Pose pose)
        {
            palletAnchor = new GameObject("ARMain_VirtualPalletAnchor");
            palletAnchor.transform.SetPositionAndRotation(pose.position, pose.rotation);
            palletTransform = palletAnchor.transform;
            GameObject visual;
            if (palletPrefab != null)
            {
                visual = Instantiate(palletPrefab, palletTransform);
                visual.name = "VirtualPallet";
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
            }
            else
            {
                visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.name = "VirtualPallet";
                visual.transform.SetParent(palletTransform, false);
                visual.transform.localPosition = Vector3.up * (palletHeight * 0.5f);
            }
            visual.transform.localScale = new Vector3(palletWidth, palletHeight, palletLength);
            Collider palletCollider = visual.GetComponent<Collider>();
            if (palletCollider != null) Destroy(palletCollider);
        }

        private void GenerateStackPreview()
        {
            ClearBoxes();
            EstimatedCapacity = 0;
            RemainingSpacePercent = 0f;

            if (palletTransform == null) return;
            if (!hasMeasurement && !TryLoadMeasurement(false))
            {
                SetStatus("상자 크기를 먼저 측정해주세요");
                return;
            }

            var product = new ProductDimensions(
                currentMeasurement.WidthM,
                currentMeasurement.DepthM,
                currentMeasurement.HeightM);

            if (!TryCalculateLayout(product, out StackLayout layout))
            {
                SetStatus($"상자 크기 불러오기 완료\n{FormatMeasurement()}\n상자가 팔레트보다 크거나 적재 높이를 초과합니다");
                return;
            }

            SpawnBoxes(product, layout);
            int perLayer = layout.columns * layout.rows;
            EstimatedCapacity = perLayer * layout.safeLayers;
            float usedArea = perLayer * layout.boxWidth * layout.boxLength;
            float palletArea = palletWidth * palletLength;
            float usagePercent = palletArea > 0f ? Mathf.Clamp01(usedArea / palletArea) * 100f : 0f;
            RemainingSpacePercent = 100f - usagePercent;

            SetStatus(
                $"상자 크기 불러오기 완료\n{FormatMeasurement()}\n" +
                $"예상 적재량: {EstimatedCapacity}개\n" +
                $"한 층 적재량: {perLayer}개\n" +
                $"예상 층수: {layout.safeLayers}층\n" +
                $"바닥 사용률: {usagePercent:F1}% · 남은 공간: {RemainingSpacePercent:F1}%");
        }

        private bool TryCalculateLayout(ProductDimensions product, out StackLayout layout)
        {
            int normalColumns = Mathf.FloorToInt(palletWidth / product.width);
            int normalRows = Mathf.FloorToInt(palletLength / product.length);
            int rotatedColumns = Mathf.FloorToInt(palletWidth / product.length);
            int rotatedRows = Mathf.FloorToInt(palletLength / product.width);
            bool rotate = rotatedColumns * rotatedRows > normalColumns * normalRows;
            int columns = rotate ? rotatedColumns : normalColumns;
            int rows = rotate ? rotatedRows : normalRows;
            float width = rotate ? product.length : product.width;
            float length = rotate ? product.width : product.length;
            int safeLayers = Mathf.Max(0, Mathf.FloorToInt((recommendedStackHeight - palletHeight) / product.height));
            int previewLayers = Mathf.Max(safeLayers, Mathf.FloorToInt((previewHeight - palletHeight) / product.height));
            layout = new StackLayout(columns, rows, safeLayers, previewLayers, width, length);
            return columns > 0 && rows > 0 && previewLayers > 0;
        }

        private void SpawnBoxes(ProductDimensions product, StackLayout layout)
        {
            float startX = -layout.columns * layout.boxWidth * 0.5f + layout.boxWidth * 0.5f;
            float startZ = -layout.rows * layout.boxLength * 0.5f + layout.boxLength * 0.5f;
            for (int layer = 0; layer < layout.previewLayers; layer++)
            {
                Color color = layer >= layout.safeLayers ? overHeightColor : safeColor;
                for (int row = 0; row < layout.rows; row++)
                for (int column = 0; column < layout.columns; column++)
                {
                    GameObject box = boxPreviewPrefab != null ? Instantiate(boxPreviewPrefab, palletTransform) : GameObject.CreatePrimitive(PrimitiveType.Cube);
                    box.name = $"StackBox_L{layer + 1}_R{row + 1}_C{column + 1}";
                    if (box.transform.parent != palletTransform) box.transform.SetParent(palletTransform, false);
                    box.transform.localPosition = new Vector3(startX + column * layout.boxWidth, palletHeight + product.height * (layer + 0.5f), startZ + row * layout.boxLength);
                    box.transform.localRotation = Quaternion.identity;
                    box.transform.localScale = new Vector3(Mathf.Max(0.01f, layout.boxWidth - boxGap), Mathf.Max(0.01f, product.height - boxGap), Mathf.Max(0.01f, layout.boxLength - boxGap));
                    Collider collider = box.GetComponent<Collider>();
                    if (collider != null) Destroy(collider);
                    ApplyPreviewColor(box, color);
                    spawnedBoxes.Add(box);
                }
            }
        }

        private static void ApplyPreviewColor(GameObject box, Color color)
        {
            foreach (Renderer targetRenderer in box.GetComponentsInChildren<Renderer>())
            {
                var block = new MaterialPropertyBlock();
                targetRenderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                targetRenderer.SetPropertyBlock(block);
            }
        }

        private Quaternion GetHorizontalFacingRotation(Quaternion fallback)
        {
            if (arCamera == null) return fallback;
            Vector3 forward = Vector3.ProjectOnPlane(arCamera.transform.forward, Vector3.up);
            return forward.sqrMagnitude < 0.001f ? fallback : Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private void ClearRuntimeObjects()
        {
            ClearBoxes();
            if (palletAnchor != null) Destroy(palletAnchor);
            palletAnchor = null;
            palletTransform = null;
        }

        private void ClearBoxes()
        {
            foreach (GameObject box in spawnedBoxes) if (box != null) Destroy(box);
            spawnedBoxes.Clear();
        }

        private void SetStatus(string message) { if (statusText != null) statusText.text = message; }

        private static bool TryGetPointerDown(out Vector2 position, out int pointerId)
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null)
                foreach (UnityEngine.InputSystem.Controls.TouchControl touch in Touchscreen.current.touches)
                {
                    if (!touch.press.wasPressedThisFrame) continue;
                    position = touch.position.ReadValue();
                    pointerId = touch.touchId.ReadValue();
                    return true;
                }
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                position = Mouse.current.position.ReadValue();
                pointerId = -1;
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            {
                Touch touch = Input.GetTouch(0);
                position = touch.position;
                pointerId = touch.fingerId;
                return true;
            }
            if (Input.GetMouseButtonDown(0))
            {
                position = Input.mousePosition;
                pointerId = -1;
                return true;
            }
#endif
            position = default;
            pointerId = -1;
            return false;
        }

        private readonly struct ProductDimensions
        {
            public readonly float width, length, height;
            public ProductDimensions(float width, float length, float height)
            {
                this.width = Mathf.Max(0.01f, width);
                this.length = Mathf.Max(0.01f, length);
                this.height = Mathf.Max(0.01f, height);
            }
        }

        private readonly struct StackLayout
        {
            public readonly int columns, rows, safeLayers, previewLayers;
            public readonly float boxWidth, boxLength;
            public StackLayout(int columns, int rows, int safeLayers, int previewLayers, float boxWidth, float boxLength)
            {
                this.columns = columns;
                this.rows = rows;
                this.safeLayers = safeLayers;
                this.previewLayers = previewLayers;
                this.boxWidth = boxWidth;
                this.boxLength = boxLength;
            }
        }
    

        private bool TryLoadMeasurement(bool updateStatus)
        {
            if (!BoxMeasurementStore.TryGet(out currentMeasurement))
            {
                hasMeasurement = false;
                if (updateStatus) SetStatus("상자 크기를 먼저 측정해주세요");
                return false;
            }

            hasMeasurement = true;
            if (updateStatus)
                SetStatus($"상자 크기 불러오기 완료\n{FormatMeasurement()}\n{PlacementMessage}");
            return true;
        }

        private string FormatMeasurement()
        {
            string source = string.IsNullOrEmpty(currentMeasurement.SourceName)
                ? string.Empty
                : $" ({currentMeasurement.SourceName})";
            return $"상자 {currentMeasurement.WidthM:F2}m × {currentMeasurement.DepthM:F2}m × {currentMeasurement.HeightM:F2}m{source}";
        }
}
}
