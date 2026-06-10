using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using ARLogistics.Detection;
using ARLogistics.Data;

namespace ARLogistics.Features
{
    /// <summary>
    /// YOLO 화물 탐지 + Gemini 창고 적재 용량 분석
    /// - 카메라에 화물을 비추면 YOLO가 실시간 탐지
    /// - '분석 시작' 버튼 → ProductSpecTable로 층수/팔레트/상자 계산 → Gemini 가이드
    /// - AppSettings.WarehouseAreaM2, CeilingHeightM 기반 계산
    /// </summary>
    public class ARMeasurementController : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Text   statusText;
        [SerializeField] private Text   resultText;
        [SerializeField] private Button analyzeButton;
        [SerializeField] private Button clearButton;

        [Header("Gemini")]
        [SerializeField] private string geminiApiKey = "";
        [SerializeField] private string geminiModel  = "gemini-3.5-flash";

        [Header("Detection")]
        [SerializeField] private float detectionInterval = 0.5f;

        private YoloDetector             _yolo;
        private List<DetectionResult>    _lastDetections = new();
        private float                    _nextDetectionTime;
        private bool                     _analyzing;

        // ─────────────────────────────────────────────
        private void Awake()
        {
            if (statusText    == null) statusText    = GameObject.Find("MS_StatusText")?.GetComponent<Text>();
            if (resultText    == null) resultText    = GameObject.Find("MS_ResultText")?.GetComponent<Text>();
            if (analyzeButton == null) analyzeButton = GameObject.Find("MS_MeasureBtn")?.GetComponent<Button>();
            if (clearButton   == null) clearButton   = GameObject.Find("MS_ClearBtn")?.GetComponent<Button>();

            _yolo = FindFirstObjectByType<YoloDetector>();
        }

        private void Start()
        {
            analyzeButton?.onClick.AddListener(StartAnalysis);
            clearButton?.onClick.AddListener(ClearAll);

            if (analyzeButton != null) analyzeButton.interactable = false;

            SetStatus(_yolo != null
                ? "📷 카메라로 화물을 비춰주세요\n탐지되면 '분석 시작'을 누르세요"
                : "⚠️ YoloDetector를 찾을 수 없습니다");
        }

        private void OnEnable()
        {
            if (_yolo != null)
                _yolo.OnDetectionResultsUpdated += HandleDetections;
        }

        private void OnDisable()
        {
            if (_yolo != null)
                _yolo.OnDetectionResultsUpdated -= HandleDetections;
        }

        // ─────────────────────────────────────────────
        private void HandleDetections(List<DetectionResult> detections)
        {
            if (Time.time < _nextDetectionTime) return;
            _nextDetectionTime = Time.time + detectionInterval;

            if (detections == null || detections.Count == 0) return;

            // 클래스별 최고 신뢰도 1개만 추출
            var best = new Dictionary<int, DetectionResult>();
            foreach (var d in detections)
                if (!best.TryGetValue(d.classId, out var ex) || d.confidence > ex.confidence)
                    best[d.classId] = d;

            _lastDetections = new List<DetectionResult>(best.Values);

            var sb = new StringBuilder();
            sb.AppendLine($"📦 탐지: {_lastDetections.Count}종");
            foreach (var det in _lastDetections)
            {
                string name = det.className;
                if (name.Length > 3 && name[2] == '_') name = name.Substring(3);
                var spec = ProductSpecTable.Get(det.classId);
                string flag = spec.IsFragile ? " ⚠️" : "";
                sb.AppendLine($"  • {name}{flag}  {det.confidence * 100:F0}%");
            }
            sb.Append("'분석 시작'을 눌러 적재 분석을 시작하세요");
            SetStatus(sb.ToString());

            if (analyzeButton != null)
                analyzeButton.interactable = !_analyzing;
        }

        // ─────────────────────────────────────────────
        private void StartAnalysis()
        {
            if (_lastDetections.Count == 0) { SetStatus("화물을 먼저 카메라에 비춰주세요"); return; }
            if (_analyzing) return;
            StartCoroutine(AnalysisCoroutine());
        }

        private IEnumerator AnalysisCoroutine()
        {
            _analyzing = true;
            if (analyzeButton != null) analyzeButton.interactable = false;

            float area       = ARLogistics.AppSettings.WarehouseAreaM2;
            float ceiling    = ARLogistics.AppSettings.CeilingHeightM;
            float pW         = ARLogistics.AppSettings.PalletWidth;
            float pL         = ARLogistics.AppSettings.PalletLength;
            float pMax       = ARLogistics.AppSettings.PalletMaxLoadKg;
            float palletArea = pW * pL;
            int   palletCount = Mathf.Max(1, Mathf.FloorToInt(area / palletArea));

            var sb          = new StringBuilder();
            var geminiLines = new List<string>();

            sb.AppendLine($"🏭 창고 {area}m²  천장 {ceiling}m");
            sb.AppendLine($"🪵 팔레트 {pW}×{pL}m  최대 {pMax}kg");
            sb.AppendLine($"   배치 가능 팔레트 수: {palletCount}개");
            sb.AppendLine();

            foreach (var det in _lastDetections)
            {
                var spec = ProductSpecTable.Get(det.classId);
                string name = det.className;
                if (name.Length > 3 && name[2] == '_') name = name.Substring(3);

                // 팔레트 1단 상자 수
                int perL     = spec.LengthM > 0.01f ? Mathf.FloorToInt(pL / spec.LengthM) : 1;
                int perW     = spec.WidthM  > 0.01f ? Mathf.FloorToInt(pW / spec.WidthM)  : 1;
                int perLayer = Mathf.Max(1, perL * perW);

                // 높이 제한 층수 (팔레트 높이 0.15m + 안전 여유 0.3m 제외)
                float usableH  = ceiling - 0.15f - 0.3f;
                int maxLayers  = spec.HeightM > 0.01f
                    ? Mathf.Max(1, Mathf.FloorToInt(usableH / spec.HeightM))
                    : 1;

                // 무게 제한 층수
                if (spec.WeightKg > 0.01f)
                {
                    int byWeight = Mathf.Max(1, Mathf.FloorToInt(pMax / (spec.WeightKg * perLayer)));
                    maxLayers = Mathf.Min(maxLayers, byWeight);
                }

                int   boxesPerPallet = perLayer * maxLayers;
                int   totalBoxes     = boxesPerPallet * palletCount;
                float totalWeightPer = spec.WeightKg * boxesPerPallet;

                string fragileNote = spec.IsFragile ? " [파손주의]" : "";
                string line =
                    $"[{name}{fragileNote}]\n" +
                    $"  1단 {perLayer}개 × {maxLayers}단 = 팔레트당 {boxesPerPallet}개\n" +
                    $"  창고 전체 {totalBoxes}개  /  {totalWeightPer:F0}kg/팔레트";

                sb.AppendLine(line);
                geminiLines.Add(
                    $"{name}: 1단 {perLayer}개×{maxLayers}단={boxesPerPallet}개/팔레트, " +
                    $"전체 {totalBoxes}개, {totalWeightPer:F0}kg/팔레트" +
                    (spec.IsFragile ? " (파손주의)" : ""));
            }

            SetResult(sb.ToString());
            SetStatus("⏳ Gemini 분석 중...");

            if (!string.IsNullOrEmpty(geminiApiKey))
                yield return CallGemini(geminiLines, area, ceiling, palletCount);
            else
                SetStatus("✅ 계산 완료\n(Gemini 가이드: Inspector에서 API Key 설정 필요)");

            _analyzing = false;
            if (analyzeButton != null) analyzeButton.interactable = true;
        }

        private IEnumerator CallGemini(List<string> productLines, float area, float ceiling, int palletCount)
        {
            string prompt =
                $"물류 창고 적재 분석 결과입니다.\n" +
                $"창고 면적: {area}m², 천장 높이: {ceiling}m, 팔레트 배치 수: {palletCount}개\n\n" +
                $"탐지된 화물 및 계산 결과:\n{string.Join("\n", productLines)}\n\n" +
                "위 적재 조건에서 최적 적재 방법, 주의사항, 안전 팁을 한국어 3문장 이내로 요약해 주세요.";

            string body = "{\"contents\":[{\"parts\":[{\"text\":\"" +
                          EscapeJson(prompt) + "\"}]}]}";

            string url = "https://generativelanguage.googleapis.com/v1beta/models/" +
                         geminiModel + ":generateContent?key=" + geminiApiKey;

            using var req = new UnityWebRequest(url, "POST");
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                SetStatus($"❌ Gemini 오류: {req.error}");
                yield break;
            }

            string guidance = ParseGeminiText(req.downloadHandler.text);
            SetStatus(string.IsNullOrEmpty(guidance) ? "✅ 분석 완료" : "🤖 " + guidance);
        }

        // ─────────────────────────────────────────────
        private void ClearAll()
        {
            _lastDetections.Clear();
            if (resultText    != null) resultText.text = "";
            if (analyzeButton != null) analyzeButton.interactable = false;
            SetStatus("📷 카메라로 화물을 비춰주세요\n탐지되면 '분석 시작'을 누르세요");
        }

        private void SetStatus(string msg) { if (statusText != null) statusText.text = msg; }
        private void SetResult(string msg) { if (resultText != null) resultText.text = msg; }

        // ─────────────────────────────────────────────
        static string EscapeJson(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"")
             .Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t");

        static string ParseGeminiText(string responseJson)
        {
            const string key = "\"text\": \"";
            int start = responseJson.IndexOf(key);
            if (start < 0) return "";
            start += key.Length;
            int end = responseJson.IndexOf('"', start);
            if (end < 0) return "";
            return responseJson.Substring(start, end - start)
                .Replace("\\n", "\n").Replace("\\\"", "\"");
        }
    }
}
