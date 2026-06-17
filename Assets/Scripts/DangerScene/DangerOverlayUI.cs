using UnityEngine;
using UnityEngine.UI;

namespace ARLogistics.DangerScene
{
    public class DangerOverlayUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Image  overlayPanel;
        [SerializeField] private Text   dangerLabel;
        [SerializeField] private Text   detailText;
        [SerializeField] private Text   geminiAdviceText;

        [Header("Colors")]
        [SerializeField] private Color safeColor    = new Color(0.10f, 0.85f, 0.20f, 0.80f);
        [SerializeField] private Color warningColor = new Color(1.00f, 0.82f, 0.00f, 0.80f);
        [SerializeField] private Color dangerColor  = new Color(1.00f, 0.15f, 0.10f, 0.80f);

        public void UpdateDisplay(DangerLevel level, int boxCount, float heightM, float weightKg)
        {
            ApplyLevel(level);
            if (detailText != null)
                detailText.text = $"박스: {boxCount}개  |  높이: {heightM:F1}m  |  무게: {weightKg:F0}kg";
            if (geminiAdviceText != null)
                geminiAdviceText.text = "🤖 AI 분석 중...";
        }

        public void ForceDisplay(DangerLevel level, string message)
        {
            ApplyLevel(level);
            if (detailText       != null) detailText.text       = message;
            if (geminiAdviceText != null) geminiAdviceText.text = "";
        }

        // Gemini 응답 수신 시 호출
        public void ShowGeminiAdvice(string advice)
        {
            if (geminiAdviceText != null)
                geminiAdviceText.text = advice;
        }

        private void ApplyLevel(DangerLevel level)
        {
            Color color;
            string label;

            switch (level)
            {
                case DangerLevel.Safe:
                    label = "안전"; color = safeColor;    break;
                case DangerLevel.Warning:
                    label = "주의"; color = warningColor; break;
                default:
                    label = "위험"; color = dangerColor;  break;
            }

            if (overlayPanel != null) overlayPanel.color = color;
            if (dangerLabel  != null) dangerLabel.text   = label;
        }
    }
}
