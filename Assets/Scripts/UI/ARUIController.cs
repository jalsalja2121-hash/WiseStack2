using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using ARLogistics.AR;
using ARLogistics.Managers;
using ARLogistics.Detection;
using System.Text;

namespace ARLogistics.UI
{
    /// <summary>
    /// UI 갱신 컨트롤러.
    ///
    /// 연결 구조:
    ///   YoloDetector.OnDetectionResultsUpdated → DetectionText (AR 평면 없이도 실시간 탐지 표시)
    ///   ARDetectionBridge.OnObjectsLocated     → DetectionText (3D 위치 확정 시 추가 표시)
    ///   WarehouseManager.OnReportReady         → ReportPanel + ReportText
    ///   WarehouseManager.OnFinalReportReady    → Gemini 가이드
    /// </summary>
    public class ARUIController : MonoBehaviour
    {
        // ─────────────────────────────────────────────
        // Inspector 참조
        // ─────────────────────────────────────────────

        [Header("Text Elements")]
        [SerializeField] private Text detectionText;
        [SerializeField] private Text reportText;

        [Header("Panels")]
        [SerializeField] private GameObject reportPanel;

        [Header("Display Settings")]
        [Tooltip("탐지 텍스트 자동 소멸 시간 (초). 0 = 유지")]
        [SerializeField] private float detectionTextTimeout = 3f;

        [Tooltip("Gemini 응답 대기 중 표시 메시지")]
        [SerializeField] private string waitingMessage = "⏳ Gemini 분석 중...";

        // ─────────────────────────────────────────────
        // 내부 상태
        // ─────────────────────────────────────────────

        private ARDetectionBridge  _bridge;
        private WarehouseManager   _warehouseMgr;
        private YoloDetector       _yoloDetector;
        // float.MaxValue = 영구 표시. 탐지 이벤트만 timeout 사용.
        private float              _detectionTextExpiry = float.MaxValue;
        private bool               _arReady;

        // ─────────────────────────────────────────────
        // 생명주기
        // ─────────────────────────────────────────────

        private void Awake()
        {
            _bridge       = FindFirstObjectByType<ARDetectionBridge>();
            _warehouseMgr = WarehouseManager.Instance
                            ?? FindFirstObjectByType<WarehouseManager>();
            _yoloDetector = FindFirstObjectByType<YoloDetector>();

            if (_bridge == null)
                Debug.LogWarning("[ARUIController] ARDetectionBridge 없음 (3D 위치 비활성)");
            if (_warehouseMgr == null)
                Debug.LogError("[ARUIController] WarehouseManager를 찾을 수 없습니다!");
            if (_yoloDetector == null)
                Debug.LogError("[ARUIController] YoloDetector를 찾을 수 없습니다!");

            // Canvas 참조 자동 탐색
            if (detectionText == null)
                detectionText = GameObject.Find("DetectionText")?.GetComponent<Text>();
            if (reportText == null)
                reportText    = GameObject.Find("ReportText")?.GetComponent<Text>();
            if (reportPanel == null)
                reportPanel   = GameObject.Find("ReportPanel");

            if (reportPanel != null) reportPanel.SetActive(false);
            if (detectionText != null) detectionText.text = "📷 AR 카메라 초기화 중...";
            if (reportText != null)    reportText.text    = "";
        }

        private void OnEnable()
        {
            // ── YoloDetector 직접 연결 (AR 평면 없이도 탐지 표시) ──
            if (_yoloDetector != null)
                _yoloDetector.OnDetectionResultsUpdated += HandleYoloDetections;

            // ── 3D 위치 확정 이벤트 ──
            if (_bridge != null)
                _bridge.OnObjectsLocated += HandleObjectsLocated;

            if (_warehouseMgr != null)
            {
                _warehouseMgr.OnReportReady      += HandleReportReady;
                _warehouseMgr.OnFinalReportReady += HandleFinalReport;
            }

            ARSession.stateChanged += OnARSessionStateChanged;
        }

        private void OnDisable()
        {
            if (_yoloDetector != null)
                _yoloDetector.OnDetectionResultsUpdated -= HandleYoloDetections;

            if (_bridge != null)
                _bridge.OnObjectsLocated -= HandleObjectsLocated;

            if (_warehouseMgr != null)
            {
                _warehouseMgr.OnReportReady      -= HandleReportReady;
                _warehouseMgr.OnFinalReportReady -= HandleFinalReport;
            }

            ARSession.stateChanged -= OnARSessionStateChanged;
        }

        // ─────────────────────────────────────────────
        // ARSession 상태 표시
        // ─────────────────────────────────────────────

        private void OnARSessionStateChanged(ARSessionStateChangedEventArgs args)
        {
            if (detectionText == null) return;

            switch (args.state)
            {
                case ARSessionState.Unsupported:
                    detectionText.text = "⚠️ ARCore 미지원 기기";
                    _detectionTextExpiry = float.MaxValue;
                    break;
                case ARSessionState.CheckingAvailability:
                    detectionText.text = "🔍 ARCore 확인 중...";
                    _detectionTextExpiry = float.MaxValue;
                    break;
                case ARSessionState.Installing:
                    detectionText.text = "⬇️ ARCore 설치 중...";
                    _detectionTextExpiry = float.MaxValue;
                    break;
                case ARSessionState.Ready:
                case ARSessionState.SessionInitializing:
                    detectionText.text = "📷 AR 카메라 시작 중...";
                    _detectionTextExpiry = float.MaxValue;
                    break;
                case ARSessionState.SessionTracking:
                    if (!_arReady)
                    {
                        _arReady = true;
                        detectionText.text = "✅ 카메라 준비 완료\n물류 물품을 카메라에 비춰주세요";
                        _detectionTextExpiry = Time.time + 5f;
                    }
                    break;
            }
        }

        private void Update()
        {
            if (detectionTextTimeout > 0 &&
                detectionText != null &&
                detectionText.text.Length > 0 &&
                Time.time > _detectionTextExpiry)
            {
                detectionText.text = "";
            }
        }

        // ─────────────────────────────────────────────
        // YOLO 직접 결과 (AR 평면 없이도 동작)
        // ─────────────────────────────────────────────

        /// <summary>
        /// YoloDetector에서 직접 받는 2D 탐지 결과.
        /// AR 평면이 없어도 카메라에 비치는 물체를 즉시 표시합니다.
        /// 클래스별 최고 신뢰도 1개만 표시 (중복 제거).
        /// </summary>
        private void HandleYoloDetections(List<DetectionResult> detections)
        {
            if (detectionText == null) return;
            if (detections == null || detections.Count == 0) return;

            // ── 클래스별 최고 신뢰도 1개만 추출 ──────────────────
            var bestPerClass = new System.Collections.Generic.Dictionary<int, DetectionResult>();
            foreach (var det in detections)
            {
                if (!bestPerClass.TryGetValue(det.classId, out var existing) ||
                    det.confidence > existing.confidence)
                {
                    bestPerClass[det.classId] = det;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"📦 탐지: {bestPerClass.Count}종");

            foreach (var kv in bestPerClass)
            {
                var det = kv.Value;
                string fragileIcon = "";
                var spec = ARLogistics.Data.ProductSpecTable.Get(det.classId);
                if (spec.IsFragile) fragileIcon = " ⚠️";

                // 클래스명에서 번호 접두어 제거 (예: "07_디지털/가전" → "디지털/가전")
                string displayName = det.className;
                if (displayName.Length > 3 && displayName[2] == '_')
                    displayName = displayName.Substring(3);

                sb.AppendLine($"  • {displayName}{fragileIcon}  {det.confidence * 100:F0}%");
            }

            detectionText.text = sb.ToString().TrimEnd();
            _detectionTextExpiry = Time.time + detectionTextTimeout;
        }

        // ─────────────────────────────────────────────
        // AR 3D 위치 확정 결과 (WarehouseManager용)
        // ─────────────────────────────────────────────

        /// <summary>AR 평면 레이캐스트 성공 시 — WarehouseManager가 이 이벤트 기반으로 계산</summary>
        private void HandleObjectsLocated(List<LocatedObject> objects)
        {
            // 3D 위치까지 확정된 결과는 WarehouseManager가 처리
            // UI는 이미 HandleYoloDetections에서 갱신됨 (중복 표시 방지)
        }

        // ─────────────────────────────────────────────
        // 창고 리포트 표시
        // ─────────────────────────────────────────────

        private void HandleReportReady(WarehouseReport report)
        {
            if (reportPanel != null) reportPanel.SetActive(true);
            if (reportText  == null) return;

            var sb = new StringBuilder();
            sb.AppendLine($"🏭 창고 {report.warehouseAreaM2}m²  천장 {report.ceilingHeightM}m");
            sb.AppendLine($"🪵 팔레트 {report.palletSpec.width}×{report.palletSpec.length}m  최대 {report.palletSpec.maxLoadKg}kg");
            sb.AppendLine();

            foreach (var p in report.products)
            {
                sb.AppendLine($"[{p.productName}]");
                sb.AppendLine($"  1단: {p.unitsPerLayer}개  ×  {p.maxLayers}단 = 총 {p.totalUnits}개");
                sb.AppendLine($"  무게: {p.totalWeightKg:F1}kg  높이: {p.stackHeightM:F2}m");
            }

            sb.AppendLine();
            sb.AppendLine(waitingMessage);

            reportText.text = sb.ToString();
        }

        private void HandleFinalReport(WarehouseReport report)
        {
            if (reportPanel != null) reportPanel.SetActive(true);
            if (reportText  == null) return;

            var sb = new StringBuilder();
            sb.AppendLine($"🏭 창고 {report.warehouseAreaM2}m²  천장 {report.ceilingHeightM}m");
            sb.AppendLine();

            foreach (var p in report.products)
            {
                sb.AppendLine($"[{p.productName}]");
                sb.AppendLine($"  1단 {p.unitsPerLayer}개 × {p.maxLayers}단 = {p.totalUnits}개");
                sb.AppendLine($"  {p.totalWeightKg:F1}kg  /  {p.stackHeightM:F2}m");
            }

            if (!string.IsNullOrEmpty(report.geminiGuidance))
            {
                sb.AppendLine();
                sb.AppendLine("─────────────────────");
                sb.AppendLine("🤖 Gemini 적재 가이드");
                sb.AppendLine("─────────────────────");
                string guide = report.geminiGuidance;
                if (guide.Length > 600) guide = guide[..600] + "...";
                sb.AppendLine(guide);
            }

            reportText.text = sb.ToString();
        }

        // ─────────────────────────────────────────────
        // 공개 메서드
        // ─────────────────────────────────────────────

        public void CloseReportPanel()
        {
            if (reportPanel != null) reportPanel.SetActive(false);
            if (reportText  != null) reportText.text = "";
        }

        public void ClearAll()
        {
            CloseReportPanel();
            if (detectionText != null) detectionText.text = "";
        }
    }
}
