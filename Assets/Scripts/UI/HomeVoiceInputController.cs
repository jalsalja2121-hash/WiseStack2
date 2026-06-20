using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace ARLogistics.UI
{
    /// <summary>
    /// Records a warehouse description and asks Gemini to transcribe it and
    /// extract the HomeScene settings as structured JSON.
    /// </summary>
    public sealed class HomeVoiceInputController : MonoBehaviour
    {
        private const int SampleRate = 16000;
        private const int MaxRecordingSeconds = 30;

        [Header("UI")]
        [SerializeField] private Button recordButton;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Text statusText;

        [Header("Gemini")]
        [SerializeField] private string geminiApiKey = "";
        [SerializeField] private string geminiModel = "gemini-3.5-flash";

        private InputField _warehouseAreaInput;
        private InputField _ceilingHeightInput;
        private InputField _palletWidthInput;
        private InputField _palletLengthInput;
        private InputField _palletMaxLoadInput;

        private AudioClip _recording;
        private string _deviceName;
        private bool _isRecording;
        private bool _isRequesting;

        private void Awake()
        {
            recordButton ??= GameObject.Find("VoiceRecordBtn")?.GetComponent<Button>();
            confirmButton ??= GameObject.Find("VoiceConfirmBtn")?.GetComponent<Button>();
            statusText ??= GameObject.Find("VoiceStatusText")?.GetComponent<Text>();

            _warehouseAreaInput = FindInput("AreaInput");
            _ceilingHeightInput = FindInput("CeilingInput");
            _palletWidthInput = FindInput("PalletWidthInput");
            _palletLengthInput = FindInput("PalletLengthInput");
            _palletMaxLoadInput = FindInput("MaxLoadInput");
        }

        private void OnEnable()
        {
            recordButton?.onClick.AddListener(OnRecordPressed);
            confirmButton?.onClick.AddListener(OnConfirmPressed);
        }

        private void Start()
        {
            if (confirmButton != null)
                confirmButton.interactable = false;

            SetStatus("창고 면적, 천장 높이, 팔레트 규격과 최대 하중을 말해 주세요.");
        }

        private void OnDisable()
        {
            recordButton?.onClick.RemoveListener(OnRecordPressed);
            confirmButton?.onClick.RemoveListener(OnConfirmPressed);

            if (_isRecording)
                StopMicrophone();
        }

        private void OnRecordPressed()
        {
            if (_isRecording || _isRequesting)
                return;

            StartCoroutine(RequestPermissionAndRecord());
        }

        private IEnumerator RequestPermissionAndRecord()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                    UnityEngine.Android.Permission.Microphone))
            {
                UnityEngine.Android.Permission.RequestUserPermission(
                    UnityEngine.Android.Permission.Microphone);
                SetStatus("마이크 권한을 허용해 주세요.");

                float timeout = Time.realtimeSinceStartup + 10f;
                while (Time.realtimeSinceStartup < timeout &&
                       !UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                           UnityEngine.Android.Permission.Microphone))
                {
                    yield return null;
                }

                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                        UnityEngine.Android.Permission.Microphone))
                {
                    SetStatus("마이크 권한이 필요합니다. 권한을 허용한 뒤 다시 눌러 주세요.");
                    yield break;
                }
            }
#else
            yield return null;
#endif

            if (Microphone.devices == null || Microphone.devices.Length == 0)
            {
                SetStatus("사용 가능한 마이크를 찾을 수 없습니다.");
                yield break;
            }

            _deviceName = Microphone.devices[0];
            _recording = Microphone.Start(_deviceName, false, MaxRecordingSeconds, SampleRate);
            if (_recording == null)
            {
                SetStatus("녹음을 시작할 수 없습니다.");
                yield break;
            }

            _isRecording = true;
            SetButtonText(recordButton, "녹음 중...");
            if (recordButton != null) recordButton.interactable = false;
            if (confirmButton != null) confirmButton.interactable = true;
            SetStatus("녹음 중입니다. 모두 말한 뒤 확인 버튼을 눌러 주세요.");
        }

        private void OnConfirmPressed()
        {
            if (!_isRecording || _isRequesting || _recording == null)
                return;

            int recordedSamples = Microphone.GetPosition(_deviceName);
            if (recordedSamples <= 0 && !Microphone.IsRecording(_deviceName))
                recordedSamples = _recording.samples;

            StopMicrophone();

            if (recordedSamples <= 0)
            {
                ResetControls("녹음된 음성이 없습니다. 다시 시도해 주세요.");
                return;
            }

            byte[] wavData = EncodeWav(_recording, recordedSamples);
            _recording = null;
            StartCoroutine(AnalyzeRecording(wavData));
        }

        private void StopMicrophone()
        {
            if (Microphone.IsRecording(_deviceName))
                Microphone.End(_deviceName);

            _isRecording = false;
        }

        private IEnumerator AnalyzeRecording(byte[] wavData)
        {
            if (string.IsNullOrWhiteSpace(geminiApiKey))
            {
                ResetControls("Gemini API Key가 설정되지 않았습니다.");
                yield break;
            }

            _isRequesting = true;
            if (confirmButton != null) confirmButton.interactable = false;
            SetStatus("음성을 전사하고 입력값을 분석하고 있습니다...");

            string prompt =
                "이 한국어 음성을 정확히 전사하고 물류 창고 설정값을 추출하세요. " +
                "단위는 창고 면적은 제곱미터(m2), 높이와 길이는 미터(m), 최대 하중은 kg로 변환하세요. " +
                "JSON 객체만 반환하고 모든 값은 문자열이어야 합니다. 말하지 않은 값은 빈 문자열로 반환하세요. " +
                "필드: transcript, warehouseAreaM2, ceilingHeightM, palletWidthM, palletLengthM, palletMaxLoadKg.";

            string audioBase64 = Convert.ToBase64String(wavData);
            string body =
                "{\"contents\":[{\"parts\":[" +
                "{\"text\":\"" + EscapeJson(prompt) + "\"}," +
                "{\"inline_data\":{\"mime_type\":\"audio/wav\",\"data\":\"" + audioBase64 + "\"}}" +
                "]}],\"generationConfig\":{\"temperature\":0.1,\"responseMimeType\":\"application/json\"}}";

            string url = "https://generativelanguage.googleapis.com/v1beta/models/" +
                         geminiModel + ":generateContent?key=" + geminiApiKey;

            using var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();
            _isRequesting = false;

            if (request.result != UnityWebRequest.Result.Success)
            {
                ResetControls($"음성 분석 실패: {request.error}");
                yield break;
            }

            if (!TryParseGeminiResult(request.downloadHandler.text, out GeminiVoiceResult result))
            {
                ResetControls("Gemini 응답에서 입력값을 읽지 못했습니다. 다시 말해 주세요.");
                yield break;
            }

            int appliedCount = ApplyResult(result);
            string transcript = string.IsNullOrWhiteSpace(result.transcript)
                ? "전사 내용 없음"
                : result.transcript.Trim();
            ResetControls(appliedCount > 0
                ? $"{appliedCount}개 항목을 자동 입력했습니다.\n인식: {transcript}"
                : $"입력 가능한 숫자를 찾지 못했습니다.\n인식: {transcript}");
        }

        private int ApplyResult(GeminiVoiceResult result)
        {
            int count = 0;
            count += ApplyValue(result.warehouseAreaM2, _warehouseAreaInput,
                value => AppSettings.WarehouseAreaM2 = value, "F0");
            count += ApplyValue(result.ceilingHeightM, _ceilingHeightInput,
                value => AppSettings.CeilingHeightM = value, "F2");
            count += ApplyValue(result.palletWidthM, _palletWidthInput,
                value => AppSettings.PalletWidth = value, "F2");
            count += ApplyValue(result.palletLengthM, _palletLengthInput,
                value => AppSettings.PalletLength = value, "F2");
            count += ApplyValue(result.palletMaxLoadKg, _palletMaxLoadInput,
                value => AppSettings.PalletMaxLoadKg = value, "F0");
            return count;
        }

        private static int ApplyValue(
            string rawValue,
            InputField input,
            Action<float> setter,
            string format)
        {
            if (input == null || string.IsNullOrWhiteSpace(rawValue))
                return 0;

            string normalized = rawValue.Trim().Replace(",", ".");
            if (!float.TryParse(normalized, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float value) || value <= 0f)
            {
                return 0;
            }

            input.text = value.ToString(format, CultureInfo.InvariantCulture);
            setter(value);
            return 1;
        }

        private static bool TryParseGeminiResult(string responseJson, out GeminiVoiceResult result)
        {
            result = null;

            GeminiResponse response;
            try
            {
                response = JsonUtility.FromJson<GeminiResponse>(responseJson);
            }
            catch
            {
                return false;
            }

            if (response?.candidates == null || response.candidates.Length == 0 ||
                response.candidates[0].content?.parts == null ||
                response.candidates[0].content.parts.Length == 0)
            {
                return false;
            }

            string json = response.candidates[0].content.parts[0].text?.Trim();
            if (string.IsNullOrEmpty(json))
                return false;

            int start = json.IndexOf('{');
            int end = json.LastIndexOf('}');
            if (start < 0 || end <= start)
                return false;

            try
            {
                result = JsonUtility.FromJson<GeminiVoiceResult>(
                    json.Substring(start, end - start + 1));
                return result != null;
            }
            catch
            {
                return false;
            }
        }

        private void ResetControls(string message)
        {
            _isRecording = false;
            _isRequesting = false;
            if (recordButton != null) recordButton.interactable = true;
            if (confirmButton != null) confirmButton.interactable = false;
            SetButtonText(recordButton, "음성 인식 시작");
            SetStatus(message);
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }

        private static InputField FindInput(string name) =>
            GameObject.Find(name)?.GetComponent<InputField>();

        private static void SetButtonText(Button button, string label)
        {
            if (button == null) return;
            Text text = button.GetComponentInChildren<Text>(true);
            if (text != null) text.text = label;
        }

        private static byte[] EncodeWav(AudioClip clip, int sampleFrames)
        {
            int frames = Mathf.Clamp(sampleFrames, 1, clip.samples);
            int channels = clip.channels;
            var samples = new float[frames * channels];
            clip.GetData(samples, 0);

            using var stream = new MemoryStream(44 + samples.Length * 2);
            using var writer = new BinaryWriter(stream);

            int dataLength = samples.Length * 2;
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(clip.frequency);
            writer.Write(clip.frequency * channels * 2);
            writer.Write((short)(channels * 2));
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);

            foreach (float sample in samples)
            {
                short pcm = (short)Mathf.RoundToInt(
                    Mathf.Clamp(sample, -1f, 1f) * short.MaxValue);
                writer.Write(pcm);
            }

            writer.Flush();
            return stream.ToArray();
        }

        private static string EscapeJson(string value) =>
            value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t");

        [Serializable]
        private sealed class GeminiVoiceResult
        {
            public string transcript = "";
            public string warehouseAreaM2 = "";
            public string ceilingHeightM = "";
            public string palletWidthM = "";
            public string palletLengthM = "";
            public string palletMaxLoadKg = "";
        }

        [Serializable]
        private sealed class GeminiResponse
        {
            public GeminiCandidate[] candidates = Array.Empty<GeminiCandidate>();
        }

        [Serializable]
        private sealed class GeminiCandidate
        {
            public GeminiContent content = new();
        }

        [Serializable]
        private sealed class GeminiContent
        {
            public GeminiPart[] parts = Array.Empty<GeminiPart>();
        }

        [Serializable]
        private sealed class GeminiPart
        {
            public string text = "";
        }
    }
}
