using UnityEngine;

/// <summary>이펙트를 lifetime 동안 보여 준 뒤 서서히 사라지며 삭제.</summary>
public class TimedFade : MonoBehaviour
{
    public float lifetime = 0.6f;
    public float fadePortion = 0.4f;

    SpriteRenderer sr;
    UnityEngine.Rendering.Universal.Light2D light2D;
    float t, baseIntensity;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        light2D = GetComponentInChildren<UnityEngine.Rendering.Universal.Light2D>();
        if (light2D) baseIntensity = light2D.intensity;
    }

    // 풀(PoolManager)에서 재사용될 때 초기화
    void OnEnable()
    {
        t = 0f;
        if (sr) { var c = sr.color; c.a = 1f; sr.color = c; }
        if (light2D) light2D.intensity = baseIntensity;
    }

    void Update()
    {
        t += Time.deltaTime;
        float fadeStart = lifetime * (1f - fadePortion);
        float a = t < fadeStart ? 1f : 1f - Mathf.Clamp01((t - fadeStart) / (lifetime - fadeStart));
        if (sr) { var c = sr.color; c.a = a; sr.color = c; }
        if (light2D) light2D.intensity = baseIntensity * a;
        if (t >= lifetime) TKPool.Release(gameObject);
    }
}
