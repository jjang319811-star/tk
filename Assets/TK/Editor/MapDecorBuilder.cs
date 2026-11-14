using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace TK.EditorTools
{
    /// <summary>
    /// Demo 씬의 무한 반복 바닥(Ground 타일맵 4장)을 tileset.png 로 다시 칠하고,
    /// RPGW_Caves(바위·크리스탈·버섯·식물) + Pixel Fire(불꽃) 에셋을 장식으로 배치.
    /// 불에는 글로우(HDR + 후광 + Light2D + Bloom), 캐릭터에는 돋보이는 셰이더 설정을 적용.
    /// 메뉴: Tools/TK/Build Map Decor  (여러 번 실행해도 같은 결과)
    /// </summary>
    public static class MapDecorBuilder
    {
        const string ScenePath = "Assets/Scenes/Demo.unity";
        const string TilesetPath = "Assets/TK/tileset.png";
        const string DecorPath = "Assets/TK/RPGW_Caves_v2.1/decorative.png";
        const string LedFolder = "Assets/TK/RPGW_Caves_v2.1/Anim";
        const string FireRoot = "Assets/TK/Pixel fire asset pack v1.2/Pixel Fire Asset Pack Colored";
        const string GroundTileFolder = "Assets/TK/Tiles/Ground";
        const string MaterialFolder = "Assets/TK/Materials";
        const string PostFxPath = "Assets/TK/Settings/TKPostFX.asset";
        const string DecorRootName = "Decor";
        const string GlowLightName = "GlowLight";
        const string PlayerLightName = "PlayerLight";
        const string VolumeName = "Global Volume (TK)";
        const float PPU = 64f;

        // 정렬 순서: 캐릭터(0 이상)가 항상 장식 위에 그려지도록 배경 쪽은 음수
        const int OrderGround = -10;
        const int OrderFlatDecor = -8;
        const int OrderRock = -7;
        const int OrderFire = -6;

        // tileset.png(11x8, 16px) 타일 분류 (인덱스 = 좌상단부터 행 우선)
        static readonly int[] PlainSand = { 0, 1, 2, 3, 11, 12, 22, 23, 33, 40, 44, 51, 55, 62, 66 };
        static readonly int[] LightCracks = { 13, 14, 15, 24, 25, 26, 34, 35, 36, 45, 46, 56, 57, 67 };
        static readonly int[] Pebbles = { 4, 8, 18, 19, 29, 41, 52, 63, 74, 78, 79 };
        static readonly int[] HeavyCracks = { 5, 16, 27, 28, 37, 38, 39, 47, 48, 49, 50, 61, 68, 77, 6, 7, 17, 20, 30, 9, 60 };
        static readonly int[] RockTiles = { 42, 53, 64, 75 };

        class Mats { public Material character, background, decor, fire, fireBlue, fireFx; }

        class Palette
        {
            public Sprite[] rocksSmall, rocksBig, crystalsBlue, crystalsGreen, mushrooms;
            public List<Sprite[]> plants = new List<Sprite[]>();
            public Sprite[] bonfire, smallFire, blueFire;
        }

        // (구버전) 2x2 반복 바닥용 — 절차적 맵(TKGameBuilder)으로 대체되어 메뉴 제거, 헬퍼 함수만 공용으로 사용
        public static void BuildFromMenu() => Debug.Log(Build());

        public static string Build()
        {
            var log = new System.Text.StringBuilder();
            var palette = ConfigureImportsAndLoad(log);
            var mats = CreateMaterials();
            var tiles = CreateGroundTiles();

            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool wasOpen = scene.IsValid() && scene.isLoaded;
            if (!wasOpen) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            var tilemaps = scene.GetRootGameObjects()
                .SelectMany(g => g.GetComponentsInChildren<Tilemap>(true))
                .Where(t => t.CompareTag("Ground"))
                .OrderBy(t => t.name)
                .ToList();
            log.AppendLine("Ground tilemaps: " + tilemaps.Count);

            for (int i = 0; i < tilemaps.Count; i++)
                PaintGround(tilemaps[i], tiles, i, mats.background);

            PlaceDecor(tilemaps, palette, mats, log);
            SetupLightingAndPostFx(scene, log);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (!wasOpen) EditorSceneManager.CloseScene(scene, true);

            UpgradeFireFxPrefabs(mats, log);
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        // ------------------------------------------------------------------ Imports

        static Palette ConfigureImportsAndLoad(System.Text.StringBuilder log)
        {
            ConfigurePixelImport(DecorPath, null);
            foreach (var led in new[] { "LedA", "LedB", "LedC", "LedD" })
                ConfigurePixelImport($"{LedFolder}/{led}.png", null);

            var p = new Palette();
            var decor = AssetDatabase.LoadAllAssetsAtPath(DecorPath).OfType<Sprite>().ToList();
            var decorTex = AssetDatabase.LoadAssetAtPath<Texture2D>(DecorPath);
            float Top(Sprite s) => decorTex.height - s.rect.yMax;
            Sprite ByName(string n) => decor.First(s => s.name == n);

            p.rocksSmall = new[] { "decorative_3", "decorative_4", "decorative_5", "decorative_6", "decorative_7", "decorative_8" }.Select(ByName).ToArray();
            p.rocksBig = new[] { "decorative_2", "decorative_9" }.Select(ByName).ToArray();
            p.crystalsBlue = decor.Where(s => Top(s) >= 560 && Top(s) < 710 && s.rect.x < 150).ToArray();
            p.crystalsGreen = decor.Where(s => Top(s) >= 560 && Top(s) < 710 && s.rect.x >= 150).ToArray();
            p.mushrooms = decor.Where(s => Top(s) >= 730).ToArray();

            foreach (var led in new[] { "LedA", "LedB", "LedC", "LedD" })
            {
                var sprites = AssetDatabase.LoadAllAssetsAtPath($"{LedFolder}/{led}.png").OfType<Sprite>().ToDictionary(s => s.name);
                for (int start = 0; start < 18; start += 6)
                {
                    var frames = Enumerable.Range(start, 6).Select(i => sprites.TryGetValue($"{led}_{i}", out var s) ? s : null).ToArray();
                    if (frames.All(f => f != null)) p.plants.Add(frames);
                }
            }

            p.bonfire = LoadFireFrames("fire asset red", "Group 4 - 2");
            p.smallFire = LoadFireFrames("fire asset red", "Group 4 - 3");
            p.blueFire = LoadFireFrames("fire asset blue", "Group 4 - 5");

            // 기존 불 FX 텍스처도 후광이 보이도록 Full Rect 메시로
            foreach (var path in new[] { "Assets/TK/FX/AreaWeapons/FireBreath.png", "Assets/TK/FX/Fireball/fireball.png" })
                SetFullRect(path);

            log.AppendLine($"Palette: rocks {p.rocksSmall.Length}+{p.rocksBig.Length}, crystals {p.crystalsBlue.Length}/{p.crystalsGreen.Length}, mushrooms {p.mushrooms.Length}, plants {p.plants.Count}, fire frames {p.bonfire.Length}/{p.smallFire.Length}/{p.blueFire.Length}");
            return p;
        }

        public static Sprite[] LoadFireFrames(string color, string group)
        {
            var frames = new List<Sprite>();
            for (int i = 1; i <= 8; i++)
            {
                string path = $"{FireRoot}/{color}/{group}/{group}_{i}.png";
                if (!File.Exists(path)) continue;
                ConfigurePixelImport(path, ti =>
                {
                    ti.spriteImportMode = SpriteImportMode.Single;
                    var s = new TextureImporterSettings();
                    ti.ReadTextureSettings(s);
                    s.spriteMeshType = SpriteMeshType.FullRect;
                    s.spriteAlignment = (int)SpriteAlignment.BottomCenter;
                    ti.SetTextureSettings(s);
                });
                var sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
                if (sprite) frames.Add(sprite);
            }
            return frames.ToArray();
        }

        public static void ConfigurePixelImport(string path, System.Action<TextureImporter> extra)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            ti.textureType = TextureImporterType.Sprite;
            ti.spritePixelsPerUnit = PPU;
            ti.filterMode = FilterMode.Point;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.mipmapEnabled = false;
            extra?.Invoke(ti);
            ti.SaveAndReimport();
        }

        public static void SetFullRect(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            if (s.spriteMeshType == SpriteMeshType.FullRect) return;
            s.spriteMeshType = SpriteMeshType.FullRect;
            ti.SetTextureSettings(s);
            ti.SaveAndReimport();
        }

        // ------------------------------------------------------------------ Materials

        static Mats CreateMaterials()
        {
            var pixel = Shader.Find("TK/PixelSprite");
            var glow = Shader.Find("TK/PixelGlow");
            var m = new Mats
            {
                character = LoadOrCreate($"{MaterialFolder}/PixelCharacter.mat", pixel),
                background = LoadOrCreate($"{MaterialFolder}/PixelBackground.mat", pixel),
                decor = LoadOrCreate($"{MaterialFolder}/PixelDecor.mat", pixel),
                fire = LoadOrCreate($"{MaterialFolder}/FireGlow.mat", glow),
                fireBlue = LoadOrCreate($"{MaterialFolder}/FireGlowBlue.mat", glow),
                fireFx = LoadOrCreate($"{MaterialFolder}/FireFXGlow.mat", glow),
            };

            // 캐릭터: 자체 발광 + 채도/대비 업 + 어두운 외곽선 → 배경보다 돋보임
            Set(m.character, ("_Unlit", 0.55f), ("_Saturation", 1.15f), ("_Contrast", 1.1f), ("_Brightness", 0.02f), ("_PixelSnap", 1f), ("_PixelsPerUnit", PPU), ("_Outline", 1f), ("_OutlineThickness", 1f));
            m.character.SetColor("_OutlineColor", new Color(0.06f, 0.03f, 0.05f, 1f));
            Keyword(m.character, "_PIXEL_OUTLINE", true);
            Keyword(m.character, "_PIXEL_SNAP", true);

            // 배경/장식: 살짝 채도·대비를 낮춰 캐릭터와 분리
            Set(m.background, ("_Unlit", 0f), ("_Saturation", 0.85f), ("_Contrast", 0.95f), ("_Brightness", -0.02f), ("_PixelSnap", 1f), ("_PixelsPerUnit", PPU), ("_Outline", 0f));
            Keyword(m.background, "_PIXEL_OUTLINE", false);
            Keyword(m.background, "_PIXEL_SNAP", true);

            Set(m.decor, ("_Unlit", 0f), ("_Saturation", 0.9f), ("_Contrast", 1f), ("_Brightness", 0f), ("_PixelSnap", 1f), ("_PixelsPerUnit", PPU), ("_Outline", 0f));
            Keyword(m.decor, "_PIXEL_OUTLINE", false);
            Keyword(m.decor, "_PIXEL_SNAP", true);

            Set(m.fire, ("_Intensity", 1.7f), ("_HaloRadius", 3f), ("_HaloStrength", 1.0f), ("_FlickerSpeed", 9f), ("_FlickerAmount", 0.18f));
            m.fire.SetColor("_GlowColor", new Color(2.2f, 0.85f, 0.25f, 1f));
            Set(m.fireBlue, ("_Intensity", 1.6f), ("_HaloRadius", 3f), ("_HaloStrength", 1.0f), ("_FlickerSpeed", 7f), ("_FlickerAmount", 0.15f));
            m.fireBlue.SetColor("_GlowColor", new Color(0.35f, 1.0f, 2.4f, 1f));
            Set(m.fireFx, ("_Intensity", 1.5f), ("_HaloRadius", 2.5f), ("_HaloStrength", 0.9f), ("_FlickerSpeed", 12f), ("_FlickerAmount", 0.12f));
            m.fireFx.SetColor("_GlowColor", new Color(2.0f, 0.7f, 0.2f, 1f));

            foreach (var mat in new[] { m.character, m.background, m.decor, m.fire, m.fireBlue, m.fireFx })
                EditorUtility.SetDirty(mat);
            return m;
        }

        static Material LoadOrCreate(string path, Shader shader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader) mat.shader = shader;
            return mat;
        }

        static void Set(Material m, params (string name, float value)[] values)
        {
            foreach (var (n, v) in values) if (m.HasProperty(n)) m.SetFloat(n, v);
        }

        static void Keyword(Material m, string kw, bool on)
        {
            if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw);
        }

        // ------------------------------------------------------------------ Ground

        static Dictionary<int, Tile> CreateGroundTiles()
        {
            Directory.CreateDirectory(GroundTileFolder);
            var sprites = AssetDatabase.LoadAllAssetsAtPath(TilesetPath).OfType<Sprite>().ToDictionary(s => s.name);
            var result = new Dictionary<int, Tile>();
            foreach (int i in PlainSand.Concat(LightCracks).Concat(Pebbles).Concat(HeavyCracks).Concat(RockTiles).Distinct())
            {
                if (!sprites.TryGetValue($"IMG_1336_{i}", out var sprite)) continue;
                string path = $"{GroundTileFolder}/Ground_{i:00}.asset";
                var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
                if (tile == null)
                {
                    tile = ScriptableObject.CreateInstance<Tile>();
                    AssetDatabase.CreateAsset(tile, path);
                }
                tile.sprite = sprite;
                tile.colliderType = Tile.ColliderType.Grid; // 기존 바닥과 동일하게 전체 칸 트리거 유지
                EditorUtility.SetDirty(tile);
                result[i] = tile;
            }
            return result;
        }

        static void PaintGround(Tilemap tm, Dictionary<int, Tile> tiles, int index, Material mat)
        {
            tm.CompressBounds();
            var bounds = tm.cellBounds;
            if (bounds.size.x <= 0) bounds = new BoundsInt(10, -20, 0, 30, 30, 1);

            var rng = new System.Random(4021 + index * 7919);
            float ox = index * 37.3f, oy = index * 91.7f;
            Tile Pick(int[] set) => tiles[set[rng.Next(set.Length)]];

            tm.ClearAllTiles();
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                float n = Mathf.PerlinNoise((x + ox) * 0.11f, (y + oy) * 0.11f);
                double r = rng.NextDouble();
                Tile t;
                if (n > 0.70f && r < 0.45) t = Pick(HeavyCracks);        // 갈라진 땅 지대
                else if (n > 0.58f && r < 0.40) t = Pick(LightCracks);
                else if (r < 0.05) t = Pick(Pebbles);                    // 드문드문 자갈
                else if (r < 0.056) t = Pick(RockTiles);                 // 아주 드물게 바위
                else t = Pick(PlainSand);
                tm.SetTile(new Vector3Int(x, y, 0), t);
            }
            tm.CompressBounds();

            var tr = tm.GetComponent<TilemapRenderer>();
            tr.sharedMaterial = mat;
            tr.sortingOrder = OrderGround;
            EditorUtility.SetDirty(tm);
            EditorUtility.SetDirty(tr);
        }

        // ------------------------------------------------------------------ Decor

        class Placer
        {
            public readonly List<(Vector2 p, float r)> taken = new List<(Vector2, float)>();
            public bool Free(Vector2 p, float r) => taken.All(t => (t.p - p).sqrMagnitude >= (t.r + r) * (t.r + r));
            public void Take(Vector2 p, float r) => taken.Add((p, r));
        }

        static void PlaceDecor(List<Tilemap> tilemaps, Palette pal, Mats mats, System.Text.StringBuilder log)
        {
            // 타일맵별 Decor 루트(타일맵 자식 → Reposition 이 바닥과 함께 반복 배치)
            var roots = new Dictionary<Tilemap, Transform>();
            var rects = new Dictionary<Tilemap, Rect>();
            foreach (var tm in tilemaps)
            {
                var old = tm.transform.Find(DecorRootName);
                if (old) Object.DestroyImmediate(old.gameObject);
                var root = new GameObject(DecorRootName).transform;
                root.SetParent(tm.transform, false);
                roots[tm] = root;

                var b = tm.localBounds;
                Vector3 min = tm.transform.TransformPoint(b.min), max = tm.transform.TransformPoint(b.max);
                rects[tm] = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }

            Transform RootAt(Vector2 world)
            {
                foreach (var kv in rects) if (kv.Value.Contains(world)) return roots[kv.Key];
                return roots[tilemaps[0]];
            }

            var placer = new Placer();
            var rng = new System.Random(77);
            int count = 0;

            // 1) 게임 시작 화면(플레이어 주변)에 바로 보이는 연출 배치
            placer.Take(Vector2.zero, 0.45f); // 플레이어 시작 위치는 비워 둠
            count += Bonfire(RootAt(new Vector2(0.62f, 0.95f)), new Vector2(0.62f, 0.95f), pal, mats, rng, placer);
            count += CrystalShrine(RootAt(new Vector2(-0.62f, -1.15f)), new Vector2(-0.62f, -1.15f), pal, mats, rng, placer, true);
            count += MushroomPatch(RootAt(new Vector2(-0.68f, 0.62f)), new Vector2(-0.68f, 0.62f), pal, mats, rng, placer);
            count += PlantClump(RootAt(new Vector2(0.66f, -0.55f)), new Vector2(0.66f, -0.55f), pal, mats, rng, placer);
            count += SmallTorch(RootAt(new Vector2(-0.2f, 1.55f)), new Vector2(-0.2f, 1.55f), pal, mats, rng, placer);

            // 2) 반복되는 2x2 블록 전체에 무작위 배치
            foreach (var tm in tilemaps)
            {
                var rect = rects[tm];
                var root = roots[tm];
                var inner = Rect.MinMaxRect(rect.xMin + 0.5f, rect.yMin + 0.5f, rect.xMax - 0.5f, rect.yMax - 0.5f);

                count += Scatter(inner, 1, 0.75f, rng, placer, p => BigRock(root, p, pal, mats, rng, placer));
                count += Scatter(inner, 5, 0.35f, rng, placer, p => SmallRock(root, p, pal, mats, rng, placer));
                count += Scatter(inner, 1, 0.6f, rng, placer, p => Bonfire(root, p, pal, mats, rng, placer));
                count += Scatter(inner, 2, 0.35f, rng, placer, p => SmallTorch(root, p, pal, mats, rng, placer));
                count += Scatter(inner, 1, 0.5f, rng, placer, p => CrystalShrine(root, p, pal, mats, rng, placer, true));
                count += Scatter(inner, 2, 0.4f, rng, placer, p => CrystalShrine(root, p, pal, mats, rng, placer, false));
                count += Scatter(inner, 5, 0.35f, rng, placer, p => MushroomPatch(root, p, pal, mats, rng, placer));
                count += Scatter(inner, 4, 0.3f, rng, placer, p => PlantClump(root, p, pal, mats, rng, placer));
            }
            log.AppendLine("Decor objects placed: " + count);
        }

        static int Scatter(Rect area, int amount, float radius, System.Random rng, Placer placer, System.Func<Vector2, int> make)
        {
            int made = 0;
            for (int i = 0; i < amount; i++)
            {
                for (int tries = 0; tries < 60; tries++)
                {
                    var p = new Vector2(Mathf.Lerp(area.xMin, area.xMax, (float)rng.NextDouble()),
                                        Mathf.Lerp(area.yMin, area.yMax, (float)rng.NextDouble()));
                    if (!placer.Free(p, radius)) continue;
                    made += make(p);
                    break;
                }
            }
            return made;
        }

        static Vector3 Snap(Vector2 p) => new Vector3(Mathf.Round(p.x * PPU) / PPU, Mathf.Round(p.y * PPU) / PPU, 0f);

        static GameObject MakeSprite(Transform parent, string name, Vector2 world, Sprite sprite, Material mat, int order, bool flip = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = Snap(world);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = mat;
            sr.sortingOrder = order;
            sr.flipX = flip;
            return go;
        }

        static T R<T>(T[] arr, System.Random rng) => arr[rng.Next(arr.Length)];
        static Vector2 Jitter(System.Random rng, float r) => new Vector2((float)(rng.NextDouble() * 2 - 1) * r, (float)(rng.NextDouble() * 2 - 1) * r);

        static int BigRock(Transform root, Vector2 p, Palette pal, Mats m, System.Random rng, Placer placer)
        {
            placer.Take(p, 0.75f);
            MakeSprite(root, "Rock_Big", p, R(pal.rocksBig, rng), m.decor, OrderRock, rng.Next(2) == 0);
            return 1;
        }

        static int SmallRock(Transform root, Vector2 p, Palette pal, Mats m, System.Random rng, Placer placer)
        {
            placer.Take(p, 0.35f);
            MakeSprite(root, "Rock", p, R(pal.rocksSmall.Take(3).ToArray(), rng) /* 작은 3종 위주 */, m.decor, OrderRock, rng.Next(2) == 0);
            return 1;
        }

        static int MushroomPatch(Transform root, Vector2 p, Palette pal, Mats m, System.Random rng, Placer placer)
        {
            placer.Take(p, 0.35f);
            int n = 3 + rng.Next(4);
            var group = new GameObject("Mushrooms").transform;
            group.SetParent(root, false);
            group.position = Snap(p);
            for (int i = 0; i < n; i++)
                MakeSprite(group, "Mushroom", p + Jitter(rng, 0.25f), R(pal.mushrooms, rng), m.decor, OrderFlatDecor, rng.Next(2) == 0);
            return 1;
        }

        static int PlantClump(Transform root, Vector2 p, Palette pal, Mats m, System.Random rng, Placer placer)
        {
            if (pal.plants.Count == 0) return 0;
            placer.Take(p, 0.3f);
            int n = 2 + rng.Next(2);
            var group = new GameObject("Plants").transform;
            group.SetParent(root, false);
            group.position = Snap(p);
            var frames = pal.plants[rng.Next(pal.plants.Count)];
            for (int i = 0; i < n; i++)
            {
                var go = MakeSprite(group, "Plant", p + Jitter(rng, 0.2f), frames[0], m.decor, OrderFlatDecor, rng.Next(2) == 0);
                var fb = go.AddComponent<SpriteFlipbook>();
                fb.frames = frames;
                fb.fps = 5f;
            }
            return 1;
        }

        static int CrystalShrine(Transform root, Vector2 p, Palette pal, Mats m, System.Random rng, Placer placer, bool withBlueFire)
        {
            placer.Take(p, withBlueFire ? 0.5f : 0.4f);
            bool blue = withBlueFire || rng.Next(2) == 0;
            var set = blue ? pal.crystalsBlue : pal.crystalsGreen;
            var group = new GameObject(withBlueFire ? "CrystalShrine" : "Crystals").transform;
            group.SetParent(root, false);
            group.position = Snap(p);
            int n = 3 + rng.Next(3);
            for (int i = 0; i < n; i++)
            {
                var dir = Quaternion.Euler(0, 0, i * 360f / n + rng.Next(30)) * Vector2.right;
                MakeSprite(group, "Crystal", p + (Vector2)(dir * (withBlueFire ? 0.28f : 0.18f)) + Jitter(rng, 0.05f), R(set, rng), m.decor, OrderFlatDecor);
            }
            if (withBlueFire && pal.blueFire.Length > 0)
                Fire(group, "BlueFire", p, pal.blueFire, m.fireBlue, new Color(0.45f, 0.75f, 1f), 0.9f, 1.15f, 8f);
            return 1;
        }

        static int Bonfire(Transform root, Vector2 p, Palette pal, Mats m, System.Random rng, Placer placer)
        {
            if (pal.bonfire.Length == 0) return 0;
            placer.Take(p, 0.6f);
            var group = new GameObject("Bonfire").transform;
            group.SetParent(root, false);
            group.position = Snap(p);
            // 모닥불 양옆의 돌
            MakeSprite(group, "Stone", p + new Vector2(-0.3f, 0.05f), pal.rocksSmall[2], m.decor, OrderRock);
            MakeSprite(group, "Stone", p + new Vector2(0.32f, 0.02f), pal.rocksSmall[3], m.decor, OrderRock, true);
            Fire(group, "Fire", p + new Vector2(0f, -0.12f), pal.bonfire, m.fire, new Color(1f, 0.58f, 0.25f), 1.25f, 1.7f, 10f);
            return 1;
        }

        static int SmallTorch(Transform root, Vector2 p, Palette pal, Mats m, System.Random rng, Placer placer)
        {
            if (pal.smallFire.Length == 0) return 0;
            placer.Take(p, 0.35f);
            var group = new GameObject("FireStone").transform;
            group.SetParent(root, false);
            group.position = Snap(p);
            MakeSprite(group, "Stone", p + new Vector2(0f, -0.08f), pal.rocksSmall[2], m.decor, OrderRock);
            Fire(group, "Fire", p + new Vector2(0f, 0.08f), pal.smallFire, m.fire, new Color(1f, 0.6f, 0.3f), 0.85f, 0.95f, 11f);
            return 1;
        }

        static void Fire(Transform parent, string name, Vector2 p, Sprite[] frames, Material mat, Color lightColor, float intensity, float radius, float fps)
        {
            var go = MakeSprite(parent, name, p, frames[0], mat, OrderFire);
            var fb = go.AddComponent<SpriteFlipbook>();
            fb.frames = frames;
            fb.fps = fps;
            AddGlowLight(go.transform, new Vector3(0f, 0.2f, 0f), lightColor, intensity, radius);
        }

        public static void AddGlowLight(Transform parent, Vector3 localPos, Color color, float intensity, float radius)
        {
            var old = parent.Find(GlowLightName);
            if (old) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject(GlowLightName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var l = go.AddComponent<Light2D>();
            l.lightType = Light2D.LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.pointLightInnerRadius = radius * 0.08f;
            l.pointLightOuterRadius = radius;
            l.falloffIntensity = 0.65f;
            ApplyToAllSortingLayers(l);
            EnableNormals(l, 0.6f);
            go.AddComponent<LightFlicker>();
        }

        /// <summary>라이트가 스프라이트 노멀맵(_NormalMap)을 사용하도록 (Accurate)</summary>
        public static void EnableNormals(Light2D light, float distance)
        {
            var so = new SerializedObject(light);
            var q = so.FindProperty("m_NormalMapQuality");
            if (q != null) q.intValue = 1; // 0 Fast, 1 Accurate, 2 Disabled
            var d = so.FindProperty("m_NormalMapDistance");
            if (d != null) d.floatValue = distance;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void ApplyToAllSortingLayers(Light2D light)
        {
            var so = new SerializedObject(light);
            var prop = so.FindProperty("m_ApplyToSortingLayers");
            if (prop == null) return;
            var layers = SortingLayer.layers;
            prop.arraySize = layers.Length;
            for (int i = 0; i < layers.Length; i++) prop.GetArrayElementAtIndex(i).intValue = layers[i].id;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ Lighting / Post FX

        static void SetupLightingAndPostFx(Scene scene, System.Text.StringBuilder log)
        {
            var all = scene.GetRootGameObjects();

            // 전역 조명을 낮춰 불빛과 캐릭터가 대비되게
            foreach (var l in all.SelectMany(g => g.GetComponentsInChildren<Light2D>(true)).Where(l => l.lightType == Light2D.LightType.Global))
            {
                l.intensity = 0.62f;
                l.color = new Color(0.82f, 0.86f, 1f);
                EditorUtility.SetDirty(l);
            }

            // 플레이어 주변을 은은하게 비추는 조명
            var player = all.SelectMany(g => g.GetComponentsInChildren<Player>(true)).FirstOrDefault();
            if (player)
            {
                var old = player.transform.Find(PlayerLightName);
                if (old) Object.DestroyImmediate(old.gameObject);
                var go = new GameObject(PlayerLightName);
                go.transform.SetParent(player.transform, false);
                var l = go.AddComponent<Light2D>();
                l.lightType = Light2D.LightType.Point;
                l.color = new Color(1f, 0.95f, 0.85f);
                l.intensity = 0.55f;
                l.pointLightInnerRadius = 0.15f;
                l.pointLightOuterRadius = 1.4f;
                l.falloffIntensity = 0.7f;
                ApplyToAllSortingLayers(l);
            }

            // 카메라 포스트 프로세싱 + Bloom 볼륨
            foreach (var cam in all.SelectMany(g => g.GetComponentsInChildren<Camera>(true)))
            {
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (data) { data.renderPostProcessing = true; EditorUtility.SetDirty(data); }
                cam.allowHDR = true;
            }

            var profile = CreatePostFxProfile();
            foreach (var g in all.Where(g => g.name == VolumeName)) Object.DestroyImmediate(g);
            var vgo = new GameObject(VolumeName);
            SceneManager.MoveGameObjectToScene(vgo, scene);
            var vol = vgo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 1;
            vol.sharedProfile = profile;
            log.AppendLine("Lighting + post FX configured");
        }

        static VolumeProfile CreatePostFxProfile()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PostFxPath));
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostFxPath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, PostFxPath);
            }

            T Get<T>() where T : VolumeComponent
            {
                if (!profile.TryGet(out T c))
                {
                    c = profile.Add<T>(true);
                    c.name = typeof(T).Name;
                    AssetDatabase.AddObjectToAsset(c, profile);
                }
                return c;
            }

            var bloom = Get<Bloom>();
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(1.4f);
            bloom.scatter.Override(0.6f);
            bloom.highQualityFiltering.Override(false);

            var vignette = Get<Vignette>();
            vignette.intensity.Override(0.3f);
            vignette.smoothness.Override(0.45f);

            var color = Get<ColorAdjustments>();
            color.contrast.Override(8f);
            color.saturation.Override(6f);

            EditorUtility.SetDirty(profile);
            return profile;
        }

        // ------------------------------------------------------------------ Fire FX prefabs

        static void UpgradeFireFxPrefabs(Mats m, System.Text.StringBuilder log)
        {
            var targets = new (string path, float intensity, float radius)[]
            {
                ("Assets/TK/FX/AreaWeapons/FireBreath.prefab", 1.0f, 1.3f),
                ("Assets/Undead Survivor/Prefabs/Bullet 1.prefab", 0.7f, 0.6f), // fireball 탄
            };
            foreach (var (path, intensity, radius) in targets)
            {
                if (!File.Exists(path)) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
                    sr.sharedMaterial = m.fireFx;
                AddGlowLight(root.transform, Vector3.zero, new Color(1f, 0.55f, 0.2f), intensity, radius);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                log.AppendLine("Fire FX glow: " + path);
            }
        }
    }
}
