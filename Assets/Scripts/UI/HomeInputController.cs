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
        private RectTransform headerRect;
        private RectTransform settingsRect;
        private RectTransform voiceRect;
        private Rect lastSafeArea;
        private Vector2 lastCanvasSize;
        private HomeSetupView presentation;
        private static bool hasConfirmedSettings;

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
            headerRect = GameObject.Find("Header")?.GetComponent<RectTransform>();
            settingsRect = GameObject.Find("SettingsCard")?.GetComponent<RectTransform>();
            voiceRect = GameObject.Find("VoiceInputPanel")?.GetComponent<RectTransform>();
        }

        private void StyleExistingSurfaces()
        {
            foreach (Image surface in GetComponentsInChildren<Image>(true))
            {
                bool card = surface.name == "SettingsCard" || surface.name == "VoiceInputPanel";
                bool input = surface.GetComponent<InputField>() != null;
                if (!card && !input) continue;

                var border = surface.GetComponent<Outline>() ?? surface.gameObject.AddComponent<Outline>();
                border.effectColor = WiseStackTheme.Border;
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

        private void WireButton(string goName, UnityEngine.Events.UnityAction action)
        {
            var go = WiseStackUI.Find<RectTransform>(transform,goName)?.gameObject;
            if (go == null) return;
            var btn = go.GetComponent<Button>();
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(action);
        }

        private InputField FindInputByName(string goName)
        {
            return WiseStackUI.Find<InputField>(transform,goName);
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
            presentation = new HomeSetupView();
            presentation.Build(transform, new[] { warehouseAreaInput, ceilingHeightInput, palletWidthInput, palletLengthInput, palletMaxLoadInput }, GoToMeasure);
            presentation.SetProvenance(hasConfirmedSettings);
            WireButton("StackPreviewBtn", GoToARMain);
            WireButton("DangerOverlayBtn", GoToDanger);
            WireButton("MeasurementBtn", GoToMeasure);
            PositionHomePanels();
        }

        private void LateUpdate()
        {
            RectTransform canvasRect = transform as RectTransform;
            if (canvasRect != null && (!lastSafeArea.Equals(Screen.safeArea) || lastCanvasSize != canvasRect.rect.size))
                PositionHomePanels();
        }

        private void PositionHomePanels()
        {
            if (presentation != null)
            {
                presentation.Layout();
                lastSafeArea = Screen.safeArea;
                lastCanvasSize = ((RectTransform)transform).rect.size;
                return;
            }
            RectTransform canvasRect = transform as RectTransform;
            Canvas canvas = GetComponent<Canvas>();
            if (canvasRect == null || canvas == null) return;
            float scale = Mathf.Max(0.01f, canvas.scaleFactor);
            float height = canvasRect.rect.height;
            float safeTop = (Screen.height - Screen.safeArea.yMax) / scale;
            float safeBottom = Screen.safeArea.yMin / scale;
            if (headerRect != null)
                headerRect.offsetMax = new Vector2(headerRect.offsetMax.x, -safeTop);
            if (settingsRect != null && voiceRect != null)
            {
                float navHeight = GameObject.Find("NavBar")?.GetComponent<RectTransform>()?.rect.height ?? 140f;
                float bottom = Mathf.Max(height * voiceRect.anchorMin.y, navHeight + safeBottom + 8f);
                float top = height * settingsRect.anchorMin.y - 8f;
                voiceRect.offsetMin = new Vector2(voiceRect.offsetMin.x, bottom - height * voiceRect.anchorMin.y);
                voiceRect.offsetMax = new Vector2(voiceRect.offsetMax.x, top - height * voiceRect.anchorMax.y);
            }
            lastSafeArea = Screen.safeArea;
            lastCanvasSize = canvasRect.rect.size;
        }

        public void ConfirmManualSave()
        {
            if (TrySaveSettings(out string summary))
                ShowConfirmation("창고 정보 저장 완료", summary + "\n측정 화면에서 화물을 분류하면 적재·추천을 확인할 수 있습니다.");
        }

        private void SetSaveStatus(string message)
        {
            Text status = WiseStackUI.Find<Text>(transform,"VoiceStatusText");
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
            card.anchorMin = new Vector2(0.08f, 0.29f);
            card.anchorMax = new Vector2(0.92f, 0.71f);
            card.offsetMin = card.offsetMax = Vector2.zero;
            WiseStackUI.Surface(card,WiseStackTheme.Card);
            PopupText(card, "Title", title, 32, new Vector2(0.07f, 0.73f), new Vector2(0.93f, 0.95f), true);
            PopupText(card, "Message", message, 26, new Vector2(0.07f, 0.25f), new Vector2(0.93f, 0.73f), false);
            var button = new GameObject("Confirm", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<RectTransform>();
            button.SetParent(card, false);
            button.anchorMin = new Vector2(0.28f, 0.04f);
            button.anchorMax = new Vector2(0.72f, 0.22f);
            button.offsetMin = button.offsetMax = Vector2.zero;
            button.GetComponent<Image>().color = WiseStackTheme.Blue;
            button.GetComponent<Button>().onClick.AddListener(() =>
            {
                GameObject popup = confirmationPopup;
                confirmationPopup = null;
                Destroy(popup);
            });
            PopupText(button, "Label", "확인", 28, Vector2.zero, Vector2.one, true).color = Color.white;
            WiseStackUI.StyleButton(button.GetComponent<Button>());
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
            text.color = WiseStackTheme.Navy;
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

        private bool TrySaveSettings(out string summary)
        {
            summary = null;
            if (!TryRead(warehouseAreaInput, out float area) || area <= 0f || area > 1000000f)
            {
                presentation?.Error(warehouseAreaInput,"0 초과~1,000,000㎡ 범위로 입력하세요.");
                SetSaveStatus("창고 면적을 0보다 큰 값으로 입력해 주세요 (㎡).");
                return false;
            }
            if (!TryRead(ceilingHeightInput, out float ceiling) || ceiling <= 0f)
            {
                presentation?.Error(ceilingHeightInput,"0보다 큰 높이를 입력하세요.");
                SetSaveStatus("창고 높이를 0보다 큰 값으로 입력해 주세요 (m).");
                return false;
            }
            if (!TryRead(palletWidthInput, out float width) ||
                width < AppSettings.MinPalletDimensionM || width > AppSettings.MaxPalletDimensionM)
            {
                presentation?.Error(palletWidthInput,"가로를 0.3~3m로 입력하세요.");
                SetSaveStatus("팔레트 가로를 0.3~3m로 입력해 주세요.");
                return false;
            }
            if (!TryRead(palletLengthInput, out float length) ||
                length < AppSettings.MinPalletDimensionM || length > AppSettings.MaxPalletDimensionM)
            {
                presentation?.Error(palletLengthInput,"세로를 0.3~3m로 입력하세요.");
                SetSaveStatus("팔레트 세로를 0.3~3m로 입력해 주세요.");
                return false;
            }
            if (!TryRead(palletMaxLoadInput, out float load) || load < 0f)
            {
                presentation?.Error(palletMaxLoadInput,"0kg 이상으로 입력하세요.");
                SetSaveStatus("팔레트 최대 하중을 0kg 이상으로 입력해 주세요.");
                return false;
            }
            AppSettings.WarehouseAreaM2 = area;
            AppSettings.CeilingHeightM = ceiling;
            AppSettings.PalletWidth = width;
            AppSettings.PalletLength = length;
            AppSettings.PalletMaxLoadKg = load;
            hasConfirmedSettings=true;
            presentation?.SetProvenance(true);
            summary = $"창고 {area:F1}㎡ · 높이 {ceiling:F2}m\n팔레트 {width:F2} × {length:F2}m · 최대 {load:F0}kg";
            SetSaveStatus("입력 완료 · 측정 화면으로 이동해 화물을 분류하세요.");
            return true;
        }

        // Called by NavBar buttons
        public void GoToARMain()
        {
            if (!TrySaveSettings(out _)) return;
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
            if (!TrySaveSettings(out _)) return;
            SceneManager.LoadScene("DangerScene");
        }

        public void GoToMeasure()
        {
            if (!TrySaveSettings(out _)) return;
            SceneManager.LoadScene("MeasureScene");
        }
    }
}
