using System.Collections.Generic;
using UnityEngine;

/// <summary>공명의 팔진도 결계. 범위 지속 피해 + 여포 돌진 기절 기믹 판정.</summary>
public class TrigramRing : MonoBehaviour
{
    public static readonly List<TrigramRing> Active = new List<TrigramRing>();

    [Tooltip("스케일 1 일 때 링 스프라이트의 반지름(유닛)")]
    public float spriteRadiusUnits = 0.45f;
    public float tickInterval = 0.5f;
    public float spinSpeed = 25f;

    SpriteRenderer sr;
    float radius, duration, damage, life, tick;
    LayerMask layer;
    Color baseColor;

    public float Radius => radius;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        baseColor = sr ? sr.color : Color.white;
    }

    void OnEnable() => Active.Add(this);
    void OnDisable() => Active.Remove(this);

    public void Setup(float r, float dur, float dmg, LayerMask l)
    {
        radius = r;
        duration = dur;
        damage = dmg;
        layer = l;
        life = 0f;
        tick = 0f;
        // 반지름에 맞는 실제 픽셀 크기 링 (스케일로 늘리지 않음)
        transform.localScale = Vector3.one;
        transform.rotation = Quaternion.identity;
        if (sr) sr.sprite = TKShapes.TrigramRing(r);
    }

    public bool Contains(Vector2 p) => (p - (Vector2)transform.position).sqrMagnitude <= radius * radius;

    void Update()
    {
        if (!GameManager.instance.isLive)
            return;
        float dt = Time.deltaTime;
        life += dt;
        tick += dt;
        // 회전 대신(픽셀 깨짐) 일정 박자로 밝기만 계단식 변화

        if (tick >= tickInterval)
        {
            tick = 0f;
            TKCombat.DamageCircle(transform.position, radius, damage, layer);
        }

        if (sr)
        {
            float a = Mathf.Min(1f, life / 0.25f) * Mathf.Min(1f, (duration - life) / 0.4f);
            float pulse = (Mathf.FloorToInt(life / 0.2f) & 1) == 0 ? 1f : 0.8f;
            sr.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * Mathf.Clamp01(a) * pulse);
        }

        if (life >= duration)
            TKPool.Release(gameObject);
    }
}
