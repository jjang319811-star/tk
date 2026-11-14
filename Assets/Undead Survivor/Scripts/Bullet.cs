using UnityEngine;

public class Bullet : MonoBehaviour
{
    public float damage;
    public int per;

    Rigidbody2D rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    public void Init(float damage, int per, Vector3 dir)
    {
        this.damage = damage;
        this.per = per;

        if (per > -1)
        {
            rb.linearVelocity = dir * 5.6f; // 시야 확대 보정 (x1.5)
        }
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (!collision.CompareTag("Enemy") || per == -1)
            return;
        
        per--;
        if (per == -1)
        {
            rb.linearVelocity = Vector2.zero;
            gameObject.SetActive(false);
        }
    }
}
