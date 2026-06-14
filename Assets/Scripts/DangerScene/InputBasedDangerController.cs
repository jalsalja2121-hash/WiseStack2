using System.Collections.Generic;
using UnityEngine;
using ARLogistics.Detection;
using ARLogistics.API;

namespace ARLogistics.DangerScene
{
    public class InputBasedDangerController : MonoBehaviour
    {
        [Header("YOLO")]
        [SerializeField] private YoloDetector     yoloDetector;

        [Header("References")]
        [SerializeField] private BoxStackAnalyzer analyzer;
        [SerializeField] private DangerOverlayUI  overlayUI;

        [Header("Gemini AI")]
        [SerializeField] private GeminiApiClient  geminiClient;

        private DangerLevel _lastLevel = (DangerLevel)(-1);

        private void Start()
        {
            if (yoloDetector == null)
                yoloDetector = FindFirstObjectByType<YoloDetector>();
            if (yoloDetector == null)
            {
                Debug.LogError("[DangerScene] YoloDetector를 찾을 수 없습니다!");
                return;
            }

            if (geminiClient == null)
                geminiClient = FindFirstObjectByType<GeminiApiClient>();

            if (geminiClient != null)
                geminiClient.OnGuidanceReceived += OnGeminiResponse;

            yoloDetector.OnDetectionResultsUpdated += OnDetectionsUpdated;
            overlayUI?.ForceDisplay(DangerLevel.Safe, "📷 카메라에 박스를 비춰주세요");
        }

        private void OnDestroy()
        {
            if (yoloDetector != null)
                yoloDetector.OnDetectionResultsUpdated -= OnDetectionsUpdated;
            if (geminiClient != null)
                geminiClient.OnGuidanceReceived -= OnGeminiResponse;
        }

        private void OnDetectionsUpdated(List<DetectionResult> detections)
        {
            if (analyzer == null || overlayUI == null) return;

            if (detections == null || detections.Count == 0)
            {
                overlayUI.ForceDisplay(DangerLevel.Safe, "📷 카메라에 박스를 비춰주세요");
                _lastLevel = (DangerLevel)(-1);
                return;
            }

            DangerLevel level = analyzer.Analyze(
                detections,
                out int   boxCount,
                out float heightM,
                out float weightKg);

            overlayUI.UpdateDisplay(level, boxCount, heightM, weightKg);

            // 위험도 등급이 바뀔 때만 Gemini 요청 (중복 호출 방지)
            if (level != _lastLevel)
            {
                _lastLevel = level;
                RequestGeminiAdvice(level, boxCount, heightM, weightKg);
            }
        }

        private void RequestGeminiAdvice(DangerLevel level, int boxCount, float heightM, float weightKg)
        {
            if (geminiClient == null) return;

            string levelStr = level switch
            {
                DangerLevel.Safe    => "안전",
                DangerLevel.Warning => "주의",
                _                   => "위험"
            };

            string json =
                $"{{" +
                $"\"danger_level\":\"{levelStr}\"," +
                $"\"box_count\":{boxCount}," +
                $"\"stack_height_m\":{heightM:F2}," +
                $"\"total_weight_kg\":{weightKg:F1}," +
                $"\"request\":\"이 적재 상황의 위험 원인과 즉각적인 안전 조치를 2~3줄 한국어로 제공해주세요.\"" +
                $"}}";

            geminiClient.RequestStackingGuidance(json);
        }

        private void OnGeminiResponse(string advice)
        {
            overlayUI?.ShowGeminiAdvice(advice);
        }
    }
}
