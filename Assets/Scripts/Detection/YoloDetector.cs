using System.Collections.Generic;
using UnityEngine;
using Unity.InferenceEngine;

namespace ARLogistics.Detection
{
    /// <summary>
    /// YOLOv8 단일 탐지 결과 구조체.
    /// AR Foundation Raycast 단계로 넘길 데이터 단위.
    /// </summary>
    [System.Serializable]
    public struct DetectionResult
    {
        /// <summary>정규화된 스크린 좌표 Rect (0~1). x/y = 좌상단, width/height.</summary>
        public Rect boundingBox;
        public int   classId;
        public float confidence;
        public string className;

        public DetectionResult(Rect bbox, int classId, float confidence, string className)
        {
            this.boundingBox = bbox;
            this.classId     = classId;
            this.confidence  = confidence;
            this.className   = className;
        }

        public override string ToString() =>
            $"[{className}] conf={confidence:F2}  box=({boundingBox.x:F3},{boundingBox.y:F3},{boundingBox.width:F3},{boundingBox.height:F3})";
    }

    /// <summary>
    /// YOLOv8 Nano ONNX 모델을 Unity Sentis 2.x 로 실행하고
    /// 바운딩박스 후처리(NMS)까지 수행하는 컴포넌트.
    ///
    /// 출력 텐서 규격 (YOLOv8 기본):
    ///   shape = [1, 4+numClasses, numAnchors]  예) [1, 84, 8400]
    ///   0~3   : cx, cy, w, h  (입력 해상도 픽셀 단위)
    ///   4~    : 클래스별 점수 (sigmoid 미적용 raw score)
    /// </summary>
    public class YoloDetector : MonoBehaviour
    {
        // ─────────────────────────────────────────────
        // Inspector 설정
        // ─────────────────────────────────────────────

        [Header("Sentis Model")]
        [Tooltip("yolov8n.onnx 를 Unity 에서 import 한 ModelAsset")]
        [SerializeField] private ModelAsset modelAsset;

        [Tooltip("모바일 GPU 추론에는 GPUCompute, 미지원 기기는 CPU 로 폴백")]
        [SerializeField] private BackendType backendType = BackendType.GPUCompute;

        [Header("Input Resolution")]
        [SerializeField] private int inputWidth  = 640;
        [SerializeField] private int inputHeight = 640;

        [Header("Post-Processing")]
        [Range(0.1f, 0.9f)]
        [SerializeField] private float confidenceThreshold = 0.50f;

        [Range(0.1f, 0.9f)]
        [SerializeField] private float iouThreshold = 0.45f;

        [Header("Class Names")]
        [Tooltip("AI-Hub 데이터셋 클래스 목록 (인덱스 순서 유지)")]
        [SerializeField] private string[] classNames = new string[0];

        // ─────────────────────────────────────────────
        // 이벤트 (Observer Pattern)
        // ─────────────────────────────────────────────

        /// <summary>새로운 탐지 결과가 확정될 때마다 발행.</summary>
        public System.Action<List<DetectionResult>> OnDetectionResultsUpdated;

        // ─────────────────────────────────────────────
        // 내부 상태
        // ─────────────────────────────────────────────

        private Model  _runtimeModel;
        private Worker _worker;
        private bool   _isReady;

        /// <summary>모델 로드가 완료되어 추론 가능한 상태인지 반환합니다.</summary>
        public bool IsReady => _isReady;

        // ─────────────────────────────────────────────
        // 생명주기
        // ─────────────────────────────────────────────

        private void Awake()
        {
            InitializeModel();
        }

        private void OnDestroy()
        {
            _worker?.Dispose();
            Debug.Log("[YoloDetector] Worker disposed.");
        }

        // ─────────────────────────────────────────────
        // 공개 API
        // ─────────────────────────────────────────────

        /// <summary>
        /// 카메라 프레임(Texture2D)으로 추론을 실행하고
        /// 결과를 OnDetectionResultsUpdated 이벤트로 발행합니다.
        /// </summary>
        public void RunInference(Texture2D frame)
        {
            if (!_isReady)
            {
                Debug.LogWarning("[YoloDetector] Model not ready.");
                return;
            }

            if (frame == null)
            {
                Debug.LogWarning("[YoloDetector] Input texture is null.");
                return;
            }

            // 1. 전처리: 텍스처 → 입력 텐서 [1, 3, H, W] (Sentis 2.x 새 API)
            using var inputTensor = new Tensor<float>(new TensorShape(1, 3, inputHeight, inputWidth));
            TextureConverter.ToTensor(frame, inputTensor, new TextureTransform());

            // 2. 추론 실행 (비동기 스케줄)
            _worker.Schedule(inputTensor);

            // 3. 출력 텐서 획득 후 CPU 로 동기 다운로드 (Tensor<float> = Sentis 2.x)
            var rawGpu = _worker.PeekOutput() as Tensor<float>;
            if (rawGpu == null)
            {
                Debug.LogError("[YoloDetector] Output tensor is null or wrong type.");
                return;
            }

            // ReadbackAndClone: GPU → CPU 복사본 반환 (동기)
            using var rawOutput = rawGpu.ReadbackAndClone();

            // 4. 후처리 → 탐지 목록
            var detections = PostProcess(rawOutput);

            // 6. Observer 알림
            OnDetectionResultsUpdated?.Invoke(detections);

            Debug.Log($"[YoloDetector] {detections.Count} object(s) detected.");
        }

        // ─────────────────────────────────────────────
        // 초기화
        // ─────────────────────────────────────────────

        private void InitializeModel()
        {
            if (modelAsset == null)
            {
                Debug.LogError("[YoloDetector] ModelAsset is not assigned in Inspector!");
                return;
            }

            try
            {
                _runtimeModel = ModelLoader.Load(modelAsset);
                _worker       = new Worker(_runtimeModel, backendType);
                _isReady      = true;
                Debug.Log($"[YoloDetector] Sentis worker created. Backend={backendType}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[YoloDetector] Failed to initialize Sentis worker: {ex.Message}");

                // GPU 실패 시 CPU 로 폴백
                if (backendType != BackendType.CPU)
                {
                    Debug.LogWarning("[YoloDetector] Falling back to CPU backend.");
                    backendType   = BackendType.CPU;
                    _worker       = new Worker(_runtimeModel, BackendType.CPU);
                    _isReady      = true;
                }
            }
        }

        // ─────────────────────────────────────────────
        // 후처리
        // ─────────────────────────────────────────────

        /// <summary>
        /// YOLOv8 출력 텐서를 파싱하고 클래스별 NMS 를 적용합니다.
        /// 텐서 shape: [1, 4+numClasses, numAnchors]
        /// </summary>
        private List<DetectionResult> PostProcess(Tensor<float> output)
        {
            // shape[1] = 4 + numClasses, shape[2] = numAnchors (예: 8400)
            int numClasses = output.shape[1] - 4;
            int numAnchors = output.shape[2];

            var candidates = new List<(Rect box, int classId, float score)>(256);

            for (int a = 0; a < numAnchors; a++)
            {
                // 최고 클래스 탐색
                float bestScore = 0f;
                int   bestClass = 0;

                for (int c = 0; c < numClasses; c++)
                {
                    // ONNX 모델이 마지막 Sigmoid 레이어를 포함하므로
                    // 클래스 점수는 이미 [0,1] 범위 — 이중 적용 금지
                    float score = output[0, 4 + c, a];
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestClass = c;
                    }
                }

                if (bestScore < confidenceThreshold) continue;

                // cx, cy, w, h → 정규화 Rect (좌상단 기준)
                float cx = output[0, 0, a] / inputWidth;
                float cy = output[0, 1, a] / inputHeight;
                float bw = output[0, 2, a] / inputWidth;
                float bh = output[0, 3, a] / inputHeight;

                float x = cx - bw * 0.5f;
                float y = cy - bh * 0.5f;

                candidates.Add((new Rect(x, y, bw, bh), bestClass, bestScore));
            }

            return ApplyNMS(candidates, numClasses);
        }

        /// <summary>클래스별 Non-Maximum Suppression</summary>
        private List<DetectionResult> ApplyNMS(
            List<(Rect box, int classId, float score)> candidates,
            int numClasses)
        {
            var result = new List<DetectionResult>();

            for (int c = 0; c < numClasses; c++)
            {
                // 클래스 필터 및 신뢰도 내림차순 정렬
                var group = new List<(Rect box, float score)>();
                foreach (var d in candidates)
                    if (d.classId == c) group.Add((d.box, d.score));

                if (group.Count == 0) continue;
                group.Sort((a, b) => b.score.CompareTo(a.score));

                // Greedy NMS
                while (group.Count > 0)
                {
                    var best = group[0];
                    group.RemoveAt(0);

                    string name = (c < classNames.Length && classNames[c] != null)
                        ? classNames[c]
                        : $"class_{c}";

                    result.Add(new DetectionResult(best.box, c, best.score, name));

                    // IoU 기반 중복 제거
                    group.RemoveAll(d => CalculateIoU(best.box, d.box) > iouThreshold);
                }
            }

            return result;
        }

        // ─────────────────────────────────────────────
        // 수학 유틸
        // ─────────────────────────────────────────────

        private static float CalculateIoU(Rect a, Rect b)
        {
            float xMin = Mathf.Max(a.xMin, b.xMin);
            float yMin = Mathf.Max(a.yMin, b.yMin);
            float xMax = Mathf.Min(a.xMax, b.xMax);
            float yMax = Mathf.Min(a.yMax, b.yMax);

            float intersection = Mathf.Max(0, xMax - xMin) * Mathf.Max(0, yMax - yMin);
            if (intersection <= 0f) return 0f;

            float union = a.width * a.height + b.width * b.height - intersection;
            return union > 0f ? intersection / union : 0f;
        }

        private static float Sigmoid(float x) => 1f / (1f + Mathf.Exp(-x));
    }
}
