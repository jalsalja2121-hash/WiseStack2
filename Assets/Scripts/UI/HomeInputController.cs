using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

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

            // Wire navigation buttons
            WireButton("ARMainBtn",  GoToARMain);
            WireButton("DangerBtn",  GoToDanger);
            WireButton("MeasureBtn", GoToMeasure);
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

        private void PopulateFields()
        {
            if (warehouseAreaInput) warehouseAreaInput.text = AppSettings.WarehouseAreaM2.ToString("F0");
            if (ceilingHeightInput) ceilingHeightInput.text = AppSettings.CeilingHeightM.ToString("F1");
            if (palletWidthInput)   palletWidthInput.text   = AppSettings.PalletWidth.ToString("F2");
            if (palletLengthInput)  palletLengthInput.text  = AppSettings.PalletLength.ToString("F2");
            if (palletMaxLoadInput) palletMaxLoadInput.text = AppSettings.PalletMaxLoadKg.ToString("F0");
        }

        private void SaveSettings()
        {
            if (warehouseAreaInput != null &&
                float.TryParse(warehouseAreaInput.text, out float area))
                AppSettings.WarehouseAreaM2 = area;

            if (ceilingHeightInput != null &&
                float.TryParse(ceilingHeightInput.text, out float ceiling))
                AppSettings.CeilingHeightM = ceiling;

            if (palletWidthInput != null &&
                float.TryParse(palletWidthInput.text, out float pw))
                AppSettings.PalletWidth = pw;

            if (palletLengthInput != null &&
                float.TryParse(palletLengthInput.text, out float pl))
                AppSettings.PalletLength = pl;

            if (palletMaxLoadInput != null &&
                float.TryParse(palletMaxLoadInput.text, out float pm))
                AppSettings.PalletMaxLoadKg = pm;
        }

        // Called by NavBar buttons
        public void GoToARMain()
        {
            SaveSettings();
            SceneManager.LoadScene("ARMain");
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
