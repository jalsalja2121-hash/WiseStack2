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
        private static readonly Color Ink = new(0.035f, 0.065f, 0.11f, 0.97f);
        private static readonly Color Blue = new(0.08f, 0.38f, 0.8f, 1f);

        public void Build(Transform parent, Text status, Button place, Button clear, Action<StackOrientation> select)
        {
            root = (RectTransform)parent;
            primary = place; reset = clear;
            font = status.font != null ? status.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            card = status.transform.parent as RectTransform;
            card.GetComponent<Image>().color = Ink;
            title = Label(card, "Title", "적재 미리보기", 44);
            title.fontStyle = FontStyle.Bold;
            Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -78), new Vector2(-28, -18));

            var viewport = Rect("ResultViewport", card);
            Anchor(viewport, Vector2.zero, Vector2.one, new Vector2(28, 20), new Vector2(-28, -92));
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.001f);
            viewport.gameObject.AddComponent<RectMask2D>();
            status.transform.SetParent(viewport, false);
            Anchor(status.rectTransform, new Vector2(0, 1), Vector2.one, Vector2.zero, Vector2.zero);
            status.rectTransform.pivot = new Vector2(0.5f, 1);
            status.fontSize = 38;
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
            handleImage.color = new Color(.4f, .65f, .95f, .8f);
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
                rect.gameObject.AddComponent<Image>().color = Ink;
                options[i] = rect.gameObject.AddComponent<Button>();
                labels[i] = Label(rect, "Label", "", 32);
                labels[i].alignment = TextAnchor.MiddleCenter;
                Anchor(labels[i].rectTransform, Vector2.zero, Vector2.one, new Vector2(12, 8), new Vector2(-12, -8));
                options[i].onClick.AddListener(() => select((StackOrientation)index));
            }
            StyleButton(primary, "상자 쌓기", false, Blue);
            StyleButton(reset, "다시 배치", true, Ink);
            Layout();
        }

        public void Layout()
        {
            var canvas = root.GetComponentInParent<Canvas>();
            float sf = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1;
            float top = (Screen.height - Screen.safeArea.yMax) / sf;
            float bottom = Screen.safeArea.yMin / sf;
            Anchor(card, new Vector2(0, 1), Vector2.one, new Vector2(24, -top - 522), new Vector2(-24, -top - 24));
            Anchor(row, Vector2.zero, new Vector2(1, 0), new Vector2(24, bottom + 320), new Vector2(-24, bottom + 446));
            Anchor((RectTransform)primary.transform, Vector2.zero, new Vector2(0.5f, 0), new Vector2(24, bottom + 172), new Vector2(-10, bottom + 298));
            Anchor((RectTransform)reset.transform, new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(10, bottom + 172), new Vector2(-24, bottom + 298));
        }

        public void ShowPlans(StackingPlan[] plans, StackOrientation selected)
        {
            string[] names = { "추천 배치", "기본 방향", "90° 회전" };
            for (int i = 0; i < 3; i++)
            {
                labels[i].text = names[i] + (plans == null ? "\n측정 후 확인" : $"\n한 단 {plans[i].PerLayer}개 · 총 {plans[i].Total}개");
                options[i].interactable = plans != null;
                options[i].GetComponent<Image>().color = (int)selected == i ? Blue : Ink;
            }
        }

        public void SetTitle(string value)
        {
            title.text = value;
            scroll.verticalNormalizedPosition = 1;
        }
        public void SetAction(string value, bool enabled)
        {
            primary.GetComponentInChildren<Text>().text = value;
            primary.interactable = enabled;
        }

        private void StyleButton(Button button, string caption, bool resetIcon, Color color)
        {
            button.GetComponent<Image>().color = color;
            var text = button.GetComponentInChildren<Text>(true);
            text.text = caption; text.fontSize = 40; text.fontStyle = FontStyle.Bold;
            text.resizeTextForBestFit = false; text.raycastTarget = false;
            Anchor(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(70, 10), new Vector2(-10, -10));
            var icon = Rect("ActionIcon", button.transform);
            icon.anchorMin = icon.anchorMax = new Vector2(0, 0.5f);
            icon.anchoredPosition = new Vector2(42, 0); icon.sizeDelta = new Vector2(40, 40);
            if (resetIcon)
            {
                for (int i = 0; i < 12; i++)
                {
                    float a = (40 + i * 24) * Mathf.Deg2Rad, b = (40 + (i + 1) * 24) * Mathf.Deg2Rad;
                    Stroke(icon, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 17, new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * 17);
                }
                Stroke(icon, new Vector2(13, 11), new Vector2(13, 23));
                Stroke(icon, new Vector2(13, 11), new Vector2(1, 11));
            }
            else
            {
                Stroke(icon, new Vector2(-18, -16), new Vector2(18, -16));
                Stroke(icon, new Vector2(18, -16), new Vector2(18, 16));
                Stroke(icon, new Vector2(18, 16), new Vector2(-18, 16));
                Stroke(icon, new Vector2(-18, 16), new Vector2(-18, -16));
                Stroke(icon, new Vector2(0, 16), new Vector2(0, 2));
            }
        }
        private static void Stroke(Transform parent, Vector2 a, Vector2 b)
        {
            var line = Rect("Stroke", parent);
            line.anchorMin = line.anchorMax = new Vector2(0.5f, 0.5f);
            line.anchoredPosition = (a + b) / 2;
            line.sizeDelta = new Vector2((b - a).magnitude, 4);
            line.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
            var image = line.gameObject.AddComponent<Image>(); image.raycastTarget = false;
        }
        private Text Label(Transform parent, string name, string value, int size)
        {
            var label = Rect(name, parent).gameObject.AddComponent<Text>();
            label.font = font; label.fontSize = size; label.color = Color.white; label.text = value;
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
