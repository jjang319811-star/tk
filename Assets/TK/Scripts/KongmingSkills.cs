using UnityEngine;

/// <summary>
/// 제갈공명 고유 스킬 (자동 시전, 임시 구현)
///  1) 화계(火計)  : 가까운 적에게 불꽃 구슬을 날려 폭발 범위 피해. 여포의 '무쌍 갑옷'을 깨는 수단.
///  2) 팔진도(八陣圖): 시전 위치에 진법 결계를 펼쳐 안의 적에게 지속 피해 + 넉백.
///                   돌진하던 여포가 결계에 들어오면 기절(보스 기믹).
/// 레벨이 오를수록 피해량과 화계 구슬 수가 증가.
/// </summary>
[RequireComponent(typeof(Player))]
public class KongmingSkills : MonoBehaviour
{
    [Header("화계 (Fire Strategy)")]
    public FireOrb fireOrbPrefab;
    public float fireInterval = 3.5f;
    public int fireCount = 2;
    public float fireDamage = 16f;
    public float fireRadius = 0.55f;

    [Header("팔진도 (Eight Trigrams)")]
    public TrigramRing trigramPrefab;
    [Tooltip("시전 순간 공명 위치에 재생할 마법진 이펙트 (Free 팩)")]
    public GameObject trigramCastFx;
    public float trigramInterval = 9f;
    public float trigramDuration = 3.5f;
    public float trigramRadius = 1.1f;
    public float trigramDamage = 5f;

    Player player;
    ProceduralSpriteAnim visual;
    float fireTimer, trigramTimer;

    float LevelScale => 1f + GameManager.instance.level * 0.15f;

    void Awake()
    {
        player = GetComponent<Player>();
        visual = GetComponentInChildren<ProceduralSpriteAnim>(true);
        trigramTimer = trigramInterval * 0.5f;
    }

    void Update()
    {
        if (!GameManager.instance.isLive)
            return;
        fireTimer += Time.deltaTime;
        trigramTimer += Time.deltaTime;
        // 대상이 없으면 타이머를 유지 → 적이 보이는 즉시 시전
        if (fireTimer >= fireInterval && CastFire()) fireTimer = 0f;
        if (trigramTimer >= trigramInterval && CastTrigram()) trigramTimer = 0f;
    }

    readonly System.Collections.Generic.List<IDamageable> targetBuffer = new System.Collections.Generic.List<IDamageable>();

    System.Collections.Generic.List<IDamageable> ScannerTargets()
    {
        targetBuffer.Clear();
        var hits = player.scanner.targets;
        if (hits == null) return targetBuffer;
        Vector2 me = transform.position;
        foreach (var h in hits)
        {
            var d = h.collider ? h.collider.GetComponentInParent<IDamageable>() : null;
            if (d != null && d.IsAlive && !targetBuffer.Contains(d)) targetBuffer.Add(d);
        }
        targetBuffer.Sort((a, b) => ((Vector2)a.transform.position - me).sqrMagnitude.CompareTo(((Vector2)b.transform.position - me).sqrMagnitude));
        return targetBuffer;
    }

    bool CastFire()
    {
        if (fireOrbPrefab == null) return false;
        var targets = ScannerTargets(); // 기존 Scanner(매 물리 프레임 탐색) 결과 재사용
        if (targets.Count == 0) return false;

        int n = Mathf.Min(fireCount + GameManager.instance.level / 3, Mathf.Max(targets.Count, 1));
        for (int i = 0; i < n; i++)
        {
            var target = targets[i % targets.Count];
            var orb = TKPool.Spawn(fireOrbPrefab, player.HandPosition, Quaternion.identity); // 손끝에서 발사
            orb.Launch(target, fireDamage * LevelScale, fireRadius, player.scanner.targetLayer, i * 0.12f);
        }
        player.PlayAttack(true, targets[0].transform.position);
        if (visual) visual.Flash(new Color(1f, 0.55f, 0.2f), 0.06f);
        return true;
    }

    bool CastTrigram()
    {
        if (trigramPrefab == null) return false;
        if (ScannerTargets().Count == 0)
            return false;

        var ring = TKPool.Spawn(trigramPrefab, transform.position, Quaternion.identity);
        ring.Setup(trigramRadius, trigramDuration, trigramDamage * LevelScale, player.scanner.targetLayer);
        if (trigramCastFx)
            TKPool.Spawn(trigramCastFx, transform.position, Quaternion.identity);
        if (visual)
        {
            visual.Play(ProceduralSpriteAnim.Action.Roar, 0.3f);
            visual.Flash(new Color(1f, 0.85f, 0.4f), 0.08f);
        }
        return true;
    }
}
