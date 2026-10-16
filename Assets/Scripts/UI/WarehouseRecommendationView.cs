using System.Text;
using ARLogistics.Data;
using UnityEngine;
using UnityEngine.UI;

namespace ARLogistics.UI
{
    public sealed class WarehouseRecommendationView : MonoBehaviour
    {
        private static readonly Color Blue = new(0.09f, 0.39f, 0.78f, 1f);
        private static readonly Color Ink = new(0.055f, 0.10f, 0.17f, 1f);
        private static readonly Color Pale = new(0.94f, 0.97f, 1f, 1f);
        private RectTransform _content;
        private Font _font;
        private WarehouseStrategyComparison _comparison;
        private WarehouseStrategy[] _strategies;
        private Image[] _choiceImages;
        private Text[] _choiceTexts;
        private Text _summary;
        private Text _detail;

        private void Start()
        {
            Text title = GameObject.Find("TitleLbl")?.GetComponent<Text>();
            if (title != null) { _font = title.font; title.text = "창고 적재 추천"; }
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            GameObject.Find("OverlayPanel")?.SetActive(false);
            Build();
            Refresh();
        }

        private void Build()
        {
            var viewport = Rect("RecommendationViewport", transform);
            viewport.anchorMin = new Vector2(0.04f, 0.105f);
            viewport.anchorMax = new Vector2(0.96f, 0.87f);
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            viewport.gameObject.AddComponent<Image>().color = new Color(0.965f, 0.977f, 0.99f, 0.97f);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.viewport = viewport;
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            _content = Rect("RecommendationContent", viewport);
            _content.anchorMin = new Vector2(0, 1);
            _content.anchorMax = new Vector2(1, 1);
            _content.pivot = new Vector2(0.5f, 1);
            _content.offsetMin = new Vector2(24, 0);
            _content.offsetMax = new Vector2(-24, 0);
            var layout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 16;
            layout.padding = new RectOffset(0, 0, 22, 24);
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = _content;
            scroll.scrollSensitivity = 35f;
        }

        private void Refresh()
        {
            for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
            if (!BoxMeasurementStore.TryGet(out BoxMeasurement cargo))
            {
                WarehouseRecommendationStore.Clear();
                Card("화물 정보 필요", "측정 화면에서 화물을 분류하고 규격을 저장한 뒤 다시 열어 주세요.", 145);
                return;
            }
            if (cargo.ClassId < 0 || cargo.ClassId >= ProductSpecTable.ClassNames.Length)
            {
                WarehouseRecommendationStore.Clear();
                Card("화물 분류 확인", "저장된 화물 분류를 확인할 수 없습니다. 측정 화면에서 다시 분류해 주세요.", 145);
                return;
            }
            var spec = ProductSpecTable.Get(cargo.ClassId);
            string cargoName = string.IsNullOrEmpty(cargo.SourceName)
                ? ProductSpecTable.ClassName(cargo.ClassId) : cargo.SourceName;
            if (!WarehouseRecommendationCalculator.TryCalculate(
                AppSettings.WarehouseAreaM2, AppSettings.CeilingHeightM,
                AppSettings.PalletWidth, AppSettings.PalletLength, AppSettings.PalletMaxLoadKg,
                cargo, spec.WeightKg, cargoName, out WarehouseRecommendation baseline))
            {
                WarehouseRecommendationStore.Clear();
                Card("입력값 확인", "창고 면적, 높이, 팔레트 조건과 화물 규격을 확인해 주세요.", 145);
                return;
            }
            WarehouseRecommendationStore.Save(baseline);
            _comparison = WarehouseStrategyCalculator.Calculate(baseline);
            _strategies = new[] { _comparison.A, _comparison.B, _comparison.C };
            Card("비교 조건", $"{cargoName} · {cargo.WidthM:F2} × {cargo.DepthM:F2} × {cargo.HeightM:F2}m\n창고 {baseline.WarehouseAreaM2:F1}㎡ · 팔레트 {_comparison.PalletCount:N0}개 배치 예상", 135);
            _summary = Card("전체 창고 적용 예상량", "", 138);
            SectionTitle("적재 전략 비교");
            Choices();
            _detail = Card("선택한 전략 상세", "", 345);
            Select(_comparison.B.Available ? 1 : _comparison.A.Available ? 0 : _comparison.C.Available ? 2 : 1);

            var basis = new StringBuilder();
            basis.AppendLine($"창고 {baseline.WarehouseAreaM2:F1}㎡ · 높이 {baseline.CeilingHeightM:F2}m");
            basis.AppendLine($"팔레트 {baseline.PalletWidthM:F2} × {baseline.PalletLengthM:F2}m · 최대 하중 {baseline.PalletMaxLoadKg:F0}kg");
            basis.AppendLine($"{cargoName}: {cargo.WidthM:F2} × {cargo.DepthM:F2} × {cargo.HeightM:F2}m · 기준 무게 {spec.WeightKg:F2}kg");
            basis.AppendLine($"통로·설비 20% 제외: {baseline.ExcludedAreaM2:F1}㎡ · 사용 가능 {baseline.UsableAreaM2:F1}㎡ · 팔레트 점유 {_comparison.OccupiedAreaM2:F1}㎡");
            basis.AppendLine("정사각형 창고를 가정하고 남은 직사각형 전체에 팔레트를 배치했습니다. 실제 좌표나 통로 위치는 계산하지 않았습니다.");
            basis.AppendLine($"이전 면적 분할 방식 참고값: {baseline.TotalCargoCapacity:N0}개. 전략별 예상량과 계산 범위가 달라 합산하지 않습니다.");
            basis.AppendLine("화물 무게와 치수는 분류별 기준 예상값입니다. 바닥 하중, 포장 강도와 적재 안정성은 별도로 확인하세요.");
            Details(basis.ToString());
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        }

        private void Choices()
        {
            var row = Rect("StrategyChoices", _content);
            Height(row, 148);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            _choiceImages = new Image[3];
            _choiceTexts = new Text[3];
            for (int i = 0; i < 3; i++)
            {
                int selected = i;
                var block = Rect("Strategy" + i, row);
                _choiceImages[i] = block.gameObject.AddComponent<Image>();
                Border(block.gameObject);
                block.gameObject.AddComponent<Button>().onClick.AddListener(() => Select(selected));
                var label = Rect("Label", block);
                label.anchorMin = Vector2.zero;
                label.anchorMax = Vector2.one;
                label.offsetMin = new Vector2(5, 5);
                label.offsetMax = new Vector2(-5, -5);
                var strategy = _strategies[i];
                _choiceTexts[i] = Text(label,
                    $"{strategy.Name}\n{(strategy.Available ? $"팔레트당 {strategy.PerPallet:N0}개" : "적재 불가")}", 22, Blue);
                _choiceTexts[i].alignment = TextAnchor.MiddleCenter;
                _choiceTexts[i].fontStyle = FontStyle.Bold;
            }
        }

        private void Select(int index)
        {
            for (int i = 0; i < _choiceImages.Length; i++)
            {
                _choiceImages[i].color = i == index ? Blue : Pale;
                _choiceTexts[i].color = i == index ? Color.white : Blue;
            }
            WarehouseStrategy strategy = _strategies[index];
            _summary.text = strategy.Available
                ? $"<b><color=#1764C7>전체 창고 적용 예상량</color></b>\n<size=38><b>{strategy.WarehouseCapacity:N0}개</b></size>\n선택한 전략을 전체 사용 가능 공간에 적용한 값"
                : "<b><color=#1764C7>전체 창고 적용 예상량</color></b>\n현재 조건에서는 적재할 수 없습니다.";
            if (!strategy.Available)
            {
                _detail.text = $"<b><color=#1764C7>{strategy.Name}</color></b>\n적재 불가\n{strategy.Message}";
                return;
            }
            string orientation = strategy.Orientation == StackOrientation.Rotated ? "90° 회전" : "기본 방향";
            string difference = index == 1 && _strategies[0].Available
                ? $"\nA 대비 팔레트당 +{strategy.PerPallet - _strategies[0].PerPallet:N0}개" : "";
            _detail.text = $"<b><color=#1764C7>{strategy.Name}</color></b>\n" +
                $"적재 방향 {orientation} · 1층 {strategy.PerLayer:N0}개 × {strategy.Layers}층\n" +
                $"팔레트당 {strategy.PerPallet:N0}개 · 예상 무게 {strategy.WeightKg:F1}kg\n" +
                $"적재 높이 {strategy.StackHeightM:F2}m · 팔레트 바닥 사용률 {strategy.FloorUtilizationPercent:F1}%\n" +
                $"전체 창고 적용 시 {strategy.WarehouseCapacity:N0}개 · 팔레트 {_comparison.PalletCount:N0}개" +
                difference + $"\n{strategy.Message}";
        }

        private void Details(string value)
        {
            var root = Rect("RecommendationDetailsButton", _content);
            Height(root, 58);
            root.gameObject.AddComponent<Image>().color = Pale;
            var button = root.gameObject.AddComponent<Button>();
            var label = Text(Rect("Label", root), "계산 근거와 한계 펼치기", 25, Blue);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            label.alignment = TextAnchor.MiddleCenter;
            var panel = Rect("RecommendationDetails", _content);
            Height(panel, 530);
            panel.gameObject.AddComponent<Image>().color = Color.white;
            Border(panel.gameObject);
            var inner = Rect("Text", panel);
            inner.anchorMin = Vector2.zero;
            inner.anchorMax = Vector2.one;
            inner.offsetMin = new Vector2(22, 12);
            inner.offsetMax = new Vector2(-22, -12);
            var detailText = Text(inner, value, 23, Ink);
            detailText.verticalOverflow = VerticalWrapMode.Overflow;
            Canvas.ForceUpdateCanvases();
            panel.GetComponent<LayoutElement>().preferredHeight = Mathf.Max(300f, detailText.preferredHeight + 24f);
            panel.gameObject.SetActive(false);
            button.onClick.AddListener(() =>
            {
                panel.gameObject.SetActive(!panel.gameObject.activeSelf);
                label.text = panel.gameObject.activeSelf ? "계산 근거와 한계 접기" : "계산 근거와 한계 펼치기";
            });
        }

        private Text Card(string title, string body, float height)
        {
            var card = Rect(title + "Card", _content);
            Height(card, height);
            card.gameObject.AddComponent<Image>().color = Color.white;
            Border(card.gameObject);
            var inner = Rect("Text", card);
            inner.anchorMin = Vector2.zero;
            inner.anchorMax = Vector2.one;
            inner.offsetMin = new Vector2(22, 12);
            inner.offsetMax = new Vector2(-22, -12);
            return Text(inner, $"<b><color=#1764C7>{title}</color></b>\n{body}", 26, Ink);
        }

        private void SectionTitle(string value)
        {
            var root = Rect("SectionTitle", _content);
            Height(root, 44);
            Text(root, value, 28, Blue).fontStyle = FontStyle.Bold;
        }

        private Text Text(RectTransform root, string value, int size, Color color)
        {
            var text = root.gameObject.AddComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.color = color;
            text.text = value;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.supportRichText = true;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var root = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            root.SetParent(parent, false);
            return root;
        }

        private static void Height(RectTransform root, float height) =>
            root.gameObject.AddComponent<LayoutElement>().preferredHeight = height;

        private static void Border(GameObject target)
        {
            var border = target.AddComponent<Outline>();
            border.effectColor = new Color(0.82f, 0.88f, 0.94f, 0.8f);
            border.effectDistance = new Vector2(1f, -1f);
        }
    }
}
