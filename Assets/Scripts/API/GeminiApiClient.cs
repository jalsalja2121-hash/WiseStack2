using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ARLogistics.API
{
    /// <summary>
    /// Google Gemini REST API 클라이언트.
    /// WarehouseManager 로부터 공간 JSON 을 받아 적재 가이드 텍스트를 반환합니다.
    ///
    /// 엔드포인트: POST https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent
    /// </summary>
    public class GeminiApiClient : MonoBehaviour
    {
        // ─────────────────────────────────────────────
        // Inspector 설정
        // ─────────────────────────────────────────────

        [Header("Gemini API Settings")]
        [Tooltip("Google AI Studio 에서 발급한 API Key")]
        [SerializeField] private string apiKey = "";

        [Tooltip("사용할 Gemini 모델 ID")]
        [SerializeField] private string modelId = "gemini-2.0-flash";

        [Tooltip("응답 최대 토큰 수")]
        [SerializeField] private int maxOutputTokens = 512;

        [Tooltip("temperature (0=결정적, 1=창의적)")]
        [Range(0f, 1f)]
        [SerializeField] private float temperature = 0.3f;

        // ─────────────────────────────────────────────
        // 이벤트
        // ─────────────────────────────────────────────

        /// <summary>Gemini 응답이 성공적으로 도착했을 때 발행됩니다.</summary>
        public System.Action<string> OnGuidanceReceived;

        /// <summary>API 오류 발생 시 발행됩니다.</summary>
        public System.Action<string> OnError;

        // ─────────────────────────────────────────────
        // 내부 상태
        // ─────────────────────────────────────────────

        private string _endpoint;
        private bool   _isRequesting;

        // ─────────────────────────────────────────────
        // 생명주기
        // ─────────────────────────────────────────────

        private void Awake()
        {
            _endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{modelId}:generateContent?key={apiKey}";
        }

        // ─────────────────────────────────────────────
        // 공개 API
        // ─────────────────────────────────────────────

        /// <summary>
        /// 창고 공간 JSON 과 시스템 프롬프트를 결합하여 Gemini 에 적재 가이드를 요청합니다.
        /// </summary>
        /// <param name="warehouseJson">WarehouseManager 가 직렬화한 공간·탐지 JSON</param>
        public void RequestStackingGuidance(string warehouseJson)
        {
            if (_isRequesting)
            {
                Debug.LogWarning("[GeminiClient] 이전 요청이 진행 중입니다.");
                return;
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError("[GeminiClient] API Key 가 설정되지 않았습니다!");
                OnError?.Invoke("API Key missing");
                return;
            }

            StartCoroutine(PostRequest(warehouseJson));
        }

        // ─────────────────────────────────────────────
        // HTTP 요청
        // ─────────────────────────────────────────────

        private IEnumerator PostRequest(string warehouseJson)
        {
            _isRequesting = true;

            // ── 프롬프트 조합 ──
            string systemPrompt =
                "당신은 물류 창고 전문가입니다. " +
                "아래 JSON 은 창고 크기, 팔레트 규격, 감지된 물품들의 3D 위치와 물리 특성을 담고 있습니다. " +
                "팔레트 손상을 방지하고 적재 효율을 극대화하는 최적 적재 순서와 배치 방법을 " +
                "간결하고 실용적인 한국어 bullet-point 로 제공해 주세요.";

            string userContent = $"{systemPrompt}\n\n```json\n{warehouseJson}\n```";

            // ── JSON Body 조립 ──
            string body = BuildRequestBody(userContent);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

            using var req = new UnityWebRequest(_endpoint, "POST");
            req.uploadHandler   = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            Debug.Log("[GeminiClient] 요청 전송 중...");
            yield return req.SendWebRequest();

            _isRequesting = false;

            // ── 응답 처리 ──
            if (req.result != UnityWebRequest.Result.Success)
            {
                string errMsg = $"HTTP {req.responseCode}: {req.error}";
                Debug.LogError($"[GeminiClient] 오류: {errMsg}\n{req.downloadHandler.text}");
                OnError?.Invoke(errMsg);
                yield break;
            }

            string guidance = ParseResponse(req.downloadHandler.text);
            Debug.Log($"[GeminiClient] 가이드 수신 ({guidance.Length}자)");
            OnGuidanceReceived?.Invoke(guidance);
        }

        // ─────────────────────────────────────────────
        // JSON 조립 / 파싱 (JsonUtility 이용)
        // ─────────────────────────────────────────────

        private string BuildRequestBody(string userContent)
        {
            // JsonUtility 는 중첩 익명 타입을 지원하지 않으므로 직접 문자열 조립
            string safeContent = userContent
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "");

            return $@"{{
  ""contents"": [
    {{
      ""parts"": [
        {{
          ""text"": ""{safeContent}""
        }}
      ]
    }}
  ],
  ""generationConfig"": {{
    ""maxOutputTokens"": {maxOutputTokens},
    ""temperature"": {temperature.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}
  }}
}}";
        }

        private static string ParseResponse(string json)
        {
            // 간단한 텍스트 추출 ("text": "..." 패턴)
            const string marker = "\"text\": \"";
            int start = json.IndexOf(marker);
            if (start < 0) return json; // 파싱 실패 시 원본 반환

            start += marker.Length;
            int end = json.IndexOf("\"", start);
            if (end < 0) return json;

            // 이스케이프 시퀀스 복원
            return json.Substring(start, end - start)
                       .Replace("\\n", "\n")
                       .Replace("\\\"", "\"")
                       .Replace("\\\\", "\\");
        }
    }
}
