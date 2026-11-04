using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ARLogistics.Data;
using ARLogistics.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Isolated render/interaction checks against the actual recommendation scene. Does not save scenes or preferences.</summary>
public static class RecommendationUIReview
{
    [MenuItem("WiseStack/Review/Verify recommendation UI")]
    public static void Run()
    {
        var report = new StringBuilder();
        Directory.CreateDirectory("Temp/RecommendationReview");
        foreach (var size in new[] { new Vector2Int(720,1280), new Vector2Int(720,1600),
            new Vector2Int(1080,1920), new Vector2Int(1280,720) })
            Render(size.x, size.y, report);
        int[] winners = new int[3];
        for (int id = 0; id < ProductSpecTable.ClassNames.Length; id++)
        {
            var spec = ProductSpecTable.Get(id);
            WarehouseRecommendationCalculator.TryCalculate(500, 3, 1.2f, 1, 1000,
                new BoxMeasurement(spec.WidthM, spec.LengthM, spec.HeightM, id, "review"), ProductSpecTable.RecommendationWeightKg(id),
                ProductSpecTable.ClassName(id), out var source);
            var comparison = WarehouseStrategyCalculator.Calculate(source);
            var decision = WarehouseStrategyEvaluator.Evaluate(source, comparison);
            if (decision.HasRecommendation) winners[decision.Index]++;
            report.AppendLine($"class {id}: selected={decision.Index}, A={comparison.A.PerPallet}, B={comparison.B.PerPallet}, C={comparison.C.PerPallet}");
        }
        report.AppendLine($"Selection distribution A/B/C: {winners[0]}/{winners[1]}/{winners[2]}");
        File.WriteAllText("Temp/RecommendationReview/results.txt", report.ToString());
        Debug.Log("[RecommendationUIReview] PASS\n" + report);
    }

    private static void Render(int width, int height, StringBuilder report)
    {
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/DangerScene.unity");
        var oldRT = RenderTexture.active;
        RenderTexture rt = null;
        Texture2D image = null;
        try
        {
            var roots = scene.GetRootGameObjects();
            var view = roots.SelectMany(r => r.GetComponentsInChildren<WarehouseRecommendationView>(true)).Single();
            var canvas = view.GetComponent<Canvas>();
            if (canvas == null) throw new Exception("Recommendation view must remain attached to the scene Canvas.");
            var scaler = canvas.GetComponent<CanvasScaler>();
            report.AppendLine($"Scene binding: {scene.path}, view={view.name}, reference={scaler.referenceResolution}");
            scaler.enabled = false;
            // 720 logical units preserve the scene's width-based scaler at each output resolution.
            float logicalHeight = height * 720f / width;
            var rootRect = (RectTransform)canvas.transform;
            canvas.renderMode = RenderMode.WorldSpace;
            rootRect.position = Vector3.zero;
            rootRect.localScale = Vector3.one;
            rootRect.sizeDelta = new Vector2(720, logicalHeight);
            var go = new GameObject("RecommendationReviewCamera", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            var camera = go.GetComponent<Camera>();
            camera.scene = scene;
            camera.orthographic = true;
            camera.orthographicSize = logicalHeight / 2;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = WiseStackTheme.Pale;
            rt = new RenderTexture(width, height, 24);
            camera.targetTexture = rt;
            canvas.worldCamera = camera;
            WarehouseRecommendationCalculator.TryCalculate(500, 3, 1.2f, 1, 1000,
                new BoxMeasurement(.35f, .48f, .315f, 6, "review"), 3.008f, "대표 화물", out var source);
            view.Present(source, WarehouseStrategyCalculator.Calculate(source));
            var content = view.transform.Find("RecommendationViewport/RecommendationContent") as RectTransform;
            var viewport = content.parent.GetComponent<RectTransform>();
            var scroll = viewport.GetComponent<ScrollRect>();
            var details = content.Find("RecommendationDetails").gameObject;
            var button = content.Find("RecommendationDetailsButton").GetComponent<Button>();
            for (int pass = 0; pass < 3; pass++)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            }
            if (details.activeSelf) throw new Exception("Details must start collapsed.");
            CheckText(content);
            float collapsed = content.rect.height;
            Capture(camera, rt, ref image, width, height, "collapsed");
            button.onClick.Invoke();
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            if (!details.activeSelf || content.rect.height <= collapsed) throw new Exception("Details did not expand.");
            CheckText(content);
            float expanded = content.rect.height;
            scroll.verticalNormalizedPosition = 0;
            Canvas.ForceUpdateCanvases();
            if (content.anchoredPosition.y <= 0) throw new Exception("Scroll cannot reach expanded details.");
            Capture(camera, rt, ref image, width, height, "details");
            button.onClick.Invoke();
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            if (details.activeSelf || Mathf.Abs(content.rect.height - collapsed) > 1) throw new Exception("Collapse did not restore height.");
            report.AppendLine($"{width}x{height}: PASS text fit, expand/collapse, scroll; viewport={viewport.rect.height:F1}, collapsed={collapsed:F1}, expanded={expanded:F1}");
            typeof(WarehouseRecommendationView).GetMethod("PositionForScreen", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(view, new object[] { new Rect(24, 24, 672, logicalHeight - 56), new Vector2(720, logicalHeight) });
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            CheckText(content);
            if (viewport.offsetMin.x < 48 || viewport.offsetMin.y < 136 || viewport.offsetMax.y > -116)
                throw new Exception("Safe area or navigation clearance failed.");
            report.AppendLine($"{width}x{height}: PASS simulated top/bottom/side safe insets");
            var unavailable = WarehouseStrategyCalculator.Calculate(source);
            unavailable.A.PerPallet = unavailable.B.PerPallet = unavailable.C.PerPallet = 0;
            view.Present(source, unavailable);
            if (content.Find("RecommendationState") == null || content.Find("RecommendedStrategy") != null)
                throw new Exception("Unavailable state failed.");
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            CheckText(content);
            report.AppendLine($"{width}x{height}: PASS no recommendation state");
            source.AssumedCargoWeightKg = 0;
            view.Present(source, WarehouseStrategyCalculator.Calculate(source));
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            CheckText(content);
            var labels = content.GetComponentsInChildren<Text>(true).Select(t => t.text).ToArray();
            if (!labels.Any(t => t.Contains("하중 확인 필요")) || labels.Any(t => t.Contains("/100점") || t.Contains("75점")))
                throw new Exception("Conditional load status or obsolete scoring text failed.");
            Capture(camera, rt, ref image, width, height, "missing-weight");
            report.AppendLine($"{width}x{height}: PASS conditional missing-weight display and no weighted score");
        }
        finally
        {
            RenderTexture.active = oldRT;
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            EditorSceneManager.ClosePreviewScene(scene);
            if (rt != null) UnityEngine.Object.DestroyImmediate(rt);
        }
    }

    private static void CheckText(RectTransform content)
    {
        foreach (var text in content.GetComponentsInChildren<Text>())
            if (text.preferredHeight > text.rectTransform.rect.height + 1)
                throw new Exception($"Text overflow: {text.text}, preferred={text.preferredHeight}, actual={text.rectTransform.rect.height}");
    }

    private static void Capture(Camera camera, RenderTexture rt, ref Texture2D image, int width, int height, string state)
    {
        camera.Render();
        RenderTexture.active = rt;
        if (image == null) image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        File.WriteAllBytes($"Temp/RecommendationReview/{width}x{height}-{state}.png", image.EncodeToPNG());
    }
}
