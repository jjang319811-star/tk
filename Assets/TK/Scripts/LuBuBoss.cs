using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 보스 여포 (임시 패턴/기믹 구현)
///  1페이즈 : 추격 → [돌진] / [방천화극 휩쓸기] 교대
///  2페이즈(HP 50% 이하, 분노): 이동/공격 속도 증가, 붉은 오라, '무쌍 갑옷'(받는 피해 35%)
///           패턴 추가 → [극 낙하(연속 장판)] / [병사 소환]
///  기믹 : - 돌진 중 공명의 '팔진도' 결계에 들어오면 기절(받는 피해 2배)
///         - 무쌍 갑옷은 공명의 '화계' 폭발에 3번 맞으면 파괴 → 기절
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class LuBuBoss : MonoBehaviour, IDamageable
{
    [Header("Stats")]
    public float maxHealth = 450f;
    public float moveSpeed = 0.6f;
    public float chargeSpeed = 4.2f;
    public float chargeLength = 3.2f;
    public float chargeDamage = 15f;
    public float sweepDamage = 12f;
    public float sweepRadius = 1.1f;
    public float strikeDamage = 10f;
    public float strikeRadius = 0.45f;

    [Header("Gimmicks")]
    public int shieldHitsToBreak = 3;
    public float shieldDamageMultiplier = 0.35f;
    public float stunDuration = 3f;
    public float stunnedDamageMultiplier = 2f;

    [Header("Refs")]
    public ProceduralSpriteAnim anim;
    public Telegraph circleTelegraph;
    public Telegraph lineTelegraph;
    public GameObject strikeFx;
    public Light2D auraLight;

    public System.Action<string> Announce;
    public System.Action Died;

    public float Health { get; private set; }
    public float Health01 => Mathf.Clamp01(Health / maxHealth);
    public bool IsAlive => alive;
    public bool Phase2 { get; private set; }
    public bool Stunned => stunTimer > 0f;
    public int ShieldHits => shieldHits;

    Rigidbody2D rb;
    Collider2D col;
    Player player;
    bool alive = true;
    Vector2 desiredVel;
    float stunTimer, speedMul = 1f;
    int shieldHits;

    bool Live => GameManager.instance.isLive;
    bool Interrupted => !alive || Stunned;
    Vector2 ToPlayer => (Vector2)player.transform.position - rb.position;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        Health = maxHealth;
        if (auraLight) auraLight.enabled = false;
    }

    void Start()
    {
        player = GameManager.instance.player;
        StartCoroutine(Brain());
    }

    void FixedUpdate()
    {
        if (!alive || !Live || Stunned)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }
        rb.MovePosition(rb.position + desiredVel * Time.fixedDeltaTime);
        rb.linearVelocity = Vector2.zero;
    }

    void Update()
    {
        if (!alive || player == null) return;
        if (stunTimer > 0f && Live)
        {
            stunTimer -= Time.deltaTime;
            if (stunTimer <= 0f) anim.Stop();
        }
        if (anim && anim.Current != ProceduralSpriteAnim.Action.Lunge)
            anim.faceRight = player.transform.position.x > transform.position.x;
        if (auraLight && auraLight.enabled)
            auraLight.intensity = 0.8f + 0.25f * Mathf.Sin(Time.time * 6f);
    }

    // 게임 일시정지(레벨업 창 등)·기절 중에는 시간이 흐르지 않는 대기
    IEnumerator Wait(float seconds)
    {
        float e = 0f;
        while (e < seconds && !Interrupted)
        {
            if (Live) e += Time.deltaTime;
            yield return null;
        }
    }

    IEnumerator Brain()
    {
        anim.Play(ProceduralSpriteAnim.Action.Roar, 1f);
        Announce?.Invoke("여포: 이 여봉선의 방천화극을 받아라!");
        yield return Wait(1.2f);

        int step = 0;
        while (alive)
        {
            while (!Live || Stunned) yield return null;
            if (!Phase2 && Health01 <= 0.5f)
                yield return EnterPhase2();

            yield return Chase(Phase2 ? 1.3f : 2.0f);
            if (!alive) yield break;
            if (Stunned) continue;

            if (!Phase2)
                yield return (step % 2 == 0) ? Charge() : Sweep();
            else
            {
                switch (step % 4)
                {
                    case 0: yield return Charge(); break;
                    case 1: yield return HalberdRain(); break;
                    case 2: yield return Sweep(); break;
                    default: yield return Summon(); break;
                }
            }
            desiredVel = Vector2.zero;
            step++;
        }
    }

    IEnumerator Chase(float duration)
    {
        float t = 0f;
        while (t < duration && !Interrupted)
        {
            if (Live)
            {
                t += Time.deltaTime;
                desiredVel = ToPlayer.normalized * moveSpeed * speedMul;
            }
            else desiredVel = Vector2.zero;
            yield return null;
        }
        desiredVel = Vector2.zero;
    }

    // 패턴 1: 돌진 — 직선 예고 후 고속 돌진. 팔진도에 걸리면 기절.
    IEnumerator Charge()
    {
        Vector2 dir = ToPlayer.normalized;
        if (dir == Vector2.zero) dir = Vector2.right;
        float windup = Phase2 ? 0.7f : 0.9f;
        var tele = Telegraph.Spawn(lineTelegraph, rb.position, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg,
            TKShapes.Line(chargeLength, 0.5f), windup); // 실제 픽셀 크기 도형
        anim.faceRight = dir.x > 0f;
        anim.Play(ProceduralSpriteAnim.Action.Windup, windup);
        yield return Wait(windup);
        if (Interrupted) { if (tele) TKPool.Release(tele.gameObject); yield break; }

        anim.Play(ProceduralSpriteAnim.Action.Lunge, 0.8f);
        bool hit = false;
        float traveled = 0f;
        while (traveled < chargeLength && !Interrupted)
        {
            if (Live)
            {
                float speed = chargeSpeed * speedMul;
                traveled += speed * Time.deltaTime;
                desiredVel = dir * speed;

                foreach (var ring in TrigramRing.Active)
                {
                    if (ring.Contains(rb.position))
                    {
                        desiredVel = Vector2.zero;
                        Stun(stunDuration, "팔진도에 갇혔다! 여포 혼란 (피해 2배)");
                        yield break;
                    }
                }
                if (!hit && ToPlayer.magnitude < 0.55f)
                {
                    hit = true;
                    player.TakeDamage(chargeDamage);
                    GameFeel.Shake(0.1f, 0.3f);
                    GameFeel.HitStop(0.08f);
                }
            }
            else desiredVel = Vector2.zero;
            yield return null;
        }
        desiredVel = Vector2.zero;
        yield return Wait(0.5f);
    }

    // 패턴 2: 방천화극 휩쓸기 — 주변 원형 예고 후 회전 베기
    IEnumerator Sweep()
    {
        float windup = Phase2 ? 0.6f : 0.8f;
        var tele = Telegraph.Spawn(circleTelegraph, rb.position, 0f, TKShapes.Circle(sweepRadius), windup);
        anim.Play(ProceduralSpriteAnim.Action.Windup, windup);
        yield return Wait(windup);
        if (Interrupted) { if (tele) TKPool.Release(tele.gameObject); yield break; }

        anim.Play(ProceduralSpriteAnim.Action.Spin, 0.45f);
        GameFeel.Shake(0.05f, 0.2f);
        if (ToPlayer.magnitude <= sweepRadius)
        {
            player.TakeDamage(sweepDamage);
            GameFeel.HitStop(0.07f);
        }
        if (strikeFx)
            for (int i = 0; i < 6; i++)
            {
                Vector2 p = rb.position + (Vector2)(Quaternion.Euler(0, 0, i * 60f) * Vector2.right) * sweepRadius * 0.75f;
                TKPool.Spawn(strikeFx, p, Quaternion.identity);
            }
        yield return Wait(0.6f);
    }

    // 패턴 3 (2페이즈): 극 낙하 — 플레이어 이동 방향을 예측한 연속 장판
    IEnumerator HalberdRain()
    {
        anim.Play(ProceduralSpriteAnim.Action.Roar, 0.6f);
        for (int i = 0; i < 4 && !Interrupted; i++)
        {
            Vector2 p = (Vector2)player.transform.position + player.inputVector * 0.5f;
            Telegraph.Spawn(circleTelegraph, p, 0f, TKShapes.Circle(strikeRadius), 0.65f);
            StartCoroutine(StrikeAt(p, 0.65f));
            yield return Wait(0.35f);
        }
        yield return Wait(0.8f);
    }

    IEnumerator StrikeAt(Vector2 p, float delay)
    {
        float e = 0f;
        while (e < delay)
        {
            if (Live) e += Time.deltaTime;
            yield return null;
        }
        if (!alive) yield break;
        GameFeel.Shake(0.035f, 0.15f);
        if (((Vector2)player.transform.position - p).magnitude <= strikeRadius)
        {
            player.TakeDamage(strikeDamage);
            GameFeel.HitStop(0.06f);
        }
        if (strikeFx) TKPool.Spawn(strikeFx, p, Quaternion.identity);
    }

    // 패턴 4 (2페이즈): 병사 소환
    IEnumerator Summon()
    {
        anim.Play(ProceduralSpriteAnim.Action.Roar, 0.8f);
        Announce?.Invoke("여포: 병사들아, 저 책사의 목을 베어라!");
        var spawner = FindFirstObjectByType<Spawner>(FindObjectsInactive.Include);
        if (spawner && spawner.spawnDatas != null && spawner.spawnDatas.Length > 0)
        {
            for (int i = 0; i < 4; i++)
            {
                var enemyGo = GameManager.instance.poolManager.Get(0);
                enemyGo.transform.position = rb.position + (Vector2)(Quaternion.Euler(0, 0, i * 90f + 45f) * Vector2.right) * 0.9f;
                enemyGo.GetComponent<Enemy>().Init(spawner.spawnDatas[0]);
            }
        }
        yield return Wait(1f);
    }

    IEnumerator EnterPhase2()
    {
        Phase2 = true;
        speedMul = 1.3f;
        shieldHits = shieldHitsToBreak;
        desiredVel = Vector2.zero;
        anim.Play(ProceduralSpriteAnim.Action.Roar, 1.2f);
        anim.Flash(new Color(1f, 0.2f, 0.1f), 0.15f);
        anim.Renderer.color = new Color(1f, 0.78f, 0.78f, 1f);
        if (auraLight) auraLight.enabled = true;
        Announce?.Invoke("여포 분노! 무쌍 갑옷 - 화계로 세 번 불태워 깨뜨려라");
        yield return Wait(1.2f);
    }

    public void OnFireStrategyHit()
    {
        if (!alive || shieldHits <= 0) return;
        shieldHits--;
        anim.Flash(new Color(1f, 0.6f, 0.2f), 0.1f);
        if (shieldHits == 0)
            Stun(stunDuration, "무쌍 갑옷이 깨졌다! 여포 기절 (피해 2배)");
        else
            Announce?.Invoke($"무쌍 갑옷 균열 {shieldHitsToBreak - shieldHits}/{shieldHitsToBreak}");
    }

    void Stun(float seconds, string message)
    {
        stunTimer = seconds;
        desiredVel = Vector2.zero;
        anim.Play(ProceduralSpriteAnim.Action.Stunned, seconds);
        Announce?.Invoke(message);
        GameFeel.Shake(0.09f, 0.35f);
        GameFeel.HitStop(0.12f);
    }

    public void TakeDamage(float damage)
    {
        if (!alive) return;
        float mul = 1f;
        if (Stunned) mul = stunnedDamageMultiplier;
        else if (shieldHits > 0) mul = shieldDamageMultiplier;
        Health -= damage * mul;
        GameFeel.DamageNumber(transform.position + new Vector3(0f, 0.9f, 0f), damage * mul, Stunned);
        anim.Flash(Stunned ? new Color(1f, 0.95f, 0.4f) : Color.white, 0.06f);
        if (Health <= 0f) Die();
    }

    void Die()
    {
        alive = false;
        Health = 0f;
        StopAllCoroutines();
        desiredVel = Vector2.zero;
        col.enabled = false;
        rb.simulated = false;
        anim.Play(ProceduralSpriteAnim.Action.Dead, 1.4f);
        if (auraLight) auraLight.enabled = false;
        GameManager.instance.kill++;
        Died?.Invoke();
        Destroy(gameObject, 2f);
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (!alive || !collision.CompareTag("Bullet")) return;
        var bullet = collision.GetComponent<Bullet>();
        if (bullet) TakeDamage(bullet.damage);
    }
}
