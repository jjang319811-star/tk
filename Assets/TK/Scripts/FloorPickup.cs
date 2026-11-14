using UnityEngine;

/// <summary>
/// 바닥 아이템 (뱀서의 광원 드롭 / 보스 상자)
///  Heal  : 만두 — 체력 회복 (뱀서의 바닥 치킨)
///  Magnet: 집결령 — 맵의 모든 경험치 보석을 끌어옴 (뱀서의 Vacuum)
///  Chest : 황제의 하사품 — 룰렛으로 업그레이드 획득
/// </summary>
public class FloorPickup : MonoBehaviour
{
    public enum Kind { Heal, Magnet, Chest }

    public Kind kind;
    public float healAmount = 30f;
    public System.Action Opened;

    SpriteRenderer sr;
    float age;
    Vector3 basePos;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        basePos = transform.position;
    }

    void OnEnable() => basePos = transform.position;

    void Update()
    {
        var gm = GameManager.instance;
        age += Time.deltaTime;
        // 대기 모션: 0.4초마다 1px 위아래
        transform.position = basePos + new Vector3(0f, (Mathf.FloorToInt(age / 0.4f) & 1) / 64f, 0f); // 1px 스텝
        if (!gm.isLive || gm.player == null) return;

        float radius = kind == Kind.Chest ? 0.45f : 0.32f;
        if (((Vector2)gm.player.transform.position - (Vector2)basePos).sqrMagnitude > radius * radius) return;

        switch (kind)
        {
            case Kind.Heal:
                float before = gm.health;
                gm.health = Mathf.Min(gm.maxHealth, gm.health + healAmount);
                GameFeel.TextPopup(transform.position, "+" + Mathf.RoundToInt(gm.health - before), new Color(0.45f, 1f, 0.45f));
                break;
            case Kind.Magnet:
                if (XpGemSystem.I) XpGemSystem.I.VacuumAll();
                GameFeel.TextPopup(transform.position, "집결령!", new Color(0.6f, 0.85f, 1f));
                break;
            case Kind.Chest:
                TreasureChestUI.Open(Opened);
                break;
        }
        Destroy(gameObject);
    }
}
