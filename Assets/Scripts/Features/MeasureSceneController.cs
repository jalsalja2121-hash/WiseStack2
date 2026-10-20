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
        private const float PalletHeightM = StackingCalculator.PalletHeight;
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
        [SerializeField] private GameObject geminiMessagePanel;
        [SerializeField] private Text geminiMessageText;

        [Header("Gemini")]
        [SerializeField] private string geminiApiKey = "";
        [SerializeField] private string geminiModel = "gemini-3.8-flash";

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
        private RectTransform _resultsViewport;
        private RectTransform _resultsContent;
        private Text _detailsText;
        private Button _detailsButton;
        private Button _geminiErrorButton;
        private Text _geminiErrorText;

        private void Awake()
        {
            statusText ??= GameObject.Find("MS_StatusText")?.GetComponent<Text>();
            resultText ??= GameObject.Find("MS_ResultText")?.GetComponent<Text>();
            analyzeButton ??= GameObject.Find("MS_MeasureBtn")?.GetComponent<Button>();
            clearButton ??= GameObject.Find("MS_ClearBtn")?.GetComponent<Button>();
            EnsureGeminiMessagePanel();
            _yolo = FindFirstObjectByType<YoloDetector>();
            _frameProvider = FindFirstObjectByType<CameraFrameProvider>();
            _boundingBoxOverlay = FindFirstObjectByType<YoloBoundingBoxOverlay>();
        }

        private void Start()
        {
            if (analyzeButton != null)
                analyzeButton.interactable = false;

            PositionActionBarAboveNavigation();
            SetupResultsLayout();
            SetClassification(_yolo != null
                ? "분류 결과\n카메라로 화물을 비춰주세요"
                : "분류 결과\nYoloDetector를 찾을 수 없습니다");
            SetAnalysis("분석 결과\n화물을 인식한 뒤 '분류하기'를 누르세요");
            HideGeminiMessage();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (isActiveAndEnabled)
            {
                PositionActionBarAboveNavigation();
                PositionResultsLayout();
            }
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

            var details = new StringBuilder();
            if (!string.IsNullOrEmpty(_savedMeasurementSummary))
                details.AppendLine(_savedMeasurementSummary);
            details.AppendLine($"창고 면적 {warehouseArea:F0}m² · 창고 높이 {ceilingHeight:F2}m");
            details.AppendLine("시뮬레이션은 팔레트 포함 1.8m 이내 · 포장 강도·결속 별도 확인");
            details.AppendLine($"팔레트 {palletWidth:F2}m × {palletLength:F2}m · 배치 가능 {palletCount}개");

            foreach (var detection in _lastDetections)
            {
                string cargoName = ResolveCargoName(detection.classId);
                GetCargoSpec(detection.classId, out float cargoWidth, out float cargoLength,
                    out float cargoHeight, out float cargoWeight, out bool fragile);

                var plan = StackingCalculator.Calculate(cargoWidth, cargoLength, cargoHeight,
                    cargoWeight, palletWidth, palletLength, ceilingHeight, palletMaxLoad,
                    isPallet: detection.classId == PalletClassId);
                int acrossWidth = plan.Columns, acrossLength = plan.Rows;
                int layers = plan.Layers, perPallet = plan.Total;
                long warehouseTotal = (long)perPallet * palletCount;

                result.AppendLine($"인식 결과: {cargoName}{(fragile ? " · 파손주의" : "")}");
                result.AppendLine($"신뢰도: {detection.confidence * 100f:F0}%");
                result.AppendLine($"등록된 대표 규격: {cargoWidth * 100f:F0} × {cargoLength * 100f:F0} × {cargoHeight * 100f:F0}cm");
                result.AppendLine($"팔레트당 적재량: {perPallet:N0}개");
                result.AppendLine($"최대 적재 층수: {layers}층");
                result.AppendLine();
                details.AppendLine($"{cargoName}: 1층당 {plan.PerLayer}개 · {plan.Message}");
                details.AppendLine($"창고 전체 예상 적재 가능 수량: {warehouseTotal:N0}개");

                geminiLines.Add(
                    $"{cargoName}: {cargoWidth:F2}m×{cargoLength:F2}m×{cargoHeight:F2}m, " +
                    $"가로 {acrossWidth}개×세로 {acrossLength}개×{layers}층={perPallet}개/팔레트, " +
                    $"창고 전체 {warehouseTotal}개" +
                    (fragile ? " (파손주의)" : ""));
            }

            _analysisSummary = result.ToString().TrimEnd();
            SetClassification("분류 완료 · 계산 결과를 확인하세요");
            SetAnalysis(_analysisSummary);
            SetDetails(details.ToString().TrimEnd());

            string apiKey = GetGeminiApiKey();
            if (!string.IsNullOrEmpty(apiKey))
                yield return CallGemini(geminiLines, warehouseArea, ceilingHeight, palletCount, apiKey);
            else
            {
                HideGeminiMessage();
                SetAnalysis(_analysisSummary);
            }

            _analyzing = false;
            if (analyzeButton != null)
                analyzeButton.interactable = false;
        }

        private string GetGeminiApiKey()
        {
            if (!string.IsNullOrWhiteSpace(geminiApiKey))
                return geminiApiKey.Trim();

            // Kept out of Git; Resources includes the local key in an Android build.
            TextAsset localKey = Resources.Load<TextAsset>("GeminiLocalKey");
            return localKey != null ? localKey.text.Trim() : "";
        }

        private IEnumerator CallGemini(
            List<string> productLines,
            float warehouseArea,
            float ceilingHeight,
            int palletCount,
            string apiKey)
        {
            ShowGeminiMessage("Gemini가 적재 가이드를 분석하고 있습니다...");

            string prompt =
                $"물류 창고 적재 분석 결과입니다.\n" +
                $"창고 면적: {warehouseArea}m², 창고 높이: {ceilingHeight}m, 팔레트 배치 수: {palletCount}개\n" +
                "앱 시뮬레이션 상한은 팔레트 포함 1.8m입니다. 천장 아래 30cm 여유와 하중 제한도 적용합니다.\n" +
                "이 수량은 포장 압축강도와 결속을 검증하지 않은 예상치입니다. 안전을 보장하거나 계산 단수를 초과하도록 권하지 마세요.\n\n" +
                $"탐지된 화물 및 계산 결과:\n{string.Join("\n", productLines)}\n\n" +
                "위 적재 조건에서 최적 적재 방법, 주의사항, 안전 팁을 한국어 3문장 이내로 요약해 주세요.";

            string body = "{\"contents\":[{\"parts\":[{\"text\":\"" +
                          EscapeJson(prompt) + "\"}]}]}";
            string url = "https://generativelanguage.googleapis.com/v1beta/models/" +
                         geminiModel + ":generateContent?key=" + apiKey;

            using var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string errorMessage = request.error ?? "";
                string responseBody = request.downloadHandler?.text ?? "";
                if (!string.IsNullOrEmpty(apiKey))
                {
                    errorMessage = errorMessage.Replace(apiKey, "[REDACTED]");
                    responseBody = responseBody.Replace(apiKey, "[REDACTED]");
                }

                string errorDetails = $"HTTP {request.responseCode}\n{errorMessage}\n{responseBody}";
                Debug.LogError($"[MeasureScene] Gemini request failed: {errorDetails}");
                SetAnalysis(_analysisSummary);
                ShowGeminiMessage(request.responseCode == 401
                    ? "Gemini 인증 실패(401). AI 오류 상세에서 Google 응답을 확인해 주세요."
                    : $"Gemini 오류\n{errorMessage}");
                SetGeminiErrorDetails(errorDetails);
                yield break;
            }

            string guidance = ParseGeminiText(request.downloadHandler.text);
            SetAnalysis(_analysisSummary);
            ShowGeminiMessage(string.IsNullOrEmpty(guidance)
                ? "Gemini가 별도 메시지를 반환하지 않았습니다."
                : guidance);
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
            HideGeminiMessage();
            SetDetails("");

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

        private void EnsureGeminiMessagePanel()
        {
            if (geminiMessagePanel == null)
                geminiMessagePanel = GameObject.Find("GeminiMessagePanel");
            if (geminiMessageText == null)
                geminiMessageText = GameObject.Find("GeminiMessageText")?.GetComponent<Text>();

            if (geminiMessagePanel != null && geminiMessageText != null)
                return;

            geminiMessagePanel = new GameObject(
                "GeminiMessagePanel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            geminiMessagePanel.transform.SetParent(transform, false);

            RectTransform panelRect = geminiMessagePanel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.06f, 0.28f);
            panelRect.anchorMax = new Vector2(0.94f, 0.53f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            Image panelImage = geminiMessagePanel.GetComponent<Image>();
            panelImage.color = new Color(0.94f, 0.97f, 1f, 1f);
            panelImage.raycastTarget = false;

            CanvasGroup canvasGroup = geminiMessagePanel.GetComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            var textObject = new GameObject(
                "GeminiMessageText", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(geminiMessagePanel.transform, false);

            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(28f, 22f);
            textRect.offsetMax = new Vector2(-28f, -22f);

            geminiMessageText = textObject.GetComponent<Text>();
            geminiMessageText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            geminiMessageText.fontSize = 34;
            geminiMessageText.color = new Color(0.075f, 0.14f, 0.23f, 1f);
            geminiMessageText.alignment = TextAnchor.UpperLeft;
            geminiMessageText.horizontalOverflow = HorizontalWrapMode.Wrap;
            geminiMessageText.verticalOverflow = VerticalWrapMode.Truncate;
            geminiMessageText.raycastTarget = false;
        }

        private void ShowGeminiMessage(string message)
        {
            EnsureGeminiMessagePanel();
            SetGeminiErrorDetails("");
            if (geminiMessageText != null)
                geminiMessageText.text = "Gemini 분석 메시지\n\n" + message;
            if (geminiMessagePanel != null)
            {
                geminiMessagePanel.SetActive(true);
                UpdateResultHeight(geminiMessageText, 52f);
            }
        }

        private void HideGeminiMessage()
        {
            SetGeminiErrorDetails("");
            if (geminiMessageText != null)
                geminiMessageText.text = "";
            if (geminiMessagePanel != null)
                geminiMessagePanel.SetActive(false);
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

        private void SetupResultsLayout()
        {
            if (_resultsViewport != null || statusText == null || resultText == null)
                return;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            var viewport = new GameObject("MeasureResultsViewport", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            viewport.transform.SetParent(canvas.transform, false);
            _resultsViewport = viewport.GetComponent<RectTransform>();
            var background = viewport.GetComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.96f);
            var border = viewport.AddComponent<Outline>();
            border.effectColor = new Color(0.82f, 0.88f, 0.94f, 0.8f);
            border.effectDistance = new Vector2(1f, -1f);
            var content = new GameObject("MeasureResultsContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            _resultsContent = content.GetComponent<RectTransform>();
            _resultsContent.anchorMin = new Vector2(0f, 1f);
            _resultsContent.anchorMax = new Vector2(1f, 1f);
            _resultsContent.pivot = new Vector2(0.5f, 1f);
            _resultsContent.offsetMin = new Vector2(20f, 0f);
            _resultsContent.offsetMax = new Vector2(-20f, 0f);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 20, 20);
            layout.spacing = 16f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.GetComponent<ScrollRect>();
            scroll.content = _resultsContent;
            scroll.viewport = _resultsViewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 35f;

            PrepareResultText(statusText);
            PrepareResultText(resultText);
            var detailButtonObject = new GameObject("MeasureDetailsButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            detailButtonObject.transform.SetParent(_resultsContent, false);
            detailButtonObject.GetComponent<Image>().color = new Color(0.94f, 0.97f, 1f, 1f);
            detailButtonObject.GetComponent<LayoutElement>().preferredHeight = 52f;
            _detailsButton = detailButtonObject.GetComponent<Button>();
            var buttonLabel = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            buttonLabel.transform.SetParent(detailButtonObject.transform, false);
            buttonLabel.rectTransform.anchorMin = Vector2.zero;
            buttonLabel.rectTransform.anchorMax = Vector2.one;
            buttonLabel.rectTransform.offsetMin = buttonLabel.rectTransform.offsetMax = Vector2.zero;
            buttonLabel.font = statusText.font;
            buttonLabel.fontSize = 34;
            buttonLabel.color = new Color(0.09f, 0.39f, 0.78f, 1f);
            buttonLabel.alignment = TextAnchor.MiddleCenter;
            buttonLabel.text = "계산 근거 펼치기";
            _detailsButton.onClick.AddListener(() =>
            {
                _detailsText.gameObject.SetActive(!_detailsText.gameObject.activeSelf);
                buttonLabel.text = _detailsText.gameObject.activeSelf ? "계산 근거 접기" : "계산 근거 펼치기";
            });
            _detailsText = new GameObject("MeasureDetails", typeof(RectTransform), typeof(Text), typeof(LayoutElement)).GetComponent<Text>();
            _detailsText.transform.SetParent(_resultsContent, false);
            _detailsText.font = statusText.font;
            _detailsText.fontSize = 32;
            _detailsText.color = new Color(0.075f, 0.14f, 0.23f, 1f);
            _detailsText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _detailsText.verticalOverflow = VerticalWrapMode.Overflow;
            _detailsText.gameObject.SetActive(false);
            detailButtonObject.SetActive(false);
            if (geminiMessagePanel != null)
            {
                geminiMessagePanel.transform.SetParent(_resultsContent, false);
                var panel = geminiMessagePanel.GetComponent<RectTransform>();
                panel.localScale = Vector3.one;
                if (geminiMessageText != null)
                {
                    geminiMessageText.horizontalOverflow = HorizontalWrapMode.Wrap;
                    geminiMessageText.verticalOverflow = VerticalWrapMode.Overflow;
                }
                panel.gameObject.AddComponent<LayoutElement>().preferredHeight = 150f;
            }
            var errorButtonObject = new GameObject("GeminiErrorDetailsButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            errorButtonObject.transform.SetParent(_resultsContent, false);
            errorButtonObject.GetComponent<Image>().color = new Color(0.94f, 0.97f, 1f, 1f);
            errorButtonObject.GetComponent<LayoutElement>().preferredHeight = 48f;
            _geminiErrorButton = errorButtonObject.GetComponent<Button>();
            var errorLabel = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            errorLabel.transform.SetParent(errorButtonObject.transform, false);
            errorLabel.rectTransform.anchorMin = Vector2.zero;
            errorLabel.rectTransform.anchorMax = Vector2.one;
            errorLabel.rectTransform.offsetMin = errorLabel.rectTransform.offsetMax = Vector2.zero;
            errorLabel.font = statusText.font;
            errorLabel.fontSize = 32;
            errorLabel.color = new Color(0.09f, 0.39f, 0.78f, 1f);
            errorLabel.alignment = TextAnchor.MiddleCenter;
            errorLabel.text = "AI 오류 상세 펼치기";
            _geminiErrorText = new GameObject("GeminiErrorDetails", typeof(RectTransform), typeof(Text), typeof(LayoutElement)).GetComponent<Text>();
            _geminiErrorText.transform.SetParent(_resultsContent, false);
            _geminiErrorText.font = statusText.font;
            _geminiErrorText.fontSize = 32;
            _geminiErrorText.color = new Color(0.075f, 0.14f, 0.23f, 1f);
            _geminiErrorText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _geminiErrorText.verticalOverflow = VerticalWrapMode.Overflow;
            _geminiErrorButton.onClick.AddListener(() =>
            {
                _geminiErrorText.gameObject.SetActive(!_geminiErrorText.gameObject.activeSelf);
                errorLabel.text = _geminiErrorText.gameObject.activeSelf ? "AI 오류 상세 접기" : "AI 오류 상세 펼치기";
            });
            errorButtonObject.SetActive(false);
            _geminiErrorText.gameObject.SetActive(false);
            PositionResultsLayout();
            viewport.transform.SetAsLastSibling();
        }

        private void PrepareResultText(Text text)
        {
            text.transform.SetParent(_resultsContent, false);
            text.fontSize = 36;
            text.color = new Color(0.075f, 0.14f, 0.23f, 1f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.alignment = TextAnchor.UpperLeft;
            text.gameObject.AddComponent<LayoutElement>();
        }

        private void PositionResultsLayout()
        {
            if (_resultsViewport == null) return;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            float scale = Mathf.Max(0.01f, canvas.scaleFactor);
            float safeBottom = Screen.safeArea.yMin / scale;
            float navHeight = Mathf.Max(140f, GameObject.Find("NavBar")?.GetComponent<RectTransform>()?.rect.height ?? 140f);
            float lower = safeBottom + navHeight + 204f;
            float upper = Mathf.Max(lower + 120f, ((RectTransform)canvas.transform).rect.height * 0.58f);
            _resultsViewport.anchorMin = _resultsViewport.anchorMax = Vector2.zero;
            _resultsViewport.pivot = Vector2.zero;
            _resultsViewport.anchoredPosition = new Vector2(24f, lower);
            _resultsViewport.sizeDelta = new Vector2(((RectTransform)canvas.transform).rect.width - 48f, upper - lower);
        }

        private static void UpdateResultHeight(Text text, float extra)
        {
            if (text == null) return;
            var element = text.GetComponent<LayoutElement>() ?? text.GetComponentInParent<LayoutElement>();
            if (element != null)
                element.preferredHeight = Mathf.Max(80f, text.preferredHeight + extra);
        }

        private void SetDetails(string message)
        {
            if (_detailsText == null || _detailsButton == null) return;
            _detailsText.text = message;
            _detailsButton.gameObject.SetActive(!string.IsNullOrEmpty(message));
            _detailsText.gameObject.SetActive(false);
            UpdateResultHeight(_detailsText, 20f);
        }

        private void SetGeminiErrorDetails(string message)
        {
            if (_geminiErrorButton == null || _geminiErrorText == null) return;
            _geminiErrorText.text = message;
            _geminiErrorText.gameObject.SetActive(false);
            _geminiErrorButton.gameObject.SetActive(!string.IsNullOrEmpty(message));
            UpdateResultHeight(_geminiErrorText, 16f);
        }

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
            UpdateResultHeight(statusText, 16f);
        }

        private void SetAnalysis(string message)
        {
            if (resultText != null && resultText.text != message)
                resultText.text = message;
            UpdateResultHeight(resultText, 16f);
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
