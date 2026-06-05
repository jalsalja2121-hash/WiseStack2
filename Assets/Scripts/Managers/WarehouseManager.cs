using System.Collections.Generic;
using System.Text;
using UnityEngine;
using ARLogistics.AR;
using ARLogistics.Data;
using ARLogistics.API;

namespace ARLogistics.Managers
{
    // ─────────────────────────────────────────────────────────────────────────
    // 데이터 구조체 (출력)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>팔레트 규격 설정</summary>
    [System.Serializable]
    public struct PalletSpec
    {
        [Tooltip("팔레트 가로 (m)")]
        public float width;
        [Tooltip("팔레트 세로 (m)")]
        public float length;
        [Tooltip("팔레트 자체 높이 (m)")]
        public float height;
        [Tooltip("팔레트 최대 적재 하중 (kg)")]
        public float maxLoadKg;

        public float FootprintArea => width * length;
    }

    /// <summary>
    /// 탐지된 물체 하나의 용량 계산 결과.
    /// </summary>
    [System.Serializable]
    public class ProductCapacity
    {
        public string   productName;
        public int      classId;
        public int      unitsPerLayer;   // 팔레트 1단에 놓을 수 있는 개수
        public int      maxLayers;       // 최대 적재 단수 (하중 기준)
        public int      totalUnits;      // 팔레트 1개당 총 수용 개수
        public float    totalWeightKg;
        public float    stackHeightM;    // 적재 후 총 높이 (m)
        public Vector3  worldPosition;   // AR 측정 위치
    }

    /// <summary>
    /// 창고 전체 분석 결과.
    /// UI 레이어와 Gemini API 가 이 데이터를 소비합니다.
    /// </summary>
    [System.Serializable]
    public class WarehouseReport
    {
        public float                  warehouseAreaM2;
        public float                  ceilingHeightM;
        public PalletSpec             palletSpec;
        public List<ProductCapacity>  products = new();
        public string                 geminiGuidance;   // Gemini 응답 텍스트
        public System.DateTime        timestamp;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WarehouseManager (Singleton)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 전역 입력값(창고 면적, 천장 높이, 팔레트 규격)을 보관하고,
    /// ARDetectionBridge 의 결과를 받아 용량을 계산한 뒤
    /// Gemini API 로 적재 가이드를 요청하는 Singleton Manager.
    /// </summary>
    public class WarehouseManager : MonoBehaviour
    {
        // ─────────────────────────────────────────────
        // Singleton
        // ─────────────────────────────────────────────

        public static WarehouseManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // ─────────────────────────────────────────────
        // Inspector 설정
        // ─────────────────────────────────────────────

        [Header("Warehouse Input")]
        [Tooltip("창고 바닥 면적 (m²)")]
        [SerializeField] private float warehouseAreaM2 = 500f;

        [Tooltip("천장 높이 (m)")]
        [SerializeField] private float ceilingHeightM  = 6f;

        [Header("Pallet Spec")]
        [SerializeField] private PalletSpec palletSpec = new PalletSpec
        {
            width    = 1.2f,
            length   = 1.0f,
            height   = 0.15f,
            maxLoadKg = 1000f
        };

        [Header("Dependencies")]
        [SerializeField] private ARDetectionBridge detectionBridge;
        [SerializeField] private GeminiApiClient   geminiClient;

        [Header("Product Spec (Optional Override)")]
        [Tooltip("비워두면 ProductSpecTable 정적 테이블을 자동 사용 (ScriptableObject 불필요)")]
        [SerializeField] private ProductDatabase   productDatabase;

        [Header("Analysis Settings")]
        [Tooltip("안전율 (실제 하중 = 최대 하중 × 안전율)")]
        [Range(0.5f, 1f)]
        [SerializeField] private float safetyFactor = 0.8f;

        [Tooltip("최소 신뢰도 — 이 값 미만 탐지 결과는 무시")]
        [Range(0f, 1f)]
        [SerializeField] private float minConfidence = 0.5f;

        // ─────────────────────────────────────────────
        // 이벤트
        // ─────────────────────────────────────────────

        /// <summary>용량 계산 완료 시 발행 (Gemini 응답 전)</summary>
        public System.Action<WarehouseReport> OnReportReady;

        /// <summary>Gemini 가이드까지 포함한 최종 리포트 발행</summary>
        public System.Action<WarehouseReport> OnFinalReportReady;

        // ─────────────────────────────────────────────
        // 내부 상태
        // ─────────────────────────────────────────────

        private WarehouseReport _lastReport;

        // ─────────────────────────────────────────────
        // 생명주기
        // ─────────────────────────────────────────────

        private void OnEnable()
        {
            // 씬 전환 후에도 최신 AppSettings 반영 (DontDestroyOnLoad 대응)
            warehouseAreaM2      = AppSettings.WarehouseAreaM2;
            ceilingHeightM       = AppSettings.CeilingHeightM;
            palletSpec.width     = AppSettings.PalletWidth;
            palletSpec.length    = AppSettings.PalletLength;
            palletSpec.maxLoadKg = AppSettings.PalletMaxLoadKg;
        }

        private void Start()
        {
            // 자동 탐색
            if (detectionBridge == null)
                detectionBridge = FindFirstObjectByType<ARDetectionBridge>();
            if (geminiClient == null)
                geminiClient = FindFirstObjectByType<GeminiApiClient>();

            if (productDatabase == null)
                Debug.Log("[WarehouseManager] ProductDatabase 미할당 → ProductSpecTable 정적 테이블 사용");

            // 이벤트 구독
            if (detectionBridge != null)
                detectionBridge.OnObjectsLocated += HandleObjectsLocated;

            if (geminiClient != null)
            {
                geminiClient.OnGuidanceReceived += HandleGeminiGuidance;
                geminiClient.OnError            += err => Debug.LogError($"[WarehouseManager] Gemini 오류: {err}");
            }
        }

        private void OnDestroy()
        {
            if (detectionBridge != null)
                detectionBridge.OnObjectsLocated -= HandleObjectsLocated;

            if (geminiClient != null)
                geminiClient.OnGuidanceReceived -= HandleGeminiGuidance;
        }

        // ─────────────────────────────────────────────
        // 공개 API (외부에서 파라미터 조정)
        // ─────────────────────────────────────────────

        public void SetWarehouseArea(float areaM2)   => warehouseAreaM2 = areaM2;
        public void SetCeilingHeight(float heightM)  => ceilingHeightM  = heightM;
        public void SetPalletSpec(PalletSpec spec)   => palletSpec       = spec;

        // ─────────────────────────────────────────────
        // 핵심 파이프라인
        // ─────────────────────────────────────────────

        private void HandleObjectsLocated(List<LocatedObject> objects)
        {
            if (objects == null || objects.Count == 0) return;

            // 1. 용량 계산
            var report = BuildReport(objects);
            _lastReport = report;

            OnReportReady?.Invoke(report);
            Debug.Log($"[WarehouseManager] 리포트 생성: {report.products.Count}종 제품 탐지");

            // 2. Gemini API 호출
            if (geminiClient != null)
            {
                string json = SerializeReportToJson(report);
                geminiClient.RequestStackingGuidance(json);
            }
        }

        private void HandleGeminiGuidance(string guidance)
        {
            if (_lastReport == null) return;

            _lastReport.geminiGuidance = guidance;
            OnFinalReportReady?.Invoke(_lastReport);

            Debug.Log($"[WarehouseManager] 최종 리포트 완성\n{guidance}");
        }

        // ─────────────────────────────────────────────
        // 용량 계산
        // ─────────────────────────────────────────────

        private WarehouseReport BuildReport(List<LocatedObject> objects)
        {
            var report = new WarehouseReport
            {
                warehouseAreaM2 = warehouseAreaM2,
                ceilingHeightM  = ceilingHeightM,
                palletSpec      = palletSpec,
                timestamp       = System.DateTime.Now
            };

            // classId 별로 그룹화 (같은 종류 제품 중복 합산 방지)
            var seen = new HashSet<int>();

            foreach (var obj in objects)
            {
                if (obj.Confidence < minConfidence) continue;
                if (seen.Contains(obj.ClassId)) continue;
                seen.Add(obj.ClassId);

                // ProductDatabase(ScriptableObject) 우선 → 없으면 ProductSpecTable 정적 조회
                ProductCapacity cap;
                if (productDatabase != null)
                {
                    var legacySpec = productDatabase.GetById(obj.ClassId);
                    if (legacySpec == null)
                    {
                        Debug.LogWarning($"[WarehouseManager] classId={obj.ClassId} ProductDatabase 에 없음 → 정적 테이블 사용");
                        cap = CalculateCapacityFromTable(obj.ClassId, obj.Position);
                    }
                    else
                    {
                        cap = CalculateCapacity(legacySpec, obj.Position);
                    }
                }
                else
                {
                    cap = CalculateCapacityFromTable(obj.ClassId, obj.Position);
                }

                report.products.Add(cap);
            }

            return report;
        }

        /// <summary>
        /// ProductSpecTable 정적 조회를 사용해 용량 계산 (ScriptableObject 불필요).
        /// size_id 는 기본 중형(3) 사용.
        /// </summary>
        private ProductCapacity CalculateCapacityFromTable(int classId, Vector3 worldPos)
        {
            var s = ProductSpecTable.Get(classId, sizeId: 3);

            float productFootprint = s.LengthM * s.WidthM;
            int unitsPerLayer = productFootprint > 0
                ? Mathf.Max(1, Mathf.FloorToInt(palletSpec.FootprintArea / productFootprint))
                : 1;

            float effectiveMaxLoad = palletSpec.maxLoadKg * safetyFactor;
            float loadPerLayer     = unitsPerLayer * s.WeightKg;
            int   maxLayers        = loadPerLayer > 0
                ? Mathf.FloorToInt(effectiveMaxLoad / loadPerLayer)
                : 1;

            float usableHeight    = ceilingHeightM - palletSpec.height - 0.3f;
            int   heightMaxLayers = s.HeightM > 0
                ? Mathf.FloorToInt(usableHeight / s.HeightM)
                : maxLayers;

            int finalLayers  = Mathf.Max(1, Mathf.Min(maxLayers, heightMaxLayers));
            int totalUnits   = unitsPerLayer * finalLayers;
            float stackHeight = palletSpec.height + s.HeightM * finalLayers;

            string note = s.IsFragile   ? " [파손주의]" : "";
            note       += s.IsIrregular ? " [비정형]"   : "";

            return new ProductCapacity
            {
                productName   = ProductSpecTable.ClassName(classId) + note,
                classId       = classId,
                unitsPerLayer = unitsPerLayer,
                maxLayers     = finalLayers,
                totalUnits    = totalUnits,
                totalWeightKg = totalUnits * s.WeightKg,
                stackHeightM  = stackHeight,
                worldPosition = worldPos
            };
        }

        /// <summary>
        /// 팔레트 1개 기준 적재 용량을 계산합니다. (Legacy: ProductDatabase 사용 시)
        /// </summary>
        private ProductCapacity CalculateCapacity(Data.ProductSpec spec, Vector3 worldPos)
        {
            // 1단당 개수: 팔레트 단면적 / 제품 단면적 (정수 내림)
            int unitsPerLayer = spec.maxPerLayer > 0
                ? spec.maxPerLayer
                : Mathf.FloorToInt(palletSpec.FootprintArea / spec.FootprintArea);
            unitsPerLayer = Mathf.Max(1, unitsPerLayer);

            // 최대 적재 단수: 하중 기준
            float effectiveMaxLoad = palletSpec.maxLoadKg * safetyFactor;
            float loadPerLayer     = unitsPerLayer * spec.weightKg;
            int   maxLayers        = loadPerLayer > 0
                ? Mathf.FloorToInt(effectiveMaxLoad / loadPerLayer)
                : 1;

            // 높이 기준 단수 제한
            float usableHeight    = ceilingHeightM - palletSpec.height - 0.3f; // 0.3m 안전 여유
            int   heightMaxLayers = spec.height > 0
                ? Mathf.FloorToInt(usableHeight / spec.height)
                : maxLayers;

            int finalLayers  = Mathf.Max(1, Mathf.Min(maxLayers, heightMaxLayers));
            int totalUnits   = unitsPerLayer * finalLayers;
            float stackHeight = palletSpec.height + spec.height * finalLayers;

            return new ProductCapacity
            {
                productName   = spec.displayName,
                classId       = spec.classId,
                unitsPerLayer = unitsPerLayer,
                maxLayers     = finalLayers,
                totalUnits    = totalUnits,
                totalWeightKg = totalUnits * spec.weightKg,
                stackHeightM  = stackHeight,
                worldPosition = worldPos
            };
        }

        // ─────────────────────────────────────────────
        // JSON 직렬화 (Gemini 입력용)
        // ─────────────────────────────────────────────

        private string SerializeReportToJson(WarehouseReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"warehouse\": {{");
            sb.AppendLine($"    \"area_m2\": {report.warehouseAreaM2},");
            sb.AppendLine($"    \"ceiling_height_m\": {report.ceilingHeightM}");
            sb.AppendLine($"  }},");
            sb.AppendLine($"  \"pallet\": {{");
            sb.AppendLine($"    \"width_m\": {report.palletSpec.width},");
            sb.AppendLine($"    \"length_m\": {report.palletSpec.length},");
            sb.AppendLine($"    \"max_load_kg\": {report.palletSpec.maxLoadKg}");
            sb.AppendLine($"  }},");
            sb.AppendLine($"  \"detected_products\": [");

            for (int i = 0; i < report.products.Count; i++)
            {
                var p = report.products[i];
                sb.AppendLine($"    {{");
                sb.AppendLine($"      \"name\": \"{p.productName}\",");
                sb.AppendLine($"      \"units_per_layer\": {p.unitsPerLayer},");
                sb.AppendLine($"      \"max_layers\": {p.maxLayers},");
                sb.AppendLine($"      \"total_units_per_pallet\": {p.totalUnits},");
                sb.AppendLine($"      \"total_weight_kg\": {p.totalWeightKg:F1},");
                sb.AppendLine($"      \"stack_height_m\": {p.stackHeightM:F2},");
                sb.AppendLine($"      \"world_position\": {{\"x\":{p.worldPosition.x:F2},\"y\":{p.worldPosition.y:F2},\"z\":{p.worldPosition.z:F2}}}");
                sb.Append(i < report.products.Count - 1 ? "    }," : "    }");
                sb.AppendLine();
            }

            sb.AppendLine($"  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
