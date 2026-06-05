using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ARLogistics.Detection;
using ARLogistics.Managers;

namespace ARLogistics.Features
{
    /// <summary>
    /// YOLO 탐지 박스 위에 위험도별 색상 오버레이를 표시합니다.
    /// 초록: 안전, 노랑: 불안정/파손주의, 빨강: 붕괴 위험
    /// </summary>
    public class DangerOverlayController : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private RectTransform overlayContainer;
        [SerializeField] private Text          statusText;

        [Header("Risk Colors")]
        [SerializeField] private Color safeColor    = new Color(0.10f, 0.90f, 0.20f, 0.45f);
        [SerializeField] private Color warningColor = new Color(1.00f, 0.82f, 0.00f, 0.55f);
        [SerializeField] private Color dangerColor  = new Color(1.00f, 0.15f, 0.10f, 0.55f);

        [Header("Throttle")]
        [SerializeField] private float updateInterval = 0.4f;

        private YoloDetector    _yolo;
        private WarehouseReport _lastReport;
        private float           _nextUpdate;
        private readonly List<GameObject> _overlays = new();

        // ─────────────────────────────────────────────
        private void Start()
        {
            _yolo = FindFirstObjectByType<YoloDetector>();
            if (WarehouseManager.Instance != null)
            {
                WarehouseManager.Instance.OnReportReady      += r => _lastReport = r;
                WarehouseManager.Instance.OnFinalReportReady += r => _lastReport = r;
            }
            SetStatus("📷 박스를 카메라에 비추면 위험 구간을 색상으로 표시합니다");
        }

        private void OnEnable()
        {
            if (_yolo != null) _yolo.OnDetectionResultsUpdated += HandleDetections;
        }

        private void OnDisable()
        {
            if (_yolo != null) _yolo.OnDetectionResultsUpdated -= HandleDetections;
            ClearOverlays();
        }

        // ─────────────────────────────────────────────
        private void HandleDetections(List<DetectionResult> detections)
        {
            if (Time.time < _nextUpdate) return;
            _nextUpdate = Time.time + updateInterval;

            ClearOverlays();
            if (detections == null || overlayContainer == null) return;

            // 클래스별 최고 신뢰도만 추출
            var best = new Dictionary<int, DetectionResult>();
            foreach (var d in detections)
                if (!best.TryGetValue(d.classId, out var ex) || d.confidence > ex.confidence)
                    best[d.classId] = d;

            foreach (var kv in best)
                SpawnOverlay(kv.Value);

            SetStatus(best.Count == 0
                ? "📷 박스를 카메라에 비추면 위험 구간을 색상으로 표시합니다"
                : $"📦 {best.Count}종 분석 완료");
        }

        private void SpawnOverlay(DetectionResult det)
        {
            var (riskLabel, color) = EvaluateRisk(det.classId);

            float cW = overlayContainer.rect.width;
            float cH = overlayContainer.rect.height;

            // YOLO bbox: y=0은 상단. Canvas: y=0은 하단 → 반전
            float bLeft   = det.boundingBox.x * cW;
            float bBottom = (1f - det.boundingBox.y - det.boundingBox.height) * cH;
            float bWidth  = det.boundingBox.width  * cW;
            float bHeight = det.boundingBox.height * cH;

            // ── 배경 박스 ──────────────────────────────
            var panel     = new GameObject($"Ovr_{det.className}");
            panel.transform.SetParent(overlayContainer, false);
            var pRect     = panel.AddComponent<RectTransform>();
            var pImg      = panel.AddComponent<Image>();
            pImg.color    = color;
            pRect.anchorMin = Vector2.zero;
            pRect.anchorMax = Vector2.zero;
            pRect.pivot     = new Vector2(0.5f, 0.5f);
            pRect.anchoredPosition = new Vector2(bLeft + bWidth * 0.5f, bBottom + bHeight * 0.5f);
            pRect.sizeDelta        = new Vector2(bWidth, bHeight);

            // ── 라벨 ───────────────────────────────────
            string displayName = det.className;
            if (displayName.Length > 3 && displayName[2] == '_')
                displayName = displayName.Substring(3);

            var lblGO   = new GameObject("Lbl");
            lblGO.transform.SetParent(panel.transform, false);
            var lRect   = lblGO.AddComponent<RectTransform>();
            lRect.anchorMin = new Vector2(0f, 1f);
            lRect.anchorMax = new Vector2(1f, 1f);
            lRect.pivot     = new Vector2(0.5f, 0f);
            lRect.sizeDelta        = new Vector2(0f, 50f);
            lRect.anchoredPosition = Vector2.zero;

            var txt      = lblGO.AddComponent<Text>();
            txt.text      = $"{riskLabel}\n{displayName}";
            txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize  = 20;
            txt.fontStyle = FontStyle.Bold;
            txt.color     = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;

            var shadow    = lblGO.AddComponent<Shadow>();
            shadow.effectColor = Color.black;

            _overlays.Add(panel);
        }

        private (string label, Color color) EvaluateRisk(int classId)
        {
            if (_lastReport != null)
            {
                var p = _lastReport.products.Find(x => x.classId == classId);
                if (p != null)
                {
                    float hRatio = p.stackHeightM / 2.5f;
                    if (hRatio > 0.88f || p.totalWeightKg > 850f)
                        return ("🔴 붕괴 위험", dangerColor);
                    if (hRatio > 0.65f || p.totalWeightKg > 500f)
                        return ("🟡 불안정",    warningColor);
                    return ("🟢 안전", safeColor);
                }
            }

            var spec = ARLogistics.Data.ProductSpecTable.Get(classId);
            if (spec.IsFragile) return ("🟡 파손주의", warningColor);
            return ("🟢 안전", safeColor);
        }

        private void ClearOverlays()
        {
            foreach (var go in _overlays)
                if (go != null) Destroy(go);
            _overlays.Clear();
        }

        private void SetStatus(string msg)
        {
            if (statusText != null) statusText.text = msg;
        }
    

private void Awake()
        {
            _yolo = FindFirstObjectByType<YoloDetector>();
            if (overlayContainer == null) overlayContainer = GameObject.Find("OverlayContainer")?.GetComponent<RectTransform>();
            if (statusText == null)       statusText       = GameObject.Find("DO_StatusText")?.GetComponent<UnityEngine.UI.Text>();
        }
}
}
