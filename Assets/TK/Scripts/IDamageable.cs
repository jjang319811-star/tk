using UnityEngine;

/// <summary>
/// 무기·스킬이 피해를 줄 수 있는 대상 (일반 적 Enemy, 보스 LuBuBoss 등).
/// </summary>
public interface IDamageable
{
    bool IsAlive { get; }
    void TakeDamage(float damage);
    Transform transform { get; }
}

public static class TKCombat
{
    static readonly Collider2D[] buffer = new Collider2D[64];
    static readonly System.Collections.Generic.HashSet<IDamageable> damaged = new System.Collections.Generic.HashSet<IDamageable>();

    /// <summary>원 범위 안의 모든 대상에게 피해. 맞힌 수를 반환.</summary>
    public static int DamageCircle(Vector2 center, float radius, float damage, LayerMask layer, System.Action<IDamageable> onHit = null, System.Func<IDamageable, bool> filter = null)
    {
        var contact = new ContactFilter2D { useTriggers = true };
        contact.SetLayerMask(layer);
        int count = Physics2D.OverlapCircle(center, radius, contact, buffer);
        damaged.Clear();
        int hitCount = 0;
        GameManager.instance.BeginDamageBatch();
        try
        {
            for (int i = 0; i < count; i++)
            {
                var target = buffer[i].GetComponentInParent<IDamageable>();
                if (target == null || !target.IsAlive || !damaged.Add(target) || (filter != null && !filter(target)))
                    continue;
                target.TakeDamage(damage);
                onHit?.Invoke(target);
                hitCount++;
            }
        }
        finally
        {
            GameManager.instance.EndDamageBatch();
        }
        return hitCount;
    }

    /// <summary>범위 안의 살아 있는 대상을 가까운 순으로.</summary>
    public static System.Collections.Generic.List<IDamageable> FindTargets(Vector2 center, float radius, LayerMask layer)
    {
        var filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(layer);
        int count = Physics2D.OverlapCircle(center, radius, filter, buffer);
        var list = new System.Collections.Generic.List<IDamageable>();
        for (int i = 0; i < count; i++)
        {
            var target = buffer[i].GetComponentInParent<IDamageable>();
            if (target != null && target.IsAlive && !list.Contains(target))
                list.Add(target);
        }
        list.Sort((a, b) => ((Vector2)a.transform.position - center).sqrMagnitude
            .CompareTo(((Vector2)b.transform.position - center).sqrMagnitude));
        return list;
    }

    public static bool IsValid(IDamageable target) => target is Object o && o != null && target.IsAlive;
}
