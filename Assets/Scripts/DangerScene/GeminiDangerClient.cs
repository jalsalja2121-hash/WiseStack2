using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ARLogistics.DangerScene
{
    /// <summary>
    /// DangerScene 전용 Gemini 클라이언트.
    /// 위험도 분석에 맞춘 시스템 프롬프트를 직접 구성하여 호출합니다.
    /// GeminiApiClient(다른 씬용)를 수정하지 않고 독립 운용합니다.
    /// </summary>
    public class GeminiDangerClient : MonoBehaviour
    {
        [Header("Gemini API")]
        [SerializeField] private string apiKey = "";
        [SerializeField] private string modelId = "gemini-2.0-flash";
        [SerializeField] private int maxOutputTokens = 256;
        [Range(0f, 1f)]
        [SerializeField] private float temperature = 0.3f;

        public System.Action<string> OnMessageReceived;
        public System.Action<string> OnError;

        private string _endpoint;
        private bool _isRequesting;

        private void Awake()
        {
            _endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{modelId}:generateContent?key={apiKey}";
        }

        /// <summary>위험도 수치를 받아 Gemini에 안전 메시지를 요청합니다.</summary>
        public void RequestDangerMessage(DangerLevel level, int boxCount, float heightM, float weightKg)
        {
            if (_isRequesting)
            {
                Debug.LogWarning("[GeminiDangerClient] 이전 요청 진행 중.");
                return;
            }
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError("[GeminiDangerClient] API Key가 설정되지 않았습니다!");
                OnError?.Invoke("API Key missing");
                return;
            }
            StartCoroutine(PostRequest(level, boxCount, heightM, weightKg));
        }

        private IEnumerator PostRequest(DangerLevel level, int boxCount, float heightM, float weightKg)
        {
            _isRequesting = true;

            string levelStr = level switch
            {
                DangerLevel.Safe    => "안전",
                DangerLevel.Warning => "주의",
                _                   => "위험"
            };

            string prompt =
                $"물류 창고 박스 적재 위험도 분석 결과입니다.\\n" +
                $"- 위험 등급: {levelStr}\\n" +
                $"- 박스 개수: {boxCount}개\\n" +
                $"- 추정 적재 높이: {heightM:F1}m\\n" +
                $"- 추정 총무게: {weightKg:F0}kg\\n\\n" +
                $"이 상황의 주요 위험 요인과 즉각적인 안전 조치를 2~3줄 한국어로 간결하게 알려주세요.";

            string body = BuildBody(prompt);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

            using var req = new UnityWebRequest(_endpoint, "POST");
            req.uploadHandler   = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            Debug.Log("[GeminiDangerClient] 위험도 메시지 요청 중...");
            yield return req.SendWebRequest();

            _isRequesting = false;

            if (req.result != UnityWebRequest.Result.Success)
            {
                string err = $"HTTP {req.responseCode}: {req.error}";
                Debug.LogError($"[GeminiDangerClient] 오류: {err}");
                OnError?.Invoke(err);
                yield break;
            }

            string message = ParseResponse(req.downloadHandler.text);
            Debug.Log($"[GeminiDangerClient] 메시지 수신 ({message.Length}자)");
            OnMessageReceived?.Invoke(message);
        }

        private string BuildBody(string prompt)
        {
            return $@"{{
  ""contents"": [
    {{
      ""parts"": [
        {{
          ""text"": ""{prompt}""
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
            const string marker = "\"text\": \"";
            int start = json.IndexOf(marker);
            if (start < 0) return json;
            start += marker.Length;
            int end = json.IndexOf("\"", start);
            if (end < 0) return json;
            return json.Substring(start, end - start)
                       .Replace("\\n", "\n")
                       .Replace("\\\"", "\"")
                       .Replace("\\\\", "\\");
        }
    }
}
