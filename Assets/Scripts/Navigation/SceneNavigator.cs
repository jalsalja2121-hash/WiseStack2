using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace ARLogistics.Navigation
{
    public class SceneNavigator : MonoBehaviour
    {
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

        public void GoToHome()    => SceneManager.LoadScene("HomeScene");
        public void GoToARMain()  => SceneManager.LoadScene("ARMain");
        public void GoToDanger()  => SceneManager.LoadScene("DangerScene");
        public void GoToMeasure() => SceneManager.LoadScene("MeasureScene");

        public void GoToScene(string sceneName) => SceneManager.LoadScene(sceneName);
    }
}
