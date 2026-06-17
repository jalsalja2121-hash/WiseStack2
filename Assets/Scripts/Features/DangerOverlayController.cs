using System.Collections.Generic;
using ARLogistics.DangerScene;
using ARLogistics.Data;
using ARLogistics.Detection;
using ARLogistics.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace ARLogistics.Features
{
    public class DangerOverlayController : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private RectTransform overlayContainer;
        [SerializeField] private Text statusText;

        [Header("Risk Colors")]
        [SerializeField] private Color safeColor = new Color(0.10f, 0.90f, 0.20f, 0.45f);
        [SerializeField] private Color warningColor = new Color(1.00f, 0.82f, 0.00f, 0.55f);
        [SerializeField] private Color dangerColor = new Color(1.00f, 0.15f, 0.10f, 0.55f);

        [Header("Risk Analysis")]
        [SerializeField] private BoxStackAnalyzer stackAnalyzer;

        [Header("Throttle")]
        [SerializeField] private float updateInterval = 0.4f;

        private readonly List<GameObject> _overlays = new();
        private YoloDetector _yolo;
        private WarehouseManager _warehouseManager;
        private WarehouseReport _lastReport;
        private float _nextUpdate;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            ResolveReferences();
            SubscribeWarehouseManager();
            SetStatus("박스를 카메라에 비추면 위험 구간을 색상으로 표시합니다.");
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (_yolo != null)
                _yolo.OnDetectionResultsUpdated += HandleDetections;
        }

        private void OnDisable()
        {
            if (_yolo != null)
                _yolo.OnDetectionResultsUpdated -= HandleDetections;

            ClearOverlays();
        }

        private void OnDestroy()
        {
            if (_warehouseManager == null)
                return;

            _warehouseManager.OnReportReady -= HandleReportReady;
            _warehouseManager.OnFinalReportReady -= OnFinalReport;
        }

        private void ResolveReferences()
        {
            if (_yolo == null)
                _yolo = FindFirstObjectByType<YoloDetector>();
            if (stackAnalyzer == null)
                stackAnalyzer = FindFirstObjectByType<BoxStackAnalyzer>();
            if (overlayContainer == null)
                overlayContainer = GameObject.Find("OverlayContainer")?.GetComponent<RectTransform>();
            if (statusText == null)
                statusText = GameObject.Find("DO_StatusText")?.GetComponent<Text>();
        }

        private void SubscribeWarehouseManager()
        {
            if (_warehouseManager != null || WarehouseManager.Instance == null)
                return;

            _warehouseManager = WarehouseManager.Instance;
            _warehouseManager.OnReportReady += HandleReportReady;
            _warehouseManager.OnFinalReportReady += OnFinalReport;
        }

        private void HandleReportReady(WarehouseReport report)
        {
            _lastReport = report;
        }

        private void OnFinalReport(WarehouseReport report)
        {
            _lastReport = report;
            if (statusText == null || string.IsNullOrEmpty(report.geminiGuidance))
                return;

            string guidance = report.geminiGuidance;
            if (guidance.Length > 220)
                guidance = guidance.Substring(0, 220) + "...";

            statusText.text = "AI: " + guidance;
        }

        private void HandleDetections(List<DetectionResult> detections)
        {
            if (Time.time < _nextUpdate)
                return;

            _nextUpdate = Time.time + updateInterval;
            ClearOverlays();

            if (detections == null || detections.Count == 0 || overlayContainer == null)
            {
                SetStatus("박스를 카메라에 비추면 위험 구간을 색상으로 표시합니다.");
                return;
            }

            Dictionary<int, List<DetectionResult>> groups = GroupByClass(detections);
            foreach (List<DetectionResult> group in groups.Values)
                SpawnOverlay(BuildUnionDetection(group), group);

            SetStatus($"{groups.Count}종 분석 완료");
        }

        private void SpawnOverlay(DetectionResult displayDetection, List<DetectionResult> group)
        {
            var (riskLabel, color) = EvaluateRisk(displayDetection.classId, group);

            float canvasWidth = overlayContainer.rect.width;
            float canvasHeight = overlayContainer.rect.height;
            Rect bbox = displayDetection.boundingBox;

            float left = bbox.x * canvasWidth;
            float bottom = (1f - bbox.y - bbox.height) * canvasHeight;
            float width = bbox.width * canvasWidth;
            float height = bbox.height * canvasHeight;

            var panel = new GameObject($"Ovr_{displayDetection.className}");
            panel.transform.SetParent(overlayContainer, false);

            var panelRect = panel.AddComponent<RectTransform>();
            var panelImage = panel.AddComponent<Image>();
            panelImage.color = color;
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.zero;
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = new Vector2(left + width * 0.5f, bottom + height * 0.5f);
            panelRect.sizeDelta = new Vector2(width, height);

            string displayName = displayDetection.className;
            if (displayName.Length > 3 && displayName[2] == '_')
                displayName = displayName.Substring(3);

            var labelObject = new GameObject("Lbl");
            labelObject.transform.SetParent(panel.transform, false);

            var labelRect = labelObject.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.sizeDelta = new Vector2(0f, 50f);
            labelRect.anchoredPosition = Vector2.zero;

            var text = labelObject.AddComponent<Text>();
            text.text = $"{riskLabel}\n{displayName}";
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 20;
            text.fontStyle = FontStyle.Bold;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;

            var shadow = labelObject.AddComponent<Shadow>();
            shadow.effectColor = Color.black;

            _overlays.Add(panel);
        }

        private (string label, Color color) EvaluateRisk(int classId, List<DetectionResult> detections)
        {
            if (_lastReport != null)
            {
                ProductCapacity product = _lastReport.products.Find(x => x.classId == classId);
                if (product != null)
                    return EvaluateReportRisk(product);
            }

            if (stackAnalyzer != null)
            {
                DangerLevel level = stackAnalyzer.Analyze(
                    detections,
                    out _,
                    out float estimatedHeightM,
                    out _);
                return ToOverlayRisk(level, estimatedHeightM);
            }

            ProductSpecTable.Spec spec = ProductSpecTable.Get(classId);
            return spec.IsFragile
                ? ("주의 파손", warningColor)
                : ("안전", safeColor);
        }

        private (string label, Color color) EvaluateReportRisk(ProductCapacity product)
        {
            float heightRatio = product.stackHeightM / 2.5f;
            if (heightRatio > 0.88f || product.totalWeightKg > 850f)
                return ($"위험 {product.stackHeightM:F1}m", dangerColor);
            if (heightRatio > 0.65f || product.totalWeightKg > 500f)
                return ($"주의 {product.stackHeightM:F1}m", warningColor);
            return ($"안전 {product.stackHeightM:F1}m", safeColor);
        }

        private (string label, Color color) ToOverlayRisk(DangerLevel level, float heightM)
        {
            return level switch
            {
                DangerLevel.Danger => ($"위험 {heightM:F1}m", dangerColor),
                DangerLevel.Warning => ($"주의 {heightM:F1}m", warningColor),
                _ => ($"안전 {heightM:F1}m", safeColor)
            };
        }

        private void ClearOverlays()
        {
            foreach (GameObject overlay in _overlays)
            {
                if (overlay != null)
                    Destroy(overlay);
            }

            _overlays.Clear();
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }

        private static Dictionary<int, List<DetectionResult>> GroupByClass(List<DetectionResult> detections)
        {
            var groups = new Dictionary<int, List<DetectionResult>>();
            foreach (DetectionResult detection in detections)
            {
                if (!groups.TryGetValue(detection.classId, out List<DetectionResult> group))
                {
                    group = new List<DetectionResult>();
                    groups[detection.classId] = group;
                }

                group.Add(detection);
            }

            return groups;
        }

        private static DetectionResult BuildUnionDetection(List<DetectionResult> detections)
        {
            DetectionResult best = detections[0];
            for (int i = 1; i < detections.Count; i++)
            {
                if (detections[i].confidence > best.confidence)
                    best = detections[i];
            }

            return new DetectionResult(
                UnionNormalizedRects(detections),
                best.classId,
                best.confidence,
                best.className);
        }

        private static Rect UnionNormalizedRects(List<DetectionResult> detections)
        {
            float minX = 1f;
            float minY = 1f;
            float maxX = 0f;
            float maxY = 0f;

            foreach (DetectionResult detection in detections)
            {
                Rect rect = ClampNormalizedRect(detection.boundingBox);
                minX = Mathf.Min(minX, rect.xMin);
                minY = Mathf.Min(minY, rect.yMin);
                maxX = Mathf.Max(maxX, rect.xMax);
                maxY = Mathf.Max(maxY, rect.yMax);
            }

            return maxX > minX && maxY > minY
                ? Rect.MinMaxRect(minX, minY, maxX, maxY)
                : Rect.zero;
        }

        private static Rect ClampNormalizedRect(Rect rect)
        {
            float minX = Mathf.Clamp01(Mathf.Min(rect.xMin, rect.xMax));
            float minY = Mathf.Clamp01(Mathf.Min(rect.yMin, rect.yMax));
            float maxX = Mathf.Clamp01(Mathf.Max(rect.xMin, rect.xMax));
            float maxY = Mathf.Clamp01(Mathf.Max(rect.yMin, rect.yMax));

            if (maxX <= minX)
                maxX = Mathf.Clamp01(minX + Mathf.Abs(rect.width));
            if (maxY <= minY)
                maxY = Mathf.Clamp01(minY + Mathf.Abs(rect.height));

            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }
    }
}
