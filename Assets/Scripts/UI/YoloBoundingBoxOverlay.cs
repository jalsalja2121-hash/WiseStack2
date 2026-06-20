using System.Collections.Generic;
using ARLogistics.Detection;
using UnityEngine;
using UnityEngine.UI;

namespace ARLogistics.UI
{
    /// <summary>Draws non-blocking screen-space outlines for YOLO detections.</summary>
    public sealed class YoloBoundingBoxOverlay : MonoBehaviour
    {
        [SerializeField] private RectTransform overlayContainer;
        [SerializeField] private Color boxColor = new(0.10f, 0.85f, 1f, 1f);
        [SerializeField] private float borderThickness = 5f;
        [SerializeField] private int maxBoxes = 20;

        private readonly List<GameObject> _boxes = new();
        private YoloDetector _yolo;

        private void Awake()
        {
            _yolo = FindFirstObjectByType<YoloDetector>();
            EnsureContainer();
        }

        private void OnEnable()
        {
            if (_yolo == null)
                _yolo = FindFirstObjectByType<YoloDetector>();
            if (_yolo != null)
                _yolo.OnDetectionResultsUpdated += RenderDetections;
        }

        private void OnDisable()
        {
            if (_yolo != null)
                _yolo.OnDetectionResultsUpdated -= RenderDetections;
            ClearBoxes();
        }

        public void ClearBoxes()
        {
            foreach (GameObject box in _boxes)
            {
                if (box != null)
                    Destroy(box);
            }
            _boxes.Clear();
        }

        public void RenderDetections(List<DetectionResult> detections)
        {
            ClearBoxes();
            EnsureContainer();
            if (overlayContainer == null || detections == null)
                return;

            int count = Mathf.Min(maxBoxes, detections.Count);
            for (int i = 0; i < count; i++)
                DrawBox(detections[i]);
        }

        private void EnsureContainer()
        {
            if (overlayContainer != null)
                return;

            Transform existing = transform.Find("DetectionBoxOverlay");
            if (existing != null)
            {
                overlayContainer = existing.GetComponent<RectTransform>();
                return;
            }

            var container = new GameObject(
                "DetectionBoxOverlay", typeof(RectTransform), typeof(CanvasGroup));
            container.transform.SetParent(transform, false);
            overlayContainer = container.GetComponent<RectTransform>();
            overlayContainer.anchorMin = Vector2.zero;
            overlayContainer.anchorMax = Vector2.one;
            overlayContainer.offsetMin = Vector2.zero;
            overlayContainer.offsetMax = Vector2.zero;
            overlayContainer.SetAsLastSibling();

            CanvasGroup group = container.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
        }

        private void DrawBox(DetectionResult detection)
        {
            Rect normalized = ClampRect(detection.boundingBox);
            if (normalized.width <= 0f || normalized.height <= 0f)
                return;

            float canvasWidth = overlayContainer.rect.width;
            float canvasHeight = overlayContainer.rect.height;
            float left = normalized.xMin * canvasWidth;
            float bottom = (1f - normalized.yMax) * canvasHeight;
            float width = normalized.width * canvasWidth;
            float height = normalized.height * canvasHeight;

            var root = new GameObject(
                $"BBox_{detection.className}", typeof(RectTransform));
            root.transform.SetParent(overlayContainer, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(left, bottom);
            rect.sizeDelta = new Vector2(width, height);

            AddBorder(root.transform, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -borderThickness), Vector2.zero);
            AddBorder(root.transform, "Bottom", Vector2.zero, new Vector2(1f, 0f),
                Vector2.zero, new Vector2(0f, borderThickness));
            AddBorder(root.transform, "Left", Vector2.zero, new Vector2(0f, 1f),
                Vector2.zero, new Vector2(borderThickness, 0f));
            AddBorder(root.transform, "Right", new Vector2(1f, 0f), Vector2.one,
                new Vector2(-borderThickness, 0f), Vector2.zero);
            AddLabel(root.transform, detection);

            _boxes.Add(root);
        }

        private void AddBorder(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            var border = new GameObject(name, typeof(RectTransform), typeof(Image));
            border.transform.SetParent(parent, false);
            RectTransform rect = border.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            Image image = border.GetComponent<Image>();
            image.color = boxColor;
            image.raycastTarget = false;
        }

        private void AddLabel(Transform parent, DetectionResult detection)
        {
            var label = new GameObject("Label", typeof(RectTransform), typeof(Image));
            label.transform.SetParent(parent, false);
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(0f, 1f);
            labelRect.pivot = new Vector2(0f, 0f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.sizeDelta = new Vector2(300f, 42f);
            Image background = label.GetComponent<Image>();
            background.color = new Color(boxColor.r, boxColor.g, boxColor.b, 0.8f);
            background.raycastTarget = false;

            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(label.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 0f);
            textRect.offsetMax = new Vector2(-8f, 0f);

            Text text = textObject.GetComponent<Text>();
            text.text = $"{detection.className}  {detection.confidence * 100f:F0}%";
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 22;
            text.fontStyle = FontStyle.Bold;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
        }

        private static Rect ClampRect(Rect rect)
        {
            float xMin = Mathf.Clamp01(Mathf.Min(rect.xMin, rect.xMax));
            float yMin = Mathf.Clamp01(Mathf.Min(rect.yMin, rect.yMax));
            float xMax = Mathf.Clamp01(Mathf.Max(rect.xMin, rect.xMax));
            float yMax = Mathf.Clamp01(Mathf.Max(rect.yMin, rect.yMax));
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
    }
}
