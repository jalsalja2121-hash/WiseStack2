using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ARLogistics.Data;
using ARLogistics.UI;
using ARLogistics.Features;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class ProductUIReview
{
    [MenuItem("WiseStack/Review/Capture product before")]
    public static void Before() => Run("before", false);
    [MenuItem("WiseStack/Review/Capture product after")]
    public static void After() => Run("after", true);

    private static void Invoke(object target, string method)
    {
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(target, null);
    }

    private static void Run(string phase, bool allSizes)
    {
        Directory.CreateDirectory("Temp/ProductUIReview");
        var report = new StringBuilder();
        var sizes = allSizes ? new[] { new Vector2Int(720,1280), new Vector2Int(1080,1920),
            new Vector2Int(1080,2400), new Vector2Int(1440,3200) } : new[] { new Vector2Int(720,1280) };
        foreach (string name in new[] { "HomeScene", "MeasureScene", "ARMain", "DangerScene" })
        foreach (var size in sizes) Render(name, size, phase, report);
        File.WriteAllText("Temp/ProductUIReview/" + phase + ".txt", report.ToString());
        Debug.Log("[ProductUIReview] " + phase + " complete\n" + report);
    }

    private static void Render(string name, Vector2Int size, string phase, StringBuilder report)
    {
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/" + name + ".unity");
        RenderTexture rt = null; Texture2D image = null;
        var previous = RenderTexture.active;
        try
        {
            var components = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Component>(true)).ToArray();
            var canvas = components.OfType<Canvas>().First(c => c.isRootCanvas);
            var rect = (RectTransform)canvas.transform;
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null) scaler.enabled = false;
            canvas.renderMode = RenderMode.WorldSpace;
            rect.localScale = Vector3.one; rect.position = Vector3.zero;
            float logicalHeight = size.y * 720f / size.x;
            rect.sizeDelta = new Vector2(720, logicalHeight);
            WarehouseRecommendationCalculator.TryCalculate(500, 3, 1.2f, 1, 1000,
                new BoxMeasurement(.35f,.48f,.315f,6,"review"),3.008f,"대표 화물", out var source);
            if (name == "HomeScene")
            {
                var controller = components.OfType<HomeInputController>().Single();
                Invoke(controller, "Awake"); Invoke(controller, "Start");
            }
            if (name == "MeasureScene")
            {
                var controller = components.OfType<MeasureSceneController>().Single();
                Invoke(controller, "Awake"); Invoke(controller, "Start");
                Invoke(controller, "LateUpdate");
            }
            if (name == "ARMain")
            {
                var text = components.OfType<Text>().Single(t => t.name == "SP_StatusText");
                var place = components.OfType<Button>().Single(t => t.name == "SP_PlaceBtn");
                var reset = components.OfType<Button>().Single(t => t.name == "SP_ClearBtn");
                var ui = new StackingPreviewUI();
                ui.Build(place.transform.parent, text, place, reset, _ => {});
                ui.ShowStrategies(WarehouseStrategyCalculator.Calculate(source), 0);
                ui.SetTitle("바닥을 인식하고 있습니다");
                text.text = "A · 표준 적재 · 기본 방향\n팔레트당 30개 · 5층 · 높이 1.73m\n바닥을 비추고 배치할 위치를 터치하세요.";
                ui.SetAction("바닥에 배치", true);
            }
            if (name == "DangerScene")
                components.OfType<WarehouseRecommendationView>().Single().Present(source, WarehouseStrategyCalculator.Calculate(source));
            foreach (var layout in canvas.GetComponentsInChildren<RectTransform>())
                LayoutRebuilder.ForceRebuildLayoutImmediate(layout);
            for (int i=0; i<3; i++) Canvas.ForceUpdateCanvases();
            if (phase=="after") Validate(canvas,components,name,report);
            var go = new GameObject("ProductReviewCamera", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
            var camera = go.GetComponent<Camera>(); camera.scene = scene;
            camera.transform.position = new Vector3(0,0,-10); camera.orthographic = true;
            camera.orthographicSize = logicalHeight/2;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.12f,.18f,.23f);
            rt = new RenderTexture(size.x,size.y,24); camera.targetTexture = rt; canvas.worldCamera = camera;
            camera.Render(); RenderTexture.active = rt;
            image = new Texture2D(size.x,size.y,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,size.x,size.y),0,0); image.Apply();
            File.WriteAllBytes($"Temp/ProductUIReview/{phase}-{name}-{size.x}x{size.y}.png",image.EncodeToPNG());
            report.AppendLine($"{name} {size.x}x{size.y}: rendered actual scene UI; camera background is a neutral Editor fixture, not device AR.");
        }
        finally
        {
            RenderTexture.active = previous;
            EditorSceneManager.ClosePreviewScene(scene);
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            if (rt != null) UnityEngine.Object.DestroyImmediate(rt);
        }
    }

    private static void Validate(Canvas canvas,Component[] components,string scene,StringBuilder report)
    {
        var nav=WiseStackUI.Find<RectTransform>(canvas.transform,"NavBar");
        if (nav==null || Mathf.Abs(nav.rect.height-WiseStackTheme.NavigationHeight)>1) throw new Exception("Navigation height mismatch");
        foreach (var button in nav.GetComponentsInChildren<Button>())
        {
            var rect=(RectTransform)button.transform;
            if (rect.rect.height<95 || rect.rect.width<96) throw new Exception("Tab touch target too small");
            if (WiseStackUI.Find<RectTransform>(rect,"TabIcon")==null) throw new Exception("Missing tab icon");
        }
        foreach (var scroll in canvas.GetComponentsInChildren<ScrollRect>())
        {
            if (scroll.content==null || scroll.viewport==null) continue;
            if (scroll.content.rect.height>scroll.viewport.rect.height+1)
            {
                scroll.verticalNormalizedPosition=0;
                Canvas.ForceUpdateCanvases();
                if (scroll.content.anchoredPosition.y<=0) throw new Exception("Scroll cannot reach content");
                scroll.verticalNormalizedPosition=1;
            }
        }
        foreach(var text in canvas.GetComponentsInChildren<Text>())
        {
            if(text.GetComponentInParent<InputField>()!=null) continue;
            if(text.preferredHeight>text.rectTransform.rect.height+2)
                throw new Exception("Text clipped: "+scene+" / "+text.name+" / "+text.text);
        }
        if (scene=="HomeScene")
        {
            var controller=components.OfType<HomeInputController>().Single();
            var input=WiseStackUI.Find<InputField>(canvas.transform,"AreaInput");
            string original=input.text; input.text="";
            var save=typeof(HomeInputController).GetMethod("TrySaveSettings",BindingFlags.Instance|BindingFlags.NonPublic);
            if ((bool)save.Invoke(controller,new object[] { null })) throw new Exception("Empty required value accepted");
            var error=WiseStackUI.Find<Text>(input.transform.parent,"FieldError");
            if (error==null || !error.gameObject.activeSelf) throw new Exception("Inline error not visible");
            input.text=original;
            var view=(HomeSetupView)typeof(HomeInputController).GetField("presentation",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
            bool confirmed=(bool)typeof(HomeInputController).GetField("hasConfirmedSettings",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            view.SetProvenance(confirmed);
            foreach(var scroll in canvas.GetComponentsInChildren<ScrollRect>()) scroll.verticalNormalizedPosition=1;
        }
        if (scene=="DangerScene")
        {
            var details=WiseStackUI.Find<RectTransform>(canvas.transform,"RecommendationDetails");
            var toggle=WiseStackUI.Find<Button>(canvas.transform,"RecommendationDetailsButton");
            if(details.gameObject.activeSelf) throw new Exception("Details should start collapsed");
            toggle.onClick.Invoke(); if(!details.gameObject.activeSelf) throw new Exception("Expand failed");
            toggle.onClick.Invoke(); if(details.gameObject.activeSelf) throw new Exception("Collapse failed");
        }
        report.AppendLine(scene+": PASS tab touch targets/icons, scroll, applicable inline validation/details callbacks");
    }
}
