using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TK.EditorTools
{
    /// <summary>
    /// 연출(뱀서 스타일) + UI 재구성 단계
    ///  - 보석/자석/보물상자 픽셀 스프라이트 생성 (1:1 픽셀, PPU 64)
    ///  - UI_atlas.png 를 9-slice 로 잘라 UI 스킨 생성, 기존 HUD/레벨업/버튼 재배치·폰트 축소 (뱀서식 배치)
    ///  - 내장 기능 사용: Pixel Perfect Camera 픽셀 스냅, Cinemachine Impulse(화면 흔들림), UI Outline(아이콘 테두리)
    /// </summary>
    public static partial class TKGameBuilder
    {
        const string AtlasPath = "Assets/TK/UI_atlas.png";

        static void SetupJuiceAndUI(Mats m, Shapes shapes, Fx fx, StringBuilder log)
        {
            // 구버전(2x2 반복 바닥) 타일 에셋 정리 — 절차적 맵 팔레트로 대체됨
            if (AssetDatabase.IsValidFolder("Assets/TK/Tiles/Ground")) AssetDatabase.DeleteAsset("Assets/TK/Tiles/Ground");

            // ---- 픽셀 스프라이트 생성
            var gemBlue = GridSprite("Gem_Blue", GemRows, GemPalette(new Color32(70, 130, 255, 255)));
            var gemGreen = GridSprite("Gem_Green", GemRows, GemPalette(new Color32(70, 210, 90, 255)));
            var gemRed = GridSprite("Gem_Red", GemRows, GemPalette(new Color32(235, 60, 60, 255)));
            var magnet = GridSprite("Pickup_Magnet", MagnetRows, new Dictionary<char, Color32>
            {
                ['o'] = new Color32(30, 15, 20, 255), ['R'] = new Color32(210, 40, 40, 255), ['G'] = new Color32(205, 210, 220, 255),
            });
            var chest = GridSprite("Pickup_Chest", ChestRows, new Dictionary<char, Color32>
            {
                ['o'] = new Color32(35, 18, 10, 255), ['W'] = new Color32(140, 80, 40, 255), ['G'] = new Color32(230, 180, 60, 255), ['Y'] = new Color32(255, 235, 120, 255),
            });
            var mandu = AssetDatabase.LoadAllAssetsAtPath("Assets/TK/mandu.png").OfType<Sprite>().First();

            // ---- 재질
            var glow = Shader.Find("TK/PixelGlow");
            var gemMat = Mat("GemGlow", glow);
            Set(gemMat, ("_Intensity", 1.15f), ("_HaloRadius", 1f), ("_HaloStrength", 0.6f), ("_FlickerAmount", 0f));
            gemMat.SetColor("_GlowColor", new Color(0.6f, 0.8f, 1.4f, 1f));
            var popMat = Mat("DeathPopGlow", glow);
            Set(popMat, ("_Intensity", 1.2f), ("_HaloRadius", 0f), ("_FlickerAmount", 0f));

            // 내장 Pixel Perfect Camera 픽셀 스냅 사용 → 셰이더의 중복 스냅 기능 끔
            foreach (var mat in new[] { m.character, m.boss, m.background, m.decor })
            {
                mat.SetFloat("_PixelSnap", 0f);
                mat.DisableKeyword("_PIXEL_SNAP");
                EditorUtility.SetDirty(mat);
            }

            // ---- 바닥 아이템 프리팹
            var heal = PickupPrefab("Pickup_Heal", mandu, FloorPickup.Kind.Heal, m.character, null);
            var mag = PickupPrefab("Pickup_Magnet", magnet, FloorPickup.Kind.Magnet, m.character, null);
            var box = PickupPrefab("Pickup_Chest", chest, FloorPickup.Kind.Chest, m.character, new Color(1f, 0.85f, 0.4f));

            // ---- 픽셀 규격 정리 (폰트 / 아이콘 / 이펙트 시트 / 프리팹 스케일)
            ConfigurePCPlayer(log);
            var font = ConfigurePixelFont();
            NormalizeIcons(log);
            DownsampleFxSheets(log, m.fireFx);
            FixPrefabScales(log);

            // ---- UI 스킨
            var skin = BuildSkin(shapes, log);
            skin.font = font;
            EditorUtility.SetDirty(skin);

            // ---- 씬
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool wasOpen = scene.IsValid() && scene.isLoaded;
            if (!wasOpen) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var roots = scene.GetRootGameObjects();
            T Find<T>() where T : Component => roots.SelectMany(g => g.GetComponentsInChildren<T>(true)).FirstOrDefault();

            var ppc = Find<PixelPerfectCamera>();
            ppc.gridSnapping = PixelPerfectCamera.GridSnapping.PixelSnapping;
            EditorUtility.SetDirty(ppc);

            var vcam = Find<CinemachineCamera>();
            if (vcam && !vcam.GetComponent<CinemachineImpulseListener>())
            {
                var listener = vcam.gameObject.AddComponent<CinemachineImpulseListener>();
                listener.Gain = 1f;
                listener.Use2DDistance = true;
            }

            foreach (var old in roots.Where(g => g.name == "TKSystems").ToList()) Object.DestroyImmediate(old);
            var sys = new GameObject("TKSystems");
            SceneManager.MoveGameObjectToScene(sys, scene);

            var impulse = sys.AddComponent<CinemachineImpulseSource>();
            impulse.ImpulseDefinition.ImpulseChannel = 1;
            impulse.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
            impulse.ImpulseDefinition.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
            impulse.ImpulseDefinition.ImpulseDuration = 0.2f;

            var feel = sys.AddComponent<GameFeel>();
            feel.popFrames = fx.deathPop;
            feel.popMaterial = popMat;

            var gems = sys.AddComponent<XpGemSystem>();
            gems.blueGem = gemBlue; gems.greenGem = gemGreen; gems.redGem = gemRed;
            gems.gemMaterial = gemMat;

            var spawner = sys.AddComponent<PickupSpawner>();
            spawner.healPrefab = heal; spawner.magnetPrefab = mag; spawner.chestPrefab = box;

            sys.AddComponent<EventDirector>();

            var canvas = roots.SelectMany(g => g.GetComponentsInChildren<Canvas>(true)).First(c => c.isRootCanvas);
            var uiRoot = sys.AddComponent<TKUIRoot>();
            uiRoot.skin = skin;
            uiRoot.canvas = canvas;

            // 정수 UI 배율 + 픽셀 퍼펙트 캔버스 + 전체 텍스트 폰트 교체
            canvas.pixelPerfect = true;
            if (!canvas.GetComponent<PixelUIScaler>()) canvas.gameObject.AddComponent<PixelUIScaler>();
            foreach (var t in canvas.GetComponentsInChildren<Text>(true)) { t.font = font; EditorUtility.SetDirty(t); }

            RestyleUI(canvas, skin, log);
            ApplyThreeKingdomsTexts(canvas, skin, log);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
            log.AppendLine("Juice + UI configured (gems, pickups, chest, damage numbers, impulse shake, VS-style HUD)");
        }

        // ================================================================== pixel sprites

        static readonly string[] GemRows =
        {
            "...o...",
            "..oLo..",
            ".oLLBo.",
            "oLLBBDo",
            "oLBBBDo",
            "oLBBDDo",
            ".oBDDo.",
            "..oDo..",
            "...o...",
        };

        static Dictionary<char, Color32> GemPalette(Color32 baseColor)
        {
            Color b = baseColor;
            return new Dictionary<char, Color32>
            {
                ['o'] = (Color32)(b * 0.3f + new Color(0, 0, 0, 0.7f)),
                ['L'] = (Color32)Color.Lerp(b, Color.white, 0.55f),
                ['B'] = baseColor,
                ['D'] = (Color32)(b * 0.65f + new Color(0, 0, 0, 0.35f)),
            };
        }

        static readonly string[] MagnetRows =
        {
            "oooo....oooo",
            "oGGo....oGGo",
            "oGGo....oGGo",
            "oRRo....oRRo",
            "oRRo....oRRo",
            "oRRo....oRRo",
            "oRRo....oRRo",
            "oRRRo..oRRRo",
            ".oRRRooRRRo.",
            "..oRRRRRRo..",
            "...oooooo...",
        };

        static readonly string[] ChestRows =
        {
            "..oooooooooooo..",
            ".oWWWWWWWWWWWWo.",
            "oWWWWWWWWWWWWWWo",
            "oWWWWWWWWWWWWWWo",
            "oGGGGGGYYGGGGGGo",
            "oGGGGGGYYGGGGGGo",
            "oWWWWWWYYWWWWWWo",
            "oWWWWWWWWWWWWWWo",
            "oWWWWWWWWWWWWWWo",
            "oGWWWWWWWWWWWWGo",
            "oooooooooooooooo",
        };

        static Sprite GridSprite(string name, string[] rows, Dictionary<char, Color32> palette)
        {
            int h = rows.Length, w = rows[0].Length;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    char ch = rows[h - 1 - y][x];
                    tex.SetPixel(x, y, palette.TryGetValue(ch, out var col) ? (Color)col : new Color(0, 0, 0, 0));
                }
            string path = $"{Gen}/{name}.png";
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
            ti.SetTextureSettings(st);
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static FloorPickup PickupPrefab(string name, Sprite sprite, FloorPickup.Kind kind, Material mat, Color? lightColor)
        {
            var go = SpriteGo(name, sprite, mat, -2);
            var p = go.AddComponent<FloorPickup>();
            p.kind = kind;
            if (lightColor.HasValue)
                MapDecorBuilder.AddGlowLight(go.transform, Vector3.zero, lightColor.Value, 0.9f, 1.2f);
            return SavePrefab(go, name).GetComponent<FloorPickup>();
        }

        // ================================================================== UI skin (UI_atlas 9-slice)

        static TKUISkin BuildSkin(Shapes shapes, StringBuilder log)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
            if (ti.filterMode != FilterMode.Point || ti.textureCompression != TextureImporterCompression.Uncompressed)
            {
                ti.filterMode = FilterMode.Point;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }

            string cachePath = $"{Gen}/UIAtlasSlices.asset";
            AssetDatabase.DeleteAsset(cachePath);
            var cache = ScriptableObject.CreateInstance<SpriteSheetCache>();
            AssetDatabase.CreateAsset(cache, cachePath);
            var list = new List<Sprite>();

            // (x, 위쪽 기준 y, w, h, 9-slice 테두리 L,B,R,T)
            Sprite Cut(string n, int x, int yTop, int w, int h, Vector4 border)
            {
                var sp = Sprite.Create(atlas, new Rect(x, atlas.height - yTop - h, w, h), new Vector2(0.5f, 0.5f), PPU, 0, SpriteMeshType.FullRect, border);
                sp.name = n;
                AssetDatabase.AddObjectToAsset(sp, cache);
                list.Add(sp);
                return sp;
            }

            string skinPath = $"{Gen}/TKUISkin.asset";
            var skin = AssetDatabase.LoadAssetAtPath<TKUISkin>(skinPath);
            if (skin == null) { skin = ScriptableObject.CreateInstance<TKUISkin>(); AssetDatabase.CreateAsset(skin, skinPath); }
            skin.panel = Cut("Panel", 86, 0, 89, 62, new Vector4(10, 10, 10, 10));
            skin.card = Cut("Card", 0, 62, 60, 31, new Vector4(7, 7, 7, 7));
            skin.ribbon = Cut("Ribbon", 236, 67, 60, 19, new Vector4(8, 4, 8, 4));
            skin.ribbonPaper = Cut("RibbonPaper", 236, 97, 60, 20, new Vector4(9, 5, 9, 5));
            skin.slot = Cut("Slot", 0, 93, 32, 29, new Vector4(6, 6, 6, 6));
            skin.button = Cut("Button", 176, 62, 59, 31, new Vector4(8, 8, 8, 8));
            skin.barFrame = Cut("BarFrame", 384, 193, 34, 12, new Vector4(4, 3, 4, 3));
            skin.barThin = Cut("BarThin", 444, 195, 33, 7, new Vector4(3, 2, 3, 2));
            skin.white = shapes.white;
            skin.skull = AssetDatabase.LoadAllAssetsAtPath("Assets/Undead Survivor/Sprites/UI.png").OfType<Sprite>().FirstOrDefault(s => s.name == "Icon 0");
            skin.bossPortrait = AssetDatabase.LoadAllAssetsAtPath("Assets/TK/LuBu.png").OfType<Sprite>().First();
            skin.headlineSize = 32;
            skin.titleSize = 16;
            skin.bodySize = 16;
            skin.smallSize = 16;
            cache.sprites = list.ToArray();
            EditorUtility.SetDirty(cache);
            EditorUtility.SetDirty(skin);
            AssetDatabase.SaveAssets();
            return skin;
        }

        // ================================================================== PC 전용 설정

        /// <summary>PC(Windows) 전용: 1920x1080 기본 해상도, 창 크기 조절 가능, 포커스를 잃어도 계속 실행</summary>
        static void ConfigurePCPlayer(StringBuilder log)
        {
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            log.AppendLine("PC player: 1920x1080 default, fullscreen window, landscape only");
        }

        // ================================================================== 픽셀 폰트

        const string FontPath = "Assets/TK/DOSMyungjo.ttf";

        /// <summary>DOSMyungjo: 원본 16px, 래스터(안티앨리어싱 없음) 렌더링 → 정수 배율에서 계단 그대로 선명</summary>
        static Font ConfigurePixelFont()
        {
            var fi = AssetImporter.GetAtPath(FontPath) as TrueTypeFontImporter;
            if (fi != null && (fi.fontRenderingMode != FontRenderingMode.HintedRaster || fi.fontSize != 16 || fi.characterPadding != 0))
            {
                fi.fontRenderingMode = FontRenderingMode.HintedRaster;
                fi.fontSize = 16;
                fi.characterPadding = 0;
                fi.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        }

        // ================================================================== 아이콘 정규화 (32px / 16px)

        // 아이템 → 원본 아이콘 (재실행해도 원본에서 다시 생성)
        static readonly Dictionary<string, (string path, string sprite)> IconSources = new Dictionary<string, (string, string)>
        {
            ["Item 0"] = ("Assets/TK/Untitled_41.png", "Untitled_41_3"),
            ["Item 1"] = ("Assets/TK/Untitled_41.png", "Untitled_41_1"),
            ["Item 2"] = ("Assets/TK/Untitled_41.png", "Untitled_41_0"),
            ["Item 3"] = ("Assets/TK/Untitled_41.png", "Untitled_41_2"),
            ["Item 4"] = ("Assets/TK/mandu.png", "mandu_0"),
            ["Item 5"] = ("Assets/TK/FX/AreaWeapons/FireIcon.png", "FireIcon"),
            ["Item 6"] = ("Assets/TK/FX/AreaWeapons/LightningIcon.png", "LightningIcon"),
        };

        static void NormalizeIcons(StringBuilder log)
        {
            Directory.CreateDirectory($"{Gen}/Icons");
            foreach (var guid in AssetDatabase.FindAssets("t:ItemData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));
                if (!IconSources.TryGetValue(data.name, out var src)) continue;
                var sprite = AssetDatabase.LoadAllAssetsAtPath(src.path).OfType<Sprite>().FirstOrDefault(s => s.name == src.sprite);
                if (sprite == null) continue;

                var pixels = ReadSpritePixels(sprite, out int w, out int h);
                // 32 보다 크면 정수배로 축소, 작으면 그대로 가운데 배치 (늘리지 않음)
                int factor = 1;
                while (Mathf.CeilToInt((float)Mathf.Max(w, h) / factor) > 32) factor *= 2;
                var down = Downsample(pixels, w, h, factor, out int dw, out int dh);
                var icon32 = Pad(down, dw, dh, 32);
                var icon16 = Downsample(icon32, 32, 32, 2, out _, out _);
                data.itemIcon = SavePixelSprite($"{Gen}/Icons/Icon_{data.name.Replace(" ", "")}_32.png", icon32, 32, 32);
                data.iconSmall = SavePixelSprite($"{Gen}/Icons/Icon_{data.name.Replace(" ", "")}_16.png", icon16, 16, 16);
                data.effectScale = 1f; // 이펙트 시트를 픽셀 단위로 축소했으므로 배율 1
                EditorUtility.SetDirty(data);
            }
            log.AppendLine("Icons normalized: 32px (cards) / 16px (inventory)");
        }

        static Color32[] ReadSpritePixels(Sprite sprite, out int w, out int h)
        {
            var path = AssetDatabase.GetAssetPath(sprite.texture);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(path));
            var r = sprite.rect;
            w = (int)r.width; h = (int)r.height;
            var px = tex.GetPixels32();
            var outPx = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    outPx[y * w + x] = px[((int)r.y + y) * tex.width + (int)r.x + x];
            Object.DestroyImmediate(tex);
            return outPx;
        }

        /// <summary>픽셀아트 축소: 블록 안 불투명 픽셀 평균색, 불투명 비율 50% 이상이면 채움 (흐림 없이 1:1 픽셀 결과)</summary>
        static Color32[] Downsample(Color32[] src, int w, int h, int f, out int dw, out int dh) => Downsample(src, w, h, f, out dw, out dh, false);

        /// <summary>soft=true: 이펙트용. 블록의 알파·색을 프리멀티플라이드 평균 → 반투명 빛 가장자리 보존 (잘린 테두리 방지)</summary>
        static Color32[] Downsample(Color32[] src, int w, int h, int f, out int dw, out int dh, bool soft)
        {
            dw = Mathf.CeilToInt((float)w / f); dh = Mathf.CeilToInt((float)h / f);
            if (f <= 1) { dw = w; dh = h; return (Color32[])src.Clone(); }
            var dst = new Color32[dw * dh];
            if (soft)
            {
                for (int y = 0; y < dh; y++)
                    for (int x = 0; x < dw; x++)
                    {
                        float pr = 0, pg = 0, pb = 0, pa = 0; int cnt = 0;
                        for (int by = 0; by < f; by++)
                            for (int bx = 0; bx < f; bx++)
                            {
                                int sx = x * f + bx, sy = y * f + by;
                                if (sx >= w || sy >= h) continue;
                                var c = src[sy * w + sx];
                                float a = c.a < 12 ? 0f : c.a / 255f; // 원본의 거의 투명한 잡티 제거 (Bloom 에 사각형으로 증폭됨)
                                pr += c.r * a; pg += c.g * a; pb += c.b * a; pa += a; cnt++;
                            }
                        float A = cnt > 0 ? pa / cnt : 0f;
                        dst[y * dw + x] = A >= 0.05f
                            ? new Color32((byte)Mathf.Clamp(pr / pa, 0, 255), (byte)Mathf.Clamp(pg / pa, 0, 255), (byte)Mathf.Clamp(pb / pa, 0, 255), (byte)Mathf.RoundToInt(A * 255f))
                            : new Color32(0, 0, 0, 0);
                    }
                return dst;
            }
            for (int y = 0; y < dh; y++)
                for (int x = 0; x < dw; x++)
                {
                    int r = 0, g = 0, b = 0, n = 0, total = 0, maxA = 0;
                    for (int by = 0; by < f; by++)
                        for (int bx = 0; bx < f; bx++)
                        {
                            int sx = x * f + bx, sy = y * f + by;
                            if (sx >= w || sy >= h) continue;
                            total++;
                            var c = src[sy * w + sx];
                            if (c.a < 32) continue;
                            r += c.r; g += c.g; b += c.b; n++;
                            maxA = Mathf.Max(maxA, c.a);
                        }
                    dst[y * dw + x] = n * 2 >= total && n > 0
                        ? new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)maxA)
                        : new Color32(0, 0, 0, 0);
                }
            return dst;
        }

        static Color32[] Pad(Color32[] src, int w, int h, int size)
        {
            var dst = new Color32[size * size];
            int ox = (size - w) / 2, oy = (size - h) / 2;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int dx = x + ox, dy = y + oy;
                    if (dx >= 0 && dy >= 0 && dx < size && dy < size) dst[dy * size + dx] = src[y * w + x];
                }
            return dst;
        }

        static Sprite SavePixelSprite(string path, Color32[] px, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
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
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>그림자: 필요한 크기 그대로 그린 픽셀 타원 (스케일로 늘리지 않음)</summary>
        static Sprite ShadowSprite(string name, int w, int h)
        {
            Directory.CreateDirectory(Gen);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float nx = (x + 0.5f - w / 2f) / (w / 2f), ny = (y + 0.5f - h / 2f) / (h / 2f);
                    float d = nx * nx + ny * ny;
                    px[y * w + x] = d <= 1f ? new Color32(0, 0, 0, (byte)(d < 0.45f ? 110 : 70)) : new Color32(0, 0, 0, 0);
                }
            return SavePixelSprite($"{Gen}/{name}.png", px, w, h);
        }

        // ================================================================== 이펙트 시트 픽셀 단위 축소

        // (시트, 축소 배율, 클립들, 프리팹들) — 예전에 0.5 / 0.6 / 0.25 배로 '스케일'해서 쓰던 것들을 원본 픽셀 비율로
        static readonly (string tex, int factor, string[] clips, string[] prefabs)[] FxSheets =
        {
            ("Assets/TK/FX/AreaWeapons/FireBreath.png", 2, new[] { "Assets/TK/FX/AreaWeapons/FireBreath.anim" }, new[] { "Assets/TK/FX/AreaWeapons/FireBreath.prefab" }),
            ("Assets/TK/FX/AreaWeapons/LightningStrike.png", 4, new[] { "Assets/TK/FX/AreaWeapons/LightningStrike.anim" }, new[] { "Assets/TK/FX/AreaWeapons/LightningStrike.prefab" }),
            ("Assets/TK/FX/Fireball/fireball.png", 2, new[] { "Assets/TK/FX/Fireball/Fireball.anim" }, new[] { "Assets/Undead Survivor/Prefabs/Bullet 1.prefab" }),
        };

        static void DownsampleFxSheets(StringBuilder log, Material fxMaterial)
        {
            Directory.CreateDirectory($"{Gen}/FX");
            foreach (var (texPath, factor, clips, prefabs) in FxSheets)
            {
                string name = Path.GetFileNameWithoutExtension(texPath);
                var srcSprites = AssetDatabase.LoadAllAssetsAtPath(texPath).OfType<Sprite>().ToList();
                var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                src.LoadImage(File.ReadAllBytes(texPath));
                var down = Downsample(src.GetPixels32(), src.width, src.height, factor, out int dw, out int dh, true);
                Object.DestroyImmediate(src);

                // 프레임 경계에서 잘린 빛 번짐 → 각 프레임 가장자리 4px 동안 알파를 0까지 서서히 줄여 '잘린 선' 제거
                const int fadePx = 4;
                foreach (var s in srcSprites)
                {
                    var r = s.rect;
                    int x0 = Mathf.FloorToInt(r.x / factor), y0 = Mathf.FloorToInt(r.y / factor);
                    int fw = Mathf.Min(Mathf.CeilToInt(r.width / factor), dw - x0), fh = Mathf.Min(Mathf.CeilToInt(r.height / factor), dh - y0);
                    for (int yy = 0; yy < fh; yy++)
                        for (int xx = 0; xx < fw; xx++)
                        {
                            int edge = Mathf.Min(Mathf.Min(xx, fw - 1 - xx), Mathf.Min(yy, fh - 1 - yy));
                            if (edge >= fadePx) continue;
                            int idx = (y0 + yy) * dw + (x0 + xx);
                            var c = down[idx];
                            c.a = (byte)(c.a * (edge / (float)fadePx) * (edge / (float)fadePx));
                            down[idx] = c;
                        }
                }

                // 옅은 광채(알파 낮은 넓은 영역)가 확대되면 판때기처럼 보임 → 알파 곡선으로 옅은 부분만 눌러 줌 (진한 불꽃·번개는 유지)
                for (int i = 0; i < down.Length; i++)
                {
                    float a = down[i].a / 255f;
                    float curved = a < 0.35f ? a * a / 0.35f * (a / 0.35f) : a;  // 0.35 미만은 세제곱 감쇠
                    down[i].a = (byte)Mathf.RoundToInt(curved * 255f);
                }

                string pngPath = $"{Gen}/FX/{name}_px.png";
                var tex = new Texture2D(dw, dh, TextureFormat.RGBA32, false);
                tex.SetPixels32(down);
                File.WriteAllBytes(pngPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(pngPath);
                var ti = (TextureImporter)AssetImporter.GetAtPath(pngPath);
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.spritePixelsPerUnit = PPU;
                ti.filterMode = FilterMode.Point;
                ti.mipmapEnabled = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
                var newTex = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);

                // 같은 이름·같은 상대 피벗으로 프레임 재생성
                // 새 프레임은 임시 에셋에 만든 뒤, 클립·프리팹 연결을 옮기고 나서 기존 에셋과 교체
                // (먼저 지우면 클립이 사라진 스프라이트를 가리켜 비어 버림)
                string cachePath = $"{Gen}/FX/{name}_px_frames.asset";
                string tempPath = $"{Gen}/FX/{name}_px_frames_new.asset";
                AssetDatabase.DeleteAsset(tempPath);
                var cache = ScriptableObject.CreateInstance<SpriteSheetCache>();
                AssetDatabase.CreateAsset(cache, tempPath);
                var map = new Dictionary<string, Sprite>();
                foreach (var s in srcSprites)
                {
                    var r = s.rect;
                    int x = Mathf.FloorToInt(r.x / factor), y = Mathf.FloorToInt(r.y / factor);
                    int w = Mathf.Min(Mathf.CeilToInt(r.width / factor), dw - x), h = Mathf.Min(Mathf.CeilToInt(r.height / factor), dh - y);
                    var pivot = new Vector2(s.pivot.x / r.width, s.pivot.y / r.height);
                    var ns = Sprite.Create(newTex, new Rect(x, y, w, h), pivot, PPU, 0, SpriteMeshType.FullRect);
                    ns.name = s.name;
                    AssetDatabase.AddObjectToAsset(ns, cache);
                    map[s.name] = ns;
                }
                cache.sprites = map.Values.ToArray();
                EditorUtility.SetDirty(cache);
                AssetDatabase.SaveAssets();

                // 애니메이션 클립의 스프라이트 키를 이름으로 교체 (원본/이전 생성본 어느 쪽이든).
                // 이미 비어 버린 키는 원본 프레임 순서(이름순)로 복구
                // 이름 끝 숫자 기준 정렬 (fireball_2 < fireball_10)
                int FrameNo(string n) { int i = n.Length; while (i > 0 && char.IsDigit(n[i - 1])) i--; return i < n.Length ? int.Parse(n.Substring(i)) : 0; }
                var ordered = srcSprites.OrderBy(s => FrameNo(s.name)).Select(s => map[s.name]).ToArray();
                foreach (var clipPath in clips)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                    if (!clip) continue;
                    foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    {
                        var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                        // 프레임 수와 같거나 '+1(마지막 프레임 유지 키)' 이면 순서대로 복구
                        bool sequential = keys.Length == ordered.Length || keys.Length == ordered.Length + 1;
                        bool anyNull = keys.Any(kf => kf.value == null);
                        // 이전 빌드의 문자순 복구로 순서가 뒤섞인 경우(0,1,10,11…)도 바로잡음
                        for (int k = 1; k < keys.Length && !anyNull; k++)
                            if (FrameNo(keys[k].value.name) < FrameNo(keys[k - 1].value.name)) anyNull = true;
                        for (int k = 0; k < keys.Length; k++)
                        {
                            if (anyNull && sequential) keys[k].value = ordered[Mathf.Min(k, ordered.Length - 1)];
                            else if (keys[k].value is Sprite sp && sp && map.TryGetValue(sp.name, out var repl)) keys[k].value = repl;
                        }
                        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
                    }
                    EditorUtility.SetDirty(clip);
                }

                // 프리팹: 기본 스프라이트 교체, 스케일 1, 콜라이더는 예전 스케일만큼 줄여 실제 크기 유지
                foreach (var prefabPath in prefabs)
                {
                    var root = PrefabUtility.LoadPrefabContents(prefabPath);
                    float oldScale = root.transform.localScale.x;
                    foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
                    {
                        if (sr.sprite && map.TryGetValue(sr.sprite.name, out var repl)) sr.sprite = repl;
                        else if (sr.sprite == null && ordered.Length > 0) sr.sprite = ordered[0];
                        // 캐릭터용(외곽선) 재질 대신 FX 전용 재질 → 검은 테두리 제거
                        sr.sharedMaterial = fxMaterial;
                    }
                    if (!Mathf.Approximately(oldScale, 1f))
                    {
                        foreach (var cc in root.GetComponentsInChildren<CircleCollider2D>(true)) cc.radius *= oldScale;
                        foreach (var bc in root.GetComponentsInChildren<BoxCollider2D>(true)) bc.size *= oldScale;
                        foreach (var cap in root.GetComponentsInChildren<CapsuleCollider2D>(true)) cap.size *= oldScale;
                        root.transform.localScale = Vector3.one;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    PrefabUtility.UnloadPrefabContents(root);
                }
                AssetDatabase.SaveAssets();
                // 연결을 모두 옮긴 뒤 기존 프레임 에셋 삭제 → 새 에셋을 고정 경로로 이동 (이동은 참조 유지)
                AssetDatabase.DeleteAsset(cachePath);
                AssetDatabase.MoveAsset(tempPath, cachePath);
                log.AppendLine($"FX sheet {name}: 1/{factor} soft pixel downsample, {map.Count} frames, scale 1");
            }
        }

        // 스케일로 크기를 조절하던 나머지 프리팹 → 1:1:1 (콜라이더는 그대로 → 그림 크기에 맞게 함께 커짐)
        static void FixPrefabScales(StringBuilder log)
        {
            foreach (var path in new[] { "Assets/Undead Survivor/Prefabs/Bullet 2.prefab", "Assets/Undead Survivor/Prefabs/Enemy Arrow.prefab" })
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                root.transform.localScale = Vector3.one;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }
            log.AppendLine("Prefab scales set to 1:1:1 (Bullet 2, Enemy Arrow)");
        }

        // ================================================================== UI restyle (뱀서식 배치, UI 2단위 = 원본 1픽셀)

        static void RestyleUI(Canvas canvas, TKUISkin skin, StringBuilder log)
        {
            const float P = TKUIRoot.UnitsPerPixel; // 원본 1px
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; // 배율은 PixelUIScaler 가 정수로
            scaler.scaleFactor = 1;
            float refPpu = scaler.referencePixelsPerUnit;
            float Mul(Sprite s) => s ? refPpu / (s.pixelsPerUnit * P) : 1f;

            RectTransform R(string path) => canvas.transform.Find(path) as RectTransform;
            void Place(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
            {
                if (!rt) return;
                rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
            }
            void Skin(Image img, Sprite s, Color c)
            {
                if (!img) return;
                img.sprite = s;
                img.type = s && s.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
                img.pixelsPerUnitMultiplier = Mul(s);
                img.preserveAspect = false;
                img.color = c;
            }
            void Native(Image img)
            {
                if (!img || !img.sprite) return;
                img.type = Image.Type.Simple;
                img.preserveAspect = false;
                img.rectTransform.sizeDelta = img.sprite.rect.size * P; // 비율 유지, 정수 배율
            }
            void SetFont(Text t, int size, TextAnchor anchor, Color? color = null)
            {
                if (!t) return;
                t.font = skin.font;
                t.fontSize = size;
                t.alignment = anchor;
                if (color.HasValue) t.color = color.Value;
                foreach (var o in t.GetComponents<Shadow>()) o.effectDistance = new Vector2(1f, -1f);
            }

            // ---- 상단 HUD
            Place(R("HUD/Exp"), new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -2), new Vector2(-4, skin.barFrame.rect.height * P));
            Skin(R("HUD/Exp/Background")?.GetComponent<Image>(), skin.barFrame, Color.white);
            var fillArea = R("HUD/Exp/Fill Area");
            if (fillArea) { fillArea.offsetMin = new Vector2(3 * P, 3 * P); fillArea.offsetMax = new Vector2(-3 * P, -3 * P); }
            Skin(R("HUD/Exp/Fill Area/Fill")?.GetComponent<Image>(), skin.white, new Color(0.28f, 0.55f, 1f));

            Place(R("HUD/Level"), new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-10, -4), new Vector2(80, 18));
            SetFont(R("HUD/Level")?.GetComponent<Text>(), skin.bodySize, TextAnchor.MiddleRight, Color.white);

            Place(R("HUD/Timer"), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -32), new Vector2(120, 36));
            SetFont(R("HUD/Timer")?.GetComponent<Text>(), skin.headlineSize, TextAnchor.UpperCenter, Color.white);

            var kill = R("HUD/Kill");
            if (kill)
            {
                var ki = kill.GetComponent<Image>();
                Native(ki);
                Place(kill, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, -34), kill.sizeDelta);
                TKUIRootIconOutline(ki);
            }
            Place(R("HUD/Kill/Kill Text"), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 0.5f), new Vector2(4, 0), new Vector2(56, 18));
            SetFont(R("HUD/Kill/Kill Text")?.GetComponent<Text>(), skin.bodySize, TextAnchor.MiddleLeft, Color.white);

            var hud = R("HUD");
            var inv = hud.Find("Inventory") as RectTransform;
            if (inv) Object.DestroyImmediate(inv.gameObject);
            var invGo = new GameObject("Inventory", typeof(RectTransform));
            inv = (RectTransform)invGo.transform;
            inv.SetParent(hud, false);
            Place(inv, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(6, -34), new Vector2(260, 84));
            invGo.AddComponent<InventoryHUD>();

            // ---- 플레이어 체력바 (Follow 가 발밑 월드 좌표로 위치)
            Place(R("HUD/Health"), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24 * P, skin.barThin.rect.height * P));
            Place(R("HUD/Health/Health Slider"), Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Skin(R("HUD/Health/Health Slider/Background")?.GetComponent<Image>(), skin.barThin, Color.white);
            var hpFillArea = R("HUD/Health/Health Slider/Fill Area");
            if (hpFillArea) { hpFillArea.offsetMin = new Vector2(2 * P, 2 * P); hpFillArea.offsetMax = new Vector2(-2 * P, -2 * P); }
            Skin(R("HUD/Health/Health Slider/Fill Area/Fill")?.GetComponent<Image>(), skin.white, new Color(0.9f, 0.15f, 0.15f));

            // ---- 레벨업 패널
            var panel = R("LevelUp/Panel");
            Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 16), new Vector2(340, 340));
            Skin(panel?.GetComponent<Image>(), skin.panel, Color.white);
            var oldRibbon = panel.Find("Ribbon");
            if (oldRibbon) Object.DestroyImmediate(oldRibbon.gameObject);
            var ribbon = new GameObject("Ribbon", typeof(RectTransform), typeof(Image));
            var rr = (RectTransform)ribbon.transform;
            rr.SetParent(panel, false);
            rr.SetSiblingIndex(0);
            Place(rr, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -4), new Vector2(200, skin.ribbon.rect.height * P));
            Skin(ribbon.GetComponent<Image>(), skin.ribbon, Color.white);
            ribbon.GetComponent<Image>().raycastTarget = false;
            var title = R("LevelUp/Panel/TextTitle");
            Place(title, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -4), new Vector2(200, skin.ribbon.rect.height * P));
            SetFont(title?.GetComponent<Text>(), skin.titleSize, TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.6f));

            var group = R("LevelUp/Panel/ItemGroup");
            if (group)
            {
                group.anchorMin = Vector2.zero; group.anchorMax = Vector2.one;
                group.offsetMin = new Vector2(20, 20); group.offsetMax = new Vector2(-20, -36);
                var vlg = group.GetComponent<VerticalLayoutGroup>();
                vlg.spacing = 6;
                vlg.childAlignment = TextAnchor.UpperCenter;
                vlg.childControlWidth = true; vlg.childControlHeight = true;
                vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
                foreach (RectTransform item in group)
                {
                    var le = item.GetComponent<LayoutElement>() ?? item.gameObject.AddComponent<LayoutElement>();
                    le.preferredHeight = 88; le.minHeight = 88;
                    Skin(item.GetComponent<Image>(), skin.card, Color.white);

                    var frame = item.Find("IconFrame");
                    if (frame) Object.DestroyImmediate(frame.gameObject);
                    var frameGo = new GameObject("IconFrame", typeof(RectTransform), typeof(Image));
                    var fr = (RectTransform)frameGo.transform;
                    fr.SetParent(item, false);
                    fr.SetSiblingIndex(0);
                    Place(fr, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(46, 0), new Vector2(76, 76));
                    Skin(frameGo.GetComponent<Image>(), skin.slot, Color.white);
                    frameGo.GetComponent<Image>().raycastTarget = false;

                    var icon = item.Find("Icon") as RectTransform;
                    if (icon)
                    {
                        var img = icon.GetComponent<Image>();
                        img.preserveAspect = false;
                        Place(icon, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(46, 0), new Vector2(32 * P, 32 * P)); // 32px 아이콘 1:1
                        TKUIRootIconOutline(img);
                    }

                    Place(item.Find("Text Name") as RectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 0.5f), new Vector2(92, 22), new Vector2(-160, 20));
                    SetFont(item.Find("Text Name")?.GetComponent<Text>(), skin.bodySize, TextAnchor.MiddleLeft, new Color(1f, 0.9f, 0.6f));
                    Place(item.Find("Text Level") as RectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-12, 22), new Vector2(64, 20));
                    SetFont(item.Find("Text Level")?.GetComponent<Text>(), skin.smallSize, TextAnchor.MiddleRight, new Color(0.65f, 1f, 0.65f));
                    Place(item.Find("Text Desc") as RectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 0.5f), new Vector2(92, -12), new Vector2(-104, 40));
                    var desc = item.Find("Text Desc")?.GetComponent<Text>();
                    SetFont(desc, skin.smallSize, TextAnchor.UpperLeft, new Color(0.92f, 0.9f, 0.85f));
                    if (desc) { desc.horizontalOverflow = HorizontalWrapMode.Wrap; desc.verticalOverflow = VerticalWrapMode.Overflow; desc.lineSpacing = 1f; }
                }
            }

            // ---- 시작/다시하기 버튼
            foreach (var path in new[] { "GameStart/Button Start", "GameResult/Button Restart" })
            {
                var b = R(path);
                if (!b) continue;
                b.anchoredPosition = new Vector2(0, -96);
                b.sizeDelta = new Vector2(144, 48);
                b.localScale = Vector3.one;
                Skin(b.GetComponent<Image>(), skin.button, Color.white);
                SetFont(b.GetComponentInChildren<Text>(true), skin.titleSize, TextAnchor.MiddleCenter, Color.white);
            }

            EditorUtility.SetDirty(canvas.gameObject);
            log.AppendLine("UI restyled: integer pixel UI (1px = 2 units), DOSMyungjo 16/32, VS-style layout");
        }

        // ================================================================== 삼국지 문구

        static void ApplyThreeKingdomsTexts(Canvas canvas, TKUISkin skin, StringBuilder log)
        {
            var items = new Dictionary<string, (string name, string desc, string intro)>
            {
                ["Item 0"] = ("병법서", "피해 {0}% 증가\n병법서 {1}권 추가", "공명의 주위를 도는 병법서가 적을 벤다."),
                ["Item 1"] = ("불화살", "피해 {0}% 증가\n관통 {1} 추가", "손끝에서 가장 가까운 적에게 불화살을 쏜다."),
                ["Item 2"] = ("제갈연노", "공격 속도 {0}% 증가", ""),
                ["Item 3"] = ("목우유마", "이동 속도 {0}% 증가", ""),
                ["Item 4"] = ("만두", "체력을 모두 회복한다", ""),
                ["Item 5"] = ("적벽 화공", "피해 {0}% 증가\n화염 범위 {1}% 증가", "손끝에서 가장 가까운 적을 향해 화공을 펼친다."),
                ["Item 6"] = ("칠성단 천뢰", "피해 {0}% 증가\n천뢰 {1}줄기 추가", "칠성단에 빌어 적에게 천뢰를 내린다."),
            };
            foreach (var guid in AssetDatabase.FindAssets("t:ItemData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));
                if (!items.TryGetValue(data.name, out var t)) continue;
                data.itemName = t.name;
                data.itemDesc = t.desc;
                data.introDesc = t.intro;
                EditorUtility.SetDirty(data);
            }

            Text Txt(string path) => (canvas.transform.Find(path) as RectTransform)?.GetComponentInChildren<Text>(true);
            void SetText(string path, string value) { var t = Txt(path); if (t) { t.text = value; EditorUtility.SetDirty(t); } }
            SetText("GameStart/Button Start", "출진");
            SetText("GameResult/Button Restart", "재출진");
            SetText("LevelUp/Panel/TextTitle", "공적 달성!");

            Text TitleText(Transform parent, string name, string value, int size, Color color, Vector2 pos)
            {
                var old = parent.Find(name);
                if (old) Object.DestroyImmediate(old.gameObject);
                var go = new GameObject(name, typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(parent, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(400, size + 8);
                rt.anchoredPosition = pos;
                var t = go.AddComponent<Text>();
                t.text = value;
                t.font = skin.font;
                t.fontSize = size;
                t.alignment = TextAnchor.MiddleCenter;
                t.color = color;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.raycastTarget = false;
                var o = go.AddComponent<Outline>();
                o.effectColor = new Color(0.12f, 0.04f, 0.02f, 1f);
                o.effectDistance = new Vector2(2f, -2f); // 32px 글자의 1px = 2단위
                return t;
            }

            var start = canvas.transform.Find("GameStart");
            if (start)
            {
                TitleText(start, "GameTitle", "삼국 서바이버", skin.headlineSize, new Color(1f, 0.85f, 0.45f), new Vector2(0, 96));
                TitleText(start, "GameSubtitle", "- 와룡 출사표 -", skin.bodySize, new Color(0.95f, 0.92f, 0.85f), new Vector2(0, 60));
            }
            foreach (var (path, value, color) in new[]
            {
                ("GameResult/Title Win", "대승!", new Color(1f, 0.85f, 0.4f)),
                ("GameResult/Title Over", "패퇴...", new Color(0.85f, 0.85f, 0.9f)),
            })
            {
                var title = canvas.transform.Find(path);
                if (!title) continue;
                title.localScale = Vector3.one; // 1.5배 스케일 제거 (픽셀 1:1:1)
                ((RectTransform)title).anchoredPosition = new Vector2(0, 80);
                var img = title.GetComponent<Image>();
                if (img) img.enabled = false;
                TitleText(title, "Label", value, skin.headlineSize, color, Vector2.zero);
            }
            log.AppendLine("Three Kingdoms texts applied (items, buttons, titles)");
        }

        static void TKUIRootIconOutline(Image img)
        {
            if (!img) return;
            var o = img.GetComponent<Outline>() ?? img.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0.04f, 0.02f, 0.02f, 0.75f);
            o.effectDistance = new Vector2(1f, -1f); // UI 1단위 = 원본 반 픽셀 (얇은 테두리)
        }
    }
}
