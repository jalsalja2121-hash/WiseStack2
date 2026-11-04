using System;
using System.Collections;
using System.Text;
using ARLogistics.Data;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace ARLogistics.UI
{
    /// <summary>Recommendation presentation only; all numbers come from the shared deterministic calculator.</summary>
    public sealed class WarehouseRecommendationView : MonoBehaviour
    {
        private RectTransform _content, _viewport, _header, _nav;
        private Canvas _canvas;
        private ScrollRect _scroll;
        private Font _font;
        private Rect _lastSafe;
        private Vector2 _lastSize;
        private static readonly string[] Names = { "A · 표준 적재", "B · 공간 효율", "C · 낮은 높이" };

        private IEnumerator Start()
        {
            Initialize();
            State("분석 중", "등록된 규격과 적재 조건을 비교하고 있습니다.");
            yield return null;
            Refresh();
        }

        private void Initialize()
        {
            if (_content != null) return;
            _canvas = GetComponentInParent<Canvas>();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            foreach (var text in GetComponentsInChildren<Text>(true))
                if (text.name == "TitleLbl")
                {
                    if (text.font != null) _font = text.font;
                    text.gameObject.SetActive(false);
                }
            foreach (var rect in GetComponentsInChildren<RectTransform>(true))
            {
                if (rect.name == "OverlayPanel") rect.gameObject.SetActive(false);
                if (rect.name == "NavBar") _nav = rect;
            }
            var background = GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            if (background != null) background.color = WiseStackTheme.Background;
            var oldHeader = WiseStackUI.Find<RectTransform>(transform,"TopBar");
            if (oldHeader != null) oldHeader.gameObject.SetActive(false);
            _header = Rect("RecommendationHeader", transform);
            _header.gameObject.AddComponent<Image>().color = WiseStackTheme.Background;
            var heading = Label(_header, "최적의 적재 전략", 34, WiseStackTheme.Ink, true);
            heading.alignment = TextAnchor.MiddleLeft;
            Stretch(heading.rectTransform);
            _viewport = Rect("RecommendationViewport", transform);
            _viewport.gameObject.AddComponent<Image>().color = WiseStackTheme.Background;
            _viewport.gameObject.AddComponent<RectMask2D>();
            _scroll = _viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.viewport = _viewport;
            _scroll.scrollSensitivity = 35;
            _content = Rect("RecommendationContent", _viewport);
            _content.anchorMin = new Vector2(0, 1);
            _content.anchorMax = Vector2.one;
            _content.pivot = new Vector2(0.5f, 1);
            _content.sizeDelta = Vector2.zero;
            Vertical(_content, 14, 0);
            _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = _content;
            Position();
        }

        private void LateUpdate()
        {
            if (_viewport == null) return;
            var size = ((RectTransform)transform).rect.size;
            if (_lastSafe != Screen.safeArea || _lastSize != size) Position();
        }

        private void Position()
        {
            PositionForScreen(Screen.safeArea, new Vector2(Screen.width, Screen.height));
        }

        private void PositionForScreen(Rect safeArea, Vector2 screenSize)
        {
            var canvasRect = (RectTransform)transform;
            _lastSafe = safeArea;
            _lastSize = canvasRect.rect.size;
            float scale = _canvas != null ? Mathf.Max(.01f, _canvas.scaleFactor) : 1;
            float bottom = safeArea.yMin / scale;
            float top = (screenSize.y - safeArea.yMax) / scale;
            float left = safeArea.xMin / scale;
            float right = (screenSize.x - safeArea.xMax) / scale;
            // Limit line length on tablets/landscape, while preserving the device's safe area.
            float inset = Mathf.Max(24, (canvasRect.rect.width - left - right - 760) / 2);
            _header.anchorMin = new Vector2(0, 1);
            _header.anchorMax = Vector2.one;
            _header.offsetMin = new Vector2(left + inset, -top - 84);
            _header.offsetMax = new Vector2(-right - inset, -top);
            float navHeight = 0;
            if (_nav != null)
            {
                navHeight = WiseStackTheme.NavigationHeight;
                _nav.anchorMin = Vector2.zero;
                _nav.anchorMax = new Vector2(1, 0);
                _nav.pivot = new Vector2(.5f, 0);
                _nav.offsetMin = new Vector2(left, bottom);
                _nav.offsetMax = new Vector2(-right, bottom + navHeight);
                WiseStackUI.Navigation(transform,"DangerOverlayBtn");
            }
            _viewport.anchorMin = Vector2.zero;
            _viewport.anchorMax = Vector2.one;
            _viewport.offsetMin = new Vector2(left + inset, bottom + navHeight + 12);
            _viewport.offsetMax = new Vector2(-right - inset, -top - 84);
        }

        private void Clear()
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                var child = _content.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
            _scroll.StopMovement();
            _scroll.verticalNormalizedPosition = 1;
        }

        private void State(string title, string message)
        {
            Clear();
            var card = Card("RecommendationState");
            Label(card, title, 32, WiseStackTheme.Navy, true);
            Label(card, message, 26, WiseStackTheme.Muted);
        }

        private void Refresh()
        {
            try
            {
                if (!BoxMeasurementStore.TryGet(out _))
                {
                    WarehouseRecommendationStore.Clear();
                    State("등록된 화물이 없습니다", "측정 탭에서 화물을 분류하고 규격을 저장해 주세요.");
                    Button(_content, "측정하러 가기", () => SceneManager.LoadScene("MeasureScene"));
                    return;
                }
                if (!WarehouseStrategySession.TryBuild(out var source, out var comparison, out string error))
                {
                    WarehouseRecommendationStore.Clear();
                    State("입력 확인이 필요합니다", error);
                    Button(_content, "홈에서 조건 확인", () => SceneManager.LoadScene("HomeScene"));
                    return;
                }
                Present(source, comparison);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                State("분석을 완료하지 못했습니다", "잠시 후 다시 시도해 주세요.");
                Button(_content, "다시 분석", Refresh);
            }
        }

        // Also used by isolated Editor previews: no stored measurements or API calls are changed.
        public void Present(WarehouseRecommendation source, WarehouseStrategyComparison comparison)
        {
            Initialize();
            Clear();
            var decision = WarehouseStrategyEvaluator.Evaluate(source, comparison);
            if (!decision.HasRecommendation)
            {
                State("추천 가능한 전략이 없습니다", decision.Reason);
                if (comparison != null)
                    for (int i = 0; i < 3; i++)
                        Label(_content, Names[i] + " · " + (decision.Evaluations[i]?.Exclusion ?? "배치 가능한 팔레트 없음"), 26, WiseStackTheme.Muted);
                Button(_content, "홈에서 조건 확인", () => SceneManager.LoadScene("HomeScene"));
                if (source != null && comparison != null) Details(source, comparison, decision);
                return;
            }
            var winner = comparison.Get(decision.Index);
            var hero = Card("RecommendedStrategy", true);
            Label(hero, decision.RequiresLoadConfirmation ? "조건부 예상안 · 하중 확인 필요" : "분석 완료  ·  최종 추천", 22, WiseStackTheme.Success, true);
            Label(hero, Names[decision.Index], 34, WiseStackTheme.Ink, true);
            Label(hero, decision.Reason, 26, WiseStackTheme.Muted);
            var metrics = Row(hero, "KeyMetrics", 12);
            Metric(metrics, "팔레트당 예상", $"{winner.PerPallet:N0}개");
            Metric(metrics, "적재 층수", $"{winner.Layers}층");
            Metric(metrics, "팔레트 포함", $"{winner.StackHeightM:F2}m");
            Label(hero, $"바닥면 활용률 {winner.FloorUtilizationPercent:F1}%", 24, WiseStackTheme.Muted);
            Button(hero, "추천 전략 AR 보기", () =>
            {
                WarehouseStrategySession.OpenInAr(decision.Index);
                SceneManager.LoadScene("ARMain");
            });

            var table = Card("StrategyComparison");
            Label(table, "전략별 핵심 비교", 28, WiseStackTheme.Navy, true);
            for (int i = 0; i < 3; i++)
            {
                var option = comparison.Get(i);
                var evaluation = decision.Evaluations[i];
                var row = Row(table, "Strategy" + i, 8);
                var name = Rect("StrategyName", row); Flexible(name, 1.35f); Vertical(name, 3, 0);
                Label(name, Names[i], 26, WiseStackTheme.Navy, true);
                Label(name, option?.Orientation == StackOrientation.Rotated ? "90° 회전" : "기본 방향", 22, WiseStackTheme.Muted);
                var count = Rect("Count", row); Flexible(count, 1); Vertical(count, 3, 0);
                Label(count, evaluation.Eligible ? $"{option.PerPallet:N0}개" : "적재 불가", 28, WiseStackTheme.Navy, true);
                Label(count, "팔레트당", 22, WiseStackTheme.Muted);
                var height = Rect("Height", row); Flexible(height, 1); Vertical(height, 3, 0);
                Label(height, evaluation.Eligible ? $"{option.StackHeightM:F2}m" : "—", 28, WiseStackTheme.Navy, true);
                Label(height, evaluation.Eligible ? $"{option.Layers}층" : "제외", 22, WiseStackTheme.Muted);
                var status = Label(row, i == decision.Index ? "추천" : !evaluation.Eligible ? "제외" :
                    evaluation.EquivalentToWinner ? "동률" : "대안", 24,
                    i == decision.Index ? WiseStackTheme.Blue : WiseStackTheme.Muted, true);
                status.alignment = TextAnchor.MiddleRight;
                Flexible(status.rectTransform, .65f);
                Label(table, evaluation.Eligible
                    ? $"바닥면 {option.FloorUtilizationPercent:F1}% · 높이 여유 {option.HeightReserveM:F2}m · " +
                        (evaluation.LoadVerified ? "기준 무게로 하중 조건 충족" : "하중 확인 필요")
                    : evaluation.Exclusion, 22, WiseStackTheme.Muted);
            }
            var analysis = Card("StrategyTradeoffs");
            Label(analysis, "선정 근거와 상충 관계", 28, WiseStackTheme.Navy, true);
            Label(analysis, Tradeoffs(comparison, decision), 26, WiseStackTheme.Muted);
            Details(source, comparison, decision);
            Rebuild();
        }

        private static string Tradeoffs(WarehouseStrategyComparison comparison, WarehouseStrategyDecision decision)
        {
            var winner = comparison.Get(decision.Index);
            var text = new StringBuilder();
            for (int i = 0; i < 3; i++)
            {
                var evaluation = decision.Evaluations[i];
                if (i == decision.Index || !evaluation.Eligible || evaluation.EquivalentToWinner) continue;
                var other = comparison.Get(i);
                string a = ((char)('A' + decision.Index)).ToString(), b = ((char)('A' + i)).ToString();
                int count = winner.PerPallet - other.PerPallet;
                float height = winner.StackHeightM - other.StackHeightM;
                if (count > 0) text.Append($"{a}는 {b}보다 팔레트당 {count:N0}개 더 적재합니다. ");
                else if (count < 0) text.Append($"{b}는 {a}보다 팔레트당 {-count:N0}개 더 적재합니다. ");
                else text.Append($"{a}와 {b}의 적재 수량은 같습니다. ");
                if (Mathf.Abs(height) > .001f)
                    text.Append(height > 0 ? $"{b}는 높이가 {height:F2}m 낮습니다." : $"{a}는 높이가 {-height:F2}m 낮습니다.");
                else text.Append("적재 높이도 같습니다.");
                text.AppendLine();
            }
            text.Append("C는 B보다 한 층 낮추되 최소 1층을 유지합니다. 높이가 낮아지는 대신 수량이 줄어 수량 우선 정책에서는 일반적으로 선정되지 않습니다.");
            return text.ToString().TrimEnd();
        }

        private void Details(WarehouseRecommendation source, WarehouseStrategyComparison comparison, WarehouseStrategyDecision decision)
        {
            var toggle = Button(_content, "계산 근거와 한계 펼치기  +", null, false);
            toggle.name = "RecommendationDetailsButton";
            var panel = Card("RecommendationDetails");
            Label(panel, "전략별 계산과 조건 확인", 28, WiseStackTheme.Navy, true);
            for (int i = 0; i < 3; i++)
            {
                var evaluation = decision.Evaluations[i];
                var option = comparison.Get(i);
                Label(panel, Names[i] + " · " + (evaluation != null && evaluation.Eligible
                    ? $"{option.Plan.Columns} × {option.Plan.Rows} × {option.Layers} = {option.PerPallet:N0}개\n" +
                        $"바닥면 {option.FloorUtilizationPercent:F1}% · 공간 {option.SpaceUtilizationPercent:F1}%\n" +
                        $"높이 {option.StackHeightM:F2}m · 여유 {option.HeightReserveM:F2}m\n" +
                        (evaluation.LoadVerified ? $"화물 {option.WeightKg:F2}kg ≤ 허용 {source.PalletMaxLoadKg:F2}kg" : "무게 누락 · 하중 확인 필요")
                    : evaluation?.Exclusion ?? "배치 불가"), 24, WiseStackTheme.Muted);
            }
            Label(panel, decision.Criteria, 24, WiseStackTheme.Muted);
            Label(panel, "수량 N = 가로 개수 × 세로 개수 × 층수. 바닥면 활용률 = 한 층 화물 바닥면적 / 팔레트 면적. " +
                "총 높이 = 0.15m + 층수 × 화물 높이 ≤ 유효 높이 한도. 높이 여유 = 한도 − 총 높이. " +
                "공간 활용률 = 화물 총부피 / (팔레트 면적 × (한도 − 0.15m)). " +
                "화물 하중 = 수량 × 개당 기준 무게 ≤ 허용하중 (팔레트 자체 중량 제외). 활용률은 백분율로 표시합니다.", 24, WiseStackTheme.Muted);
            var basis = new StringBuilder();
            basis.AppendLine($"창고 {source.WarehouseAreaM2:N1}㎡ · 천장 {source.CeilingHeightM:F2}m");
            basis.AppendLine($"팔레트 {source.PalletWidthM:F2} × {source.PalletLengthM:F2}m · 최대 하중 {source.PalletMaxLoadKg:N1}kg");
            basis.AppendLine($"{source.CargoName}: {source.Cargo.WidthM:F2} × {source.Cargo.DepthM:F2} × {source.Cargo.HeightM:F2}m · " +
                (source.AssumedCargoWeightKg > 0 ? $"기준 무게 {source.AssumedCargoWeightKg:F2}kg" : "무게 미등록"));
            if (decision.HasRecommendation)
            {
                var winner = comparison.Get(decision.Index);
                basis.AppendLine(decision.RequiresLoadConfirmation ? "선정안 하중 확인 필요" : $"선정안 하중 {winner.WeightKg:N1}kg · 바닥 사용률 {winner.FloorUtilizationPercent:F1}%");
                basis.AppendLine($"창고 전체 예상 적재량: 약 {winner.WarehouseCapacity:N0}개");
                basis.AppendLine($"예상 팔레트 {comparison.PalletCount:N0}개 × 팔레트당 {winner.PerPallet:N0}개");
            }
            basis.AppendLine($"통로·설비 20% 제외 ({source.ExcludedAreaM2:N1}㎡), 사용 가능 {source.UsableAreaM2:N1}㎡.");
            basis.AppendLine("정사각형 창고의 한쪽 깊이 20%를 제외한 직사각형에 팔레트를 배치한 개략치입니다. 창고 형상 및 통로 배치에 따라 실제 적재량은 달라질 수 있습니다.");
            basis.Append("무게는 화물 분류별 기준값입니다. 기둥·출입구·재고량·바닥 하중·포장 강도·결속·전도 위험은 반영하지 않았습니다. 낮은 높이가 안전성을 보장하지 않습니다.");
            Label(panel, "입력과 계산 범위", 28, WiseStackTheme.Navy, true);
            Label(panel, basis.ToString(), 24, WiseStackTheme.Muted);
            Label(panel, "ISO 6780(치수), ISO 8611(하중 시험), CTU Code(적재·고정)를 참고한 조건 검토입니다. " +
                "기하학적 예상치이며 국제표준 준수·인증이나 실제 안전성을 보증하지 않습니다. 하중 분포·무게중심·고정 상태는 검증하지 않습니다.", 24, WiseStackTheme.Muted);
            panel.gameObject.SetActive(false);
            toggle.onClick.AddListener(() =>
            {
                bool expanded = !panel.gameObject.activeSelf;
                panel.gameObject.SetActive(expanded);
                toggle.GetComponentInChildren<Text>().text = expanded ? "계산 근거와 한계 접기  −" : "계산 근거와 한계 펼치기  +";
                Rebuild();
            });
        }

        private void Rebuild()
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        }

        private RectTransform Card(string name, bool featured = false)
        {
            var root = Rect(name, _content);
            WiseStackUI.Surface(root,WiseStackTheme.Card);
            Vertical(root, 16, 24);
            return root;
        }

        private void Metric(Transform parent, string caption, string value)
        {
            var root = Rect("Metric", parent);
            Flexible(root, 1);
            WiseStackUI.Surface(root,WiseStackTheme.Background);
            Vertical(root, 5, 12);
            Label(root, value, 32, WiseStackTheme.Navy, true);
            Label(root, caption, 22, WiseStackTheme.Muted);
        }

        private Button Button(Transform parent, string caption, UnityEngine.Events.UnityAction action, bool primary = true)
        {
            var root = Rect("RecommendationAction", parent);
            var element = root.gameObject.AddComponent<LayoutElement>();
            element.minHeight = 96;
            var image = root.gameObject.AddComponent<Image>();
            image.color = primary ? WiseStackTheme.Blue : WiseStackTheme.Pale;
            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            Vertical(root, 0, 14);
            var label = Label(root, caption, 28, primary ? Color.white : WiseStackTheme.Blue, true);
            label.alignment = TextAnchor.MiddleCenter;
            WiseStackUI.StyleButton(button,primary);
            if (action != null) button.onClick.AddListener(action);
            return button;
        }

        private Text Label(Transform parent, string value, int size, Color color, bool bold = false)
        {
            var root = Rect("Label", parent);
            var text = root.gameObject.AddComponent<Text>();
            text.font = _font; text.fontSize = size; text.color = color; text.text = value;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.supportRichText = false;
            text.raycastTarget = false;
            return text;
        }

        private static void Vertical(RectTransform root, float spacing, int padding)
        {
            var group = root.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.padding = new RectOffset(padding, padding, padding, padding);
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            group.childAlignment = TextAnchor.MiddleLeft;
        }

        private static RectTransform Row(Transform parent, string name, float spacing)
        {
            var root = Rect(name, parent);
            var group = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            group.childAlignment = TextAnchor.MiddleLeft;
            return root;
        }

        private static void Flexible(RectTransform root, float weight)
        {
            var layout = root.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = 0; layout.preferredWidth = 0; layout.flexibleWidth = weight;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var root = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(parent, false);
            return root;
        }

        private static void Stretch(RectTransform root)
        {
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
        }
    }
}
