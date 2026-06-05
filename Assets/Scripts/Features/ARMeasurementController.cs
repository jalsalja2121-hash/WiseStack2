using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ARLogistics.Features
{
    /// <summary>
    /// AR 거리/높이 측정 — 두 지점을 찍으면 수평 거리, 높이 차이, 직선 거리를 표시합니다.
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

        private readonly List<ARRaycastHit> _hits    = new();
        private readonly List<Vector3>      _points  = new();
        private readonly List<GameObject>   _markers = new();
        private readonly List<GameObject>   _lines   = new();

        // ─────────────────────────────────────────────
        private void Start()
        {
            if (raycastManager == null)
                raycastManager = FindFirstObjectByType<ARRaycastManager>();

            measureButton?.onClick.AddListener(AddPoint);
            clearButton?.onClick.AddListener(ClearAll);
            SetStatus("팔레트/박스를 비추고 '지점 추가' 버튼으로 두 지점을 찍으세요");
        }

        private void OnDisable() => ClearAll();

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
            SpawnMarker(pos, _points.Count == 1 ? Color.cyan : Color.yellow);

            if (_points.Count == 2)
            {
                DrawLine(_points[0], _points[1]);
                ShowResults();
            }
            else
            {
                SetStatus($"✅ 첫 번째 지점 설정 완료\n두 번째 지점을 추가하세요");
            }
        }

        private void ShowResults()
        {
            var a = _points[0];
            var b = _points[1];

            float horiz  = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
            float vert   = Mathf.Abs(b.y - a.y);
            float direct = Vector3.Distance(a, b);

            if (resultText != null)
                resultText.text =
                    $"📏 수평 거리:  {horiz  * 100f:F1} cm\n" +
                    $"📐 높이 차이:  {vert   * 100f:F1} cm\n" +
                    $"📍 직선 거리:  {direct * 100f:F1} cm";

            SetStatus("측정 완료! 초기화 버튼으로 다시 측정할 수 있습니다.");
        }

        // ─────────────────────────────────────────────
        private void SpawnMarker(Vector3 pos, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.position   = pos;
            go.transform.localScale = Vector3.one * 0.04f;
            Destroy(go.GetComponent<SphereCollider>());

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = color;
            go.GetComponent<Renderer>().material = mat;
            _markers.Add(go);
        }

        private void DrawLine(Vector3 from, Vector3 to)
        {
            var go = new GameObject("MeasureLine");
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.startWidth    = 0.008f;
            lr.endWidth      = 0.008f;
            lr.useWorldSpace = true;

            var mat   = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            mat.color = Color.white;
            lr.material = mat;
            _lines.Add(go);
        }

        private void ClearAll()
        {
            _points.Clear();
            foreach (var go in _markers) if (go != null) Destroy(go);
            foreach (var go in _lines)   if (go != null) Destroy(go);
            _markers.Clear();
            _lines.Clear();
            if (resultText != null) resultText.text = "";
            SetStatus("팔레트/박스를 비추고 '지점 추가' 버튼으로 두 지점을 찍으세요");
        }

        private void SetStatus(string msg)
        {
            if (statusText != null) statusText.text = msg;
        }
    

private void Awake()
        {
            if (statusText == null)   statusText   = GameObject.Find("MS_StatusText")?.GetComponent<UnityEngine.UI.Text>();
            if (resultText == null)   resultText   = GameObject.Find("MS_ResultText")?.GetComponent<UnityEngine.UI.Text>();
            if (measureButton == null) measureButton = GameObject.Find("MS_MeasureBtn")?.GetComponent<UnityEngine.UI.Button>();
            if (clearButton == null)  clearButton  = GameObject.Find("MS_ClearBtn")?.GetComponent<UnityEngine.UI.Button>();
        }
}
}
