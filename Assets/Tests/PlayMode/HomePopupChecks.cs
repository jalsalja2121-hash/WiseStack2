using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class HomePopupChecks
{
    [UnityTest]
    public IEnumerator ExistingButton_OpensClosesAndReopensPopup()
    {
        SceneManager.LoadScene("HomeScene");
        yield return null;
        Button existing = GameObject.Find("VoiceConfirmBtn")?.GetComponent<Button>();
        Assert.IsNotNull(existing);
        Assert.IsTrue(existing.interactable);
        existing.onClick.Invoke();
        yield return null;
        GameObject popup = GameObject.Find("WarehouseConfirmationPopup");
        string status = GameObject.Find("VoiceStatusText")?.GetComponent<Text>()?.text;
        string inputs = string.Join(",", new[] { "AreaInput", "CeilingInput", "PalletWidthInput", "PalletLengthInput", "MaxLoadInput" }
            .Select(name => name + "=" + GameObject.Find(name)?.GetComponent<InputField>()?.text));
        Assert.IsNotNull(popup, $"The existing confirm button must open the popup. status={status}, inputs={inputs}");
        Assert.IsTrue(popup.activeInHierarchy);
        Assert.AreEqual(100, popup.GetComponent<Canvas>().sortingOrder);
        Assert.IsNotNull(popup.GetComponent<GraphicRaycaster>());
        Assert.Greater(popup.GetComponent<RectTransform>().rect.width, 0f);
        Assert.Greater(popup.GetComponent<RectTransform>().rect.height, 0f);
        popup.transform.Find("Card/Confirm").GetComponent<Button>().onClick.Invoke();
        yield return null;
        Assert.IsNull(GameObject.Find("WarehouseConfirmationPopup"));
        existing.onClick.Invoke();
        yield return null;
        Assert.IsNotNull(GameObject.Find("WarehouseConfirmationPopup"));
        GameObject.Find("WarehouseConfirmationPopup").transform.Find("Card/Confirm")
            .GetComponent<Button>().onClick.Invoke();
        yield return null;
        var controller = GameObject.Find("Canvas").GetComponent("HomeInputController");
        Assert.IsNotNull(controller);
        controller.GetType().GetMethod("ShowAutomaticEntryConfirmation").Invoke(controller, null);
        yield return null;
        Assert.IsNotNull(GameObject.Find("WarehouseConfirmationPopup"));
        GameObject.Find("WarehouseConfirmationPopup").transform.Find("Card/Confirm")
            .GetComponent<Button>().onClick.Invoke();
        yield return null;
        GameObject.Find("PalletWidthInput").GetComponent<InputField>().text = "99";
        existing.onClick.Invoke();
        yield return null;
        Assert.IsNull(GameObject.Find("WarehouseConfirmationPopup"));
    }
}

public sealed class RecommendationChoiceChecks
{
    private static readonly string[] Keys =
    {
        "WiseStack.BoxMeasurement.HasValue", "WiseStack.BoxMeasurement.WidthM",
        "WiseStack.BoxMeasurement.DepthM", "WiseStack.BoxMeasurement.HeightM",
        "WiseStack.BoxMeasurement.ClassId", "WiseStack.BoxMeasurement.SourceName"
    };

    [UnityTest]
    public IEnumerator Recommendation_ShowsComparisonAndTogglesCalculationDetails()
    {
        bool[] existed = Keys.Select(PlayerPrefs.HasKey).ToArray();
        string[] values = Keys.Select(key => PlayerPrefs.GetString(key)).ToArray();
        float[] floats = Keys.Select(key => PlayerPrefs.GetFloat(key)).ToArray();
        int[] ints = Keys.Select(key => PlayerPrefs.GetInt(key)).ToArray();
        try
        {
            var store = System.Type.GetType("ARLogistics.Data.BoxMeasurementStore, Assembly-CSharp");
            Assert.IsNotNull(store);
            store.GetMethod("Save").Invoke(null, new object[] { 0.7f, 0.4f, 0.25f, 0, "test" });
            SceneManager.LoadScene("DangerScene");
            yield return null;
            yield return null;
            Assert.IsNotNull(GameObject.Find("StrategyComparison"));
            Assert.IsNull(GameObject.Find("ZoneMap"));
            var view = GameObject.Find("RecommendedStrategy");
            Assert.IsNotNull(view);
            var content = view.transform.parent;
            var detail = content.Find("RecommendationDetails").gameObject;
            var toggle = content.Find("RecommendationDetailsButton").GetComponent<Button>();
            Assert.IsFalse(detail.activeSelf);
            toggle.onClick.Invoke();
            Assert.IsTrue(detail.activeSelf);
            var text = string.Join("\n", detail.GetComponentsInChildren<Text>().Select(t => t.text));
            StringAssert.Contains("수량", text);
            StringAssert.Contains("허용", text);
            StringAssert.DoesNotContain("/100점", text);
            for (int i = 0; i < 3; i++)
            {
                Assert.IsNotNull(GameObject.Find("Strategy" + i));
                StringAssert.Contains(new[] { "A · 표준 적재", "B · 공간 효율", "C · 낮은 높이" }[i], text);
            }
            toggle.onClick.Invoke();
            Assert.IsFalse(detail.activeSelf);
            Assert.IsTrue(view.GetComponentInChildren<Button>().interactable);
        }
        finally
        {
            for (int i = 0; i < Keys.Length; i++)
            {
                if (!existed[i]) { PlayerPrefs.DeleteKey(Keys[i]); continue; }
                if (i == 0 || i == 4) PlayerPrefs.SetInt(Keys[i], ints[i]);
                else if (i == 5) PlayerPrefs.SetString(Keys[i], values[i]);
                else PlayerPrefs.SetFloat(Keys[i], floats[i]);
            }
            PlayerPrefs.Save();
        }
    }
}
