using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ARLogistics.Detection;

namespace ARLogistics.Detection
{
    /// <summary>
    /// AR 카메라 프레임을 캡처해 YoloDetector 에 공급합니다.
    ///
    /// 파이프라인:
    ///   ARCameraManager.frameReceived
    ///       → XRCpuImage → Texture2D (640×640 RGB24)
    ///       → YoloDetector.RunInference()
    /// </summary>
    [RequireComponent(typeof(ARCameraManager))]
    public class CameraFrameProvider : MonoBehaviour
    {
        // ─────────────────────────────────────────────
        // Inspector
        // ─────────────────────────────────────────────

        [Header("Dependencies")]
        [Tooltip("YoloDetector 컴포넌트 (자동 탐색 또는 직접 할당)")]
        [SerializeField] private YoloDetector yoloDetector;

        [Header("Performance")]
        [Tooltip("추론 최소 간격 (초). 모바일 과부하 방지")]
        [SerializeField] private float inferenceInterval = 0.5f;

        [Tooltip("YOLOv8 입력 해상도 (모델과 반드시 일치)")]
        [SerializeField] private int inputSize = 640;

        // ─────────────────────────────────────────────
        // 내부 상태
        // ─────────────────────────────────────────────

        private ARCameraManager _cameraManager;
        private Texture2D       _reuseTexture;   // GC 절약용 재사용 텍스처
        private float           _lastInferTime;
        private bool            _isProcessing;
        private bool            _inferenceEnabled = true;

        public bool IsInferenceEnabled => _inferenceEnabled;

        public void SetInferenceEnabled(bool enabled)
        {
            _inferenceEnabled = enabled;
            if (enabled)
                _lastInferTime = 0f;
        }

        // ─────────────────────────────────────────────
        // 생명주기
        // ─────────────────────────────────────────────

        private void Awake()
        {
            _cameraManager = GetComponent<ARCameraManager>();

            if (yoloDetector == null)
                yoloDetector = FindFirstObjectByType<YoloDetector>();

            if (yoloDetector == null)
                Debug.LogError("[CameraFrameProvider] YoloDetector를 찾을 수 없습니다!");
        }

        private void OnEnable()
        {
            if (_cameraManager != null)
                _cameraManager.frameReceived += OnCameraFrameReceived;
        }

        private void OnDisable()
        {
            if (_cameraManager != null)
                _cameraManager.frameReceived -= OnCameraFrameReceived;
        }

        private void OnDestroy()
        {
            if (_reuseTexture != null)
                Destroy(_reuseTexture);
        }

        // ─────────────────────────────────────────────
        // 프레임 처리
        // ─────────────────────────────────────────────

        private void OnCameraFrameReceived(ARCameraFrameEventArgs args)
        {
            if (!_inferenceEnabled) return;

            // 처리 주기 제한
            if (Time.time - _lastInferTime < inferenceInterval) return;
            if (_isProcessing) return;
            if (yoloDetector == null || !yoloDetector.IsReady) return;

            // CPU 이미지 획득
            if (!_cameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage))
                return;

            _isProcessing = true;
            _lastInferTime = Time.time;

            try
            {
                ConvertAndInfer(cpuImage);
            }
            finally
            {
                cpuImage.Dispose();
                _isProcessing = false;
            }
        }

        private void ConvertAndInfer(XRCpuImage cpuImage)
        {
            int srcW = cpuImage.width;
            int srcH = cpuImage.height;

            // ── XRCpuImage.Convert 제약: outputDimensions ≤ inputRect 크기 ──
            // 카메라가 가로로 긴 프레임(예: 640×480)을 줄 때
            // 640×640 출력을 요청하면 높이(640>480) 조건 위반으로 예외 발생.
            // → 중앙 정사각형 크롭 후 min(cropSize, inputSize)로 다운스케일.
            // → YoloDetector.RunInference 내부 TextureConverter.ToTensor가
            //   텐서 해상도(640×640)로 자동 스케일업 처리.

            int cropSize = Mathf.Min(srcW, srcH);           // 가장 큰 정사각형
            int cropX    = (srcW - cropSize) / 2;           // 가로 중앙 정렬
            int cropY    = (srcH - cropSize) / 2;           // 세로 중앙 정렬
            int outSize  = Mathf.Min(cropSize, inputSize);  // 업스케일 금지

            // 재사용 텍스처 초기화 (해상도 변경 시 재생성)
            if (_reuseTexture == null ||
                _reuseTexture.width  != outSize ||
                _reuseTexture.height != outSize)
            {
                if (_reuseTexture != null) Destroy(_reuseTexture);
                _reuseTexture = new Texture2D(outSize, outSize,
                                               TextureFormat.RGB24, false);
            }

            // XRCpuImage → Texture2D 변환 파라미터
            var convParams = new XRCpuImage.ConversionParams
            {
                inputRect        = new RectInt(cropX, cropY, cropSize, cropSize),
                outputDimensions = new Vector2Int(outSize, outSize), // ≤ cropSize
                outputFormat     = TextureFormat.RGB24,
                // AR Foundation 기본 이미지는 Y축 반전 — MirrorY로 보정
                transformation   = XRCpuImage.Transformation.MirrorY
            };

            // 픽셀 데이터 직접 기록 (복사 최소화)
            var rawData = _reuseTexture.GetRawTextureData<byte>();
            cpuImage.Convert(convParams, rawData);
            _reuseTexture.Apply();

            // 추론 실행 (TextureConverter.ToTensor가 inputSize×inputSize로 스케일)
            yoloDetector.RunInference(_reuseTexture);

            Debug.Log($"[CameraFrameProvider] 프레임 전달 ({outSize}×{outSize}, 크롭: {srcW}×{srcH}→{cropSize}×{cropSize})");
        }
    }
}
