using UnityEngine;
using UnityEngine.InputSystem;

public class Player : MonoBehaviour
{
    public Vector2 inputVector;
    public float speed;
    public Scanner scanner;
    public Hand[] hands;
    Rigidbody2D rb;
    SpriteRenderer sr; 
    Animator anim; 

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        anim = GetComponent<Animator>();
        scanner = GetComponent<Scanner>();
        hands = GetComponentsInChildren<Hand>(true);
    }

    void OnMove(InputValue value)
    {
        inputVector = value.Get<Vector2>();
    }
    
    void FixedUpdate()
    {
        if (!GameManager.instance.isLive)
            return;
        Vector2 nextVector = speed * Time.fixedDeltaTime * inputVector;
        rb.MovePosition(rb.position + nextVector);
    }

    // 공명처럼 Animator 없이 ProceduralSpriteAnim(자식 Visual)으로 움직이는 캐릭터 지원
    bool HasAnimator => anim != null && anim.isActiveAndEnabled && anim.runtimeAnimatorController != null;
    ProceduralSpriteAnim visual;
    float hurtFlashCooldown;

    void Start()
    {
        visual = GetComponentInChildren<ProceduralSpriteAnim>(true);
        if (visual && visual.handAnchor == null)
        {
            var anchor = new GameObject("HandAnchor").transform;
            anchor.SetParent(transform, false);
            visual.handAnchor = anchor;
        }
    }

    /// <summary>손끝(투사체·이펙트 발사 지점). 비주얼이 없으면 몸 중심</summary>
    public Transform HandAnchor => visual && visual.handAnchor ? visual.handAnchor : transform;
    public Vector3 HandPosition => visual ? visual.HandWorldPosition : transform.position;

    /// <summary>모든 자동 공격 공통 시전 동작 (손을 툭 내지르기). 너무 잦으면 무시</summary>
    float lastAttackAnim;
    public void PlayAttack(bool faceTarget = false, Vector3 target = default)
    {
        if (!visual) return;
        if (Time.time - lastAttackAnim < 0.12f) return;
        lastAttackAnim = Time.time;
        // 공격 순간 대상 쪽을 바라봄 (이동 입력이 없을 때만)
        if (faceTarget && inputVector.x == 0f && sr) sr.flipX = target.x < transform.position.x;
        visual.Play(ProceduralSpriteAnim.Action.Attack, 0.14f);
    }

    void LateUpdate()
    {
        if (!GameManager.instance.isLive)
            return;
        hurtFlashCooldown -= Time.deltaTime;
        if (HasAnimator)
            anim.SetFloat("Speed", inputVector.magnitude);
        
        if (inputVector.x != 0)
        {
            // Handle horizontal input
            sr.flipX = inputVector.x < 0;
        }
    }

    void OnCollisionStay2D(Collision2D collision)
    {
        TakeDamage(Time.deltaTime * 10);
    }

    public void TakeDamage(float damage)
    {
        if (!GameManager.instance.isLive)
            return;
        
        GameManager.instance.health -= damage;
        if (visual && hurtFlashCooldown <= 0f)
        {
            hurtFlashCooldown = 0.25f;
            visual.Flash(new Color(1f, 0.25f, 0.2f), 0.06f);
        }

        if (GameManager.instance.health <= 0)
        {
            for (int i = 2; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child.GetComponent<ProceduralSpriteAnim>() == null)
                    child.gameObject.SetActive(false);
            }

            if (HasAnimator)
                anim.SetTrigger("Dead");
            if (visual)
                visual.Play(ProceduralSpriteAnim.Action.Dead, 1.2f);
            GameManager.instance.GameOver();
        }
    }
}
