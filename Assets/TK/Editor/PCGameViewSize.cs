using System;
using System.Reflection;
using UnityEditor;

namespace TK.EditorTools
{
    /// <summary>PC 전용 게임: 에디터 Game 뷰를 1920x1080 고정 해상도로 맞춤 (없으면 추가).</summary>
    public static class PCGameViewSize
    {
        [MenuItem("Tools/TK/Game View 1920x1080")]
        public static void SetMenu() => UnityEngine.Debug.Log(Set1080p());

        public static string Set1080p()
        {
            var asm = typeof(Editor).Assembly;
            var sizesType = asm.GetType("UnityEditor.GameViewSizes");
            var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var instance = singleton.GetProperty("instance").GetValue(null);
            var group = sizesType.GetMethod("GetGroup").Invoke(instance, new object[] { GameViewSizeGroupType.Standalone });
            var groupType = group.GetType();
            int total = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null);
            int found = -1;
            for (int i = 0; i < total; i++)
            {
                var size = groupType.GetMethod("GetGameViewSize").Invoke(group, new object[] { i });
                var st = size.GetType();
                int w = (int)st.GetProperty("width").GetValue(size), h = (int)st.GetProperty("height").GetValue(size);
                if (w == 1920 && h == 1080 && st.GetProperty("sizeType").GetValue(size).ToString() == "FixedResolution") { found = i; break; }
            }
            if (found < 0)
            {
                var gvsType = asm.GetType("UnityEditor.GameViewSize");
                var gvstType = asm.GetType("UnityEditor.GameViewSizeType");
                var ctor = gvsType.GetConstructor(new[] { gvstType, typeof(int), typeof(int), typeof(string) });
                var newSize = ctor.Invoke(new object[] { Enum.Parse(gvstType, "FixedResolution"), 1920, 1080, "PC 1920x1080" });
                groupType.GetMethod("AddCustomSize").Invoke(group, new[] { newSize });
                found = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null) - 1;
            }
            var gvType = asm.GetType("UnityEditor.GameView");
            var gv = EditorWindow.GetWindow(gvType);
            gvType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(gv, found);
            return "Game view → 1920x1080 (index " + found + ")";
        }
    }
}
