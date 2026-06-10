#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
using UnityEngine.Rendering.Universal;
using ARLogistics.UI;
using ARLogistics.Managers;
using ARLogistics.API;
using ARLogistics.Detection;
using ARLogistics.AR;

public static class WiseStackSceneSetup
{
    const string NAVBAR_PREFAB = "Assets/Prefabs/NavBar.prefab";

    [MenuItem("WiseStack/Setup Feature Scenes")]
    public static void SetupAll()
    {
        EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        AddNavBarToHome();
        SetupDangerScene();
        SetupMeasureScene();
        FixARMain();

        Debug.Log("[WiseStack] 모든 씬 설정 완료!");
    }

    [MenuItem("WiseStack/Add NavBar to All Scenes")]
    public static void AddNavBarToAllScenes()
    {
        EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        AddNavBarToHome();
        AddNavBarToScene("Assets/Scenes/DangerScene.unity",  "Canvas");
        AddNavBarToScene("Assets/Scenes/MeasureScene.unity", "Canvas");
        // ARMain은 이미 NavBar 존재
        EnsureNavBarInCurrentARMain();

        Debug.Log("[WiseStack] NavBar 추가 완료!");
    }

    // ── HomeScene ──────────────────────────────────────────────────────
    static void AddNavBarToHome()
    {
        var scene = EditorSceneManager.OpenScene(
            "Assets/Scenes/HomeScene.unity", OpenSceneMode.Single);
        EnsureNavBar("Canvas");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[WiseStack] HomeScene NavBar 추가 완료");
    }

    // ── DangerScene ────────────────────────────────────────────────────
    static void SetupDangerScene()
    {
        var scene = EditorSceneManager.OpenScene(
            "Assets/Scenes/DangerScene.unity", OpenSceneMode.Single);

        EnsureARSession();
        EnsureXROrigin(addFrameProvider: true);
        EnsureManagers();
        EnsureDirectionalLight();

        var canvas = GameObject.Find("Canvas");
        if (canvas != null && canvas.GetComponent<ARUIController>() == null)
            canvas.AddComponent<ARUIController>();

        EnsureNavBar("Canvas");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[WiseStack] DangerScene 설정 완료");
    }

    // ── MeasureScene ───────────────────────────────────────────────────
    static void SetupMeasureScene()
    {
        var scene = EditorSceneManager.OpenScene(
            "Assets/Scenes/MeasureScene.unity", OpenSceneMode.Single);

        EnsureARSession();
        EnsureXROrigin(addFrameProvider: false);
        EnsureDirectionalLight();
        EnsureNavBar("Canvas");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[WiseStack] MeasureScene 설정 완료");
    }

    // ── ARMain: AppNavigator 제거, StackPreviewPanel 활성화 ─────────────
    static void FixARMain()
    {
        var scene = EditorSceneManager.OpenScene(
            "Assets/Scenes/ARMain.unity", OpenSceneMode.Single);

        var canvas = GameObject.Find("AR Canvas");
        if (canvas != null)
        {
            var nav = canvas.GetComponent<ARLogistics.Navigation.AppNavigator>();
            if (nav != null)
            {
                Object.DestroyImmediate(nav);
                Debug.Log("[WiseStack] AppNavigator 제거됨");
            }
        }

        string[] toHide = { "HomePanel", "DangerOverlayPanel", "MeasurementPanel" };
        foreach (var root in scene.GetRootGameObjects())
            SetActiveByNameRecursive(root.transform, toHide, false);
        SetActiveByNameInScene(scene, "StackPreviewPanel", true);

        // NavBar는 AR Canvas 안에 이미 존재 — 중복 방지
        EnsureNavBar("AR Canvas");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[WiseStack] ARMain 정리 완료");
    }

    static void EnsureNavBarInCurrentARMain()
    {
        var scene = EditorSceneManager.OpenScene(
            "Assets/Scenes/ARMain.unity", OpenSceneMode.Single);
        EnsureNavBar("AR Canvas");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    // ── NavBar 프리팹 인스턴스화 ────────────────────────────────────────
    static void EnsureNavBar(string canvasName)
    {
        // 이미 있으면 건너뜀
        if (GameObject.Find("NavBar") != null) return;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NAVBAR_PREFAB);
        if (prefab == null)
        {
            Debug.LogError($"[WiseStack] NavBar 프리팹을 찾을 수 없습니다: {NAVBAR_PREFAB}");
            return;
        }

        var canvas = GameObject.Find(canvasName);
        if (canvas == null)
        {
            Debug.LogError($"[WiseStack] Canvas '{canvasName}' 를 찾을 수 없습니다");
            return;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas.transform);
        instance.name = "NavBar";
        Debug.Log($"[WiseStack] NavBar 추가됨 → {canvasName}");
    }

    static void AddNavBarToScene(string scenePath, string canvasName)
    {
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        EnsureNavBar(canvasName);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    // ── 공통 헬퍼 ───────────────────────────────────────────────────────

    static void SetActiveByNameInScene(UnityEngine.SceneManagement.Scene scene, string name, bool active)
    {
        foreach (var root in scene.GetRootGameObjects())
            if (SetActiveByNameRecursive(root.transform, new[] { name }, active)) return;
    }

    static bool SetActiveByNameRecursive(Transform t, string[] names, bool active)
    {
        bool found = false;
        foreach (var n in names)
            if (t.name == n) { t.gameObject.SetActive(active); found = true; }
        foreach (Transform child in t)
            SetActiveByNameRecursive(child, names, active);
        return found;
    }

    static void EnsureARSession()
    {
        if (GameObject.Find("AR Session") != null) return;
        var go = new GameObject("AR Session");
        go.AddComponent<ARSession>();
        Debug.Log("[WiseStack] AR Session 추가됨");
    }

    static void EnsureXROrigin(bool addFrameProvider)
    {
        if (GameObject.Find("XR Origin") != null) return;

        var xrGO   = new GameObject("XR Origin");
        var origin = xrGO.AddComponent<XROrigin>();
        xrGO.AddComponent<ARPlaneManager>();
        xrGO.AddComponent<ARRaycastManager>();

        var offsetGO = new GameObject("Camera Offset");
        offsetGO.transform.SetParent(xrGO.transform, false);

        var camGO = new GameObject("Main Camera");
        camGO.transform.SetParent(offsetGO.transform, false);
        camGO.tag = "MainCamera";

        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.nearClipPlane   = 0.1f;
        cam.farClipPlane    = 20f;

        camGO.AddComponent<ARCameraManager>();
        camGO.AddComponent<ARCameraBackground>();
        camGO.AddComponent<UniversalAdditionalCameraData>();

        if (addFrameProvider)
            camGO.AddComponent<CameraFrameProvider>();

        var so = new SerializedObject(origin);
        var camProp = so.FindProperty("m_Camera");
        if (camProp != null) camProp.objectReferenceValue = cam;
        var offsetProp = so.FindProperty("m_CameraFloorOffsetObject");
        if (offsetProp != null) offsetProp.objectReferenceValue = offsetGO;
        so.ApplyModifiedProperties();

        Debug.Log($"[WiseStack] XR Origin 추가됨 (FrameProvider: {addFrameProvider})");
    }

    static void EnsureManagers()
    {
        if (GameObject.Find("Managers") != null) return;
        var mgr = new GameObject("Managers");
        mgr.AddComponent<WarehouseManager>();
        mgr.AddComponent<GeminiApiClient>();
        mgr.AddComponent<YoloDetector>();
        mgr.AddComponent<ARDetectionBridge>();
        Debug.Log("[WiseStack] Managers 추가됨");
    }

    static void EnsureDirectionalLight()
    {
        var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (var l in lights)
            if (l.type == LightType.Directional) return;
        var go = new GameObject("Directional Light");
        go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        var light = go.AddComponent<Light>();
        light.type      = LightType.Directional;
        light.intensity = 1f;
        go.AddComponent<UniversalAdditionalLightData>();
        Debug.Log("[WiseStack] Directional Light 추가됨");
    }
}
#endif
