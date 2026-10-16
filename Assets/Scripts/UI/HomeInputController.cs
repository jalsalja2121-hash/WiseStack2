using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using ARLogistics.Navigation;
using System.Globalization;

namespace ARLogistics.UI
{
    public class HomeInputController : MonoBehaviour
    {
        [Header("Warehouse Inputs")]
        [SerializeField] private InputField warehouseAreaInput;
        [SerializeField] private InputField ceilingHeightInput;

        [Header("Pallet Inputs")]
        [SerializeField] private InputField palletWidthInput;
        [SerializeField] private InputField palletLengthInput;
        [SerializeField] private InputField palletMaxLoadInput;
        private GameObject confirmationPopup;

        private void Awake()
        {
            // Auto-discover InputFields by name if not wired in Inspector
            if (warehouseAreaInput == null) warehouseAreaInput = FindInputByName("AreaInput");
            if (ceilingHeightInput == null) ceilingHeightInput = FindInputByName("CeilingInput");
            if (palletWidthInput   == null) palletWidthInput   = FindInputByName("PalletWidthInput");
            if (palletLengthInput  == null) palletLengthInput  = FindInputByName("PalletLengthInput");
            if (palletMaxLoadInput == null) palletMaxLoadInput = FindInputByName("MaxLoadInput");

            WireInputField(warehouseAreaInput);
            WireInputField(ceilingHeightInput);
            WireInputField(palletWidthInput);
            WireInputField(palletLengthInput);
            WireInputField(palletMaxLoadInput);

            // Wire the shared bottom navigation bar. The old HomeScene-only
            // NavButtonPanel has been removed, so navigation uses the same
            // button names as every other scene.
            WireButton("StackPreviewBtn",  GoToARMain);
            WireButton("DangerOverlayBtn", GoToDanger);
            WireButton("MeasurementBtn",   GoToMeasure);
            StyleExistingSurfaces();
        }

        private void StyleExistingSurfaces()
        {
            foreach (Image surface in GetComponentsInChildren<Image>(true))
            {
                bool card = surface.name == "SettingsCard" || surface.name == "VoiceInputPanel";
                bool input = surface.GetComponent<InputField>() != null;
                if (!card && !input) continue;

                var border = surface.GetComponent<Outline>() ?? surface.gameObject.AddComponent<Outline>();
                border.effectColor = new Color(0.82f, 0.88f, 0.94f, 0.8f);
                border.effectDistance = new Vector2(1f, -1f);
                if (!card) continue;
                Shadow shadow = null;
                foreach (Shadow effect in surface.GetComponents<Shadow>())
                    if (effect.GetType() == typeof(Shadow)) { shadow = effect; break; }
                shadow ??= surface.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0.07f, 0.14f, 0.23f, 0.08f);
                shadow.effectDistance = new Vector2(0f, -3f);
            }
        }

        private static void WireButton(string goName, UnityEngine.Events.UnityAction action)
        {
            var go = GameObject.Find(goName);
            if (go == null) return;
            var btn = go.GetComponent<Button>();
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(action);
        }

        private static InputField FindInputByName(string goName)
        {
            var go = GameObject.Find(goName);
            return go != null ? go.GetComponent<InputField>() : null;
        }

        private static void WireInputField(InputField field)
        {
            if (field == null) return;
            // Unity's DecimalNumber validator uses the device locale and can strip
            // the decimal mark from a value written by code (1.20 -> 120).
            field.contentType = InputField.ContentType.Custom;
            field.keyboardType = TouchScreenKeyboardType.DecimalPad;
            field.characterValidation = InputField.CharacterValidation.None;
            field.onValidateInput = (current, index, character) =>
                character >= '0' && character <= '9' ? character :
                (character == '.' || character == ',') && !current.Contains(".") ? '.' : '\0';
            if (field.textComponent == null)
            {
                var t = field.transform.Find("Text");
                if (t != null) field.textComponent = t.GetComponent<Text>();
            }
            if (field.placeholder == null)
            {
                var p = field.transform.Find("Placeholder");
                if (p != null) field.placeholder = p.GetComponent<Text>();
            }
        }

        private void Start()
        {
            PopulateFields();
        }

        public void ConfirmManualSave()
        {
            if (!TryRead(warehouseAreaInput, out float area) ||
                !TryRead(ceilingHeightInput, out float height) ||
                !TryRead(palletWidthInput, out float width) ||
                !TryRead(palletLengthInput, out float length) ||
                !TryRead(palletMaxLoadInput, out float load) ||
                area <= 0f || height <= 0f || load < 0f ||
                width < AppSettings.MinPalletDimensionM || width > AppSettings.MaxPalletDimensionM ||
                length < AppSettings.MinPalletDimensionM || length > AppSettings.MaxPalletDimensionM)
            {
                SetSaveStatus("입력값을 확인해 주세요. 팔레트 길이는 0.3~3m여야 합니다.");
                return;
            }

            SaveSettings();
            if (AppSettings.WarehouseAreaM2 != area || AppSettings.CeilingHeightM != height ||
                AppSettings.PalletWidth != width || AppSettings.PalletLength != length ||
                AppSettings.PalletMaxLoadKg != load)
            {
                SetSaveStatus("창고 정보를 저장하지 못했습니다. 입력값을 확인해 주세요.");
                return;
            }

            ShowConfirmation("창고 정보 저장 완료",
                $"입력한 창고 정보가 정상적으로 저장되었습니다.\n\n창고 {area:F1}㎡ · 높이 {height:F2}m\n팔레트 {width:F2} × {length:F2}m · 최대 {load:F0}kg");
        }

        private static void SetSaveStatus(string message)
        {
            Text status = GameObject.Find("VoiceStatusText")?.GetComponent<Text>();
            if (status != null) status.text = message;
        }

        private static bool TryRead(InputField field, out float value)
        {
            value = 0f;
            return field != null && float.TryParse(field.text, NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) &&
                !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public void ShowAutomaticEntryConfirmation()
        {
            ShowConfirmation("창고 정보 자동 입력 완료", "창고 정보가 자동으로 입력되었습니다.");
        }

        private void ShowConfirmation(string title, string message)
        {
            if (confirmationPopup != null) return;
            confirmationPopup = new GameObject("WarehouseConfirmationPopup", typeof(RectTransform),
                typeof(Canvas), typeof(GraphicRaycaster), typeof(Image));
            confirmationPopup.transform.SetParent(transform, false);
            var backdrop = (RectTransform)confirmationPopup.transform;
            backdrop.anchorMin = Vector2.zero;
            backdrop.anchorMax = Vector2.one;
            backdrop.offsetMin = backdrop.offsetMax = Vector2.zero;
            var popupCanvas = confirmationPopup.GetComponent<Canvas>();
            popupCanvas.overrideSorting = true;
            popupCanvas.sortingOrder = 100;
            confirmationPopup.GetComponent<Image>().color = new Color(0.03f, 0.07f, 0.16f, 0.55f);

            var card = new GameObject("Card", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            card.SetParent(backdrop, false);
            card.anchorMin = new Vector2(0.08f, 0.34f);
            card.anchorMax = new Vector2(0.92f, 0.66f);
            card.offsetMin = card.offsetMax = Vector2.zero;
            card.GetComponent<Image>().color = Color.white;
            PopupText(card, "Title", title, 32, new Vector2(0.07f, 0.73f), new Vector2(0.93f, 0.95f), true);
            PopupText(card, "Message", message, 25, new Vector2(0.07f, 0.25f), new Vector2(0.93f, 0.73f), false);
            var button = new GameObject("Confirm", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<RectTransform>();
            button.SetParent(card, false);
            button.anchorMin = new Vector2(0.28f, 0.04f);
            button.anchorMax = new Vector2(0.72f, 0.22f);
            button.offsetMin = button.offsetMax = Vector2.zero;
            button.GetComponent<Image>().color = new Color(0.09f, 0.39f, 0.78f, 1f);
            button.GetComponent<Button>().onClick.AddListener(() =>
            {
                GameObject popup = confirmationPopup;
                confirmationPopup = null;
                Destroy(popup);
            });
            PopupText(button, "Label", "확인", 26, Vector2.zero, Vector2.one, true).color = Color.white;
        }

        private static Text PopupText(Transform parent, string name, string value, int size,
            Vector2 min, Vector2 max, bool bold)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent, false);
            text.rectTransform.anchorMin = min;
            text.rectTransform.anchorMax = max;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.075f, 0.14f, 0.23f, 1f);
            text.text = value;
            text.raycastTarget = false;
            return text;
        }

        private void PopulateFields()
        {
            if (warehouseAreaInput) warehouseAreaInput.text = AppSettings.WarehouseAreaM2.ToString("F0", CultureInfo.InvariantCulture);
            if (ceilingHeightInput) ceilingHeightInput.text = AppSettings.CeilingHeightM.ToString("F1", CultureInfo.InvariantCulture);
            if (palletWidthInput)   palletWidthInput.text   = AppSettings.PalletWidth.ToString("F2", CultureInfo.InvariantCulture);
            if (palletLengthInput)  palletLengthInput.text  = AppSettings.PalletLength.ToString("F2", CultureInfo.InvariantCulture);
            if (palletMaxLoadInput) palletMaxLoadInput.text = AppSettings.PalletMaxLoadKg.ToString("F0", CultureInfo.InvariantCulture);
        }

        private void SaveSettings()
        {
            if (warehouseAreaInput != null &&
                float.TryParse(warehouseAreaInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out float area))
                AppSettings.WarehouseAreaM2 = area;

            if (ceilingHeightInput != null &&
                float.TryParse(ceilingHeightInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out float ceiling))
                AppSettings.CeilingHeightM = ceiling;

            if (palletWidthInput != null &&
                float.TryParse(palletWidthInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out float pw))
                AppSettings.PalletWidth = pw;

            if (palletLengthInput != null &&
                float.TryParse(palletLengthInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out float pl))
                AppSettings.PalletLength = pl;

            if (palletMaxLoadInput != null &&
                float.TryParse(palletMaxLoadInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out float pm))
                AppSettings.PalletMaxLoadKg = pm;
        }

        // Called by NavBar buttons
        public void GoToARMain()
        {
            SaveSettings();
            var navigator = FindFirstObjectByType<SceneNavigator>();
            if (navigator != null)
            {
                navigator.GoToARMain();
                return;
            }

            Debug.LogError("[HomeInputController] SceneNavigator를 찾을 수 없어 적재 화면을 열지 않았습니다.", this);
        }

        public void GoToDanger()
        {
            SaveSettings();
            SceneManager.LoadScene("DangerScene");
        }

        public void GoToMeasure()
        {
            SaveSettings();
            SceneManager.LoadScene("MeasureScene");
        }
    }
}
