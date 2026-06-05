#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Reflection;

[InitializeOnLoad]
public static class GameViewSetup
{
    static GameViewSetup()
    {
        EditorApplication.delayCall += ApplyS25;
    }

    [MenuItem("WiseStack/Game View - Galaxy S25 Portrait")]
    public static void ApplyS25()
    {
        const int W = 1080, H = 2340;
        const string LABEL = "Galaxy S25";
        var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        var editorAsm = typeof(Editor).Assembly;         // UnityEditor.dll
        var sizesT    = editorAsm.GetType("UnityEditor.GameViewSizes");
        var groupT    = editorAsm.GetType("UnityEditor.GameViewSizeGroup");
        var gvSizeT   = editorAsm.GetType("UnityEditor.GameViewSize");
        var sizeEnumT = editorAsm.GetType("UnityEditor.GameViewSizeType");

        if (sizesT == null) { Debug.LogError("[GameViewSetup] GameViewSizes 없음"); return; }

        // ScriptableSingleton<GameViewSizes> is in UnityEditor assembly
        object inst = null;
        var ssGenericT = editorAsm.GetType("UnityEditor.ScriptableSingleton`1");
        if (ssGenericT != null)
        {
            var ssConcreteT = ssGenericT.MakeGenericType(sizesT);
            inst = ssConcreteT.GetProperty("instance", flags)?.GetValue(null);
        }

        if (inst == null)
        {
            Debug.LogError("[GameViewSetup] singleton 없음 — 아래 BaseType 확인");
            Debug.Log($"BaseType: {sizesT.BaseType?.FullName}");
            Debug.Log($"BaseType2: {sizesT.BaseType?.BaseType?.FullName}");
            return;
        }

        object group = sizesT.GetMethod("GetGroup")?.Invoke(inst, new object[] { 2 }); // 2=Android
        if (group == null) { Debug.LogError("[GameViewSetup] Android group 없음"); return; }

        int total = (int)(groupT?.GetMethod("GetTotalCount")?.Invoke(group, null) ?? 0);
        var bfInst = BindingFlags.NonPublic | BindingFlags.Instance;

        for (int i = 0; i < total; i++)
        {
            object s = groupT.GetMethod("GetGameViewSize")?.Invoke(group, new object[] { i });
            if (s == null) continue;
            int w = (int)(gvSizeT.GetField("m_Width",  bfInst)?.GetValue(s) ?? 0);
            int h = (int)(gvSizeT.GetField("m_Height", bfInst)?.GetValue(s) ?? 0);
            if (w == W && h == H) { SelectGameViewIndex(i); Debug.Log($"[GameViewSetup] {LABEL} 선택됨 idx={i}"); return; }
        }

        // Add new size
        object fixedRes = System.Enum.ToObject(sizeEnumT, 0);
        var ctor = gvSizeT.GetConstructor(new[] { sizeEnumT, typeof(int), typeof(int), typeof(string) });
        object newSize = ctor?.Invoke(new object[] { fixedRes, W, H, LABEL });
        if (newSize == null) { Debug.LogError("[GameViewSetup] GameViewSize ctor 실패"); return; }

        groupT.GetMethod("AddCustomSize")?.Invoke(group, new object[] { newSize });
        sizesT.GetMethod("SaveToHDD")?.Invoke(inst, null);

        int newTotal = (int)(groupT.GetMethod("GetTotalCount")?.Invoke(group, null) ?? 0);
        SelectGameViewIndex(newTotal - 1);
        Debug.Log($"[GameViewSetup] Galaxy S25 ({W}x{H}) 추가+선택 완료");
    }

    static void SelectGameViewIndex(int index)
    {
        var gvT = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        if (gvT == null) return;
        var gv = EditorWindow.GetWindow(gvT, false, "Game", false);
        if (gv == null) return;
        gvT.GetProperty("selectedSizeIndex", BindingFlags.NonPublic | BindingFlags.Instance)
           ?.SetValue(gv, index);
        gv.Repaint();
    }
}
#endif
