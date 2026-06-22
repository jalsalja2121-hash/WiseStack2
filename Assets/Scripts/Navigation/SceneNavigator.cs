using ARLogistics.Data;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace ARLogistics.Navigation
{
    public class SceneNavigator : MonoBehaviour
    {
        private GameObject measurementRequiredDialog;

        private void Awake()
        {
            var navBar = GameObject.Find("NavBar");
            if (navBar == null)
            {
                Debug.LogError($"[{nameof(SceneNavigator)}] NavBar was not found in scene '{gameObject.scene.name}'.", this);
                return;
            }

            Wire(navBar, "HomeBtn",          GoToHome);
            Wire(navBar, "StackPreviewBtn",  GoToARMain);
            Wire(navBar, "DangerOverlayBtn", GoToDanger);
            Wire(navBar, "MeasurementBtn",   GoToMeasure);
        }

        private static void Wire(
            GameObject navBar,
            string buttonName,
            UnityEngine.Events.UnityAction action)
        {
            foreach (var button in navBar.GetComponentsInChildren<Button>(true))
            {
                if (button.name != buttonName) continue;

                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(action);
                return;
            }

            Debug.LogError($"[{nameof(SceneNavigator)}] Button '{buttonName}' was not found under NavBar.", navBar);
        }

        public void GoToHome() => SceneManager.LoadScene("HomeScene");

        public void GoToARMain()
        {
            if (!BoxMeasurementStore.TryGet(out _))
            {
                ShowMeasurementRequiredDialog();
                return;
            }

            SceneManager.LoadScene("ARMain");
        }

        public void GoToDanger()  => SceneManager.LoadScene("DangerScene");
        public void GoToMeasure() => SceneManager.LoadScene("MeasureScene");

        public void ShowMeasurementRequiredDialog()
        {
            if (measurementRequiredDialog != null)
            {
                measurementRequiredDialog.SetActive(true);
                measurementRequiredDialog.transform.SetAsLastSibling();
                return;
            }

            var canvas = GameObject.Find("NavBar")?.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                Debug.LogError($"[{nameof(SceneNavigator)}] 안내창을 표시할 Canvas를 찾을 수 없습니다.", this);
                return;
            }

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            measurementRequiredDialog = CreateUiObject("MeasurementRequiredDialog", canvas.transform);
            var overlayRect = measurementRequiredDialog.GetComponent<RectTransform>();
            Stretch(overlayRect);
            var overlayImage = measurementRequiredDialog.AddComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0.72f);
            overlayImage.raycastTarget = true;

            GameObject panel = CreateUiObject("DialogPanel", measurementRequiredDialog.transform);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.1f, 0.36f);
            panelRect.anchorMax = new Vector2(0.9f, 0.64f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            var panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.06f, 0.09f, 0.16f, 1f);

            CreateText(panel.transform, "Title", "측정이 필요합니다", font,
                new Vector2(0.08f, 0.68f), new Vector2(0.92f, 0.92f), 42, FontStyle.Bold);
            CreateText(panel.transform, "Message", "적재 기능을 사용하려면 먼저 측정을 완료해 주세요.", font,
                new Vector2(0.08f, 0.38f), new Vector2(0.92f, 0.68f), 30, FontStyle.Normal);

            CreateButton(panel.transform, "CancelButton", "취소", font,
                new Vector2(0.08f, 0.09f), new Vector2(0.47f, 0.31f),
                new Color(0.22f, 0.25f, 0.31f, 1f), HideMeasurementRequiredDialog);
            CreateButton(panel.transform, "GoToMeasureButton", "측정하러 가기", font,
                new Vector2(0.53f, 0.09f), new Vector2(0.92f, 0.31f),
                new Color(0.18f, 0.55f, 1f, 1f), GoToMeasure);

            measurementRequiredDialog.transform.SetAsLastSibling();
        }

        private void HideMeasurementRequiredDialog()
        {
            if (measurementRequiredDialog != null)
                measurementRequiredDialog.SetActive(false);
        }

        private static GameObject CreateUiObject(string objectName, Transform parent)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void CreateText(
            Transform parent,
            string objectName,
            string value,
            Font font,
            Vector2 anchorMin,
            Vector2 anchorMax,
            int fontSize,
            FontStyle fontStyle)
        {
            GameObject textObject = CreateUiObject(objectName, parent);
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = textObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.text = value;
        }

        private static void CreateButton(
            Transform parent,
            string objectName,
            string label,
            Font font,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Color color,
            UnityEngine.Events.UnityAction action)
        {
            GameObject buttonObject = CreateUiObject(objectName, parent);
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = buttonObject.AddComponent<Image>();
            image.color = color;
            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);

            CreateText(buttonObject.transform, "Label", label, font,
                Vector2.zero, Vector2.one, 30, FontStyle.Bold);
        }

        public void GoToScene(string sceneName)
        {
            if (sceneName == "ARMain")
            {
                GoToARMain();
                return;
            }

            SceneManager.LoadScene(sceneName);
        }
    }
}
