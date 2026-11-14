using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WeaponEffect : MonoBehaviour
{
    const float LightningImpactTime = 23f / 30f;
    const float FireDuration = 45f / 30f;
    const float LightningDuration = 91f / 30f;
    const float FireHalfAngle = 30f;
    // Burn tick: hits at 0, 0.5, 1.0s of the 1.5s breath (3 hits per cast).
    const float FireTickInterval = 0.5f;

    Animator animator;
    Transform followTarget;

    void Awake()
    {
        animator = GetComponent<Animator>();
    }

    void OnDisable()
    {
        followTarget = null;
    }

    void LateUpdate()
    {
        if (followTarget != null)
            transform.position = followTarget.position;
    }

    public void PlayFire(Transform owner, Vector2 direction, float scale, float range, float damage, LayerMask targetLayer)
    {
        Vector2 origin = owner.position;
        followTarget = owner;
        transform.position = origin;
        transform.rotation = Quaternion.FromToRotation(Vector3.right, direction);
        transform.localScale = Vector3.one * scale;
        animator.Play(0, 0, 0f);
        animator.Update(0f);
        StartCoroutine(Burn(owner, direction, range, damage, targetLayer));
    }

    IEnumerator Burn(Transform owner, Vector2 direction, float range, float damage, LayerMask targetLayer)
    {
        float elapsed = 0f;
        while (elapsed < FireDuration)
        {
            if (GameManager.instance.isLive && owner != null)
                DamageEnemies(owner.position, range, damage, targetLayer, direction, true);
            float wait = Mathf.Min(FireTickInterval, FireDuration - elapsed);
            yield return new WaitForSeconds(wait);
            elapsed += wait;
        }
        gameObject.SetActive(false);
    }

    public void PlayLightning(Vector2 position, float scale, float radius, float damage, LayerMask targetLayer)
    {
        followTarget = null;
        transform.position = position;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one * scale;
        animator.Play(0, 0, 0f);
        animator.Update(0f);
        StartCoroutine(Strike(position, radius, damage, targetLayer));
    }

    IEnumerator Strike(Vector2 position, float radius, float damage, LayerMask targetLayer)
    {
        yield return new WaitForSeconds(LightningImpactTime);
        while (!GameManager.instance.isLive && Time.timeScale == 0f)
            yield return null;
        if (GameManager.instance.isLive)
            DamageEnemies(position, radius, damage, targetLayer, Vector2.zero, false);
        yield return new WaitForSeconds(LightningDuration - LightningImpactTime);
        gameObject.SetActive(false);
    }

    // 범위 피해는 공용 TKCombat.DamageCircle(비할당 버퍼, 일반 적+보스 공통)로 통합
    static void DamageEnemies(Vector2 origin, float radius, float damage, LayerMask targetLayer, Vector2 direction, bool cone)
    {
        TKCombat.DamageCircle(origin, radius, damage, targetLayer, null, enemy =>
        {
            if (!cone) return true;
            Vector2 offset = (Vector2)enemy.transform.position - origin;
            return offset.sqrMagnitude <= radius * radius &&
                   (offset == Vector2.zero || Vector2.Angle(direction, offset) <= FireHalfAngle);
        });
    }
}
