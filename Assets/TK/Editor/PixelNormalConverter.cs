using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TK.EditorTools
{
    /// <summary>
    /// 픽셀아트 전용 노멀맵 변환기.
    ///  높이 = 밝기(어두운 선·균열은 들어가고 밝은 면은 나옴) + 실루엣 베벨(투명 경계에서 N 픽셀 안쪽까지 경사)
    ///  → 소벨 대신 픽셀 단위 중앙차분 → 법선을 단계 양자화해 픽셀아트 특유의 계단형 음영 유지.
    /// 결과는 Assets/TK/Generated/Normals 에 *_n.png 로 저장하고, 원본 스프라이트의 보조 텍스처(_NormalMap)로 연결.
    /// 메뉴: Tools/TK/Pixel Normal Converter/...
    /// </summary>
    public static class PixelNormalConverter
    {
        public const string OutRoot = "Assets/TK/Generated/Normals";

        [System.Serializable]
        public struct Settings
        {
            public float luminanceWeight;   // 밝기 → 높이 비중
            public float bevelWeight;       // 실루엣 베벨 비중
            public int bevelPixels;         // 베벨 폭(픽셀)
            public float strength;          // 경사 강도
            public int quantizeSteps;       // 법선 XY 양자화 단계 (0 = 끔)

            public static Settings Default => new Settings
            {
                luminanceWeight = 0.55f, bevelWeight = 0.45f, bevelPixels = 3, strength = 2.2f, quantizeSteps = 4
            };
            public static Settings Tile => new Settings
            {
                luminanceWeight = 1f, bevelWeight = 0f, bevelPixels = 0, strength = 0.8f, quantizeSteps = 0
            };
        }

        static readonly string[] VfxKeywords =
        {
            "/FX/", "Pixel fire", "Effect and Bullet", "Fire Effect 2", "/Free/", "/Generated/", "Icon", "Lightning",
            "MainLev", "/Undead Survivor/Sprites/UI.png"
        };

        public static bool IsVfxOrExcluded(string path) => VfxKeywords.Any(k => path.Contains(k));

        /// <summary>VFX 를 제외한 모든 스프라이트 텍스처 경로</summary>
        public static List<string> FindTargets()
        {
            return AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/TK", "Assets/Undead Survivor/Sprites" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".png") && !IsVfxOrExcluded(p))
                .Where(p => (AssetImporter.GetAtPath(p) as TextureImporter)?.textureType == TextureImporterType.Sprite)
                .ToList();
        }

        [MenuItem("Tools/TK/Pixel Normal Converter/Generate For All Sprites (non-VFX)")]
        public static void GenerateAllMenu() => Debug.Log(GenerateAll());

        [MenuItem("Tools/TK/Pixel Normal Converter/Generate For Selected Textures")]
        public static void GenerateSelectedMenu()
        {
            foreach (var obj in Selection.objects)
            {
                var path = AssetDatabase.GetAssetPath(obj);
                if (path.EndsWith(".png")) GenerateAndAssign(path, SettingsFor(path));
            }
        }

        public static string GenerateAll()
        {
            var targets = FindTargets();
            var normals = new Dictionary<string, string>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in targets)
                    normals[path] = WriteNormal(path, SettingsFor(path));
            }
            finally { AssetDatabase.StopAssetEditing(); }

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var kv in normals) ConfigureNormalImporter(kv.Value);
            }
            finally { AssetDatabase.StopAssetEditing(); }

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var kv in normals)
                    Assign(kv.Key, AssetDatabase.LoadAssetAtPath<Texture2D>(kv.Value));
            }
            finally { AssetDatabase.StopAssetEditing(); }
            return $"Pixel normals generated: {normals.Count}";
        }

        public static Settings SettingsFor(string path) =>
            path.Contains("tile_01") || path.Contains("tileset") || path.Contains("/Tiles") ? Settings.Tile : Settings.Default;

        public static Texture2D GenerateAndAssign(string path, Settings s)
        {
            var outPath = WriteNormal(path, s);
            ConfigureNormalImporter(outPath);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
            Assign(path, tex);
            return tex;
        }

        public static string NormalPathFor(string path)
        {
            string rel = path.StartsWith("Assets/") ? path.Substring("Assets/".Length) : path;
            return $"{OutRoot}/{Path.ChangeExtension(rel, null)}_n.png".Replace('\\', '/');
        }

        /// <summary>픽셀 → 노멀 계산 후 PNG 저장 (임포트는 호출자가)</summary>
        public static string WriteNormal(string path, Settings s)
        {
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            src.LoadImage(File.ReadAllBytes(path));
            int w = src.width, h = src.height;
            var px = src.GetPixels32();
            Object.DestroyImmediate(src);

            var solid = new bool[w * h];
            for (int i = 0; i < px.Length; i++) solid[i] = px[i].a > 16;

            // 투명 픽셀로부터의 체스판 거리 (베벨)
            var dist = new int[w * h];
            var queue = new Queue<int>();
            for (int i = 0; i < dist.Length; i++)
            {
                if (!solid[i]) { dist[i] = 0; queue.Enqueue(i); }
                else dist[i] = int.MaxValue;
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int n = ny * w + nx;
                        if (dist[n] > dist[i] + 1) { dist[n] = dist[i] + 1; queue.Enqueue(n); }
                    }
            }

            var height = new float[w * h];
            for (int i = 0; i < height.Length; i++)
            {
                if (!solid[i]) { height[i] = 0f; continue; }
                var c = px[i];
                float lum = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
                float bevel = s.bevelPixels <= 0 || dist[i] == int.MaxValue ? 1f : Mathf.Min(dist[i], s.bevelPixels) / (float)s.bevelPixels;
                height[i] = s.luminanceWeight * lum + s.bevelWeight * bevel;
            }

            float H(int x, int y, int self)
            {
                x = Mathf.Clamp(x, 0, w - 1);
                y = Mathf.Clamp(y, 0, h - 1);
                int i = y * w + x;
                // 투명 이웃은 실루엣 경사로 (베벨 비중이 0이면 자기 높이 유지 → 타일 경계 무시)
                return solid[i] ? height[i] : (s.bevelWeight > 0f ? 0f : height[self]);
            }

            var outPx = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (!solid[i]) { outPx[i] = new Color32(128, 128, 255, 0); continue; }
                    float gx = (H(x + 1, y, i) - H(x - 1, y, i)) * 0.5f;
                    float gy = (H(x, y + 1, i) - H(x, y - 1, i)) * 0.5f;
                    var n = new Vector3(-gx * s.strength, -gy * s.strength, 1f).normalized;
                    if (s.quantizeSteps > 0)
                    {
                        float q = s.quantizeSteps;
                        n.x = Mathf.Round(n.x * q) / q;
                        n.y = Mathf.Round(n.y * q) / q;
                        n.z = Mathf.Sqrt(Mathf.Max(0.05f, 1f - n.x * n.x - n.y * n.y));
                        n.Normalize();
                    }
                    outPx[i] = new Color32((byte)Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f),
                                           (byte)Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f),
                                           (byte)Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f), px[i].a);
                }

            var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            outTex.SetPixels32(outPx);
            string outPath = NormalPathFor(path);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllBytes(outPath, outTex.EncodeToPNG());
            Object.DestroyImmediate(outTex);
            AssetDatabase.ImportAsset(outPath);
            return outPath;
        }

        public static void ConfigureNormalImporter(string normalPath)
        {
            var ti = AssetImporter.GetAtPath(normalPath) as TextureImporter;
            if (ti == null) return;
            ti.textureType = TextureImporterType.NormalMap;
            ti.filterMode = FilterMode.Point;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }

        /// <summary>스프라이트 텍스처의 보조 텍스처 _NormalMap 으로 연결</summary>
        public static void Assign(string spritePath, Texture2D normal)
        {
            var ti = AssetImporter.GetAtPath(spritePath) as TextureImporter;
            if (ti == null || normal == null) return;
            var list = (ti.secondarySpriteTextures ?? new SecondarySpriteTexture[0]).Where(t => t.name != "_NormalMap").ToList();
            list.Add(new SecondarySpriteTexture { name = "_NormalMap", texture = normal });
            ti.secondarySpriteTextures = list.ToArray();
            ti.SaveAndReimport();
        }
    }
}
