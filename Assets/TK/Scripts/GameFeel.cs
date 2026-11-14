using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Pool;
using Unity.Cinemachine;

/// <summary>
/// 타격감 연출 (뱀서 스타일)
///  - 데미지 숫자 팝업 (UI 텍스트 풀링, 위로 튀었다 사라짐 / 기절·크리티컬은 노란색 큰 숫자)
///  - 히트스톱 (짧게 timeScale 감속, 레벨업 일시정지와 충돌하지 않음)
///  - 화면 흔들림 (Cinemachine 갱신 후 카메라에 픽셀 단위 오프셋)
///  - 적 사망 팝 이펙트 (16px Effect and Bullet 팩, 풀링)
/// </summary>
[DefaultExecutionOrder(10000)]
public class GameFeel : MonoBehaviour
{
    public static GameFeel I { get; private set; }

    [Header("Death pop")]
    public Sprite[] popFrames;
    public Material popMaterial;
    public float popFps = 18f;

    [Header("Damage numbers")]
    public int maxNumbers = 40;
    public int numberFontSize = 16; // 폰트 원본 크기 (정수배만)
    public bool showDamageNumbers = true;

    class Number { public Text text; public Vector3 world; public float life, max; public Vector2 drift; }
    class Pop { public SpriteRenderer sr; public float t; }

    readonly List<Number> numbers = new List<Number>();
    ObjectPool<Text> textPool;
    readonly List<Pop> pops = new List<Pop>();
    ObjectPool<SpriteRenderer> popPool;
    CinemachineImpulseSource impulse;
    RectTransform numberRoot;
    Canvas canvas;
    Font font;
    Camera cam;
    float hitstopRemain;
    bool hitstopActive;

    void Awake()
    {
        I = this;
        impulse = GetComponent<CinemachineImpulseSource>();
        textPool = new ObjectPool<Text>(CreateText, t => t.gameObject.SetActive(true), t => t.gameObject.SetActive(false), null, true, 16, maxNumbers);
        popPool = new ObjectPool<SpriteRenderer>(CreatePop, s => s.gameObject.SetActive(true), s => s.gameObject.SetActive(false), null, true, 16, 128);
    }

    Text CreateText()
    {
        var go = new GameObject("Num", typeof(RectTransform));
        go.transform.SetParent(numberRoot, false);
        var t = go.AddComponent<Text>();
        t.font = font;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var o = go.AddComponent<Shadow>(); // 단일 그림자 (Outline 4방향 복사로 생기는 지터 방지)
        o.effectColor = new Color(0.02f, 0.01f, 0.01f, 1f);
        o.effectDistance = new Vector2(1f, -1f);
        ((RectTransform)t.transform).sizeDelta = new Vector2(60f, 20f);
        return t;
    }

    SpriteRenderer CreatePop()
    {
        var go = new GameObject("DeathPop", typeof(SpriteRenderer));
        var sr = go.GetComponent<SpriteRenderer>();
        sr.sharedMaterial = popMaterial;
        sr.sortingOrder = 7;
        return sr;
    }

    void Start()
    {
        cam = Camera.main;
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
            if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay) { canvas = c; break; }
        if (canvas)
        {
            var reference = canvas.GetComponentInChildren<Text>(true);
            font = reference ? reference.font : null;
            var go = new GameObject("DamageNumbers", typeof(RectTransform));
            numberRoot = (RectTransform)go.transform;
            numberRoot.SetParent(canvas.transform, false);
            numberRoot.anchorMin = Vector2.zero;
            numberRoot.anchorMax = Vector2.one;
            numberRoot.offsetMin = numberRoot.offsetMax = Vector2.zero;
            numberRoot.SetAsFirstSibling(); // 다른 UI 아래
        }
    }

    // ------------------------------------------------------------------ API

    public static void Shake(float amplitude, float duration)
    {
        if (I == null || I.impulse == null) return;
        I.impulse.ImpulseDefinition.ImpulseDuration = duration;
        I.impulse.GenerateImpulseWithVelocity(Random.insideUnitCircle.normalized * amplitude);
    }

    public static void HitStop(float seconds)
    {
        if (I == null || !GameManager.instance.isLive) return;
        if (!I.hitstopActive && Time.timeScale != 1f) return; // 레벨업 등으로 멈춘 상태면 무시
        I.hitstopRemain = Mathf.Max(I.hitstopRemain, seconds);
        I.hitstopActive = true;
        Time.timeScale = 0.05f;
    }

    public static void DamageNumber(Vector3 world, float damage, bool emphasis)
    {
        if (I == null || !I.showDamageNumbers || I.numberRoot == null) return;
        I.SpawnNumber(world, Mathf.Max(1, Mathf.RoundToInt(damage)).ToString(), emphasis ? new Color(1f, 0.85f, 0.25f) : Color.white, emphasis ? 1.4f : 1f);
    }

    public static void TextPopup(Vector3 world, string text, Color color)
    {
        if (I == null || I.numberRoot == null) return;
        I.SpawnNumber(world, text, color, 1.2f);
    }

    public static void DeathPop(Vector3 world)
    {
        if (I == null || I.popFrames == null || I.popFrames.Length == 0) return;
        I.SpawnPop(world);
    }

    // ------------------------------------------------------------------ internals

    void SpawnNumber(Vector3 world, string value, Color color, float scale)
    {
        Text t;
        if (numbers.Count >= maxNumbers)
        {
            t = numbers[0].text;
            numbers.RemoveAt(0);
        }
        else t = textPool.Get();        t.gameObject.SetActive(true);
        t.text = value;
        t.color = color;
        t.fontSize = numberFontSize; // 강조는 색으로만 (크기를 바꾸면 픽셀 폰트가 깨짐)
        numbers.Add(new Number
        {
            text = t,
            world = world + new Vector3(Random.Range(-0.1f, 0.1f), 0.25f, 0f),
            life = 0f,
            max = 0.6f,
            drift = new Vector2(Random.Range(-0.15f, 0.15f), 0f),
        });
    }

    void SpawnPop(Vector3 world)
    {
        var sr = popPool.Get();
        sr.transform.position = new Vector3(Mathf.Round(world.x * 64f) / 64f, Mathf.Round(world.y * 64f) / 64f, 0f);
        sr.sprite = popFrames[0];
        sr.gameObject.SetActive(true);
        pops.Add(new Pop { sr = sr, t = 0f });
    }

    void Update()
    {
        // 히트스톱은 실제 시간 기준
        if (hitstopActive)
        {
            hitstopRemain -= Time.unscaledDeltaTime;
            if (hitstopRemain <= 0f)
            {
                hitstopActive = false;
                if (Mathf.Approximately(Time.timeScale, 0.05f) && GameManager.instance.isLive) Time.timeScale = 1f;
            }
        }

        // 사망 팝 재생
        for (int i = pops.Count - 1; i >= 0; i--)
        {
            var p = pops[i];
            p.t += Time.deltaTime;
            int frame = (int)(p.t * popFps);
            if (frame >= popFrames.Length)
            {
                popPool.Release(p.sr);
                pops.RemoveAt(i);
            }
            else p.sr.sprite = popFrames[frame];
        }
    }

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;

        // 데미지 숫자: 월드 → 캔버스 좌표
        for (int i = numbers.Count - 1; i >= 0; i--)
        {
            var n = numbers[i];
            n.life += Time.unscaledDeltaTime;
            float k = n.life / n.max;
            if (k >= 1f)
            {
                textPool.Release(n.text);
                numbers.RemoveAt(i);
                continue;
            }
            // 위로 툭 튀었다가 살짝 내려오는 곡선
            float rise = Mathf.Sin(Mathf.Min(k * 1.6f, 1f) * Mathf.PI * 0.5f) * 0.3f;
            Vector3 w = n.world + new Vector3(n.drift.x * k, rise, 0f);
            Vector2 screen = TKPixelGrid.SnapScreen(RectTransformUtility.WorldToScreenPoint(cam, w)); // 게임 아트 픽셀 격자에 맞춰 이동
            RectTransformUtility.ScreenPointToLocalPointInRectangle(numberRoot, screen, null, out Vector2 local);
            local = new Vector2(Mathf.Round(local.x), Mathf.Round(local.y));
            ((RectTransform)n.text.transform).anchoredPosition = local;
            var c = n.text.color;
            c.a = k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f;
            n.text.color = c;
        }
    }
}
