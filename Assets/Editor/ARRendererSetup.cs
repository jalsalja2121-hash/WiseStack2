// ARRendererSetup.cs  — Editor only
// Adds ARBackgroundRendererFeature + ARCommandBufferSupportRendererFeature to Mobile_Renderer
// ARCommandBufferSupportRendererFeature is REQUIRED for Vulkan (Samsung/Android)
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;

public static class ARRendererSetup
{
    [MenuItem("Tools/AR/Add AR Renderer Features (Vulkan Fix)")]
    public static void AddARFeatures()
    {
        string[] guids = AssetDatabase.FindAssets("t:UniversalRendererData");
        if (guids.Length == 0)
        {
            Debug.LogError("[ARRendererSetup] No UniversalRendererData found!");
            return;
        }

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (renderer == null) continue;

            bool hasBackground = false;
            bool hasCommandBuffer = false;

            foreach (var feature in renderer.rendererFeatures)
            {
                if (feature is ARBackgroundRendererFeature)         hasBackground = true;
                if (feature is ARCommandBufferSupportRendererFeature) hasCommandBuffer = true;
            }

            bool changed = false;

            // 1. ARBackgroundRendererFeature
            if (!hasBackground)
            {
                var feat = ScriptableObject.CreateInstance<ARBackgroundRendererFeature>();
                feat.name = "ARBackgroundRendererFeature";
                feat.SetActive(true);
                AssetDatabase.AddObjectToAsset(feat, path);
                renderer.rendererFeatures.Add(feat);
                Debug.Log($"[ARRendererSetup] Added ARBackgroundRendererFeature to: {renderer.name}");
                changed = true;
            }
            else
            {
                Debug.Log($"[ARRendererSetup] {renderer.name}: ARBackgroundRendererFeature already present");
            }

            // 2. ARCommandBufferSupportRendererFeature (Vulkan 필수)
            if (!hasCommandBuffer)
            {
                var feat = ScriptableObject.CreateInstance<ARCommandBufferSupportRendererFeature>();
                feat.name = "ARCommandBufferSupportRendererFeature";
                feat.SetActive(true);
                AssetDatabase.AddObjectToAsset(feat, path);
                renderer.rendererFeatures.Add(feat);
                Debug.Log($"[ARRendererSetup] Added ARCommandBufferSupportRendererFeature to: {renderer.name}");
                changed = true;
            }
            else
            {
                Debug.Log($"[ARRendererSetup] {renderer.name}: ARCommandBufferSupportRendererFeature already present");
            }

            if (changed)
            {
                EditorUtility.SetDirty(renderer);
                AssetDatabase.SaveAssets();
            }
        }

        AssetDatabase.Refresh();
        Debug.Log("[ARRendererSetup] DONE.");
    }
}
#endif
