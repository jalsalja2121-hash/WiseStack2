using System.Collections;
using System.Collections.Generic;
using System.Text;
using ARLogistics.Data;
using ARLogistics.Detection;
using ARLogistics.UI;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace ARLogistics.Features
{
    /// <summary>
    /// MeasureScene-only controller for live classification and stacking analysis.
    /// </summary>
    public sealed class MeasureSceneController : MonoBehaviour
    {
        private const float PalletHeightM = 0.15f;
        private const float HeightSafetyMarginM = 0.3f;
        private const int PalletClassId = 10;

        private static readonly string[] CargoNames =
        {
            "가공식품",
            "신선식품",
            "일상용품",
            "의약품/의료기기",
            "교육/문화용품",
            "디지털/가전",
            "가구/인테리어",
            "의류",
            "전문스포츠/레저",
            "패션잡화",
            "팔레트"
        };

        [Header("UI References")]
        [SerializeField] private Text statusText;
        [SerializeField] private Text resultText;
        [SerializeField] private Button analyzeButton;
        [SerializeField] private Button clearButton;

        [Header("Gemini")]
        [SerializeField] private string geminiApiKey = "";
        [SerializeField] private string geminiModel = "gemini-3.5-flash";

        [Header("Detection")]
        [SerializeField] private float detectionInterval = 0.5f;

        private readonly List<DetectionResult> _lastDetections = new();
        private YoloDetector _yolo;
        private CameraFrameProvider _frameProvider;
        private YoloBoundingBoxOverlay _boundingBoxOverlay;
        private float _nextDetectionTime;
        private bool _analyzing;
        private bool _analysisComplete;

        private string _savedMeasurementSummary = "";
        private string _analysisSummary = "";

        private void Awake()
        {
            statusText ??= GameObject.Find("MS_StatusText")?.GetComponent<Text>();
            resultText ??= GameObject.Find("MS_ResultText")?.GetComponent<Text>();
            analyzeButton ??= GameObject.Find("MS_MeasureBtn")?.GetComponent<Button>();
            clearButton ??= GameObject.Find("MS_ClearBtn")?.GetComponent<Button>();
            _yolo = FindFirstObjectByType<YoloDetector>();
            _frameProvider = FindFirstObjectByType<CameraFrameProvider>();
            _boundingBoxOverlay = FindFirstObjectByType<YoloBoundingBoxOverlay>();
        }

        private void Start()
        {
            if (analyzeButton != null)
                analyzeButton.interactable = false;

            PositionActionBarAboveNavigation();
            SetClassification(_yolo != null
                ? "분류 결과\n카메라로 화물을 비춰주세요"
                : "분류 결과\nYoloDetector를 찾을 수 없습니다");
            SetAnalysis("분석 결과\n화물을 인식한 뒤 '분류하기'를 누르세요");
        }

        private void OnRectTransformDimensionsChange()
        {
            if (isActiveAndEnabled)
                PositionActionBarAboveNavigation();
        }

        private void OnEnable()
        {
            if (_yolo != null)
                _yolo.OnDetectionResultsUpdated += HandleDetections;

            analyzeButton?.onClick.AddListener(StartAnalysis);
            clearButton?.onClick.AddListener(ClearAll);
        }

        private void OnDisable()
        {
            if (_yolo != null)
                _yolo.OnDetectionResultsUpdated -= HandleDetections;

            analyzeButton?.onClick.RemoveListener(StartAnalysis);
            clearButton?.onClick.RemoveListener(ClearAll);
        }

        private void HandleDetections(List<DetectionResult> detections)
        {
            if (_analyzing || _analysisComplete)
                return;

            if (Time.time < _nextDetectionTime)
                return;

            _nextDetectionTime = Time.time + detectionInterval;
            if (detections == null || detections.Count == 0)
                return;

            var bestPerClass = new Dictionary<int, DetectionResult>();
            foreach (var detection in detections)
            {
                if (!bestPerClass.TryGetValue(detection.classId, out var existing) ||
                    detection.confidence > existing.confidence)
                {
                    bestPerClass[detection.classId] = detection;
                }
            }

            _lastDetections.Clear();
            _lastDetections.AddRange(bestPerClass.Values);
            _lastDetections.Sort((left, right) => left.classId.CompareTo(right.classId));

            var text = new StringBuilder();
            text.AppendLine($"분류 결과 · {_lastDetections.Count}종");
            foreach (var detection in _lastDetections)
            {
                string cargoName = ResolveCargoName(detection.classId);
                string fragile = IsFragile(detection.classId) ? " · 파손주의" : "";
                text.AppendLine($"• {cargoName}{fragile}  {detection.confidence * 100f:F0}%");
            }
            text.Append("'분류하기'를 누르면 적재 가능 수량을 계산합니다");

            SetClassification(text.ToString());
            if (analyzeButton != null)
                analyzeButton.interactable = !_analyzing;
        }

        private void StartAnalysis()
        {
            if (_lastDetections.Count == 0)
            {
                SetAnalysis("분석 결과\n먼저 카메라로 화물을 인식해 주세요");
                return;
            }

            if (!_analyzing)
            {
                _analysisComplete = true;
                SetDetectionEnabled(false);
                StartCoroutine(AnalysisCoroutine());
            }
        }

        private IEnumerator AnalysisCoroutine()
        {
            _analyzing = true;
            if (analyzeButton != null)
                analyzeButton.interactable = false;

            float warehouseArea = ARLogistics.AppSettings.WarehouseAreaM2;
            float ceilingHeight = ARLogistics.AppSettings.CeilingHeightM;
            float palletWidth = ARLogistics.AppSettings.PalletWidth;
            float palletLength = ARLogistics.AppSettings.PalletLength;
            float palletMaxLoad = ARLogistics.AppSettings.PalletMaxLoadKg;
            float palletArea = palletWidth * palletLength;
            int palletCount = palletArea > 0.01f
                ? Mathf.Max(0, Mathf.FloorToInt(warehouseArea / palletArea))
                : 0;

            SaveBestBoxMeasurement();

            var result = new StringBuilder();
            var geminiLines = new List<string>();

            if (!string.IsNullOrEmpty(_savedMeasurementSummary))
                result.AppendLine(_savedMeasurementSummary);
            result.AppendLine("분석 결과");
            result.AppendLine($"창고 면적 {warehouseArea:F0}m² · 적재 가능 높이 {ceilingHeight:F2}m");
            result.AppendLine($"팔레트 {palletWidth:F2}m × {palletLength:F2}m · 배치 가능 {palletCount}개");
            result.AppendLine();

            foreach (var detection in _lastDetections)
            {
                string cargoName = ResolveCargoName(detection.classId);
                GetCargoSpec(detection.classId, out float cargoWidth, out float cargoLength,
                    out float cargoHeight, out float cargoWeight, out bool fragile);

                CalculateHorizontalFit(
                    palletWidth,
                    palletLength,
                    cargoWidth,
                    cargoLength,
                    out int acrossWidth,
                    out int acrossLength);

                int perLayer = acrossWidth * acrossLength;
                float usableHeight = Mathf.Max(0f, ceilingHeight - PalletHeightM - HeightSafetyMarginM);
                int layersByHeight = cargoHeight > 0.01f
                    ? Mathf.Max(0, Mathf.FloorToInt(usableHeight / cargoHeight))
                    : 0;
                int layersByWeight = cargoWeight > 0.01f && perLayer > 0
                    ? Mathf.Max(0, Mathf.FloorToInt(palletMaxLoad / (cargoWeight * perLayer)))
                    : layersByHeight;
                int layers = detection.classId == PalletClassId
                    ? (perLayer > 0 ? 1 : 0)
                    : Mathf.Min(layersByHeight, layersByWeight);
                int perPallet = perLayer * layers;
                int warehouseTotal = perPallet * palletCount;

                result.AppendLine($"[{cargoName}{(fragile ? " · 파손주의" : "")}]");
                result.AppendLine($"화물 크기 {cargoWidth:F2}m × {cargoLength:F2}m × {cargoHeight:F2}m");
                result.AppendLine($"가로 {acrossWidth}개 × 세로 {acrossLength}개 × {layers}층 = 총 {perPallet}개");
                result.AppendLine($"창고 전체 적재 가능 수량: {warehouseTotal}개");
                result.AppendLine();

                geminiLines.Add(
                    $"{cargoName}: {cargoWidth:F2}m×{cargoLength:F2}m×{cargoHeight:F2}m, " +
                    $"가로 {acrossWidth}개×세로 {acrossLength}개×{layers}층={perPallet}개/팔레트, " +
                    $"창고 전체 {warehouseTotal}개" +
                    (fragile ? " (파손주의)" : ""));
            }

            _analysisSummary = result.ToString().TrimEnd();
            SetAnalysis(_analysisSummary + "\n\n계산 완료 · Gemini 가이드 요청 중...");

            if (!string.IsNullOrEmpty(geminiApiKey))
                yield return CallGemini(geminiLines, warehouseArea, ceilingHeight, palletCount);
            else
                SetAnalysis(_analysisSummary + "\n\n계산 완료");

            _analyzing = false;
            if (analyzeButton != null)
                analyzeButton.interactable = false;
        }

        private IEnumerator CallGemini(
            List<string> productLines,
            float warehouseArea,
            float ceilingHeight,
            int palletCount)
        {
            string prompt =
                $"물류 창고 적재 분석 결과입니다.\n" +
                $"창고 면적: {warehouseArea}m², 적재 가능 높이: {ceilingHeight}m, 팔레트 배치 수: {palletCount}개\n\n" +
                $"탐지된 화물 및 계산 결과:\n{string.Join("\n", productLines)}\n\n" +
                "위 적재 조건에서 최적 적재 방법, 주의사항, 안전 팁을 한국어 3문장 이내로 요약해 주세요.";

            string body = "{\"contents\":[{\"parts\":[{\"text\":\"" +
                          EscapeJson(prompt) + "\"}]}]}";
            string url = "https://generativelanguage.googleapis.com/v1beta/models/" +
                         geminiModel + ":generateContent?key=" + geminiApiKey;

            using var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                SetAnalysis(_analysisSummary + $"\n\nGemini 오류: {request.error}");
                yield break;
            }

            string guidance = ParseGeminiText(request.downloadHandler.text);
            SetAnalysis(string.IsNullOrEmpty(guidance)
                ? _analysisSummary + "\n\n분석 완료"
                : _analysisSummary + "\n\n적재 가이드\n" + guidance);
        }

        private void ClearAll()
        {
            StopAllCoroutines();
            _lastDetections.Clear();

            _savedMeasurementSummary = "";
            _analysisSummary = "";
            _analyzing = false;
            _analysisComplete = false;
            _nextDetectionTime = 0f;

            SetDetectionEnabled(true);
            _boundingBoxOverlay?.ClearBoxes();

            if (analyzeButton != null)
                analyzeButton.interactable = false;

            SetClassification("분류 결과\n카메라로 화물을 비춰주세요");
            SetAnalysis("분석 결과\n화물을 인식한 뒤 '분류하기'를 누르세요");
        }

        private void SetDetectionEnabled(bool enabled)
        {
            if (_frameProvider == null)
                _frameProvider = FindFirstObjectByType<CameraFrameProvider>();

            _frameProvider?.SetInferenceEnabled(enabled);
        }

        private void PositionActionBarAboveNavigation()
        {
            var actionBar = GameObject.Find("BottomBar")?.GetComponent<RectTransform>();
            var navBar = GameObject.Find("NavBar")?.GetComponent<RectTransform>();
            var canvas = GetComponentInParent<Canvas>();
            if (actionBar == null || navBar == null || canvas == null)
                return;

            Canvas.ForceUpdateCanvases();
            float safeBottom = Screen.safeArea.yMin / Mathf.Max(0.01f, canvas.scaleFactor);
            float navHeight = Mathf.Max(140f, navBar.rect.height);

            actionBar.anchorMin = new Vector2(0f, 0f);
            actionBar.anchorMax = new Vector2(1f, 0f);
            actionBar.pivot = new Vector2(0.5f, 0f);
            actionBar.anchoredPosition = new Vector2(0f, navHeight + safeBottom + 12f);
            actionBar.sizeDelta = new Vector2(0f, 180f);
            actionBar.SetAsLastSibling();
        }

        private static void CalculateHorizontalFit(
            float palletWidth,
            float palletLength,
            float cargoWidth,
            float cargoLength,
            out int acrossWidth,
            out int acrossLength)
        {
            int normalWidth = FitCount(palletWidth, cargoWidth);
            int normalLength = FitCount(palletLength, cargoLength);
            int rotatedWidth = FitCount(palletWidth, cargoLength);
            int rotatedLength = FitCount(palletLength, cargoWidth);

            if (rotatedWidth * rotatedLength > normalWidth * normalLength)
            {
                acrossWidth = rotatedWidth;
                acrossLength = rotatedLength;
                return;
            }

            acrossWidth = normalWidth;
            acrossLength = normalLength;
        }

        private static int FitCount(float available, float required) =>
            available > 0f && required > 0.01f
                ? Mathf.Max(0, Mathf.FloorToInt(available / required))
                : 0;

        private static void GetCargoSpec(
            int classId,
            out float width,
            out float length,
            out float height,
            out float weight,
            out bool fragile)
        {
            if (classId == PalletClassId)
            {
                width = ARLogistics.AppSettings.PalletWidth;
                length = ARLogistics.AppSettings.PalletLength;
                height = PalletHeightM;
                weight = 0f;
                fragile = false;
                return;
            }

            var spec = ProductSpecTable.Get(classId);
            width = spec.WidthM;
            length = spec.LengthM;
            height = spec.HeightM;
            weight = spec.WeightKg;
            fragile = spec.IsFragile;
        }

        private static bool IsFragile(int classId) =>
            classId != PalletClassId && ProductSpecTable.Get(classId).IsFragile;

        private static string ResolveCargoName(int classId) =>
            classId >= 0 && classId < CargoNames.Length
                ? CargoNames[classId]
                : $"Unknown (ID: {classId})";

        private void SetClassification(string message)
        {
            if (statusText != null && statusText.text != message)
                statusText.text = message;
        }

        private void SetAnalysis(string message)
        {
            if (resultText != null && resultText.text != message)
                resultText.text = message;
        }

        private static string EscapeJson(string value) =>
            value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t");

        private static string ParseGeminiText(string responseJson)
        {
            const string key = "\"text\": \"";
            int start = responseJson.IndexOf(key, System.StringComparison.Ordinal);
            if (start < 0)
                return "";

            start += key.Length;
            int end = responseJson.IndexOf('"', start);
            if (end < 0)
                return "";

            return responseJson.Substring(start, end - start)
                .Replace("\\n", "\n").Replace("\\\"", "\"");
        }
    

        private void SaveBestBoxMeasurement()
        {
            DetectionResult? bestBox = null;
            foreach (var detection in _lastDetections)
            {
                if (detection.classId == PalletClassId)
                    continue;

                if (!bestBox.HasValue || detection.confidence > bestBox.Value.confidence)
                    bestBox = detection;
            }

            if (!bestBox.HasValue)
                return;

            DetectionResult selected = bestBox.Value;
            GetCargoSpec(selected.classId, out float width, out float depth,
                out float height, out _, out _);
            string cargoName = ResolveCargoName(selected.classId);

            _savedMeasurementSummary = $"상자 크기 저장 완료 · {width:F2}m × {depth:F2}m × {height:F2}m";
            BoxMeasurementStore.Save(width, depth, height, selected.classId, cargoName);
            Debug.Log($"[MeasureScene] 상자 크기 저장: {width:F3}m x {depth:F3}m x {height:F3}m ({cargoName})");
        }
}
}
