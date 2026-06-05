using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ARLogistics.Managers;

namespace ARLogistics.Features
{
    /// <summary>
    /// AR 적재 미리보기 — 팔레트 위에 가상 박스를 색상 코드로 쌓아서 보여줍니다.
    /// 초록: 안전, 노랑: 주의, 빨강: 높이 초과
    /// </summary>
    public class ARStackPreviewController : MonoBehaviour
    {
        [Header("AR")]
        [SerializeField] private ARRaycastManager raycastManager;

        [Header("UI References")]
        [SerializeField] private Text   statusText;
        [SerializeField] private Button placeButton;
        [SerializeField] private Button clearButton;

        [Header("Preview Settings")]
        [SerializeField] private float boxWidth    = 0.30f;
        [SerializeField] private float boxLength   = 0.30f;
        [SerializeField] private float boxHeight   = 0.25f;
        [SerializeField] private int   maxLayers   = 6;
        [SerializeField] private float maxSafeHeight = 1.8f;

        private readonly List<ARRaycastHit> _hits   = new();
        private readonly List<GameObject>   _boxes  = new();
        private WarehouseReport _lastReport;

        // ─────────────────────────────────────────────
        private void Start()
        {
            if (raycastManager == null)
                raycastManager = FindFirstObjectByType<ARRaycastManager>();

            if (WarehouseManager.Instance != null)
            {
                WarehouseManager.Instance.OnReportReady      += OnReport;
                WarehouseManager.Instance.OnFinalReportReady += OnReport;
            }

            placeButton?.onClick.AddListener(PlacePreview);
            clearButton?.onClick.AddListener(ClearBoxes);
            SetStatus("팔레트를 비추고 '적재 미리보기' 버튼을 누르세요");
        }

        private void OnEnable()  { }
        private void OnDisable() => ClearBoxes();

        private void OnDestroy()
        {
            ClearBoxes();
            if (WarehouseManager.Instance != null)
            {
                WarehouseManager.Instance.OnReportReady      -= OnReport;
                WarehouseManager.Instance.OnFinalReportReady -= OnReport;
            }
        }

        private void OnReport(WarehouseReport r) => _lastReport = r;

        // ─────────────────────────────────────────────
        private void PlacePreview()
        {
            if (raycastManager == null) { SetStatus("AR 초기화 중..."); return; }

            var screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (!raycastManager.Raycast(screenCenter, _hits,
                    TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds))
            {
                SetStatus("평면을 찾을 수 없습니다. 팔레트를 비춰주세요.");
                return;
            }

            SpawnStack(_hits[0].pose.position);
        }

        private void SpawnStack(Vector3 basePos)
        {
            ClearBoxes();

            int   layers      = maxLayers;
            float bH          = boxHeight;
            string productName = "박스";

            if (_lastReport != null && _lastReport.products.Count > 0)
            {
                var p   = _lastReport.products[0];
                layers  = Mathf.Min(p.maxLayers, maxLayers);
                bH      = Mathf.Clamp(p.stackHeightM / Mathf.Max(1, p.maxLayers), 0.10f, 0.50f);
                productName = p.productName;
            }

            float dangerHeight = maxSafeHeight * 0.75f;

            for (int i = 0; i < layers; i++)
            {
                float totalH = bH * (i + 1);
                Color col;
                float alpha = 0.60f;

                if (totalH > maxSafeHeight)
                    col = new Color(1.00f, 0.20f, 0.15f, alpha);  // 빨강 — 위험
                else if (totalH > dangerHeight)
                    col = new Color(1.00f, 0.82f, 0.00f, alpha);  // 노랑 — 주의
                else
                    col = new Color(0.15f, 0.90f, 0.25f, alpha);  // 초록 — 안전

                var box = CreateBox(col);
                box.transform.position   = new Vector3(basePos.x, basePos.y + bH * i + bH * 0.5f, basePos.z);
                box.transform.localScale = new Vector3(boxWidth, bH, boxLength);
                _boxes.Add(box);
            }

            float finalHeight = bH * layers;
            string status = finalHeight > maxSafeHeight ? "⚠️ 높이 초과!"
                          : finalHeight > dangerHeight  ? "⚡ 주의 구간"
                          :                               "✅ 안전";
            SetStatus($"[{productName}]\n{layers}단  {finalHeight:F2}m  {status}");
        }

        private GameObject CreateBox(Color color)
        {
            var go   = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<BoxCollider>());
            go.GetComponent<Renderer>().material = MakeTransparent(color);
            return go;
        }

        private static Material MakeTransparent(Color color)
        {
            var sh  = Shader.Find("Universal Render Pipeline/Lit")
                   ?? Shader.Find("Standard");
            var mat = new Material(sh);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend",   0f);
            mat.SetFloat("_ZWrite",  0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
            mat.color       = color;
            return mat;
        }

        private void ClearBoxes()
        {
            foreach (var go in _boxes)
                if (go != null) Destroy(go);
            _boxes.Clear();
        }

        private void SetStatus(string msg)
        {
            if (statusText != null) statusText.text = msg;
        }
    

private void Awake()
        {
            if (statusText == null)  statusText  = GameObject.Find("SP_StatusText")?.GetComponent<UnityEngine.UI.Text>();
            if (placeButton == null) placeButton = GameObject.Find("SP_PlaceBtn")?.GetComponent<UnityEngine.UI.Button>();
            if (clearButton == null) clearButton = GameObject.Find("SP_ClearBtn")?.GetComponent<UnityEngine.UI.Button>();
        }
}
}
