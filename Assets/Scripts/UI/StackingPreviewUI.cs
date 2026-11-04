using System;
using UnityEngine;
using UnityEngine.UI;
using ARLogistics.Data;

namespace ARLogistics.UI
{
    /// <summary>Runtime uGUI layout, using the existing scene buttons and status field.</summary>
    public sealed class StackingPreviewUI
    {
        private readonly Button[] options = new Button[3];
        private readonly Text[] labels = new Text[3];
        private RectTransform root, card, row;
        private Button primary, reset;
        private ScrollRect scroll;
        private Text title;
        private Font font;
        private static readonly Color Ink = WiseStackTheme.Navy;
        private static readonly Color Blue = WiseStackTheme.Blue;
        private static readonly Color Pale = WiseStackTheme.Pale;

        public void Build(Transform parent, Text status, Button place, Button clear, Action<int> select)
        {
            root = (RectTransform)parent;
            primary = place; reset = clear;
            font = status.font != null ? status.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            card = status.transform.parent as RectTransform;
            WiseStackUI.Surface(card,WiseStackTheme.Card);
            var cardBorder = card.GetComponent<Outline>() ?? card.gameObject.AddComponent<Outline>();
            cardBorder.enabled = false;
            cardBorder.effectDistance = new Vector2(1f, -1f);
            title = Label(card, "Title", "바닥 인식 중", 28);
            title.fontStyle = FontStyle.Bold;
            Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -62), new Vector2(-24, -16));

            var viewport = Rect("ResultViewport", card);
            Anchor(viewport, Vector2.zero, Vector2.one, new Vector2(24, 16), new Vector2(-24, -70));
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.001f);
            viewport.gameObject.AddComponent<RectMask2D>();
            status.transform.SetParent(viewport, false);
            Anchor(status.rectTransform, new Vector2(0, 1), Vector2.one, Vector2.zero, Vector2.zero);
            status.rectTransform.pivot = new Vector2(0.5f, 1);
            status.fontSize = 24;
            status.color = Ink;
            status.resizeTextForBestFit = false;
            status.alignment = TextAnchor.UpperLeft;
            status.lineSpacing = 1.15f;
            status.supportRichText = true;
            status.raycastTarget = false;
            status.horizontalOverflow = HorizontalWrapMode.Wrap;
            status.verticalOverflow = VerticalWrapMode.Overflow;
            var fitter = status.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = status.rectTransform;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 35;
            var barRect = Rect("ScrollBar", card);
            Anchor(barRect, new Vector2(1, 0), Vector2.one, new Vector2(-18, 22), new Vector2(-10, -94));
            var handle = Rect("Handle", barRect);
            Anchor(handle, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = new Color(.54f, .68f, .84f, .85f);
            var bar = barRect.gameObject.AddComponent<Scrollbar>();
            bar.handleRect = handle; bar.targetGraphic = handleImage;
            bar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            row = Rect("SP_LayoutOptions", root);
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                var rect = Rect("Layout_" + i, row);
                Anchor(rect, new Vector2(i / 3f, 0), new Vector2((i + 1) / 3f, 1), new Vector2(5, 0), new Vector2(-5, 0));
                WiseStackUI.Surface(rect,Pale);
                options[i] = rect.gameObject.AddComponent<Button>();
                labels[i] = Label(rect, "Label", "", 24);
                labels[i].alignment = TextAnchor.MiddleCenter;
                Anchor(labels[i].rectTransform, Vector2.zero, Vector2.one, new Vector2(12, 8), new Vector2(-12, -8));
                options[i].onClick.AddListener(() => select(index));
            }
            StyleButton(primary, "상자 쌓기", false, Blue);
            StyleButton(reset, "다시 배치", true, Pale);
            WiseStackUI.Navigation(root.GetComponentInParent<Canvas>().transform,"StackPreviewBtn");
            Layout();
        }

        public void Layout()
        {
            var canvas = root.GetComponentInParent<Canvas>();
            float sf = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1;
            float top = (Screen.height - Screen.safeArea.yMax) / sf;
            float bottom = Screen.safeArea.yMin / sf;
            var safe = WiseStackUI.Insets(root);
            float actionBottom = bottom + WiseStackTheme.NavigationHeight + 16;
            Anchor(card, new Vector2(0, 1), Vector2.one, new Vector2(safe.x+24, -top - 240), new Vector2(-safe.z-24, -top - 24));
            Anchor(row, Vector2.zero, new Vector2(1, 0), new Vector2(safe.x+24, actionBottom+112), new Vector2(-safe.z-24, actionBottom+216));
            Anchor((RectTransform)primary.transform, Vector2.zero, new Vector2(0.6f, 0), new Vector2(safe.x+24, actionBottom), new Vector2(-8, actionBottom+96));
            Anchor((RectTransform)reset.transform, new Vector2(0.6f, 0), new Vector2(1, 0), new Vector2(8, actionBottom), new Vector2(-safe.z-24, actionBottom+96));
        }

        public void ShowStrategies(WarehouseStrategyComparison comparison, int selected)
        {
            string[] names = { "A · 표준", "B · 공간 효율", "C · 낮은 높이" };
            for (int i = 0; i < 3; i++)
            {
                WarehouseStrategy strategy = comparison?.Get(i);
                labels[i].text = names[i] + (strategy == null ? "\n측정 후 확인" :
                    strategy.Available ? $"\n{strategy.PerPallet:N0}개 · {strategy.Layers}층" : "\n적재 불가");
                options[i].interactable = strategy != null;
                bool isSelected = selected == i;
                options[i].GetComponent<Image>().color = isSelected ? Blue : Pale;
                labels[i].color = isSelected ? Color.white : Ink;
                labels[i].fontStyle = isSelected ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        public void SetTitle(string value)
        {
            title.text = value;
            title.color = value.Contains("오류") || value.Contains("어려") ? WiseStackTheme.Error :
                value.Contains("완료") ? WiseStackTheme.Success : Ink;
            scroll.verticalNormalizedPosition = 1;
        }
        public void SetAction(string value, bool enabled)
        {
            primary.GetComponentInChildren<Text>().text = value;
            primary.interactable = enabled;
        }

        private void StyleButton(Button button, string caption, bool resetIcon, Color color)
        {
            WiseStackUI.StyleButton(button, !resetIcon, caption);
        }
        private Text Label(Transform parent, string name, string value, int size)
        {
            var label = Rect(name, parent).gameObject.AddComponent<Text>();
            label.font = font; label.fontSize = size; label.color = Ink; label.text = value;
            label.raycastTarget = false; return label;
        }
        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); return rect;
        }
        private static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 low, Vector2 high)
        {
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = low; rect.offsetMax = high;
        }
    }
}
