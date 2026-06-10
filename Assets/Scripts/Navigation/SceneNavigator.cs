using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace ARLogistics.Navigation
{
    public class SceneNavigator : MonoBehaviour
    {
        private void Awake()
        {
            WireToHome("BackBtn");
            WireToHome("BackToHomeBtn");
            WireToHome("HomeBtn");
            Wire("DangerOverlayBtn", GoToDanger);
            Wire("MeasurementBtn",   GoToMeasure);
            Wire("ARMainBtn",        GoToARMain);
            Wire("StackPreviewBtn",  GoToARMain);
        }

        private void WireToHome(string goName) => Wire(goName, GoToHome);

        private void Wire(string goName, UnityEngine.Events.UnityAction action)
        {
            var go = GameObject.Find(goName);
            if (go == null) return;
            var btn = go.GetComponent<Button>();
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(action);
        }

        public void GoToHome()    => SceneManager.LoadScene("HomeScene");
        public void GoToARMain()  => SceneManager.LoadScene("ARMain");
        public void GoToDanger()  => SceneManager.LoadScene("DangerScene");
        public void GoToMeasure() => SceneManager.LoadScene("MeasureScene");

        public void GoToScene(string sceneName) => SceneManager.LoadScene(sceneName);
    }
}
