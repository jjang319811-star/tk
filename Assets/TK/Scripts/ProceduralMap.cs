using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.Pool;

/// <summary>
/// 무한 절차적 생성 맵 (청크 스트리밍).
///  - 바닥: tile_01 의 32px 매크로 타일을 셀 해시 + 균열 노이즈로 배치 (같은 좌표는 항상 같은 결과)
///  - 지형 블렌드: 카메라를 덮는 사각형 1장(PixelBlend 셰이더)이 동굴 흙/이끼/꽃밭 지형을 디더링으로 섞음
///  - 장식: 청크마다 결정론적으로 생성, 블렌드 지형(biome)에 맞는 장식 선택 (흙=바위·불, 이끼=식물·버섯, 꽃밭=크리스탈·푸른 불)
///  - 최적화: 시야+여유 범위 청크만 유지, 프레임당 로드 개수 제한, 장식/라이트 오브젝트 풀링, 바닥 콜라이더 없음
/// </summary>
[DefaultExecutionOrder(102)]
public class ProceduralMap : MonoBehaviour
{
    public Tilemap ground;
    public SpriteRenderer blendQuad;
    public ProceduralMapPalette palette;
    [Tooltip("청크 한 변의 셀 수")]
    public int chunkCells = 8;
    [Tooltip("카메라 시야 밖으로 미리 로드할 여유(유닛)")]
    public float loadMargin = 2f;
    public int maxChunkLoadsPerFrame = 2;
    [Tooltip("균열 지대 노이즈 스케일(1/유닛)")]
    public float crackScale = 0.3f;
    [Range(0f, 2f)] public float decorDensity = 1f;
    [Tooltip("게임 시작 지점 주변은 비워 둠")]
    public float startClearRadius = 0.9f;

    public const int OrderGround = -10, OrderBlend = -9, OrderFlat = -8, OrderRock = -7, OrderFire = -6;

    class Chunk
    {
        public readonly List<GameObject> sprites = new List<GameObject>();
        public readonly List<GameObject> lights = new List<GameObject>();
    }

    enum Biome { Sand, Dirt, Moss, Flower }

    readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
    ObjectPool<GameObject> spritePool, lightPool; // Unity 내장 풀
    readonly List<Vector2Int> scratch = new List<Vector2Int>();
    readonly List<(Vector2 p, float r)> placed = new List<(Vector2, float)>();
    readonly List<Sprite[]> plantAnims = new List<Sprite[]>();

    TileBase[] block, empty;
    Transform decorRoot;
    float cellSize, chunkWorld;
    uint seed;
    float scaleA, coverA, scaleB, coverB, scaleC, coverC, edge;

    public int LoadedChunks => chunks.Count;
    public int ActiveDecor { get; private set; }

    void Awake()
    {
        cellSize = ground.layoutGrid.cellSize.x;
        chunkWorld = chunkCells * cellSize;
        block = new TileBase[chunkCells * chunkCells];
        empty = new TileBase[block.Length];
        decorRoot = new GameObject("Decor (Procedural)").transform;
        decorRoot.SetParent(transform, false);
        spritePool = new ObjectPool<GameObject>(() =>
        {
            var go = new GameObject("Decor", typeof(SpriteRenderer), typeof(SpriteFlipbook));
            go.transform.SetParent(decorRoot, false);
            return go;
        }, go => go.SetActive(true), go => go.SetActive(false), null, false, 128, 1024);
        lightPool = new ObjectPool<GameObject>(() => Instantiate(palette.fireLightPrefab, decorRoot),
            go => go.SetActive(true), go => go.SetActive(false), null, false, 16, 128);

        // 셰이더와 같은 파라미터로 biome 판정
        var m = blendQuad ? blendQuad.sharedMaterial : null;
        if (m)
        {
            seed = (uint)Mathf.RoundToInt(m.GetFloat("_Seed"));
            scaleA = m.GetFloat("_ScaleA"); coverA = m.GetFloat("_CoverA");
            scaleB = m.GetFloat("_ScaleB"); coverB = m.GetFloat("_CoverB");
            scaleC = m.GetFloat("_ScaleC"); coverC = m.GetFloat("_CoverC");
            edge = 0.01f; // biome 판정은 경계 안/밖(0.5)만 보면 됨
        }

        if (palette && palette.plantFrames != null)
            for (int i = 0; i + palette.plantFrameCount <= palette.plantFrames.Length; i += palette.plantFrameCount)
            {
                var anim = new Sprite[palette.plantFrameCount];
                System.Array.Copy(palette.plantFrames, i, anim, 0, anim.Length);
                plantAnims.Add(anim);
            }
    }

    void Start()
    {
        ground.ClearAllTiles();
        Refresh(int.MaxValue);
    }

    void LateUpdate() => Refresh(maxChunkLoadsPerFrame);

    void Refresh(int budget)
    {
        if (!CameraView.TryGetWorldRect(0f, loadMargin, out Rect view))
            return;
        UpdateBlendQuad(view);

        int minX = Mathf.FloorToInt(view.xMin / chunkWorld), maxX = Mathf.FloorToInt(view.xMax / chunkWorld);
        int minY = Mathf.FloorToInt(view.yMin / chunkWorld), maxY = Mathf.FloorToInt(view.yMax / chunkWorld);

        // 1칸 여유(히스테리시스)를 두고 해제
        scratch.Clear();
        foreach (var key in chunks.Keys)
            if (key.x < minX - 1 || key.x > maxX + 1 || key.y < minY - 1 || key.y > maxY + 1)
                scratch.Add(key);
        foreach (var key in scratch) Unload(key);

        scratch.Clear();
        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            {
                var key = new Vector2Int(x, y);
                if (!chunks.ContainsKey(key)) scratch.Add(key);
            }
        Vector2 center = view.center / chunkWorld;
        scratch.Sort((a, b) => (a - center).sqrMagnitude.CompareTo((b - center).sqrMagnitude));
        for (int i = 0; i < scratch.Count && i < budget; i++) Load(scratch[i]);
    }

    void UpdateBlendQuad(Rect view)
    {
        if (!blendQuad || !blendQuad.sprite) return;
        Vector2 size = blendQuad.sprite.bounds.size;
        Vector2 c = view.center;
        blendQuad.transform.position = new Vector3(Mathf.Round(c.x * 64f) / 64f, Mathf.Round(c.y * 64f) / 64f, 0f);
        blendQuad.transform.localScale = new Vector3(view.width / size.x, view.height / size.y, 1f);
    }

    // ------------------------------------------------------------------ chunk load / unload

    void Load(Vector2Int key)
    {
        var chunk = new Chunk();
        chunks.Add(key, chunk);
        int x0 = key.x * chunkCells, y0 = key.y * chunkCells;

        for (int j = 0; j < chunkCells; j++)
            for (int i = 0; i < chunkCells; i++)
                block[j * chunkCells + i] = PickTile(x0 + i, y0 + j);
        ground.SetTilesBlock(new BoundsInt(x0, y0, 0, chunkCells, chunkCells, 1), block);

        if (palette) SpawnDecor(key, chunk);
    }

    void Unload(Vector2Int key)
    {
        var chunk = chunks[key];
        int x0 = key.x * chunkCells, y0 = key.y * chunkCells;
        ground.SetTilesBlock(new BoundsInt(x0, y0, 0, chunkCells, chunkCells, 1), empty);
        foreach (var go in chunk.sprites) spritePool.Release(go);
        foreach (var go in chunk.lights) lightPool.Release(go);
        ActiveDecor -= chunk.sprites.Count;
        chunks.Remove(key);
    }

    TileBase PickTile(int x, int y)
    {
        Vector2 w = new Vector2((x + 0.5f) * cellSize, (y + 0.5f) * cellSize);
        float crack = ProceduralNoise.Fbm(w * crackScale, seed + 5u);
        float h = ProceduralNoise.Hash(x, y, seed + 11u);
        float pick = ProceduralNoise.Hash(x, y, seed + 23u);
        TileBase[] set;
        // 기본은 차분한 모래, 균열은 노이즈가 높은 '지대'에서만 드문드문
        if (crack > 0.7f && h < 0.4f) set = palette.heavyTiles;
        else if ((crack > 0.6f && h < 0.35f) || h < 0.04f) set = palette.lightTiles;
        else set = palette.plainTiles;
        if (set == null || set.Length == 0) set = palette.plainTiles;
        return set[Mathf.Min((int)(pick * set.Length), set.Length - 1)];
    }

    Biome BiomeAt(Vector2 p)
    {
        if (ProceduralNoise.Layer(p, scaleC, coverC, 3u, seed, edge) > 0.5f) return Biome.Flower;
        if (ProceduralNoise.Layer(p, scaleB, coverB, 2u, seed, edge) > 0.5f) return Biome.Moss;
        if (ProceduralNoise.Layer(p, scaleA, coverA, 1u, seed, edge) > 0.5f) return Biome.Dirt;
        return Biome.Sand;
    }

    // ------------------------------------------------------------------ decor

    void SpawnDecor(Vector2Int key, Chunk chunk)
    {
        var rng = new System.Random(unchecked((int)(ProceduralNoise.Hash(key.x, key.y, seed + 77u) * 2147483000f)));
        Vector2 min = (Vector2)key * chunkWorld;
        placed.Clear();

        // 시작 지점 연출: 첫 화면에 바로 보이는 장식 세트
        if (Contains(min, new Vector2(1.0f, 1.4f))) Bonfire(chunk, new Vector2(1.0f, 1.4f), rng);
        if (Contains(min, new Vector2(-1.0f, -1.6f))) CrystalShrine(chunk, new Vector2(-1.0f, -1.6f), rng, true);
        if (Contains(min, new Vector2(-1.1f, 0.9f))) Mushrooms(chunk, new Vector2(-1.1f, 0.9f), rng);
        if (Contains(min, new Vector2(1.1f, -0.9f))) Plants(chunk, new Vector2(1.1f, -0.9f), rng);
        if (Contains(min, new Vector2(-0.3f, 2.2f))) FireStone(chunk, new Vector2(-0.3f, 2.2f), rng);

        int attempts = Mathf.RoundToInt(8 * decorDensity);
        for (int i = 0; i < attempts; i++)
        {
            var p = min + new Vector2((float)rng.NextDouble(), (float)rng.NextDouble()) * chunkWorld;
            if (p.magnitude < startClearRadius + 1.4f) continue;
            double r = rng.NextDouble();
            switch (BiomeAt(p))
            {
                case Biome.Flower:
                    if (r < 0.25) CrystalShrine(chunk, p, rng, true);
                    else if (r < 0.65) Plants(chunk, p, rng);
                    else Crystals(chunk, p, rng, true);
                    break;
                case Biome.Moss:
                    if (r < 0.4) Plants(chunk, p, rng);
                    else if (r < 0.7) Mushrooms(chunk, p, rng);
                    else Crystals(chunk, p, rng, false);
                    break;
                case Biome.Dirt:
                    if (r < 0.4) Rock(chunk, p, rng, false);
                    else if (r < 0.5) Rock(chunk, p, rng, true);
                    else if (r < 0.66) FireStone(chunk, p, rng);
                    else if (r < 0.74) Bonfire(chunk, p, rng);
                    else Mushrooms(chunk, p, rng);
                    break;
                default:
                    if (r < 0.14) Rock(chunk, p, rng, false);
                    else if (r < 0.26) Mushrooms(chunk, p, rng);
                    break;
            }
        }
    }

    bool Contains(Vector2 min, Vector2 p) =>
        p.x >= min.x && p.x < min.x + chunkWorld && p.y >= min.y && p.y < min.y + chunkWorld;

    bool Reserve(Vector2 p, float r)
    {
        foreach (var t in placed)
            if ((t.p - p).sqrMagnitude < (t.r + r) * (t.r + r)) return false;
        placed.Add((p, r));
        return true;
    }

    static T Pick<T>(T[] arr, System.Random rng) => arr[rng.Next(arr.Length)];
    static Vector2 Jitter(System.Random rng, float r) => new Vector2((float)(rng.NextDouble() * 2 - 1) * r, (float)(rng.NextDouble() * 2 - 1) * r);

    void Rock(Chunk c, Vector2 p, System.Random rng, bool big)
    {
        if (!Reserve(p, big ? 0.8f : 0.4f)) return;
        Sprite(c, p, big ? Pick(palette.rocksBig, rng) : Pick(palette.rocksSmall, rng), palette.decorMaterial, OrderRock, rng.Next(2) == 0, null);
    }

    void Mushrooms(Chunk c, Vector2 p, System.Random rng)
    {
        if (!Reserve(p, 0.4f)) return;
        int n = 3 + rng.Next(4);
        for (int i = 0; i < n; i++)
            Sprite(c, p + Jitter(rng, 0.28f), Pick(palette.mushrooms, rng), palette.decorMaterial, OrderFlat, rng.Next(2) == 0, null);
    }

    void Plants(Chunk c, Vector2 p, System.Random rng)
    {
        if (plantAnims.Count == 0 || !Reserve(p, 0.35f)) return;
        var anim = plantAnims[rng.Next(plantAnims.Count)];
        int n = 2 + rng.Next(2);
        for (int i = 0; i < n; i++)
            Sprite(c, p + Jitter(rng, 0.22f), anim[0], palette.decorMaterial, OrderFlat, rng.Next(2) == 0, anim, 5f);
    }

    void Crystals(Chunk c, Vector2 p, System.Random rng, bool blue)
    {
        if (!Reserve(p, 0.4f)) return;
        var set = blue ? palette.crystalsBlue : palette.crystalsGreen;
        int n = 3 + rng.Next(3);
        for (int i = 0; i < n; i++)
            Sprite(c, p + (Vector2)(Quaternion.Euler(0, 0, i * 360f / n + rng.Next(30)) * Vector2.right) * 0.2f, Pick(set, rng), palette.decorMaterial, OrderFlat, false, null);
    }

    void CrystalShrine(Chunk c, Vector2 p, System.Random rng, bool withFire)
    {
        if (!Reserve(p, 0.55f)) return;
        int n = 4 + rng.Next(2);
        for (int i = 0; i < n; i++)
            Sprite(c, p + (Vector2)(Quaternion.Euler(0, 0, i * 360f / n) * Vector2.right) * 0.32f, Pick(palette.crystalsBlue, rng), palette.decorMaterial, OrderFlat, false, null);
        if (withFire && palette.blueFireFrames.Length > 0)
            Fire(c, p, palette.blueFireFrames, palette.fireBlueMaterial, new Color(0.45f, 0.75f, 1f), 0.9f, 1.3f, 8f);
    }

    void Bonfire(Chunk c, Vector2 p, System.Random rng)
    {
        if (palette.bonfireFrames.Length == 0 || !Reserve(p, 0.7f)) return;
        Sprite(c, p + new Vector2(-0.32f, 0.04f), palette.rocksSmall[0], palette.decorMaterial, OrderRock, false, null);
        Sprite(c, p + new Vector2(0.34f, 0.02f), palette.rocksSmall[Mathf.Min(1, palette.rocksSmall.Length - 1)], palette.decorMaterial, OrderRock, true, null);
        Fire(c, p + new Vector2(0f, -0.14f), palette.bonfireFrames, palette.fireMaterial, new Color(1f, 0.58f, 0.25f), 1.25f, 2.0f, 10f);
    }

    void FireStone(Chunk c, Vector2 p, System.Random rng)
    {
        if (palette.smallFireFrames.Length == 0 || !Reserve(p, 0.4f)) return;
        Sprite(c, p + new Vector2(0f, -0.08f), palette.rocksSmall[0], palette.decorMaterial, OrderRock, rng.Next(2) == 0, null);
        Fire(c, p + new Vector2(0f, 0.1f), palette.smallFireFrames, palette.fireMaterial, new Color(1f, 0.6f, 0.3f), 0.85f, 1.15f, 11f);
    }

    void Fire(Chunk c, Vector2 p, Sprite[] frames, Material mat, Color lightColor, float intensity, float radius, float fps)
    {
        var go = Sprite(c, p, frames[0], mat, OrderFire, false, frames, fps);
        if (!palette.fireLightPrefab) return;
        var light = lightPool.Get();
        light.transform.position = go.transform.position + new Vector3(0f, 0.2f, 0f);
        var l2d = light.GetComponent<UnityEngine.Rendering.Universal.Light2D>();
        l2d.color = lightColor;
        l2d.intensity = intensity;
        l2d.pointLightOuterRadius = radius;
        var flicker = light.GetComponent<LightFlicker>();
        if (flicker) flicker.Rebase();
        c.lights.Add(light);

        // 뱀서의 광원: 무기로 맞히면 꺼지며 아이템 드롭
        var source = go.GetComponent<LightSource>();
        if (source == null)
        {
            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.18f;
            source = go.AddComponent<LightSource>();
        }
        source.ResetSource(light);
    }

    GameObject Sprite(Chunk c, Vector2 p, Sprite sprite, Material mat, int order, bool flip, Sprite[] frames, float fps = 10f)
    {
        GameObject go = spritePool.Get();
        // 풀에서 꺼낸 오브젝트가 예전에 불(광원)이었으면 광원 기능 끔
        var oldSource = go.GetComponent<LightSource>();
        if (oldSource) { oldSource.enabled = false; go.GetComponent<CircleCollider2D>().enabled = false; }
        go.transform.position = new Vector3(Mathf.Round(p.x * 64f) / 64f, Mathf.Round(p.y * 64f) / 64f, 0f);
        var sr = go.GetComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sharedMaterial = mat;
        sr.sortingOrder = order;
        sr.flipX = flip;
        sr.enabled = true;
        var fb = go.GetComponent<SpriteFlipbook>();
        fb.frames = frames;
        fb.fps = fps;
        fb.enabled = frames != null;
        go.SetActive(true);
        c.sprites.Add(go);
        ActiveDecor++;
        return go;
    }
}
