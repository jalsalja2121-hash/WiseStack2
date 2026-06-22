using ARLogistics.Data;
using UnityEngine;
using UnityEngine.UI;

namespace ARLogistics.Navigation
{
    public enum AppScreen { Home = 0, StackPreview = 1, DangerOverlay = 2, Measurement = 3 }

    public class AppNavigator : MonoBehaviour
    {
        public static AppNavigator Instance { get; private set; }

        [Header("Screen Panels")]
        [SerializeField] private GameObject homePanel;
        [SerializeField] private GameObject stackPreviewPanel;
        [SerializeField] private GameObject dangerOverlayPanel;
        [SerializeField] private GameObject measurementPanel;

        [Header("Nav Buttons")]
        [SerializeField] private Button homeBtn;
        [SerializeField] private Button stackPreviewBtn;
        [SerializeField] private Button dangerOverlayBtn;
        [SerializeField] private Button measurementBtn;

        [Header("Colors")]
        [SerializeField] private Color activeColor   = new Color(0.18f, 0.60f, 1.00f, 1f);
        [SerializeField] private Color inactiveColor = new Color(0.35f, 0.35f, 0.35f, 1f);

        public AppScreen CurrentScreen { get; private set; } = AppScreen.Home;

private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            AutoDiscover();
        }

private void AutoDiscover()
        {
            foreach (Transform child in transform)
            {
                switch (child.name)
                {
                    case "HomePanel":          homePanel          = homePanel          ?? child.gameObject; break;
                    case "StackPreviewPanel":  stackPreviewPanel  = stackPreviewPanel  ?? child.gameObject; break;
                    case "DangerOverlayPanel": dangerOverlayPanel = dangerOverlayPanel ?? child.gameObject; break;
                    case "MeasurementPanel":   measurementPanel   = measurementPanel   ?? child.gameObject; break;
                    case "NavBar":
                        foreach (Transform nb in child)
                        {
                            switch (nb.name)
                            {
                                case "HomeBtn":          homeBtn          = homeBtn          ?? nb.GetComponent<Button>(); break;
                                case "StackPreviewBtn":  stackPreviewBtn  = stackPreviewBtn  ?? nb.GetComponent<Button>(); break;
                                case "DangerOverlayBtn": dangerOverlayBtn = dangerOverlayBtn ?? nb.GetComponent<Button>(); break;
                                case "MeasurementBtn":   measurementBtn   = measurementBtn   ?? nb.GetComponent<Button>(); break;
                            }
                        }
                        break;
                }
            }
        }


        private void Start()
        {
            homeBtn?.onClick.AddListener(() => NavigateTo(AppScreen.Home));
            stackPreviewBtn?.onClick.AddListener(() => NavigateTo(AppScreen.StackPreview));
            dangerOverlayBtn?.onClick.AddListener(() => NavigateTo(AppScreen.DangerOverlay));
            measurementBtn?.onClick.AddListener(() => NavigateTo(AppScreen.Measurement));
            NavigateTo(AppScreen.Home);
        }

        public void NavigateTo(AppScreen screen)
        {
            if (screen == AppScreen.StackPreview && !BoxMeasurementStore.TryGet(out _))
            {
                FindFirstObjectByType<SceneNavigator>()?.ShowMeasurementRequiredDialog();
                return;
            }

            CurrentScreen = screen;
            homePanel?.SetActive(screen == AppScreen.Home);
            stackPreviewPanel?.SetActive(screen == AppScreen.StackPreview);
            dangerOverlayPanel?.SetActive(screen == AppScreen.DangerOverlay);
            measurementPanel?.SetActive(screen == AppScreen.Measurement);
            RefreshButtonColors();
        }

        private void RefreshButtonColors()
        {
            SetBtnColor(homeBtn,          CurrentScreen == AppScreen.Home);
            SetBtnColor(stackPreviewBtn,  CurrentScreen == AppScreen.StackPreview);
            SetBtnColor(dangerOverlayBtn, CurrentScreen == AppScreen.DangerOverlay);
            SetBtnColor(measurementBtn,   CurrentScreen == AppScreen.Measurement);
        }

        private void SetBtnColor(Button btn, bool active)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = active ? activeColor : inactiveColor;
        }
    }
}
