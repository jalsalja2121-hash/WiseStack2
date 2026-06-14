using UnityEngine;

namespace ARLogistics.DangerScene
{
    public class PresetScenarioController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private DangerOverlayUI overlayUI;

        // SafeButton.onClick 에 연결
        public void OnSafePreset()
        {
            overlayUI?.ForceDisplay(DangerLevel.Safe,
                "박스 2개  |  높이 0.8m  |  무게 15kg\n모든 기준 안전 범위 이내");
        }

        // WarningButton.onClick 에 연결
        public void OnWarningPreset()
        {
            overlayUI?.ForceDisplay(DangerLevel.Warning,
                "박스 6개  |  높이 1.6m  |  무게 32kg\n적재 높이 주의 구간");
        }

        // DangerButton.onClick 에 연결
        public void OnDangerPreset()
        {
            overlayUI?.ForceDisplay(DangerLevel.Danger,
                "박스 9개  |  높이 2.5m  |  무게 45kg\n붕괴 위험! 즉시 재배치 필요");
        }
    }
}
