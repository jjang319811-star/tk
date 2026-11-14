using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace TK.EditorTools
{
    /// <summary>
    /// 전체 구성 빌더 (여러 번 실행해도 같은 결과)
    ///  1) 픽셀 규격 통일: 모든 픽셀 에셋 PPU 64 / Point / 비압축  (측정 결과 전부 1:1 픽셀아트)
    ///  2) 도형 텍스처(마법진 링, 장판 원/사각) 생성, 동굴 바닥 3종 추출(지형 블렌드용, Mirror 랩)
    ///  3) 픽셀 노멀맵 생성 + 스프라이트 보조 텍스처 연결 (VFX 제외)
    ///  4) 32px 매크로 타일(tile_01) + 절차적 맵 팔레트
    ///  5) 이펙트/보스 프리팹 (Fire Effect 2, Free, Pixel Fire 팩 사용)
    ///  6) Demo 씬: 절차적 맵 교체, 공명 플레이어, 보스 디렉터, 조명/카메라
    /// 메뉴: Tools/TK/Build All
    /// </summary>
    public static partial class TKGameBuilder
    {
        const float PPU = 64f;
        const string Gen = "Assets/TK/Generated";
        const string PrefabDir = "Assets/TK/Prefabs";
        const string MatDir = "Assets/TK/Materials";
        const string ScenePath = "Assets/Scenes/Demo.unity";
        // PC 전용, 1920x1080 기준: 정확히 1/4 해상도 → 아트 1px = 화면 4x4px (1280x720 은 2배, 2560x1440 은 5배, 4K 는 8배)
        static readonly Vector2Int RefRes = new Vector2Int(480, 270);

        class Shapes { public Sprite ring, circle, square, white; }
        class Overlay { public Texture2D dirt, moss, flower; }
        class Mats { public Material character, boss, background, decor, fire, fireBlue, fireFx, blend, telegraph, trigram; }
        class Fx { public Sprite[] explosion, trigramCast, deathPop; }
        class Prefabs { public GameObject fireLight, explosion, fireOrb, trigramCast, trigramRing, teleCircle, teleLine, lubu; }

        [MenuItem("Tools/TK/Build All (PPU, Normals, Procedural Map, Characters, Boss)")]
        public static void Menu() => Debug.Log(BuildAll());

        public static string BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "Play 모드에서는 빌드할 수 없습니다. Play 를 종료한 뒤 다시 실행하세요.";
            var log = new StringBuilder();
            Directory.CreateDirectory(Gen);
            Directory.CreateDirectory(PrefabDir);

            UnifyPpu(log);
            var shapes = GenerateShapes();
            var overlay = ExtractOverlays();
            log.AppendLine(PixelNormalConverter.GenerateAll());
            var mats = CreateMaterials(overlay);
            var fx = BuildFxFrames();
            var prefabs = new Prefabs();
            prefabs.fireLight = BuildFireLightPrefab();
            var palette = BuildPalette(mats, prefabs, log);
            BuildPrefabs(mats, shapes, fx, prefabs);
            SetupScene(mats, shapes, palette, prefabs, log);
            SetupJuiceAndUI(mats, shapes, fx, log);
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        // ================================================================== 1. PPU

        static void UnifyPpu(StringBuilder log)
        {
            var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/TK", "Assets/Undead Survivor/Sprites" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".png") && !p.StartsWith(Gen) && !p.EndsWith("/UI.png") && !p.Contains("Icon"))
                // Pixel Fire 팩은 실제로 쓰는 그룹만 (수천 장 재임포트 방지)
                .Where(p => !p.Contains("Pixel fire") || p.Contains("fire asset red/Group 4 - 2/") || p.Contains("fire asset red/Group 4 - 3/") || p.Contains("fire asset blue/Group 4 - 5/"))
                .ToList();
            int changed = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var p in paths)
                {
                    var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                    if (ti == null || ti.textureType != TextureImporterType.Sprite) continue;
                    bool dirty = false;
                    if (!Mathf.Approximately(ti.spritePixelsPerUnit, PPU)) { ti.spritePixelsPerUnit = PPU; dirty = true; }
                    if (ti.filterMode != FilterMode.Point) { ti.filterMode = FilterMode.Point; dirty = true; }
                    if (ti.mipmapEnabled) { ti.mipmapEnabled = false; dirty = true; }
                    if (ti.textureCompression != TextureImporterCompression.Uncompressed) { ti.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
                    if (dirty) { ti.SaveAndReimport(); changed++; }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            log.AppendLine($"PPU unify: {changed}/{paths.Count} textures updated to PPU {PPU}");
        }

        // ================================================================== 2. textures

        static Shapes GenerateShapes()
        {
            var s = new Shapes();
            s.ring = WriteShape("Shape_TrigramRing", 64, (x, y) =>
            {
                float dx = x - 31.5f, dy = y - 31.5f, d = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                if (d >= 27f && d <= 29.6f) return 1f;
                if (d >= 22.5f && d <= 23.6f) return 0.55f;
                // 팔괘 막대 8개 (홀수는 가운데가 끊긴 음효)
                for (int k = 0; k < 8; k++)
                {
                    float diff = Mathf.Abs(Mathf.DeltaAngle(ang, k * 45f));
                    if (diff < 10f && d >= 14.5f && d <= 20.5f)
                        return (k % 2 == 1 && diff < 3f) ? 0f : 1f;
                }
                return 0f;
            }, new Vector2(0.5f, 0.5f));
            s.circle = WriteShape("Shape_Circle", 32, (x, y) =>
            {
                float dx = x - 15.5f, dy = y - 15.5f, d = Mathf.Sqrt(dx * dx + dy * dy);
                return d > 15.6f ? 0f : d > 14.2f ? 1f : 0.45f;
            }, new Vector2(0.5f, 0.5f));
            s.square = WriteShape("Shape_Square", 8, (x, y) => (x == 0 || y == 0 || x == 7 || y == 7) ? 1f : 0.45f, new Vector2(0f, 0.5f));
            s.white = WriteShape("Shape_White", 4, (x, y) => 1f, new Vector2(0.5f, 0.5f));
            return s;
        }

        static Sprite WriteShape(string name, int size, System.Func<int, int, float> alpha, Vector2 pivot)
        {
            string path = $"{Gen}/{name}.png";
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha(x, y)));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = PPU;
            ti.filterMode = FilterMode.Point;
            ti.mipmapEnabled = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            var st = new TextureImporterSettings();
            ti.ReadTextureSettings(st);
            st.spriteMeshType = SpriteMeshType.FullRect;
            st.spriteAlignment = (int)SpriteAlignment.Custom;
            st.spritePivot = pivot;
            ti.SetTextureSettings(st);
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static Overlay ExtractOverlays()
        {
            var src = new Texture2D(2, 2);
            src.LoadImage(File.ReadAllBytes("Assets/TK/RPGW_Caves_v2.1/MainLev2.0.png"));
            Texture2D Crop(string name, int x, int yTop, int w, int h)
            {
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
                t.SetPixels(src.GetPixels(x, src.height - yTop - h, w, h));
                string path = $"{Gen}/Terrain_{name}.png";
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.textureType = TextureImporterType.Default;
                ti.wrapMode = TextureWrapMode.Mirror; // 미러 반복 → 이음매 없음
                ti.filterMode = FilterMode.Point;
                ti.mipmapEnabled = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            var o = new Overlay
            {
                dirt = Crop("Dirt", 1144, 1080, 112, 112),
                moss = Crop("Moss", 24, 1080, 112, 112),
                flower = Crop("Flower", 140, 1300, 128, 128),
            };
            Object.DestroyImmediate(src);
            return o;
        }

        // ================================================================== 3. materials

        static Mats CreateMaterials(Overlay overlay)
        {
            var glow = Shader.Find("TK/PixelGlow");
            var m = new Mats
            {
                character = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/PixelCharacter.mat"),
                background = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/PixelBackground.mat"),
                decor = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/PixelDecor.mat"),
                fire = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/FireGlow.mat"),
                fireBlue = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/FireGlowBlue.mat"),
                fireFx = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/FireFXGlow.mat"),
            };
            m.character.SetFloat("_Unlit", 0.4f); // 노멀 음영이 보이도록 자체발광 약간 낮춤

            // 시트 애니메이션 FX(화염 분사·낙뢰·화염구·폭발): 원본의 반투명 빛 가장자리를 그대로 살리고,
            // 프레임 경계에서 잘리는 후광(halo) 대신 HDR 밝기 → Bloom 으로만 빛나게
            Set(m.fireFx, ("_Intensity", 1.35f), ("_HaloRadius", 0f), ("_HaloStrength", 0f), ("_FlickerSpeed", 12f), ("_FlickerAmount", 0.08f));
            EditorUtility.SetDirty(m.fireFx);
            EditorUtility.SetDirty(m.character);

            m.boss = Mat("PixelBoss", m.character.shader);
            m.boss.CopyPropertiesFromMaterial(m.character);
            m.boss.shaderKeywords = m.character.shaderKeywords;
            m.boss.SetColor("_OutlineColor", new Color(0.32f, 0.02f, 0.03f, 1f));
            m.boss.SetFloat("_Unlit", 0.35f);

            m.blend = Mat("TerrainBlend", Shader.Find("TK/PixelBlend"));
            m.blend.SetTexture("_LayerA", overlay.dirt);
            m.blend.SetTexture("_LayerB", overlay.moss);
            m.blend.SetTexture("_LayerC", overlay.flower);
            // 경계 디더 폭을 좁혀(0.035) 하프톤 얼룩 대신 픽셀 단위로 또렷한 가장자리, 커버리지는 '조금씩' 보이는 수준
            Set(m.blend, ("_PPU", PPU), ("_Seed", 1234f), ("_ScaleA", 0.17f), ("_CoverA", 0.3f), ("_ScaleB", 0.21f), ("_CoverB", 0.22f),
                ("_ScaleC", 0.28f), ("_CoverC", 0.1f), ("_EdgeWidth", 0.035f), ("_EdgePixels", 2f), ("_RimPixels", 2f), ("_RimDarken", 0.72f),
                ("_Harmonize", 0.2f), ("_Brightness", 1.55f));
            m.blend.SetColor("_HarmonizeColor", new Color(0.93f, 0.72f, 0.38f, 1f));

            m.telegraph = Mat("Telegraph", glow);
            Set(m.telegraph, ("_Intensity", 1.3f), ("_HaloRadius", 0f), ("_FlickerAmount", 0.05f));
            m.trigram = Mat("TrigramGlow", glow);
            Set(m.trigram, ("_Intensity", 0.85f), ("_HaloRadius", 1f), ("_HaloStrength", 0.6f), ("_FlickerAmount", 0.08f), ("_FlickerSpeed", 5f));
            m.trigram.SetColor("_GlowColor", new Color(1.3f, 0.95f, 0.35f, 1f));
            return m;
        }

        static Material Mat(string name, Shader shader)
        {
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            else mat.shader = shader;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void Set(Material m, params (string n, float v)[] values)
        {
            foreach (var (n, v) in values) if (m.HasProperty(n)) m.SetFloat(n, v);
        }

        // ================================================================== FX frames (new packs)

        static Fx BuildFxFrames()
        {
            string path = $"{Gen}/FXFrames.asset";
            AssetDatabase.DeleteAsset(path);
            var cache = ScriptableObject.CreateInstance<SpriteSheetCache>();
            AssetDatabase.CreateAsset(cache, path);
            var all = new List<Sprite>();

            Sprite[] Slice(string texPath, int frameW, int frameH, int rowFromTop, int first, int count, string prefix)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                var list = new List<Sprite>();
                for (int i = first; i < first + count; i++)
                {
                    var rect = new Rect(i * frameW, tex.height - (rowFromTop + 1) * frameH, frameW, frameH);
                    var sp = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), PPU, 0, SpriteMeshType.FullRect);
                    sp.name = $"{prefix}_{i}";
                    AssetDatabase.AddObjectToAsset(sp, cache);
                    list.Add(sp);
                }
                all.AddRange(list);
                return list.ToArray();
            }

            var fx = new Fx
            {
                // Fire Effect 2: 48px 폭발 (앞쪽 섬광 프레임 제외)
                explosion = Slice("Assets/TK/Fire Effect 2/Explosion 2 SpriteSheet.png", 48, 48, 0, 6, 12, "Explosion"),
                // Free Part 1 / 03: 64px 마법진 소용돌이 (첫 줄 = 주황/금색)
                trigramCast = Slice("Assets/TK/Free/Part 1/03.png", 64, 64, 0, 0, 13, "TrigramCast"),
                // Effect and Bullet 16x16: 원형으로 퍼지는 4프레임 팝 (적 사망)
                deathPop = Slice("Assets/TK/Effect and Bullet 16x16/Fire Effect and Bullet 16x16.png", 16, 16, 5, 14, 4, "DeathPop"),
            };
            cache.sprites = all.ToArray();
            EditorUtility.SetDirty(cache);
            AssetDatabase.SaveAssets();
            return fx;
        }

        // ================================================================== 4. palette (tiles + decor)

        static ProceduralMapPalette BuildPalette(Mats mats, Prefabs prefabs, StringBuilder log)
        {
            string path = $"{Gen}/ProceduralMapPalette.asset";
            var pal = AssetDatabase.LoadAssetAtPath<ProceduralMapPalette>(path);
            if (pal == null) { pal = ScriptableObject.CreateInstance<ProceduralMapPalette>(); AssetDatabase.CreateAsset(pal, path); }

            BuildMacroTiles(pal, log);

            var decor = AssetDatabase.LoadAllAssetsAtPath("Assets/TK/RPGW_Caves_v2.1/decorative.png").OfType<Sprite>().ToList();
            var decorTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/TK/RPGW_Caves_v2.1/decorative.png");
            float Top(Sprite s) => decorTex.height - s.rect.yMax;
            Sprite N(string n) => decor.First(s => s.name == n);
            pal.rocksSmall = new[] { "decorative_5", "decorative_6", "decorative_4", "decorative_7", "decorative_3", "decorative_8" }.Select(N).ToArray();
            pal.rocksBig = new[] { "decorative_2", "decorative_9" }.Select(N).ToArray();
            pal.crystalsBlue = decor.Where(s => Top(s) >= 560 && Top(s) < 710 && s.rect.x < 150).ToArray();
            pal.crystalsGreen = decor.Where(s => Top(s) >= 560 && Top(s) < 710 && s.rect.x >= 150).ToArray();
            pal.mushrooms = decor.Where(s => Top(s) >= 730).ToArray();

            var plants = new List<Sprite>();
            foreach (var led in new[] { "LedA", "LedB", "LedC", "LedD" })
            {
                var sp = AssetDatabase.LoadAllAssetsAtPath($"Assets/TK/RPGW_Caves_v2.1/Anim/{led}.png").OfType<Sprite>().ToDictionary(s => s.name);
                for (int start = 0; start < 18; start += 6)
                {
                    var frames = Enumerable.Range(start, 6).Select(i => sp.TryGetValue($"{led}_{i}", out var s) ? s : null).ToArray();
                    if (frames.All(f => f)) plants.AddRange(frames);
                }
            }
            pal.plantFrames = plants.ToArray();
            pal.plantFrameCount = 6;

            pal.bonfireFrames = MapDecorBuilder.LoadFireFrames("fire asset red", "Group 4 - 2");
            pal.smallFireFrames = MapDecorBuilder.LoadFireFrames("fire asset red", "Group 4 - 3");
            pal.blueFireFrames = MapDecorBuilder.LoadFireFrames("fire asset blue", "Group 4 - 5");
            pal.decorMaterial = mats.decor;
            pal.fireMaterial = mats.fire;
            pal.fireBlueMaterial = mats.fireBlue;
            pal.fireLightPrefab = prefabs.fireLight;
            EditorUtility.SetDirty(pal);
            log.AppendLine($"Palette: tiles {pal.plainTiles.Length}/{pal.lightTiles.Length}/{pal.heavyTiles.Length}, plants {plants.Count / 6}");
            return pal;
        }

        // tile_01 을 16px 간격으로 훑어 32x32 창을 잘라 매크로 타일로 (흙/테두리 영역 제외, 균열 양으로 분류)
        static void BuildMacroTiles(ProceduralMapPalette pal, StringBuilder log)
        {
            const string texPath = "Assets/TK/Tiles/tile_01.png";
            string folder = $"{Gen}/GroundTiles";
            if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(PixelNormalConverter.NormalPathFor(texPath));
            var px = new Texture2D(2, 2);
            px.LoadImage(File.ReadAllBytes(texPath));

            var ranked = new List<(Tile tile, float detail, Color[] block)>();
            for (int yTop = 0; yTop + 32 <= px.height; yTop += 16)
                for (int x = 0; x + 32 <= 160; x += 16)
                {
                    int y = px.height - yTop - 32;
                    var block = px.GetPixels(x, y, 32, 32);
                    float dark = 0, veryDark = 0, sum = 0, sumSq = 0;
                    foreach (var c in block)
                    {
                        float l = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                        if (l < 0.42f) dark++;
                        if (l < 0.3f) veryDark++;
                        sum += l;
                        sumSq += l * l;
                    }
                    dark /= block.Length;
                    veryDark /= block.Length;
                    float mean = sum / block.Length;
                    float detail = Mathf.Sqrt(Mathf.Max(0f, sumSq / block.Length - mean * mean)) + dark;
                    if (veryDark > 0.12f || dark > 0.3f) continue; // 흙 지대/큰 바위 → 지형 블렌드가 담당

                    var tile = ScriptableObject.CreateInstance<Tile>();
                    tile.name = $"G_{x}_{yTop}";
                    tile.colliderType = Tile.ColliderType.None;
                    AssetDatabase.CreateAsset(tile, $"{folder}/{tile.name}.asset");
                    var secondary = normal ? new[] { new SecondarySpriteTexture { name = "_NormalMap", texture = normal } } : new SecondarySpriteTexture[0];
                    var sprite = Sprite.Create(tex, new Rect(x, y, 32, 32), new Vector2(0.5f, 0.5f), PPU, 0, SpriteMeshType.FullRect, Vector4.zero, false, secondary);
                    sprite.name = tile.name;
                    AssetDatabase.AddObjectToAsset(sprite, tile);
                    tile.sprite = sprite;
                    EditorUtility.SetDirty(tile);

                    ranked.Add((tile, detail, block));
                }
            Object.DestroyImmediate(px);

            // 디테일(밝기 표준편차 + 어두운 픽셀 비율) 순위: 차분한 35% = 잔균열, 나머지 = 균열 지대
            ranked.Sort((a, b) => a.detail.CompareTo(b.detail));
            int n = ranked.Count, lightN = Mathf.RoundToInt(n * 0.35f);
            pal.lightTiles = ranked.Take(lightN).Select(r => (TileBase)r.tile).ToArray();
            pal.heavyTiles = ranked.Skip(lightN).Select(r => (TileBase)r.tile).ToArray();

            // 기본 바닥 = '깨끗한 모래': 가장 차분한 타일들에서 균열 픽셀만 같은 타일의 모래 픽셀로 치환
            pal.plainTiles = BuildCleanSand(ranked.Take(6).Select(r => r.block).ToList(), folder);
        }

        static TileBase[] BuildCleanSand(List<Color[]> blocks, string folder)
        {
            string atlasPath = $"{Gen}/Ground_CleanSand.png";
            var atlas = new Texture2D(32 * blocks.Count, 32, TextureFormat.RGBA32, false);
            for (int b = 0; b < blocks.Count; b++)
            {
                var block = (Color[])blocks[b].Clone();
                float mean = block.Average(c => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b);
                var sand = block.Where(c => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b >= mean - 0.02f).ToArray();
                for (int i = 0; i < block.Length; i++)
                {
                    float l = 0.299f * block[i].r + 0.587f * block[i].g + 0.114f * block[i].b;
                    if (l < mean - 0.06f && sand.Length > 0)
                        block[i] = sand[(int)(ProceduralNoise.Hash(i, b, 99u) * (sand.Length - 1))];
                }
                atlas.SetPixels(b * 32, 0, 32, 32, block);
            }
            File.WriteAllBytes(atlasPath, atlas.EncodeToPNG());
            Object.DestroyImmediate(atlas);
            AssetDatabase.ImportAsset(atlasPath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(atlasPath);
            ti.textureType = TextureImporterType.Sprite;
            ti.spritePixelsPerUnit = PPU;
            ti.filterMode = FilterMode.Point;
            ti.mipmapEnabled = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();

            string nPath = PixelNormalConverter.WriteNormal(atlasPath, PixelNormalConverter.Settings.Tile);
            PixelNormalConverter.ConfigureNormalImporter(nPath);
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(nPath);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);

            var tiles = new List<TileBase>();
            for (int b = 0; b < blocks.Count; b++)
            {
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.name = $"Clean_{b}";
                tile.colliderType = Tile.ColliderType.None;
                AssetDatabase.CreateAsset(tile, $"{folder}/{tile.name}.asset");
                var sprite = Sprite.Create(tex, new Rect(b * 32, 0, 32, 32), new Vector2(0.5f, 0.5f), PPU, 0, SpriteMeshType.FullRect, Vector4.zero, false,
                    normal ? new[] { new SecondarySpriteTexture { name = "_NormalMap", texture = normal } } : new SecondarySpriteTexture[0]);
                sprite.name = tile.name;
                AssetDatabase.AddObjectToAsset(sprite, tile);
                tile.sprite = sprite;
                EditorUtility.SetDirty(tile);
                tiles.Add(tile);
            }
            return tiles.ToArray();
        }

        // ================================================================== 5. prefabs

        static GameObject BuildFireLightPrefab()
        {
            var go = new GameObject("FireLight");
            var l = go.AddComponent<Light2D>();
            l.lightType = Light2D.LightType.Point;
            l.color = new Color(1f, 0.6f, 0.3f);
            l.intensity = 1f;
            l.pointLightInnerRadius = 0.12f;
            l.pointLightOuterRadius = 1.5f;
            l.falloffIntensity = 0.65f;
            MapDecorBuilder.ApplyToAllSortingLayers(l);
            MapDecorBuilder.EnableNormals(l, 0.6f);
            go.AddComponent<LightFlicker>();
            return SavePrefab(go, "FireLight");
        }

        static void BuildPrefabs(Mats m, Shapes s, Fx fx, Prefabs p)
        {
            var smallFire = MapDecorBuilder.LoadFireFrames("fire asset red", "Group 4 - 3");

            // 폭발 (Fire Effect 2)
            var ex = SpriteGo("FireExplosion", fx.explosion[0], m.fireFx, 8);
            Flip(ex, fx.explosion, 22f, false);
            var fade = ex.AddComponent<TimedFade>(); fade.lifetime = 0.55f; fade.fadePortion = 0.3f;
            MapDecorBuilder.AddGlowLight(ex.transform, Vector3.zero, new Color(1f, 0.55f, 0.2f), 1.4f, 1.6f);
            p.explosion = SavePrefab(ex, "FireExplosion");

            // 화계 불꽃 구슬
            var orb = SpriteGo("FireOrb", smallFire[0], m.fire, 8);
            Flip(orb, smallFire, 14f, true);
            var fo = orb.AddComponent<FireOrb>(); fo.speed = 4.8f; fo.lifetime = 1.8f; fo.explosionPrefab = p.explosion;
            MapDecorBuilder.AddGlowLight(orb.transform, Vector3.zero, new Color(1f, 0.6f, 0.25f), 0.7f, 0.8f);
            p.fireOrb = SavePrefab(orb, "FireOrb");

            // 팔진도 시전 이펙트 (Free 팩)
            var cast = SpriteGo("TrigramCastFx", fx.trigramCast[0], m.trigram, -4); // 캐릭터 아래
            Flip(cast, fx.trigramCast, 20f, false);
            var cf = cast.AddComponent<TimedFade>(); cf.lifetime = 0.65f; cf.fadePortion = 0.2f;
            cast.transform.localScale = Vector3.one; // 픽셀 1:1:1
            p.trigramCast = SavePrefab(cast, "TrigramCastFx");

            // 팔진도 결계
            var ring = SpriteGo("TrigramRing", s.ring, m.trigram, -5);
            ring.GetComponent<SpriteRenderer>().color = new Color(1f, 0.85f, 0.4f, 0.6f);
            ring.AddComponent<TrigramRing>().spriteRadiusUnits = 29.5f / PPU;
            MapDecorBuilder.AddGlowLight(ring.transform, Vector3.zero, new Color(1f, 0.85f, 0.45f), 0.6f, 1.8f);
            p.trigramRing = SavePrefab(ring, "TrigramRing");

            // 보스 장판
            var tc = SpriteGo("TelegraphCircle", s.circle, m.telegraph, -4);
            tc.AddComponent<Telegraph>();
            p.teleCircle = SavePrefab(tc, "TelegraphCircle");
            var tl = SpriteGo("TelegraphLine", s.square, m.telegraph, -4);
            tl.AddComponent<Telegraph>();
            p.teleLine = SavePrefab(tl, "TelegraphLine");

            p.lubu = BuildLuBu(m, p);
        }

        static GameObject BuildLuBu(Mats m, Prefabs p)
        {
            var player = Object.FindAnyObjectByType<Player>();
            int enemyLayer = 6;
            if (player)
            {
                int mask = player.GetComponent<Scanner>().targetLayer.value;
                for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) { enemyLayer = i; break; }
            }

            var root = new GameObject("LuBu");
            root.layer = enemyLayer;
            root.tag = "Enemy";
            var rb = root.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.mass = 50f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var cap = root.AddComponent<CapsuleCollider2D>();
            cap.direction = CapsuleDirection2D.Horizontal;
            cap.size = new Vector2(1.2f, 0.7f);
            cap.offset = new Vector2(0f, -0.3f);

            // 그림자: 스케일로 늘리지 않고 필요한 크기(56x12px)로 직접 생성
            var shadowSprite = ShadowSprite("Shadow_Boss", 56, 12);
            {
                var sh = new GameObject("Shadow", typeof(SpriteRenderer));
                sh.layer = enemyLayer;
                sh.transform.SetParent(root.transform, false);
                sh.transform.localPosition = new Vector3(0f, -0.62f, 0f);
                var ssr = sh.GetComponent<SpriteRenderer>();
                ssr.sprite = shadowSprite;
                ssr.sharedMaterial = m.decor;
                ssr.sortingOrder = 1;
            }

            var visual = new GameObject("Visual", typeof(SpriteRenderer));
            visual.layer = enemyLayer;
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            var vsr = visual.GetComponent<SpriteRenderer>();
            vsr.sprite = AssetDatabase.LoadAllAssetsAtPath("Assets/TK/LuBu.png").OfType<Sprite>().First();
            vsr.sharedMaterial = m.boss;
            vsr.sortingOrder = 3;
            var anim = visual.AddComponent<ProceduralSpriteAnim>();
            anim.spriteFacesRight = false;
            // 말발굽 박자: 0.18초마다 2px 들썩 (부드러운 출렁임 대신 끊어지는 움직임)
            anim.walkStepPixels = 2;
            anim.walkStepInterval = 0.18f;
            anim.idleStepInterval = 0.6f;

            var aura = new GameObject("Aura");
            aura.transform.SetParent(root.transform, false);
            aura.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            var al = aura.AddComponent<Light2D>();
            al.lightType = Light2D.LightType.Point;
            al.color = new Color(1f, 0.25f, 0.15f);
            al.intensity = 0.9f;
            al.pointLightOuterRadius = 2.6f;
            al.falloffIntensity = 0.6f;
            MapDecorBuilder.ApplyToAllSortingLayers(al);
            MapDecorBuilder.EnableNormals(al, 0.8f);

            var boss = root.AddComponent<LuBuBoss>();
            boss.anim = anim;
            boss.circleTelegraph = p.teleCircle.GetComponent<Telegraph>();
            boss.lineTelegraph = p.teleLine.GetComponent<Telegraph>();
            boss.strikeFx = p.explosion;
            boss.auraLight = al;
            // 시야 확대(x1.5) 기준 수치
            boss.moveSpeed = 0.9f;
            boss.chargeSpeed = 6.3f;
            boss.chargeLength = 4.5f;
            boss.sweepRadius = 1.5f;
            boss.strikeRadius = 0.5f; // 원형 장판 2배
            return SavePrefab(root, "LuBu");
        }

        static GameObject SpriteGo(string name, Sprite sprite, Material mat, int order)
        {
            var go = new GameObject(name, typeof(SpriteRenderer));
            var sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = mat;
            sr.sortingOrder = order;
            return go;
        }

        static void Flip(GameObject go, Sprite[] frames, float fps, bool randomStart)
        {
            var fb = go.AddComponent<SpriteFlipbook>();
            fb.frames = frames;
            fb.fps = fps;
            fb.randomStart = randomStart;
        }

        static GameObject SavePrefab(GameObject go, string name)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ================================================================== 6. scene

        static void SetupScene(Mats m, Shapes s, ProceduralMapPalette pal, Prefabs p, StringBuilder log)
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool wasOpen = scene.IsValid() && scene.isLoaded;
            if (!wasOpen) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var roots = scene.GetRootGameObjects();
            T Find<T>() where T : Component => roots.SelectMany(g => g.GetComponentsInChildren<T>(true)).FirstOrDefault();

            // --- 기존 2x2 반복 바닥 제거 → 절차적 맵
            var grid = Find<Grid>();
            foreach (var tm in grid.GetComponentsInChildren<Tilemap>(true))
                Object.DestroyImmediate(tm.gameObject);
            grid.cellSize = new Vector3(0.5f, 0.5f, 0f);
            grid.transform.position = Vector3.zero;

            var groundGo = new GameObject("ProceduralGround", typeof(Tilemap), typeof(TilemapRenderer));
            groundGo.transform.SetParent(grid.transform, false);
            var tr = groundGo.GetComponent<TilemapRenderer>();
            tr.sharedMaterial = m.background;
            tr.sortingOrder = ProceduralMap.OrderGround;
            tr.mode = TilemapRenderer.Mode.Chunk;

            foreach (var old in roots.Where(g => g.name == "ProceduralMap").ToList()) Object.DestroyImmediate(old);
            roots = scene.GetRootGameObjects();
            var mapGo = new GameObject("ProceduralMap");
            SceneManager.MoveGameObjectToScene(mapGo, scene);
            var blend = new GameObject("TerrainBlend", typeof(SpriteRenderer));
            blend.transform.SetParent(mapGo.transform, false);
            var bsr = blend.GetComponent<SpriteRenderer>();
            bsr.sprite = s.white;
            bsr.sharedMaterial = m.blend;
            bsr.sortingOrder = ProceduralMap.OrderBlend;
            var map = mapGo.AddComponent<ProceduralMap>();
            map.ground = groundGo.GetComponent<Tilemap>();
            map.blendQuad = bsr;
            map.palette = pal;

            // --- 카메라: 픽셀 규격 통일로 공명 64px / 여포 128px → 시야 확대
            var ppc = Find<PixelPerfectCamera>();
            ppc.assetsPPU = (int)PPU;
            ppc.refResolutionX = RefRes.x;
            ppc.refResolutionY = RefRes.y;
            EditorUtility.SetDirty(ppc);

            // --- 조명 (노멀맵이 보이도록 키 라이트 추가, 전역광 낮춤)
            foreach (var l in roots.SelectMany(g => g.GetComponentsInChildren<Light2D>(true)))
            {
                if (l.lightType == Light2D.LightType.Global) { l.intensity = 0.55f; EditorUtility.SetDirty(l); }
            }
            var cam = ppc.GetComponent<Camera>();
            var oldKey = cam.transform.Find("KeyLight");
            if (oldKey) Object.DestroyImmediate(oldKey.gameObject);
            var key = new GameObject("KeyLight");
            key.transform.SetParent(cam.transform, false);
            key.transform.localPosition = new Vector3(-3f, 3.5f, 10f);
            var kl = key.AddComponent<Light2D>();
            kl.lightType = Light2D.LightType.Point;
            kl.color = new Color(1f, 0.95f, 0.85f);
            kl.intensity = 0.5f;
            kl.pointLightInnerRadius = 2f;
            kl.pointLightOuterRadius = 14f;
            kl.falloffIntensity = 0.3f;
            MapDecorBuilder.ApplyToAllSortingLayers(kl);
            MapDecorBuilder.EnableNormals(kl, 2.5f);

            // --- 플레이어 = 제갈공명
            var player = Find<Player>();
            SetupPlayer(player, m, p);

            // --- 스폰 데이터: 시야 확대 보정 (절대값으로 설정해 재실행해도 누적 안 됨)
            var spawner = Find<Spawner>();
            if (spawner && spawner.spawnDatas.Length >= 2)
            {
                spawner.spawnDatas[0].speed = 0.5625f; spawner.spawnDatas[0].arrowSpeed = 3.75f; spawner.spawnDatas[0].attackRange = 3f;
                spawner.spawnDatas[1].speed = 0.9375f; spawner.spawnDatas[1].arrowSpeed = 2.625f; spawner.spawnDatas[1].attackRange = 3f;
                EditorUtility.SetDirty(spawner);
            }

            // --- 보스 디렉터
            foreach (var old in roots.Where(g => g.name == "BossDirector").ToList()) Object.DestroyImmediate(old);
            roots = scene.GetRootGameObjects();
            var dirGo = new GameObject("BossDirector");
            SceneManager.MoveGameObjectToScene(dirGo, scene);
            var director = dirGo.AddComponent<BossDirector>();
            director.bossPrefab = p.lubu.GetComponent<LuBuBoss>();
            director.spawnTime = 30f;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
            log.AppendLine("Scene configured: procedural map, kongming, boss director, lighting, camera " + RefRes);
        }

        static void SetupPlayer(Player player, Mats m, Prefabs p)
        {
            player.speed = 1.125f;
            var scanner = player.GetComponent<Scanner>();
            scanner.scanRange = 2f;
            var cap = player.GetComponent<CapsuleCollider2D>();
            cap.size = new Vector2(0.4f, 0.7f);
            cap.offset = new Vector2(0f, -0.05f);

            var rootSr = player.GetComponent<SpriteRenderer>();
            rootSr.sprite = null; // flip 상태만 보관 (Hand 가 참조) — 실제 그림은 Visual
            var animator = player.GetComponent<Animator>();
            if (animator) animator.enabled = false;

            var visualT = player.transform.Find("Visual");
            GameObject visual = visualT ? visualT.gameObject : new GameObject("Visual", typeof(SpriteRenderer));
            visual.transform.SetParent(player.transform, false);
            visual.transform.SetSiblingIndex(0);
            visual.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            visual.layer = player.gameObject.layer;
            var vsr = visual.GetComponent<SpriteRenderer>();
            vsr.sprite = AssetDatabase.LoadAllAssetsAtPath("Assets/TK/kongming.png").OfType<Sprite>().First();
            vsr.sharedMaterial = m.character;
            vsr.sortingOrder = 5;
            var anim = visual.GetComponent<ProceduralSpriteAnim>() ?? visual.AddComponent<ProceduralSpriteAnim>();
            anim.source = rootSr;
            anim.spriteFacesRight = true;
            anim.walkStepPixels = 1;
            anim.walkStepInterval = 0.14f;
            anim.idleStepInterval = 0.55f;
            anim.handPixels = new Vector2(20f, 14f); // 뻗은 오른손 끝 (원화 기준)

            var shadow = player.transform.Find("Shadow");
            if (shadow)
            {
                shadow.localPosition = new Vector3(0f, -0.42f, 0f);
                shadow.localScale = Vector3.one;
                var ssr = shadow.GetComponent<SpriteRenderer>();
                ssr.sprite = ShadowSprite("Shadow_Player", 24, 6);
                ssr.sharedMaterial = m.decor;
            }
            // 손 무기 그림은 숨김 (공명 원화가 이미 손을 뻗고 있음) — Hand 는 좌우 방향/무기 장착 로직용으로만 유지
            foreach (var handName in new[] { "Hand_Left", "Hand_Right" })
            {
                var hand = player.transform.Find(handName);
                if (!hand) continue;
                hand.localScale = Vector3.one;
                var hsr = hand.GetComponent<SpriteRenderer>();
                if (hsr) hsr.enabled = false;
            }

            var pl = player.transform.Find("PlayerLight");
            if (pl)
            {
                var l = pl.GetComponent<Light2D>();
                l.intensity = 0.6f;
                l.pointLightOuterRadius = 1.8f;
                MapDecorBuilder.EnableNormals(l, 0.7f);
            }

            var skills = player.GetComponent<KongmingSkills>() ?? player.gameObject.AddComponent<KongmingSkills>();
            skills.fireOrbPrefab = p.fireOrb.GetComponent<FireOrb>();
            skills.trigramPrefab = p.trigramRing.GetComponent<TrigramRing>();
            skills.trigramCastFx = p.trigramCast;
            skills.fireRadius = 0.75f;
            skills.trigramRadius = 3f * 29.5f / PPU; // 링 스프라이트 정확히 3배 (픽셀 규격 유지)

            EditorUtility.SetDirty(player);
            EditorUtility.SetDirty(player.gameObject);
        }
    }
}
