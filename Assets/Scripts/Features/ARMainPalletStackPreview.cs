using System.Collections;
using System.Collections.Generic;
using ARLogistics.Data;
using ARLogistics.Detection;
using ARLogistics.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ARLogistics.Features
{
    /// <summary>ARMain-only virtual pallet placement and stacking preview.</summary>
    public sealed class ARMainPalletStackPreview : MonoBehaviour
    {
        private const int MaxPreviewObjects = 512;
        private const int StatusFontSize = 38;
        private const int ButtonFontSize = 34;
        private const float MinimumButtonHeight = 112f;
        private const float StatusPanelHeight = 280f;
        private const float LayoutButtonHeight = 96f;
        private const string PlacementMessage = "바닥을 터치하여 팔레트를 배치하세요";

        [Header("ARMain References")]
        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private Camera arCamera;
        [Header("Prefabs")]
        [SerializeField] private GameObject palletPrefab;
        [SerializeField] private GameObject boxPreviewPrefab;
        [Header("UI")]
        [SerializeField] private Text statusText;
        [SerializeField] private Button placeButton;
        [SerializeField] private Button resetButton;
        [SerializeField, Range(4, 64)] private int boxesPerFrame = 24;
        [Header("Pallet Size (m)")]
        [SerializeField, Min(0.1f)] private float palletWidth = 1.1f;
        [SerializeField, Min(0.1f)] private float palletLength = 1.1f;
        [SerializeField, Min(0.01f)] private float palletHeight = 0.15f;
        [Header("Preview Appearance")]
        [SerializeField, Range(0f, 0.05f)] private float boxGap = 0.01f;
        [SerializeField] private Color safeColor = new(0.1f, 0.9f, 0.25f, 0.85f);
        [SerializeField] private Color overHeightColor = new(1f, 0.15f, 0.1f, 0.9f);

        public GameObject PalletAnchor => palletAnchor;
        public Transform PalletTransform => palletTransform;
        public int EstimatedCapacity { get; private set; }
        public float RemainingSpacePercent { get; private set; }

        private readonly List<ARRaycastHit> raycastHits = new();
        private readonly List<GameObject> boxPool = new(MaxPreviewObjects);
        private readonly List<Renderer[]> boxPoolRenderers = new(MaxPreviewObjects);
        private readonly List<GameObject> activeBoxes = new(MaxPreviewObjects);
        private GameObject palletAnchor;
        private Transform palletTransform;
        private GameObject boxPoolRoot;
        private CameraFrameProvider cameraFrameProvider;
        private MaterialPropertyBlock safeColorBlock;
        private MaterialPropertyBlock overHeightColorBlock;
        private Coroutine spawnBoxesRoutine;

        private BoxMeasurement currentMeasurement;
        private bool hasMeasurement;
        private bool placementEnabled = true;
        private bool restoreInferenceOnDisable;
        private int lastProcessedTouchFrame = -1;
        private int displayedBoxCount;
        private float palletTopHeight;
        private Image statusBackground;
        private Button layoutButton;
        private Text layoutButtonText;
        private LayoutPreference layoutPreference = LayoutPreference.Auto;

        private enum LayoutPreference
        {
            Auto,
            Normal,
            Rotated
        }

        private void Awake()
        {
            ResolveReferences();
            ConfigureFriendlyUI();
            InitializeBoxPool();
        }

        private void OnEnable()
        {
            restoreInferenceOnDisable = cameraFrameProvider != null && cameraFrameProvider.IsInferenceEnabled;
            cameraFrameProvider?.SetInferenceEnabled(false);
            placeButton?.onClick.AddListener(EnablePlacement);
            resetButton?.onClick.AddListener(ResetSimulation);
            layoutButton?.onClick.AddListener(CycleLayoutPreference);
            ResetSimulation();
        }

        private void OnDisable()
        {
            if (restoreInferenceOnDisable)
                cameraFrameProvider?.SetInferenceEnabled(true);
            placeButton?.onClick.RemoveListener(EnablePlacement);
            resetButton?.onClick.RemoveListener(ResetSimulation);
            layoutButton?.onClick.RemoveListener(CycleLayoutPreference);
            ClearRuntimeObjects();
        }

        private void OnDestroy()
        {
            if (boxPoolRoot != null) Destroy(boxPoolRoot);
        }

        private void Update()
        {
            if (lastSafeArea != Screen.safeArea || lastScreenSize != new Vector2(Screen.width, Screen.height))
            {
                lastSafeArea = Screen.safeArea;
                lastScreenSize = new Vector2(Screen.width, Screen.height);
                ui?.Layout();
            }
            if (!placementEnabled || palletAnchor != null) return;
            if (!hasMeasurement || plan == null || plan.Total == 0) return;
            if (!TryGetPointerDown(out Vector2 screenPosition, out int pointerId)) return;
            if (lastProcessedTouchFrame == Time.frameCount) return;
            lastProcessedTouchFrame = Time.frameCount;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointerId)) return;
            TryPlacePallet(screenPosition);
        }

        private void ResolveReferences()
        {
            if (raycastManager == null) raycastManager = FindFirstObjectByType<ARRaycastManager>();
            if (planeManager == null) planeManager = FindFirstObjectByType<ARPlaneManager>();
            if (arCamera == null) arCamera = Camera.main;
            if (cameraFrameProvider == null) cameraFrameProvider = FindFirstObjectByType<CameraFrameProvider>();
            if (statusText == null) statusText = GameObject.Find("SP_StatusText")?.GetComponent<Text>();
            if (placeButton == null) placeButton = GameObject.Find("SP_PlaceBtn")?.GetComponent<Button>();


            palletWidth = ARLogistics.AppSettings.SanitizePalletDimension(
                ARLogistics.AppSettings.PalletWidth, 1.2f);
            palletLength = ARLogistics.AppSettings.SanitizePalletDimension(
                ARLogistics.AppSettings.PalletLength, 1.0f);
            ARLogistics.AppSettings.PalletWidth = palletWidth;
            ARLogistics.AppSettings.PalletLength = palletLength;
            if (resetButton == null) resetButton = GameObject.Find("SP_ClearBtn")?.GetComponent<Button>();
        }

        public void EnablePlacement()
        {
            if (!hasMeasurement && !TryLoadMeasurement(true))
                return;

            RefreshPlan();
            if (plan.Total == 0) { ShowSummary(); return; }
            if (palletAnchor != null)
            {
                GenerateStackPreview();
                return;
            }

            placementEnabled = true;
            ShowSummary();
        }

        public void ResetSimulation()
        {
            ClearRuntimeObjects();
            currentMeasurement = default;
            hasMeasurement = false;
            EstimatedCapacity = 0;
            RemainingSpacePercent = 0f;
            displayedBoxCount = 0;
            palletTopHeight = palletHeight;
            placementEnabled = true;
            orientation = StackOrientation.Recommended;
            if (TryLoadMeasurement(false)) { RefreshPlan(); ShowSummary(); }
            else
            {
                plan = null;
                ui?.ShowPlans(null, orientation);
                ui?.SetAction("측정이 필요해요", false);
                ui?.SetTitle("먼저 상자를 확인해 주세요");
                SetStatus("하단의 측정 화면에서 화물을 인식해 주세요.\n측정 결과를 저장하면 여기서 적재 모습을 확인할 수 있어요.");
            }
        }

        private void TryPlacePallet(Vector2 screenPosition)
        {
            if (raycastManager == null) { SetStatus("바닥을 인식할 준비가 되지 않았어요. 카메라 상태를 확인해 주세요."); return; }
            raycastHits.Clear();
            if (!raycastManager.Raycast(screenPosition, raycastHits, TrackableType.PlaneWithinPolygon))
            {
                SetStatus("카메라를 천천히 움직여 바닥을 비춰 주세요. 바닥이 인식되면 놓을 위치를 터치해 주세요.");
                return;
            }
            if (!TrySelectHorizontalFloorHit(out ARRaycastHit floorHit))
            {
                SetStatus("벽 대신 평평한 바닥을 선택해 주세요.");
                return;
            }

            Pose hitPose = floorHit.pose;
            CreatePallet(new Pose(hitPose.position, GetHorizontalFacingRotation(hitPose.rotation)));
            CreateSafetyHeightGuide();
            placementEnabled = false;
            GenerateStackPreview();
        }

        private void CreatePallet(Pose pose)
        {
            palletAnchor = new GameObject("ARMain_VirtualPalletAnchor");
            palletAnchor.transform.SetPositionAndRotation(pose.position, pose.rotation);
            palletTransform = palletAnchor.transform;
            GameObject visual;
            if (palletPrefab != null)
            {
                visual = Instantiate(palletPrefab, palletTransform);
                visual.name = "VirtualPallet";
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
            }
            else
            {
                visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.name = "VirtualPallet";
                visual.transform.SetParent(palletTransform, false);
                visual.transform.localPosition = Vector3.up * (palletHeight * 0.5f);
            }
            visual.transform.localScale = new Vector3(palletWidth, palletHeight, palletLength);
            Collider palletCollider = visual.GetComponent<Collider>();
            if (palletCollider != null) Destroy(palletCollider);

            Renderer[] palletRenderers = visual.GetComponentsInChildren<Renderer>();
            if (TryGetRendererBounds(palletRenderers, out Bounds bounds))
            {
                if (bounds.size.y > 0.001f)
                {
                    Vector3 scale = visual.transform.localScale;
                    scale.y *= palletHeight / bounds.size.y;
                    visual.transform.localScale = scale;
                    TryGetRendererBounds(palletRenderers, out bounds);
                }

                float floorOffset = pose.position.y - bounds.min.y;
                visual.transform.position += Vector3.up * floorOffset;
                palletTopHeight = bounds.max.y + floorOffset - pose.position.y;
            }
            else
            {
                palletTopHeight = palletHeight;
            }

            foreach (Renderer palletRenderer in palletRenderers)
            {
                palletRenderer.shadowCastingMode = ShadowCastingMode.Off;
                palletRenderer.receiveShadows = false;
                palletRenderer.lightProbeUsage = LightProbeUsage.Off;
                palletRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }

        private void GenerateStackPreview()
        {
            ClearBoxes();
            EstimatedCapacity = 0;
            RemainingSpacePercent = 0f;

            if (palletTransform == null) return;
            if (!hasMeasurement && !TryLoadMeasurement(false))
            {
                SetStatus("상자 크기를 먼저 측정해주세요");
                return;
            }

            var product = new ProductDimensions(
                currentMeasurement.WidthM,
                currentMeasurement.DepthM,
                currentMeasurement.HeightM);

            if (!TryCalculateLayout(product, out StackLayout layout))
            {
                if (heightGuide != null) Destroy(heightGuide);
                ShowSummary();
                return;
            }

            long perLayer = (long)layout.columns * layout.rows;
            long totalPreviewBoxes = perLayer * layout.previewLayers;
            int visualBoxCount = totalPreviewBoxes > MaxPreviewObjects
                ? MaxPreviewObjects
                : (int)totalPreviewBoxes;
            long estimatedCapacity = perLayer * layout.safeLayers;
            EstimatedCapacity = estimatedCapacity > int.MaxValue
                ? int.MaxValue
                : (int)estimatedCapacity;
            double usedArea = perLayer * (double)layout.boxWidth * layout.boxLength;
            float palletArea = palletWidth * palletLength;
            float usagePercent = palletArea > 0f
                ? Mathf.Clamp01((float)(usedArea / palletArea)) * 100f
                : 0f;
            RemainingSpacePercent = 100f - usagePercent;

            string finalStatus =
                $"<b>{GetLayoutTitle()} · 안전 적재</b>\n" +
                $"{FormatMeasurement()}\n" +
                $"{layout.columns} × {layout.rows}개 · 한 층 {perLayer}개 · 총 {estimatedCapacity:N0}개\n" +
                $"안전 {layout.safeLayers}층 · 높이 {palletHeight + layout.safeLayers * product.height:F2}m\n" +
                $"공간 활용 {usagePercent:F1}% · 여유 {RemainingSpacePercent:F1}%" +
                (visualBoxCount < totalPreviewBoxes
                    ? $"\n화면에는 {visualBoxCount:N0}개만 표시해요. 계산 수량은 {totalPreviewBoxes:N0}개예요."
                    : string.Empty);

            ui?.SetAction("처음부터 보기", true);
            ui?.SetTitle("아래에서부터 쌓고 있어요");
            SetStatus(finalStatus);
            spawnBoxesRoutine = StartCoroutine(
                SpawnBoxesOverFrames(product, layout, visualBoxCount, finalStatus));
        }

        private bool TryCalculateLayout(ProductDimensions product, out StackLayout layout)
        {
            int normalColumns = Mathf.FloorToInt(palletWidth / product.width);
            int normalRows = Mathf.FloorToInt(palletLength / product.length);
            int rotatedColumns = Mathf.FloorToInt(palletWidth / product.length);
            int rotatedRows = Mathf.FloorToInt(palletLength / product.width);
            bool rotate = layoutPreference == LayoutPreference.Rotated ||
                          (layoutPreference == LayoutPreference.Auto &&
                           (long)rotatedColumns * rotatedRows > (long)normalColumns * normalRows);
            int columns = rotate ? rotatedColumns : normalColumns;
            int rows = rotate ? rotatedRows : normalRows;
            float width = rotate ? product.length : product.width;
            float length = rotate ? product.width : product.length;
            int safeLayers = Mathf.Max(0, Mathf.FloorToInt((recommendedStackHeight - palletHeight) / product.height));
            int previewLayers = Mathf.Max(safeLayers, Mathf.FloorToInt((previewHeight - palletHeight) / product.height));
            layout = new StackLayout(columns, rows, safeLayers, previewLayers, width, length);
            return columns > 0 && rows > 0 && previewLayers > 0;
        }

        private IEnumerator SpawnBoxesOverFrames(
            ProductDimensions product,
            StackLayout layout,
            int visualBoxCount,
            string finalStatus)
        {
            float startX = -layout.columns * layout.boxWidth * 0.5f + layout.boxWidth * 0.5f;
            float startZ = -layout.rows * layout.boxLength * 0.5f + layout.boxLength * 0.5f;
            Vector3 scale = new(
                Mathf.Max(0.01f, layout.boxWidth - boxGap),
                Mathf.Max(0.01f, product.height - boxGap),
                Mathf.Max(0.01f, layout.boxLength - boxGap));
            displayedBoxCount = 0;

            for (int layer = 0; layer < layout.previewLayers; layer++)
            {
                if (displayedBoxCount >= visualBoxCount) break;
                bool isOverHeight = layer >= layout.safeLayers;

                SetStatus(
                    $"<b>적재 시뮬레이션 생성 중</b>\n" +
                    $"{layer + 1} / {layout.previewLayers}층 · {displayedBoxCount:N0}개 배치됨");

                for (int row = 0; row < layout.rows; row++)
                for (int column = 0; column < layout.columns; column++)
                {
                    if (displayedBoxCount >= visualBoxCount) break;

                    Vector3 position = new(
                        startX + column * layout.boxWidth,
                        palletTopHeight + product.height * (layer + 0.5f),
                        startZ + row * layout.boxLength);

                    GameObject box = GetPooledBox(displayedBoxCount);
                    box.transform.SetParent(palletTransform, false);
                    box.transform.localPosition = position;
                    box.transform.localRotation = Quaternion.identity;
                    box.transform.localScale = scale;
                    ApplyBoxColor(displayedBoxCount, isOverHeight);
                    box.SetActive(true);
                    activeBoxes.Add(box);
                    displayedBoxCount++;

                    if (displayedBoxCount % Mathf.Max(1, boxesPerFrame) == 0)
                        yield return null;
                }

                // Let the user follow the stacking order one layer at a time.
                yield return new WaitForSeconds(0.08f);
            }

            spawnBoxesRoutine = null;
            ui?.SetTitle("적재 미리보기");
            SetStatus(finalStatus);
        }

        private void RefreshPlan()
        {
            palletWidth = AppSettings.PalletWidth;
            palletLength = AppSettings.PalletLength;
            palletHeight = StackingCalculator.PalletHeight;
            var plans = new StackingPlan[3];
            float weight = ProductSpecTable.Get(currentMeasurement.ClassId).WeightKg;
            for (int i = 0; i < plans.Length; i++)
                plans[i] = StackingCalculator.Calculate(currentMeasurement.WidthM, currentMeasurement.DepthM,
                    currentMeasurement.HeightM, weight, palletWidth, palletLength,
                    AppSettings.CeilingHeightM, AppSettings.PalletMaxLoadKg, (StackOrientation)i);
            plan = plans[(int)orientation];
            EstimatedCapacity = plan.Total;
            RemainingSpacePercent = 100 - plan.Utilization;
            ui?.ShowPlans(plans, orientation);
        }

        private void SelectLayout(StackOrientation selected)
        {
            if (!hasMeasurement) return;
            orientation = selected;
            RefreshPlan();
            if (palletTransform != null && plan.Total > 0) GenerateStackPreview();
            else { ClearBoxes(); if (heightGuide != null) Destroy(heightGuide); ShowSummary(); }
        }

        private string SummaryText()
        {
            string size = $"등록 규격 {currentMeasurement.WidthM * 100:F0} × {currentMeasurement.DepthM * 100:F0} × {currentMeasurement.HeightM * 100:F0}cm";
            if (plan.Total == 0) return size + "\n\n" + plan.Message + "\n방향을 바꾸거나 홈에서 적재 조건을 확인해 주세요.";
            return $"<b>이번 조건에서는 {plan.Layers}단으로 미리 봐요</b>\n" +
                $"한 단에 {plan.PerLayer:N0}개 · 팔레트당 총 {plan.Total:N0}개\n" +
                $"전체 높이 {plan.StackHeight:F2}m · 화물 무게 {plan.TotalWeight:F1}kg\n" +
                $"바닥 사용률 {plan.Utilization:F0}%\n" + size + "\n" + plan.Message +
                "\n실제 적재 전 포장 강도·결속을 확인하세요.";
        }

        private void ShowSummary()
        {
            ui?.SetTitle(plan.Total > 0 ? "어떻게 쌓을지 확인해 보세요" : "지금 조건으로는 쌓기 어려워요");
            ui?.SetAction(palletTransform == null ? "바닥에 배치" : "상자 쌓기", plan.Total > 0);
            SetStatus(SummaryText() + (plan.Total > 0 && palletTransform == null ? "\n\n" + PlacementMessage : ""));
        }

        private void CreateHeightGuide()
        {
            if (heightGuide != null) Destroy(heightGuide);
            heightGuide = new GameObject("계산된 적재 높이");
            heightGuide.transform.SetParent(palletTransform, false);
            heightGuide.transform.localPosition = Vector3.up * plan.StackHeight;
            var materialSource = boxPreviewPrefab != null ? boxPreviewPrefab.GetComponentInChildren<Renderer>() : null;
            var block = CreateColorBlock(new Color(1f, 0.7f, 0.1f, 1));
            for (int i = 0; i < 4; i++)
            {
                var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                edge.transform.SetParent(heightGuide.transform, false);
                edge.transform.localPosition = i < 2 ? new Vector3(0, 0, (i == 0 ? -1 : 1) * palletLength / 2)
                    : new Vector3((i == 2 ? -1 : 1) * palletWidth / 2, 0, 0);
                edge.transform.localScale = i < 2 ? new Vector3(palletWidth, 0.012f, 0.012f) : new Vector3(0.012f, 0.012f, palletLength);
                Destroy(edge.GetComponent<Collider>());
                var renderer = edge.GetComponent<Renderer>();
                if (materialSource != null) renderer.sharedMaterial = materialSource.sharedMaterial;
                renderer.SetPropertyBlock(block);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        private void InitializeBoxPool()
        {
            boxPoolRoot = new GameObject("ARMain_StackPreviewBoxPool");
            boxPoolRoot.SetActive(false);
            safeColorBlock = CreateColorBlock(safeColor);
            overHeightColorBlock = CreateColorBlock(overHeightColor);
        }

        private static MaterialPropertyBlock CreateColorBlock(Color color)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            return block;
        }

        private GameObject GetPooledBox(int index)
        {
            while (boxPool.Count <= index)
            {
                GameObject box;
                if (boxPreviewPrefab != null)
                {
                    box = Instantiate(boxPreviewPrefab, boxPoolRoot.transform);
                }
                else
                {
                    box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    box.transform.SetParent(boxPoolRoot.transform, false);
                }

                box.name = $"StackPreviewBox_{boxPool.Count + 1}";
                foreach (Collider targetCollider in box.GetComponentsInChildren<Collider>(true))
                    targetCollider.enabled = false;

                Renderer[] renderers = box.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer boxRenderer in renderers)
                {
                    boxRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    boxRenderer.receiveShadows = false;
                    boxRenderer.lightProbeUsage = LightProbeUsage.Off;
                    boxRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }

                box.SetActive(false);
                boxPool.Add(box);
                boxPoolRenderers.Add(renderers);
            }

            return boxPool[index];
        }

        private void ApplyBoxColor(int poolIndex, bool isOverHeight)
        {
            MaterialPropertyBlock block = isOverHeight ? overHeightColorBlock : safeColorBlock;
            foreach (Renderer boxRenderer in boxPoolRenderers[poolIndex])
                boxRenderer.SetPropertyBlock(block);
        }

        private Quaternion GetHorizontalFacingRotation(Quaternion fallback)
        {
            if (arCamera == null) return fallback;
            Vector3 forward = Vector3.ProjectOnPlane(arCamera.transform.forward, Vector3.up);
            return forward.sqrMagnitude < 0.001f ? fallback : Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private void ClearRuntimeObjects()
        {
            ClearBoxes();
            if (palletAnchor != null) Destroy(palletAnchor);
            palletAnchor = null;
            palletTransform = null;
        }

        private void ClearBoxes()
        {
            if (spawnBoxesRoutine != null)
            {
                StopCoroutine(spawnBoxesRoutine);
                spawnBoxesRoutine = null;
            }

            foreach (GameObject box in activeBoxes)
            {
                if (box == null) continue;
                box.SetActive(false);
                if (boxPoolRoot != null)
                    box.transform.SetParent(boxPoolRoot.transform, false);
            }

            activeBoxes.Clear();
            displayedBoxCount = 0;
        }

        private bool TrySelectHorizontalFloorHit(out ARRaycastHit floorHit)
        {
            foreach (ARRaycastHit hit in raycastHits)
            {
                ARPlane plane = planeManager != null ? planeManager.GetPlane(hit.trackableId) : null;
                if (plane != null)
                {
                    if (plane.alignment != PlaneAlignment.HorizontalUp) continue;
                }
                else if (Vector3.Dot(hit.pose.up, Vector3.up) < 0.9f)
                {
                    continue;
                }

                floorHit = hit;
                return true;
            }

            floorHit = default;
            return false;
        }

        private static bool TryGetRendererBounds(Renderer[] renderers, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (Renderer targetRenderer in renderers)
            {
                if (targetRenderer == null || !targetRenderer.enabled) continue;
                if (!found)
                {
                    bounds = targetRenderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(targetRenderer.bounds);
                }
            }

            return found;
        }

        private void ConfigureFriendlyUI()
        {
            if (statusText != null)
            {
                statusText.fontSize = Mathf.Max(statusText.fontSize, StatusFontSize);
                statusText.fontStyle = FontStyle.Normal;
                statusText.alignment = TextAnchor.MiddleLeft;
                statusText.lineSpacing = 1.15f;
                statusText.raycastTarget = false;
                statusText.supportRichText = true;

                RectTransform statusRect = statusText.rectTransform;
                statusRect.offsetMin = new Vector2(32f, 20f);
                statusRect.offsetMax = new Vector2(-32f, -20f);

                statusBackground = statusText.GetComponentInParent<Image>();
                if (statusBackground != null)
                {
                    statusBackground.color = new Color(0.035f, 0.055f, 0.09f, 0.94f);
                    RectTransform panelRect = statusBackground.rectTransform;
                    panelRect.sizeDelta = new Vector2(panelRect.sizeDelta.x, StatusPanelHeight);
                    panelRect.anchoredPosition = new Vector2(panelRect.anchoredPosition.x, -StatusPanelHeight * 0.5f);
                }
            }

            ConfigureActionButton(placeButton, "📦  적재 시작", new Color(0.08f, 0.48f, 0.92f, 1f));
            ConfigureActionButton(resetButton, "↻  다시 시작", new Color(0.25f, 0.28f, 0.34f, 1f));
            CreateLayoutButton();
        }

        private void CreateLayoutButton()
        {
            Transform panel = transform;
            GameObject existing = GameObject.Find("SP_LayoutBtn");
            if (existing != null)
            {
                layoutButton = existing.GetComponent<Button>();
                layoutButtonText = existing.GetComponentInChildren<Text>(true);
                UpdateLayoutButtonLabel();
                return;
            }

            var buttonObject = new GameObject("SP_LayoutBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(panel, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.04f, 0f);
            rect.anchorMax = new Vector2(0.96f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 360f);
            rect.sizeDelta = new Vector2(0f, LayoutButtonHeight);

            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.10f, 0.16f, 0.27f, 0.98f);
            layoutButton = buttonObject.GetComponent<Button>();
            layoutButton.targetGraphic = background;

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(20f, 8f);
            labelRect.offsetMax = new Vector2(-20f, -8f);

            layoutButtonText = labelObject.GetComponent<Text>();
            layoutButtonText.font = statusText != null && statusText.font != null
                ? statusText.font
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            layoutButtonText.fontSize = 32;
            layoutButtonText.fontStyle = FontStyle.Bold;
            layoutButtonText.alignment = TextAnchor.MiddleCenter;
            layoutButtonText.color = Color.white;
            layoutButtonText.raycastTarget = false;
            UpdateLayoutButtonLabel();
        }

        private void CycleLayoutPreference()
        {
            layoutPreference = layoutPreference switch
            {
                LayoutPreference.Auto => LayoutPreference.Normal,
                LayoutPreference.Normal => LayoutPreference.Rotated,
                _ => LayoutPreference.Auto
            };
            UpdateLayoutButtonLabel();

            if (palletTransform != null)
                GenerateStackPreview();
            else if (hasMeasurement)
                SetStatus($"<b>{GetLayoutTitle()}</b>\n{FormatMeasurement()}\n팔레트를 배치하면 이 방식으로 계산합니다");
        }

        private void UpdateLayoutButtonLabel()
        {
            if (layoutButtonText != null)
                layoutButtonText.text = $"⇄  배치안: {GetLayoutTitle()}";
        }

        private string GetLayoutTitle()
        {
            return layoutPreference switch
            {
                LayoutPreference.Normal => "방향 고정",
                LayoutPreference.Rotated => "90° 회전",
                _ => "효율 추천"
            };
        }

        private void CreateSafetyHeightGuide()
        {
            if (palletTransform == null) return;

            GameObject guideRoot = new("권장 적재 높이 1.8m");
            guideRoot.transform.SetParent(palletTransform, false);
            guideRoot.transform.localPosition = Vector3.up * recommendedStackHeight;
            Color guideColor = new(1f, 0.72f, 0.08f, 0.95f);
            MaterialPropertyBlock guideBlock = CreateColorBlock(guideColor);
            const float thickness = 0.018f;

            CreateGuideEdge(guideRoot.transform, new Vector3(0f, 0f, palletLength * 0.5f), new Vector3(palletWidth, thickness, thickness), guideBlock);
            CreateGuideEdge(guideRoot.transform, new Vector3(0f, 0f, -palletLength * 0.5f), new Vector3(palletWidth, thickness, thickness), guideBlock);
            CreateGuideEdge(guideRoot.transform, new Vector3(palletWidth * 0.5f, 0f, 0f), new Vector3(thickness, thickness, palletLength), guideBlock);
            CreateGuideEdge(guideRoot.transform, new Vector3(-palletWidth * 0.5f, 0f, 0f), new Vector3(thickness, thickness, palletLength), guideBlock);
        }

        private static void CreateGuideEdge(Transform parent, Vector3 position, Vector3 scale, MaterialPropertyBlock block)
        {
            GameObject edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            edge.name = "SafetyHeightEdge";
            edge.transform.SetParent(parent, false);
            edge.transform.localPosition = position;
            edge.transform.localScale = scale;
            Collider edgeCollider = edge.GetComponent<Collider>();
            if (edgeCollider != null) Destroy(edgeCollider);
            Renderer edgeRenderer = edge.GetComponent<Renderer>();
            if (edgeRenderer != null)
            {
                edgeRenderer.SetPropertyBlock(block);
                edgeRenderer.shadowCastingMode = ShadowCastingMode.Off;
                edgeRenderer.receiveShadows = false;
            }
        }

        private static void ConfigureActionButton(Button button, string label, Color backgroundColor)
        {
            if (button == null) return;

            Image image = button.targetGraphic as Image ?? button.GetComponent<Image>();
            if (image != null) image.color = backgroundColor;

            RectTransform buttonRect = button.transform as RectTransform;
            if (buttonRect != null && buttonRect.sizeDelta.y < MinimumButtonHeight)
                buttonRect.sizeDelta = new Vector2(buttonRect.sizeDelta.x, MinimumButtonHeight);

            Text buttonText = button.GetComponentInChildren<Text>(true);
            if (buttonText == null) return;
            buttonText.text = label;
            buttonText.fontSize = Mathf.Max(buttonText.fontSize, ButtonFontSize);
            buttonText.fontStyle = FontStyle.Bold;
            buttonText.alignment = TextAnchor.MiddleCenter;
            buttonText.raycastTarget = false;
            buttonText.resizeTextForBestFit = true;
            buttonText.resizeTextMinSize = 26;
            buttonText.resizeTextMaxSize = 42;
        }

        private void SetStatus(string message)
        {
            if (statusText == null) return;
            statusText.text = message;

            if (statusBackground == null) return;
            bool isDanger = message.Contains("초과") || message.Contains("실패") || message.Contains("오류");
            bool isComplete = message.Contains("완료") || message.Contains("안전");
            statusBackground.color = isDanger
                ? new Color(0.38f, 0.055f, 0.07f, 0.96f)
                : isComplete
                    ? new Color(0.035f, 0.22f, 0.14f, 0.96f)
                    : new Color(0.035f, 0.055f, 0.09f, 0.94f);
        }

        private static bool TryGetPointerDown(out Vector2 position, out int pointerId)
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null)
                foreach (UnityEngine.InputSystem.Controls.TouchControl touch in Touchscreen.current.touches)
                {
                    if (!touch.press.wasPressedThisFrame) continue;
                    position = touch.position.ReadValue();
                    pointerId = touch.touchId.ReadValue();
                    return true;
                }
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                position = Mouse.current.position.ReadValue();
                pointerId = -1;
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            {
                Touch touch = Input.GetTouch(0);
                position = touch.position;
                pointerId = touch.fingerId;
                return true;
            }
            if (Input.GetMouseButtonDown(0))
            {
                position = Input.mousePosition;
                pointerId = -1;
                return true;
            }
#endif
            position = default;
            pointerId = -1;
            return false;
        }

        private readonly struct ProductDimensions
        {
            public readonly float width, length, height;
            public ProductDimensions(float width, float length, float height)
            {
                this.width = Mathf.Max(0.01f, width);
                this.length = Mathf.Max(0.01f, length);
                this.height = Mathf.Max(0.01f, height);
            }
        }

        private readonly struct StackLayout
        {
            public readonly int columns, rows, safeLayers, previewLayers;
            public readonly float boxWidth, boxLength;
            public StackLayout(int columns, int rows, int safeLayers, int previewLayers, float boxWidth, float boxLength)
            {
                this.columns = columns;
                this.rows = rows;
                this.safeLayers = safeLayers;
                this.previewLayers = previewLayers;
                this.boxWidth = boxWidth;
                this.boxLength = boxLength;
            }
        }
    

        private bool TryLoadMeasurement(bool updateStatus)
        {
            if (!BoxMeasurementStore.TryGet(out currentMeasurement))
            {
                hasMeasurement = false;
                if (updateStatus) SetStatus("상자 크기를 먼저 측정해주세요");
                return false;
            }

            hasMeasurement = true;
            if (updateStatus)
                SetStatus($"상자 크기 불러오기 완료\n{FormatMeasurement()}\n{PlacementMessage}");
            return true;
        }

        private string FormatMeasurement()
        {
            string source = string.IsNullOrEmpty(currentMeasurement.SourceName)
                ? string.Empty
                : $" ({currentMeasurement.SourceName})";
            return $"상자 {currentMeasurement.WidthM:F2}m × {currentMeasurement.DepthM:F2}m × {currentMeasurement.HeightM:F2}m{source}";
        }
}
}
