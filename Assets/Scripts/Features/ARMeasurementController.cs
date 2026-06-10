using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ARLogistics.Features
{
    /// <summary>
    /// AR 거리/높이 측정
    /// - 두 지점을 찍으면 수평 거리, 높이 차이, 직선 거리 표시
    /// - 첫 번째 점 이후 실시간 미리보기 (라이브 줄자)
    /// - AR 공간 중간에 3D 치수 라벨 (Billboard)
    /// - 수평/수직 보조선으로 입체 시각화
    /// - 천장 높이 기반 적재 가용 공간 판정
    /// </summary>
    public class ARMeasurementController : MonoBehaviour
    {
        [Header("AR")]
        [SerializeField] private ARRaycastManager raycastManager;

        [Header("UI References")]
        [SerializeField] private Text   statusText;
        [SerializeField] private Text   resultText;
        [SerializeField] private Button measureButton;
        [SerializeField] private Button clearButton;

        [Header("Visual")]
        [SerializeField] private Color firstPointColor  = Color.cyan;
        [SerializeField] private Color secondPointColor = Color.yellow;
        [SerializeField] private float markerScale = 0.04f;
        [SerializeField] private float lineWidth   = 0.008f;

        // AR 오브젝트 목록
        private readonly List<ARRaycastHit> _hits    = new();
        private readonly List<Vector3>      _points  = new();
        private readonly List<GameObject>   _markers = new();
        private readonly List<GameObject>   _lines   = new();
        private readonly List<GameObject>   _labels  = new();

        // 실시간 미리보기
        private GameObject   _liveLineGO;
        private LineRenderer  _liveLR;

        // ─────────────────────────────────────────────
        private void Awake()
        {
            if (statusText    == null) statusText    = GameObject.Find("MS_StatusText")?.GetComponent<Text>();
            if (resultText    == null) resultText    = GameObject.Find("MS_ResultText")?.GetComponent<Text>();
            if (measureButton == null) measureButton = GameObject.Find("MS_MeasureBtn")?.GetComponent<Button>();
            if (clearButton   == null) clearButton   = GameObject.Find("MS_ClearBtn")?.GetComponent<Button>();
        }

        private void Start()
        {
            if (raycastManager == null)
                raycastManager = FindFirstObjectByType<ARRaycastManager>();

            measureButton?.onClick.AddListener(AddPoint);
            clearButton?.onClick.AddListener(ClearAll);

            // 실시간 라인 오브젝트 (항상 존재, 필요할 때만 활성화)
            _liveLineGO = new GameObject("LiveMeasureLine");
            _liveLR = _liveLineGO.AddComponent<LineRenderer>();
            _liveLR.positionCount = 2;
            _liveLR.startWidth    = lineWidth * 0.7f;
            _liveLR.endWidth      = lineWidth * 0.7f;
            _liveLR.useWorldSpace = true;
            _liveLR.material      = MakeUnlitMat(new Color(1f, 1f, 1f, 0.4f));
            _liveLR.enabled       = false;

            SetStatus("팔레트/박스를 비추고\n'지점 추가' 버튼으로 첫 번째 지점을 찍으세요");
        }

        // 첫 번째 점 이후 실시간 미리보기
        private void Update()
        {
            if (_points.Count != 1 || raycastManager == null)
            {
                _liveLR.enabled = false;
                return;
            }

            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (!raycastManager.Raycast(center, _hits,
                    TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds))
            {
                _liveLR.enabled = false;
                SetStatus("✅ 1지점 완료 — 두 번째 지점을 비춰주세요");
                return;
            }

            Vector3 live = _hits[0].pose.position;
            _liveLR.enabled = true;
            _liveLR.SetPosition(0, _points[0]);
            _liveLR.SetPosition(1, live);

            float horiz  = Horiz(_points[0], live);
            float vert   = Mathf.Abs(live.y - _points[0].y);
            float direct = Vector3.Distance(_points[0], live);

            SetStatus(
                $"📡 실시간\n" +
                $"수평 {horiz * 100f:F1}cm  높이 {vert * 100f:F1}cm  직선 {direct * 100f:F1}cm\n" +
                "두 번째 지점을 찍으세요");
        }

        private void OnDisable()
        {
            ClearAll();
            if (_liveLineGO != null) Destroy(_liveLineGO);
        }

        // ─────────────────────────────────────────────
        private void AddPoint()
        {
            if (raycastManager == null) { SetStatus("AR 초기화 중..."); return; }

            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (!raycastManager.Raycast(center, _hits,
                    TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds))
            {
                SetStatus("평면을 찾을 수 없습니다. 물체를 비춰주세요.");
                return;
            }

            if (_points.Count >= 2) ClearAll();

            Vector3 pos = _hits[0].pose.position;
            _points.Add(pos);
            SpawnMarker(pos, _points.Count == 1 ? firstPointColor : secondPointColor);

            if (_points.Count == 2)
            {
                _liveLR.enabled = false;
                DrawLines(_points[0], _points[1]);
                DrawBillboardLabel(_points[0], _points[1]);
                ShowResults();
            }
        }

        private void ShowResults()
        {
            var a = _points[0];
            var b = _points[1];

            float horiz  = Horiz(a, b);
            float vert   = Mathf.Abs(b.y - a.y);
            float direct = Vector3.Distance(a, b);

            // 적재 분석
            float ceiling    = ARLogistics.AppSettings.CeilingHeightM;
            float highPoint  = Mathf.Max(a.y, b.y);
            float usable     = ceiling - highPoint - 0.3f; // 0.3m 안전 여유

            string stackJudge;
            if      (usable <= 0f)    stackJudge = "🚫 적재 불가 (천장 초과)";
            else if (usable < 0.5f)   stackJudge = $"⚠️ 여유 {usable*100f:F0}cm (주의)";
            else if (usable < 1.5f)   stackJudge = $"🟡 여유 {usable*100f:F0}cm";
            else                      stackJudge = $"✅ 여유 {usable*100f:F0}cm (충분)";

            string vertLabel = vert * 100f < 3f ? "수평"
                             : vert < 0.5f      ? "낮음"
                             : vert < 1.2f      ? "중간"
                             :                    "높음";

            if (resultText != null)
                resultText.text =
                    $"📏 수평:  {horiz  * 100f:F1} cm\n" +
                    $"📐 높이:  {vert   * 100f:F1} cm  ({vertLabel})\n" +
                    $"📍 직선:  {direct * 100f:F1} cm\n\n" +
                    $"🏭 천장 {ceiling:F1}m 기준\n{stackJudge}";

            SetStatus("✅ 측정 완료 — 초기화 후 재측정 가능");
        }

        // ─────────────────────────────────────────────
        // 시각화 헬퍼

        // 직선 + 수평/수직 보조선 세트
        private void DrawLines(Vector3 a, Vector3 b)
        {
            // 주 직선 (흰색)
            _lines.Add(SpawnLine(a, b, Color.white, lineWidth));

            float vert = Mathf.Abs(b.y - a.y);
            if (vert > 0.02f)
            {
                // 수평 보조선 (노랑): a → foot(b)
                Vector3 foot = new Vector3(b.x, a.y, b.z);
                _lines.Add(SpawnLine(a,    foot, new Color(1f, 0.85f, 0.2f, 0.85f), lineWidth * 0.55f));
                // 수직 보조선 (파랑): foot(b) → b
                _lines.Add(SpawnLine(foot, b,    new Color(0.3f, 0.85f, 1f, 0.85f), lineWidth * 0.55f));
            }
        }

        private GameObject SpawnLine(Vector3 from, Vector3 to, Color color, float width)
        {
            var go = new GameObject("Line");
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.startWidth    = width;
            lr.endWidth      = width;
            lr.useWorldSpace = true;
            lr.material      = MakeUnlitMat(color);
            return go;
        }

        // AR 공간 중간에 거리 수치 떠있는 Billboard 라벨
        private void DrawBillboardLabel(Vector3 a, Vector3 b)
        {
            float direct = Vector3.Distance(a, b);
            Vector3 mid  = (a + b) * 0.5f + Vector3.up * 0.10f;

            var root = new GameObject("MeasureLabel");
            root.transform.position = mid;

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            root.AddComponent<CanvasScaler>();

            var rt = root.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0.32f, 0.09f);

            // 배경 패널 (반투명 검정)
            var bg = new GameObject("BG");
            bg.transform.SetParent(root.transform, false);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.55f);
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.sizeDelta = Vector2.zero;

            // 텍스트
            var textGO = new GameObject("Txt");
            textGO.transform.SetParent(root.transform, false);
            var txtRt = textGO.AddComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.sizeDelta = Vector2.zero;

            var txt = textGO.AddComponent<Text>();
            txt.text      = $"  {direct * 100f:F1} cm  ";
            txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize  = 60;
            txt.fontStyle = FontStyle.Bold;
            txt.color     = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.resizeTextForBestFit = true;
            txt.resizeTextMinSize    = 10;
            txt.resizeTextMaxSize    = 60;

            root.AddComponent<BillboardFace>();
            _labels.Add(root);
        }

        private void SpawnMarker(Vector3 pos, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.position   = pos;
            go.transform.localScale = Vector3.one * markerScale;
            Destroy(go.GetComponent<SphereCollider>());
            go.GetComponent<Renderer>().material = MakeLitMat(color);
            _markers.Add(go);
        }

        private void ClearAll()
        {
            _points.Clear();
            if (_liveLR != null) _liveLR.enabled = false;
            foreach (var go in _markers) if (go != null) Destroy(go);
            foreach (var go in _lines)   if (go != null) Destroy(go);
            foreach (var go in _labels)  if (go != null) Destroy(go);
            _markers.Clear();
            _lines.Clear();
            _labels.Clear();
            if (resultText != null) resultText.text = "";
            SetStatus("팔레트/박스를 비추고\n'지점 추가' 버튼으로 첫 번째 지점을 찍으세요");
        }

        private void SetStatus(string msg)
        {
            if (statusText != null) statusText.text = msg;
        }

        // ─────────────────────────────────────────────
        // 유틸

        static float Horiz(Vector3 a, Vector3 b) =>
            Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        static Material MakeUnlitMat(Color c)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            mat.color = c;
            return mat;
        }

        static Material MakeLitMat(Color c)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = c;
            return mat;
        }
    }

    // AR 공간의 라벨이 항상 카메라를 향하도록 회전
    public class BillboardFace : MonoBehaviour
    {
        Camera _cam;
        void Start() => _cam = Camera.main;
        void LateUpdate()
        {
            if (_cam == null) return;
            transform.LookAt(_cam.transform.position);
            transform.Rotate(0f, 180f, 0f);
        }
    }
}
