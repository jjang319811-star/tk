using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Light2D 의 세기/반경을 노이즈로 흔들어 불빛처럼 깜빡이게 함.
/// </summary>
[RequireComponent(typeof(Light2D))]
public class LightFlicker : MonoBehaviour
{
    [Min(0f)] public float intensityVariation = 0.25f;
    [Min(0f)] public float radiusVariation = 0.08f;
    [Min(0f)] public float speed = 6f;

    Light2D light2D;
    float baseIntensity;
    float baseRadius;
    float seed;

    void Awake()
    {
        light2D = GetComponent<Light2D>();
        Rebase();
        seed = Random.Range(0f, 100f);
    }

    /// <summary>풀에서 재사용하며 세기/반경을 바꾼 뒤 호출 → 새 값 기준으로 깜빡임</summary>
    public void Rebase()
    {
        if (light2D == null) light2D = GetComponent<Light2D>();
        baseIntensity = light2D.intensity;
        baseRadius = light2D.pointLightOuterRadius;
    }

    void Update()
    {
        float t = Time.unscaledTime * speed + seed;
        float n = Mathf.PerlinNoise(t, seed) * 2f - 1f;
        light2D.intensity = Mathf.Max(0f, baseIntensity * (1f + n * intensityVariation));
        light2D.pointLightOuterRadius = Mathf.Max(0.01f, baseRadius * (1f + n * radiusVariation));
    }
}
