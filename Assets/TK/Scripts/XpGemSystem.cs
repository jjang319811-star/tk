using System.Collections.Generic;
using UnityEngine.Pool;
using UnityEngine;

/// <summary>
/// 경험치 보석 (뱀서 룰)
///  - 적 처치 시 보석 드롭 (가치에 따라 파랑 1 / 초록 2~9 / 빨강 10+)
///  - 자석 범위 안에 들어오면 살짝 튕겨 나갔다가 가속하며 플레이어에게 빨려옴
///  - 획득 시 경험치(가치만큼), 화면 위 보석이 너무 많으면 멀리 있는 것들을 빨간 보석 1개로 병합
///  - 자석 아이템(VacuumAll): 맵의 모든 보석을 끌어옴
/// </summary>
public class XpGemSystem : MonoBehaviour
{
    public static XpGemSystem I { get; private set; }

    public Sprite blueGem, greenGem, redGem;
    public Material gemMaterial;
    public float magnetRadius = 1.0f;
    public float pickupRadius = 0.22f;
    public int mergeThreshold = 220;

    class Gem
    {
        public Transform t;
        public SpriteRenderer sr;
        public int value;
        public bool attracted;
        public float speed;
        public float age;
    }

    readonly List<Gem> active = new List<Gem>();
    ObjectPool<Gem> pool;
    Transform root;

    public int ActiveCount => active.Count;

    void Awake()
    {
        I = this;
        root = new GameObject("XpGems").transform;
        pool = new ObjectPool<Gem>(CreateGem, g => g.t.gameObject.SetActive(true), g => g.t.gameObject.SetActive(false), null, false, 64, 512);
    }

    Gem CreateGem()
    {
        var go = new GameObject("Gem", typeof(SpriteRenderer));
        go.transform.SetParent(root, false);
        var g = new Gem { t = go.transform, sr = go.GetComponent<SpriteRenderer>() };
        g.sr.sharedMaterial = gemMaterial;
        g.sr.sortingOrder = -3;
        return g;
    }

    public static void Drop(Vector3 position, int value)
    {
        if (I == null) { for (int i = 0; i < value; i++) GameManager.instance.GetExp(); return; }
        I.Spawn(position, value);
    }

    Gem Spawn(Vector3 position, int value)
    {
        Gem g = pool.Get();
        g.value = value;
        g.attracted = false;
        g.speed = 0f;
        g.age = 0f;
        g.sr.sprite = value >= 10 ? redGem : value >= 2 ? greenGem : blueGem;
        g.t.position = Snap(position);
        g.t.gameObject.SetActive(true);
        active.Add(g);
        return g;
    }

    static Vector3 Snap(Vector3 p) => new Vector3(Mathf.Round(p.x * 64f) / 64f, Mathf.Round(p.y * 64f) / 64f, 0f);

    public void VacuumAll()
    {
        foreach (var g in active) { g.attracted = true; g.speed = -1.5f; }
    }

    void Update()
    {
        var gm = GameManager.instance;
        if (!gm.isLive || gm.player == null) return;
        Vector2 player = gm.player.transform.position;
        float dt = Time.deltaTime;
        float magnet2 = magnetRadius * magnetRadius;
        int collected = 0;

        for (int i = active.Count - 1; i >= 0; i--)
        {
            var g = active[i];
            g.age += dt;
            Vector2 pos = g.t.position;
            Vector2 to = player - pos;
            float d2 = to.sqrMagnitude;

            if (!g.attracted && d2 < magnet2)
            {
                g.attracted = true;
                g.speed = -1.2f; // 처음엔 살짝 밀려났다가
            }
            if (g.attracted)
            {
                g.speed += 9f * dt; // 가속하며 빨려옴
                pos += to.normalized * g.speed * dt;
                g.t.position = Snap(pos);
                if (to.magnitude < pickupRadius || (g.speed > 0f && Vector2.Dot(to, player - pos) < 0f))
                {
                    collected += g.value;
                    Recycle(i);
                    continue;
                }
            }
            else
            {
                // 제자리에서 반짝임
                g.sr.color = new Color(1f, 1f, 1f, 0.85f + 0.15f * Mathf.Sin(g.age * 6f + i));
            }
        }

        if (collected > 0)
        {
            gm.BeginDamageBatch();
            try { for (int k = 0; k < collected; k++) gm.GetExp(); }
            finally { gm.EndDamageBatch(); }
        }

        if (active.Count > mergeThreshold) MergeFar(player);
    }

    void Recycle(int index)
    {
        var g = active[index];
        active.RemoveAt(index);
        pool.Release(g);
    }

    // 뱀서처럼: 보석이 너무 많으면 플레이어에서 먼 보석들을 빨간 보석 하나로 합침
    void MergeFar(Vector2 player)
    {
        active.Sort((a, b) => ((Vector2)b.t.position - player).sqrMagnitude.CompareTo(((Vector2)a.t.position - player).sqrMagnitude));
        int removeCount = active.Count - mergeThreshold / 2;
        int total = 0;
        Vector3 at = active[0].t.position;
        for (int i = removeCount - 1; i >= 0; i--)
        {
            if (active[i].attracted) continue;
            total += active[i].value;
            Recycle(i);
        }
        if (total > 0) Spawn(at, total);
    }
}
