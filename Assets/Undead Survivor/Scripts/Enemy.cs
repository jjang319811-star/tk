using UnityEngine;
using System.Collections;

public class Enemy : MonoBehaviour, IDamageable
{
    public float speed;
    public Rigidbody2D target;
    Animator anim;
    public float health;
    public float maxHealth;
    public RuntimeAnimatorController[] animatorController;
    public float deadDelay = 0.5f;
    public float flashDuration = 0.08f;
    public float knockbackForce = 0.75f;
    public float knockbackDuration = 0.15f;
    [Tooltip("Enemy arrow prefab index in PoolManager. Required for ranged spawn data.")]
    public int arrowPrefabIndex = -1;

    // 피격 플래시: 재질 교체(GUI/Text Shader) 대신 TK/PixelSprite 셰이더의 _FlashAmount 사용
    // → 픽셀 외곽선/조명 유지, 배칭 깨짐 최소화
    static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");
    static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");
    MaterialPropertyBlock mpb;

    // 적 떼(EventDirector) 모드: 플레이어를 쫓지 않고 일직선으로 지나감
    bool swarm;
    Vector2 swarmDir;
    float swarmSpeed, swarmLife;
    public bool IsSwarm => swarm;
    bool hasHitParam;
    float spawnTime;
    /// <summary>생성 직후 3초: 대군 대형이 화면 밖에서 재배치되지 않도록 유예</summary>
    public bool InSpawnGrace => Time.time - spawnTime < 3f;

    bool isLive;
    public bool IsAlive => isLive;
    bool isHit;
    Rigidbody2D rb;
    Collider2D col;
    SpriteRenderer sr;
    Material defaultMaterial;
    Coroutine hitRoutine;
    WaitForFixedUpdate wait;
    SpawnData attackData;
    float attackTimer;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        anim = GetComponent<Animator>();
        col = GetComponent<Collider2D>();
        wait = new WaitForFixedUpdate();
        defaultMaterial = sr.sharedMaterial;
        mpb = new MaterialPropertyBlock();
    }

    public void SetSwarm(Vector2 direction, float moveSpeed, float life)
    {
        swarm = true;
        swarmDir = direction.normalized;
        swarmSpeed = moveSpeed;
        swarmLife = life;
        attackData = null;
    }

    void SetFlash(float amount)
    {
        sr.GetPropertyBlock(mpb);
        mpb.SetFloat(FlashAmountId, amount);
        mpb.SetColor(FlashColorId, Color.white);
        sr.SetPropertyBlock(mpb);
    }

    void FixedUpdate()
    {
        if (!GameManager.instance.isLive)
            return;
        if (!isLive || isHit || anim.GetCurrentAnimatorStateInfo(0).IsName("Hit"))
            return;

        if (swarm)
        {
            rb.MovePosition(rb.position + swarmDir * swarmSpeed * Time.fixedDeltaTime);
            rb.linearVelocity = Vector2.zero;
            swarmLife -= Time.fixedDeltaTime;
            if (swarmLife <= 0f)
                gameObject.SetActive(false);
            return;
        }

        Vector2 dirVec = target.position - rb.position;
        if (attackData != null && attackData.usesRangedAttack
            && dirVec.sqrMagnitude <= attackData.attackRange * attackData.attackRange)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }
        Vector2 nextVec = dirVec.normalized * speed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + nextVec);
        rb.linearVelocity = Vector2.zero;
    }

    void Update()
    {
        if (!GameManager.instance.isLive || !isLive || isHit
            || attackData == null || !attackData.usesRangedAttack)
            return;

        attackTimer += Time.deltaTime;
        Vector2 direction = target.position - rb.position;
        if (direction.sqrMagnitude > attackData.attackRange * attackData.attackRange
            || direction.sqrMagnitude < 0.0001f || attackTimer < attackData.attackInterval)
            return;

        attackTimer = 0f;
        EnemyArrow arrow = GameManager.instance.poolManager.Get(arrowPrefabIndex).GetComponent<EnemyArrow>();
        Vector3 origin = sr.bounds.center + (Vector3)direction.normalized * 0.2f;
        arrow.Init(origin, direction.normalized, attackData.arrowDamage, attackData.arrowSpeed);
    }

    void LateUpdate()
    {
        if (!GameManager.instance.isLive)
            return; 
        if (!isLive)
            return;

        if (swarm)
            sr.flipX = swarmDir.x < 0f;
        else if (target.position.x < rb.position.x)
            sr.flipX = true;
        else
            sr.flipX = false;
    }
    void OnEnable()
    {
        target = GameManager.instance.player.GetComponent<Rigidbody2D>();
        isLive = true;
        col.enabled = true;
        rb.simulated = true;
        isHit = false;
        swarm = false;
        spawnTime = Time.time;
        sr.sharedMaterial = defaultMaterial;
        SetFlash(0f);
        sr.sortingOrder = 2;
        anim.SetBool("Dead", false);
        health = maxHealth;
        attackData = null;
        attackTimer = 0f;
        rb.linearVelocity = Vector2.zero;
    }

    public void Init(SpawnData spawnData)
    {
        anim.runtimeAnimatorController = animatorController[spawnData.SpriteType];
        // 컨트롤러에 Hit 파라미터가 있을 때만 트리거 (없으면 매 피격마다 경고가 쌓임)
        hasHitParam = false;
        foreach (var p in anim.parameters) if (p.name == "Hit") { hasHitParam = true; break; }
        maxHealth = spawnData.health;
        speed = spawnData.speed;
        health = maxHealth;
        attackData = spawnData;
        attackTimer = 0f;
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (!collision.CompareTag("Bullet") || !isLive)
            return;
        TakeDamage(collision.GetComponent<Bullet>().damage);
    }

    public void TakeDamage(float damage)
    {
        if (!isLive)
            return;

        health -= damage;
        GameFeel.DamageNumber(transform.position, damage, false);
        if (hitRoutine != null)
            StopCoroutine(hitRoutine);
        hitRoutine = StartCoroutine(HitRoutine());

        if (health > 0)
        {
            if (hasHitParam) anim.SetTrigger("Hit");
        }
        else
        {
            isLive = false;
            col.enabled = false;
            rb.simulated = false;
            sr.sortingOrder = 1;
            anim.SetBool("Dead", true);
            GameManager.instance.kill++;
            // 뱀서 룰: 즉시 경험치 대신 보석 드롭 (XpGemSystem 이 없으면 기존처럼 즉시 지급)
            XpGemSystem.Drop(transform.position, 1);
            GameFeel.DeathPop(transform.position);
            StartCoroutine(DeadRoutine());
        }
    }

    IEnumerator DeadRoutine()
    {
        // Wait for the animator to enter the dead state before reading its length
        yield return null;
        while (anim.IsInTransition(0))
            yield return null;

        yield return new WaitForSeconds(anim.GetCurrentAnimatorStateInfo(0).length + deadDelay);
        gameObject.SetActive(false);
    }

    IEnumerator HitRoutine()
    {
        isHit = true;
        rb.linearVelocity = Vector2.zero;

        SetFlash(1f);
        yield return new WaitForSeconds(flashDuration);
        SetFlash(0f);

        if (isLive)
        {
            yield return wait;
            Vector3 dirVec = transform.position - GameManager.instance.player.transform.position;
            rb.AddForce(dirVec.normalized * knockbackForce, ForceMode2D.Impulse);
            yield return new WaitForSeconds(knockbackDuration);
            rb.linearVelocity = Vector2.zero;
        }

        isHit = false;
        hitRoutine = null;
    }
    // Animation event receiver kept for existing clips; despawn is handled by DeadRoutine
    void Dead()
    {
    }
}
