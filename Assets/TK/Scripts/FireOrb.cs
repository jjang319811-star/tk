using UnityEngine;

/// <summary>공명의 화계 불꽃 구슬. 대상을 따라가 폭발하며 범위 피해.</summary>
public class FireOrb : MonoBehaviour
{
    public float speed = 3.2f;
    public float lifetime = 1.8f;
    public GameObject explosionPrefab;

    IDamageable target;
    Vector3 targetPos;
    float damage, radius, life, delay;
    LayerMask layer;
    bool launched;

    public void Launch(IDamageable t, float dmg, float r, LayerMask l, float startDelay)
    {
        target = t;
        targetPos = t.transform.position;
        damage = dmg;
        radius = r;
        layer = l;
        life = lifetime;
        delay = startDelay;
        launched = true;
    }

    void Update()
    {
        if (!launched || !GameManager.instance.isLive)
            return;
        float dt = Time.deltaTime;
        if (delay > 0f) { delay -= dt; return; }

        if (TKCombat.IsValid(target))
            targetPos = target.transform.position;

        Vector3 to = targetPos - transform.position;
        float step = speed * dt;
        life -= dt;
        if (to.magnitude <= step + 0.05f || life <= 0f)
        {
            Explode();
            return;
        }
        transform.position += to.normalized * step;
    }

    void Explode()
    {
        TKCombat.DamageCircle(transform.position, radius, damage, layer, hit =>
        {
            if (hit is LuBuBoss boss) boss.OnFireStrategyHit();
        });
        if (explosionPrefab)
            TKPool.Spawn(explosionPrefab, transform.position, Quaternion.identity);
        launched = false;
        TKPool.Release(gameObject);
    }
}
