using System;
using System.Collections.Generic;
using ARLogistics.Detection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ARLogistics.Features
{
    /// <summary>
    /// ARMain-only MVP controller for pallet detection and grid stack preview.
    /// </summary>
    public sealed class ARMainPalletStackPreview : MonoBehaviour
    {
        private const string InitialMessage = "팔레트를 카메라로 비춰주세요";
        private const string DetectedMessage = "팔레트 감지 완료\n적재 미리보기 생성 가능";

        [Header("Detection")]
        [SerializeField] private YoloDetector yoloDetector;
        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private string palletClassName = "pallet";
        [SerializeField, Range(0f, 1f)] private float minimumConfidence = 0.45f;

        [Header("UI")]
        [SerializeField] private Text statusText;
        [SerializeField] private Button generateButton;
        [SerializeField] private Button resetButton;

        [Header("Pallet Layout")]
        [SerializeField] private float palletWidth = 1.2f;
        [SerializeField] private float palletLength = 1.0f;
        [SerializeField] private float palletHeight = 0.15f;
        [SerializeField] private int columns = 3;
        [SerializeField] private int rows = 3;
        [SerializeField] private int layers = 8;
        [SerializeField] private float boxHeight = 0.25f;
        [SerializeField] private float boxGap = 0.02f;
        [SerializeField] private float recommendedHeight = 1.8f;

        public bool IsPalletDetected { get; private set; }
        public bool CanGeneratePreview { get; private set; }
        public Vector3 PalletAnchorPosition { get; private set; }

        private Pose _palletAnchorPose;
        private readonly List<ARRaycastHit> _raycastHits = new();
        private readonly List<DetectionResult> _detectedObjects = new();
        private readonly List<GameObject> _spawnedBoxes = new();
        private readonly List<Material> _runtimeMaterials = new();

        private void Awake()
        {
            if (yoloDetector == null)
                yoloDetector = FindFirstObjectByType<YoloDetector>();
            if (raycastManager == null)
                raycastManager = FindFirstObjectByType<ARRaycastManager>();
            if (statusText == null)
                statusText = GameObject.Find("SP_StatusText")?.GetComponent<Text>();
            if (generateButton == null)
                generateButton = GameObject.Find("SP_PlaceBtn")?.GetComponent<Button>();
            if (resetButton == null)
                resetButton = GameObject.Find("SP_ClearBtn")?.GetComponent<Button>();
        }

        private void OnEnable()
        {
            if (yoloDetector != null)
                yoloDetector.OnDetectionResultsUpdated += HandleDetections;

            generateButton?.onClick.AddListener(GeneratePreview);
            resetButton?.onClick.AddListener(ResetARPreview);
            ResetARPreview();
        }

        private void OnDisable()
        {
            if (yoloDetector != null)
                yoloDetector.OnDetectionResultsUpdated -= HandleDetections;

            generateButton?.onClick.RemoveListener(GeneratePreview);
            resetButton?.onClick.RemoveListener(ResetARPreview);
            ClearPreview();
        }

        private void HandleDetections(List<DetectionResult> detections)
        {
            _detectedObjects.Clear();
            if (detections != null)
                _detectedObjects.AddRange(detections);

            DetectionResult? bestPallet = null;
            foreach (var detection in _detectedObjects)
            {
                if (!string.Equals(detection.className, palletClassName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (detection.confidence < minimumConfidence)
                    continue;
                if (!bestPallet.HasValue || detection.confidence > bestPallet.Value.confidence)
                    bestPallet = detection;
            }

            if (!bestPallet.HasValue || raycastManager == null)
                return;

            Vector2 screenPoint = BoundingBoxCenterToScreen(bestPallet.Value.boundingBox);
            _raycastHits.Clear();
            if (!raycastManager.Raycast(
                    screenPoint,
                    _raycastHits,
                    TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds))
                return;

            _palletAnchorPose = _raycastHits[0].pose;
            PalletAnchorPosition = _palletAnchorPose.position;
            IsPalletDetected = true;
            CanGeneratePreview = true;
            SetStatus(DetectedMessage);
        }

        public void GeneratePreview()
        {
            if (!CanGeneratePreview || !IsPalletDetected)
            {
                SetStatus(InitialMessage);
                return;
            }

            ClearPreview();

            int safeColumns = Mathf.Max(1, columns);
            int safeRows = Mathf.Max(1, rows);
            int safeLayers = Mathf.Max(1, layers);
            float cellWidth = palletWidth / safeColumns;
            float cellLength = palletLength / safeRows;
            float previewWidth = Mathf.Max(0.01f, cellWidth - boxGap);
            float previewLength = Mathf.Max(0.01f, cellLength - boxGap);
            float bottomY = palletHeight;

            Vector3 right = _palletAnchorPose.rotation * Vector3.right;
            Vector3 forward = _palletAnchorPose.rotation * Vector3.forward;

            for (int layer = 0; layer < safeLayers; layer++)
            {
                float topHeight = bottomY + boxHeight * (layer + 1);
                bool exceedsRecommendedHeight = topHeight > recommendedHeight;
                Color color = exceedsRecommendedHeight
                    ? new Color(1f, 0.15f, 0.1f, 0.55f)
                    : new Color(0.1f, 0.9f, 0.25f, 0.55f);

                for (int row = 0; row < safeRows; row++)
                {
                    for (int column = 0; column < safeColumns; column++)
                    {
                        float x = -palletWidth * 0.5f + cellWidth * (column + 0.5f);
                        float z = -palletLength * 0.5f + cellLength * (row + 0.5f);
                        float y = bottomY + boxHeight * (layer + 0.5f);

                        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        box.name = $"AR_PalletBox_L{layer + 1}_R{row + 1}_C{column + 1}";
                        box.transform.SetPositionAndRotation(
                            PalletAnchorPosition + right * x + forward * z + Vector3.up * y,
                            _palletAnchorPose.rotation);
                        box.transform.localScale = new Vector3(previewWidth, boxHeight - boxGap, previewLength);

                        var collider = box.GetComponent<Collider>();
                        if (collider != null)
                            Destroy(collider);

                        var material = CreateTransparentMaterial(color);
                        box.GetComponent<Renderer>().material = material;
                        _runtimeMaterials.Add(material);
                        _spawnedBoxes.Add(box);
                    }
                }
            }

            SetStatus($"팔레트 기준 적재 미리보기\n{safeColumns} x {safeRows} x {safeLayers}단");
        }

        public void ResetARPreview()
        {
            ClearPreview();
            IsPalletDetected = false;
            CanGeneratePreview = false;
            PalletAnchorPosition = Vector3.zero;
            _palletAnchorPose = new Pose(Vector3.zero, Quaternion.identity);
            _detectedObjects.Clear();
            _raycastHits.Clear();
            SetStatus(InitialMessage);
        }

        private void ClearPreview()
        {
            foreach (var box in _spawnedBoxes)
                if (box != null)
                    Destroy(box);
            _spawnedBoxes.Clear();

            foreach (var material in _runtimeMaterials)
                if (material != null)
                    Destroy(material);
            _runtimeMaterials.Clear();
        }

        private static Vector2 BoundingBoxCenterToScreen(Rect boundingBox)
        {
            float x = (boundingBox.x + boundingBox.width * 0.5f) * Screen.width;
            float y = (1f - boundingBox.y - boundingBox.height * 0.5f) * Screen.height;
            return new Vector2(x, y);
        }

        private static Material CreateTransparentMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader)
            {
                color = color,
                renderQueue = 3000
            };
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return material;
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }
    }
}
