using UnityEngine;

/// <summary>보스 공격 예고 장판. 단계적으로(스텝) 빨라지는 점멸 후 duration 뒤 사라짐.</summary>
public class Telegraph : MonoBehaviour
{
    public Color color = new Color(1f, 0.15f, 0.1f, 1f);

    SpriteRenderer sr;
    float duration = 1f, t;

    /// <summary>실제 픽셀 크기로 만든 도형 스프라이트로 장판 생성 (스케일 1 고정)</summary>
    public static Telegraph Spawn(Telegraph prefab, Vector3 position, float angleDeg, Sprite shape, float duration)
    {
        var tele = TKPool.Spawn(prefab, position, Quaternion.Euler(0f, 0f, angleDeg));
        tele.transform.localScale = Vector3.one;
        if (tele.sr == null) tele.sr = tele.GetComponent<SpriteRenderer>();
        tele.sr.sprite = shape;
        tele.duration = Mathf.Max(0.05f, duration);
        tele.t = 0f;
        return tele;
    }

    void Awake() => sr = GetComponent<SpriteRenderer>();

    void Update()
    {
        if (!GameManager.instance.isLive)
            return;
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / duration);
        // 점멸 간격이 0.16 → 0.05초로 계단식으로 빨라짐 (부드러운 사인 대신 딱딱 끊김)
        float interval = k < 0.5f ? 0.16f : k < 0.8f ? 0.09f : 0.05f;
        bool on = (Mathf.FloorToInt(t / interval) & 1) == 0;
        float alpha = (k < 0.5f ? 0.35f : 0.6f) * (on ? 1f : 0.55f);
        sr.color = new Color(color.r, color.g, color.b, alpha);
        if (t >= duration)
            TKPool.Release(gameObject);
    }
}
