using System.Collections.Generic;
using UnityEngine;
using ARLogistics.Detection;

namespace ARLogistics.DangerScene
{
    public class InputBasedDangerController : MonoBehaviour
    {
        [Header("YOLO")]
        [SerializeField] private YoloDetector       yoloDetector;

        [Header("References")]
        [SerializeField] private BoxStackAnalyzer   analyzer;
        [SerializeField] private DangerOverlayUI    overlayUI;

        [Header("Gemini AI")]
        [SerializeField] private GeminiDangerClient geminiDangerClient;

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

            if (geminiDangerClient == null)
                geminiDangerClient = FindFirstObjectByType<GeminiDangerClient>();

            if (geminiDangerClient != null)
                geminiDangerClient.OnMessageReceived += OnGeminiResponse;

            yoloDetector.OnDetectionResultsUpdated += OnDetectionsUpdated;
            overlayUI?.ForceDisplay(DangerLevel.Safe, "📷 카메라에 박스를 비춰주세요");
        }

        private void OnDestroy()
        {
            if (yoloDetector != null)
                yoloDetector.OnDetectionResultsUpdated -= OnDetectionsUpdated;
            if (geminiDangerClient != null)
                geminiDangerClient.OnMessageReceived -= OnGeminiResponse;
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
                geminiDangerClient?.RequestDangerMessage(level, boxCount, heightM, weightKg);
            }
        }

        private void OnGeminiResponse(string advice)
        {
            overlayUI?.ShowGeminiAdvice(advice);
        }
    }
}
