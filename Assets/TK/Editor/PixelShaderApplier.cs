using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TK.EditorTools
{
    /// <summary>
    /// TK/PixelSprite 셰이더용 머티리얼을 만들고 씬·프리팹의 SpriteRenderer / TilemapRenderer 에 일괄 적용.
    /// 메뉴: Tools/TK/Apply Pixel Shader
    /// </summary>
    public static class PixelShaderApplier
    {
        public const string ShaderName = "TK/PixelSprite";
        public const string CharacterMatPath = "Assets/TK/Materials/PixelCharacter.mat";
        public const string BackgroundMatPath = "Assets/TK/Materials/PixelBackground.mat";

        static readonly string[] ScenePaths =
        {
            "Assets/Scenes/Demo.unity",
            "Assets/Scenes/TKScene.unity",
        };

        static readonly string[] PrefabFolders =
        {
            "Assets/TK",
            "Assets/Undead Survivor/Prefabs",
        };

        [MenuItem("Tools/TK/Apply Pixel Shader")]
        public static void ApplyFromMenu()
        {
            Debug.Log(ApplyAll());
        }

        public static string ApplyAll()
        {
            var log = new System.Text.StringBuilder();
            var shader = Shader.Find(ShaderName);
            if (shader == null)
                return "Shader not found: " + ShaderName;

            var character = GetOrCreateMaterial(CharacterMatPath, shader, m =>
            {
                m.SetFloat("_PixelSize", 1);
                m.SetFloat("_ColorLevels", 0);
                m.SetFloat("_Saturation", 1.1f);
                m.SetFloat("_Contrast", 1.05f);
                m.SetFloat("_Brightness", 0);
                SetKeyword(m, "_PIXEL_OUTLINE", "_Outline", true);
                m.SetColor("_OutlineColor", new Color(0.08f, 0.05f, 0.05f, 1f));
                m.SetFloat("_OutlineThickness", 1);
                SetKeyword(m, "_PIXEL_SNAP", "_PixelSnap", true);
                m.SetFloat("_PixelsPerUnit", 64);
            });

            var background = GetOrCreateMaterial(BackgroundMatPath, shader, m =>
            {
                m.SetFloat("_PixelSize", 1);
                m.SetFloat("_ColorLevels", 0);
                m.SetFloat("_Saturation", 1.0f);
                m.SetFloat("_Contrast", 1.0f);
                m.SetFloat("_Brightness", 0);
                SetKeyword(m, "_PIXEL_OUTLINE", "_Outline", false);
                SetKeyword(m, "_PIXEL_SNAP", "_PixelSnap", true);
                m.SetFloat("_PixelsPerUnit", 64);
            });

            // 1) 프리팹 먼저 (씬의 프리팹 인스턴스가 변경을 그대로 상속하도록)
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", PrefabFolders);
            foreach (var guid in prefabGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                int n = ApplyToHierarchy(root, character, background, false);
                if (n > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    log.AppendLine($"[Prefab] {path}: {n}");
                }
                PrefabUtility.UnloadPrefabContents(root);
            }

            // 2) 씬 (이미 열린 씬은 그대로 두고, 닫혀 있으면 Additive 로 열어 저장 후 닫음)
            foreach (var path in ScenePaths)
            {
                if (!File.Exists(path)) continue;
                var scene = EditorSceneManager.GetSceneByPath(path);
                bool wasOpen = scene.IsValid() && scene.isLoaded;
                if (!wasOpen)
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

                int n = 0;
                foreach (var go in scene.GetRootGameObjects())
                    n += ApplyToHierarchy(go, character, background, true);

                if (n > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                log.AppendLine($"[Scene] {path}: {n}");

                if (!wasOpen)
                    EditorSceneManager.CloseScene(scene, true);
            }

            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        static int ApplyToHierarchy(GameObject root, Material character, Material background, bool recordUndo)
        {
            int count = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                Material target;
                if (r is TilemapRenderer) target = background;
                else if (r is SpriteRenderer) target = character;
                else continue;

                if (r.sharedMaterial == target) continue;
                // 이미 TK 셰이더(불 글로우, 장식 등)를 쓰는 렌더러는 건드리지 않음
                if (r.sharedMaterial != null && r.sharedMaterial.shader != null
                    && r.sharedMaterial.shader.name.StartsWith("TK/")) continue;
                if (recordUndo) Undo.RecordObject(r, "Apply Pixel Shader");
                r.sharedMaterial = target;
                if (PrefabUtility.IsPartOfPrefabInstance(r))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                EditorUtility.SetDirty(r);
                count++;
            }
            return count;
        }

        static Material GetOrCreateMaterial(string path, Shader shader, System.Action<Material> setup)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                setup(mat);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
                setup(mat);
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        static void SetKeyword(Material m, string keyword, string toggleProp, bool on)
        {
            m.SetFloat(toggleProp, on ? 1 : 0);
            if (on) m.EnableKeyword(keyword); else m.DisableKeyword(keyword);
        }
    }
}
