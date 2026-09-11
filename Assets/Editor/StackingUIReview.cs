using System.IO;
using ARLogistics.Data;
using ARLogistics.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using System;
using System.Reflection;
using ARLogistics.Features;
using ARLogistics.Managers;

/// <summary>Render the actual UI builder in an isolated preview scene without camera/network access.</summary>
public static class StackingUIReview
{
    [MenuItem("WiseStack/Review/Check box rendering")]
    public static void CheckRendering()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run in ARMain Play Mode.");
        var preview = UnityEngine.Object.FindFirstObjectByType<ARMainPalletStackPreview>();
        if (preview == null) throw new InvalidOperationException("ARMain preview missing.");
        bool runInBackground = Application.runInBackground;
        Application.runInBackground = true;
        preview.ResetSimulation();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(ARMainPalletStackPreview);
        var s = ProductSpecTable.Get(6);
        type.GetField("currentMeasurement", flags).SetValue(preview, new BoxMeasurement(s.WidthM, s.LengthM, s.HeightM, 6, "Review fixture"));
        type.GetField("hasMeasurement", flags).SetValue(preview, true);
        type.GetMethod("RefreshPlan", flags).Invoke(preview, null);
        var camera = Camera.main;
        Vector3 position = camera != null ? camera.transform.position + Vector3.forward * 3 - Vector3.up : Vector3.zero;
        type.GetMethod("CreatePallet", flags).Invoke(preview, new object[] { new Pose(position, Quaternion.identity) });
        float originalCeiling = ARLogistics.AppSettings.CeilingHeightM;
        try
        {
            // Reproduce the reported runaway tower with an excessively high ceiling input.
            ARLogistics.AppSettings.CeilingHeightM = 100;
            type.GetMethod("GenerateStackPreview", flags).Invoke(preview, null);
        }
        finally { ARLogistics.AppSettings.CeilingHeightM = originalCeiling; }
        double deadline = EditorApplication.timeSinceStartup + 8;
        EditorApplication.CallbackFunction poll = null;
        poll = () =>
        {
            if (!Application.isPlaying || preview == null)
            { EditorApplication.update -= poll; Application.runInBackground = runInBackground; return; }
            EditorApplication.QueuePlayerLoopUpdate();
            bool busy = type.GetField("spawnBoxesRoutine", flags).GetValue(preview) != null;
            if (busy && EditorApplication.timeSinceStartup < deadline) return;
            EditorApplication.update -= poll;
            if (busy) { Application.runInBackground = runInBackground; Debug.LogError("[StackingReview] Rendering timeout."); return; }
            var boxes = (System.Collections.Generic.List<GameObject>)type.GetField("activeBoxes", flags).GetValue(preview);
            var p = (StackingPlan)type.GetField("plan", flags).GetValue(preview);
            var guide = (GameObject)type.GetField("heightGuide", flags).GetValue(preview);
            if (p.Layers != 5 || p.Total != 30 || p.StackHeight > StackingCalculator.PreviewHeightLimit + .001f ||
                boxes.Count != Math.Min(512, p.Total) || guide == null || Math.Abs(guide.transform.localPosition.y - p.StackHeight) > .001f)
            { Application.runInBackground = runInBackground; Debug.LogError("[StackingReview] Rendering count or height guide mismatch."); return; }
            var pallet = (Transform)type.GetField("palletTransform", flags).GetValue(preview);
            foreach (var box in boxes)
            foreach (var renderer in box.GetComponentsInChildren<Renderer>())
                if (renderer.bounds.max.y - pallet.position.y > StackingCalculator.PreviewHeightLimit + .001f)
                { Application.runInBackground = runInBackground; Debug.LogError("[StackingReview] Rendered box exceeds preview height limit."); return; }
            Debug.Log($"[StackingReview] PASS: {boxes.Count} boxes, {p.Layers} layers, guide at {p.StackHeight:F2}m.");
            Directory.CreateDirectory("Temp/UXReview");
            ScreenCapture.CaptureScreenshot("Temp/UXReview/stacking-live.png");
            EditorApplication.delayCall += () => Application.runInBackground = runInBackground;
        };
        EditorApplication.update += poll;
    }

    [MenuItem("WiseStack/Review/Check active stacking flow")]
    public static void CheckFlow()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run in ARMain Play Mode.");
        var preview = UnityEngine.Object.FindFirstObjectByType<ARMainPalletStackPreview>();
        var manager = UnityEngine.Object.FindFirstObjectByType<WarehouseManager>();
        if (preview == null || manager == null) throw new InvalidOperationException("ARMain components missing.");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(ARMainPalletStackPreview);
        float originalLoad = ARLogistics.AppSettings.PalletMaxLoadKg;
        try
        {
            for (int id = 0; id < 10; id++)
            {
                var s = ProductSpecTable.Get(id);
                var expected = StackingCalculator.Calculate(s.WidthM, s.LengthM, s.HeightM, s.WeightKg,
                    ARLogistics.AppSettings.PalletWidth, ARLogistics.AppSettings.PalletLength,
                    ARLogistics.AppSettings.CeilingHeightM, originalLoad);
                var report = (ProductCapacity)typeof(WarehouseManager).GetMethod("CalculateCapacityFromTable", flags)
                    .Invoke(manager, new object[] { id, Vector3.zero });
                if (report.totalUnits != expected.Total) throw new Exception("Manager parity failed for " + id);
                type.GetField("currentMeasurement", flags).SetValue(preview,
                    new BoxMeasurement(s.WidthM, s.LengthM, s.HeightM, id, "Review fixture"));
                type.GetField("hasMeasurement", flags).SetValue(preview, true);
                type.GetMethod("SelectLayout", flags).Invoke(preview, new object[] { StackOrientation.Recommended });
                if (preview.EstimatedCapacity != expected.Total) throw new Exception("Preview parity failed for " + id);
            }
            var sample = ProductSpecTable.Get(9);
            foreach (var button in preview.GetComponentsInChildren<Button>())
            {
                if (!button.name.StartsWith("Layout_")) continue;
                int index = int.Parse(button.name.Substring(7));
                button.onClick.Invoke();
                var expected = StackingCalculator.Calculate(sample.WidthM, sample.LengthM, sample.HeightM, sample.WeightKg,
                    ARLogistics.AppSettings.PalletWidth, ARLogistics.AppSettings.PalletLength,
                    ARLogistics.AppSettings.CeilingHeightM, originalLoad, (StackOrientation)index);
                if (preview.EstimatedCapacity != expected.Total) throw new Exception("Button wiring failed: " + index);
            }
            ARLogistics.AppSettings.PalletMaxLoadKg = 0;
            type.GetMethod("SelectLayout", flags).Invoke(preview, new object[] { StackOrientation.Recommended });
            if (preview.EstimatedCapacity != 0) throw new Exception("Zero-load preview not empty.");
            var action = (Button)type.GetField("placeButton", flags).GetValue(preview);
            if (action.interactable) throw new Exception("Zero-load placement should be disabled.");
            Debug.Log("[StackingReview] PASS: 10 class parity checks, 3 live buttons, zero-load action blocked.");
        }
        finally
        {
            ARLogistics.AppSettings.PalletMaxLoadKg = originalLoad;
            preview.ResetSimulation();
        }
    }

    [MenuItem("WiseStack/Review/Render stacking UI")]
    public static void Render()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var previousRT = RenderTexture.active;
        var rt = new RenderTexture(1080, 1920, 24);
        Texture2D image = null;
        Camera camera = null;
        try
        {
            var cameraObject = new GameObject("ReviewCamera", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
            camera = cameraObject.GetComponent<Camera>();
            camera.scene = scene;
            camera.orthographic = true;
            camera.orthographicSize = 960;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.15f, .19f, .23f);
            camera.targetTexture = rt;
            var canvasObject = new GameObject("ReviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasObject, scene);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1080, 1920);
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.enabled = false;
            var panel = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
            panel.SetParent(canvas.transform, false); panel.anchorMin = Vector2.zero; panel.anchorMax = Vector2.one;
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(panel, false);
            var textObject = new GameObject("Result", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(card.transform, false);
            var status = textObject.GetComponent<Text>();
            status.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); status.color = Color.white;
            var ui = new StackingPreviewUI();
            ui.Build(panel, status, Button(panel, "Place"), Button(panel, "Reset"), _ => { });
            var plans = new StackingPlan[3];
            for (int i = 0; i < 3; i++) plans[i] = StackingCalculator.Calculate(.35f, .48f, .315f, 3.008f, 1.2f, 1, 100, 1000, (StackOrientation)i);
            ui.ShowPlans(plans, StackOrientation.Recommended);
            ui.SetTitle("적재 미리보기");
            var plan = plans[0];
            status.text = $"<b>이번 조건에서는 {plan.Layers}단으로 미리 봐요</b>\n한 단에 {plan.PerLayer}개 · 팔레트당 총 {plan.Total}개\n전체 높이 {plan.StackHeight:F2}m · 화물 무게 {plan.TotalWeight:F1}kg\n바닥 사용률 {plan.Utilization:F0}%\n등록 규격 35 × 48 × 32cm\n{plan.Message}\n실제 적재 전 포장 강도·결속을 확인하세요.";
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = rt;
            image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply();
            Directory.CreateDirectory("Temp/UXReview");
            File.WriteAllBytes("Temp/UXReview/stacking-ui.png", image.EncodeToPNG());
            Debug.Log("[StackingReview] Rendered Temp/UXReview/stacking-ui.png");
        }
        finally
        {
            RenderTexture.active = previousRT;
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            if (camera != null) camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static Button Button(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
        label.transform.SetParent(go.transform, false);
        var text = label.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.color = Color.white; text.alignment = TextAnchor.MiddleCenter;
        return go.GetComponent<Button>();
    }
}
