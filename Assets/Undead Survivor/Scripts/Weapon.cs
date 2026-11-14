using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class Weapon : MonoBehaviour
{
    public int id;
    public int prefabId;
    public float damage;
    public int count;
    public float speed;
    // Update is called once per frame
    float timer;
    Player player;
    ItemData data;

    public bool IsAreaWeapon => data != null &&
        (data.itemType == ItemData.ItemType.FireBreath || data.itemType == ItemData.ItemType.LightningStrike);

    void Awake()
    {
        player = GameManager.instance.player;
    }


    void Update()
    {
        if (!GameManager.instance.isLive)
            return;
        switch (id)
        {
            case 0:
                transform.Rotate(Vector3.back * speed * Time.deltaTime);
                for (int i = 0; i < transform.childCount; i++)
                {
                    transform.GetChild(i).rotation = Quaternion.identity;
                }
                break;
            default:
                timer += Time.deltaTime;
                if (timer >= speed)
                {
                    timer = 0;
                    if (IsAreaWeapon)
                        FireAreaWeapon();
                    else
                        Fire();
                }
                break;
        }
    }
    public void LevelUp(float damage, int count)
    {
        this.damage = damage;
        this.count += count;
        
        if (id ==0)
        {
            Batch();
        }
        player.BroadcastMessage("ApplyGear", SendMessageOptions.DontRequireReceiver);
    }

    public void Init(ItemData data)
    {
        //basic set
        name = "Weapon_" + data.itemId;
        transform.parent = player.transform;
        transform.localPosition = Vector3.zero;


        //property set
        id = data.itemId;
        this.data = data;
        damage = data.baseDamage;
        count = data.baseCount;

        bool foundPrefab = false;
        for (int i = 0; i < GameManager.instance.poolManager.prefabs.Length; i++)
        {
            if (data.projectile == GameManager.instance.poolManager.prefabs[i])
            {
                prefabId = i;
                foundPrefab = true;
                break;
            }
        }
        if (IsAreaWeapon && !foundPrefab)
        {
            Debug.LogError("Weapon projectile is not registered in PoolManager: " + data.name, this);
            enabled = false;
            return;
        }

        switch (id)
        {
            case 0:
                speed = 150;
                Batch();
                break;
            default:
                speed = 0.3f;
                break;

        }
        if (IsAreaWeapon)
            speed = data.attackInterval;
        else
        {
            Hand hand = player.hands[(int)data.itemType];
            hand.sr.sprite = data.hand;
            hand.gameObject.SetActive(true);
        }

        player.BroadcastMessage("ApplyGear", SendMessageOptions.DontRequireReceiver);
    }

    public void SetAreaAttackRate(float rate)
    {
        speed = data.attackInterval * (1f - rate);
    }

    void FireAreaWeapon()
    {
        Vector2 origin = player.transform.position;
        // 공용 비할당 탐색(TKCombat) 사용 — 일반 적 + 보스, 가까운 순 정렬
        List<IDamageable> enemies = TKCombat.FindTargets(origin, player.scanner.scanRange, player.scanner.targetLayer);

        if (enemies.Count == 0)
            return;

        if (data.itemType == ItemData.ItemType.FireBreath)
        {
            IDamageable nearest = enemies[0];
            float nearestDistance = ((Vector2)nearest.transform.position - origin).sqrMagnitude;
            foreach (IDamageable enemy in enemies)
            {
                float distance = ((Vector2)enemy.transform.position - origin).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearest = enemy;
                    nearestDistance = distance;
                }
            }

            // 손을 내지르며 손끝에서 화염 분사 (이펙트는 손 앵커를 따라감)
            player.PlayAttack(true, nearest.transform.position);
            Vector2 hand = player.HandPosition;
            Vector2 direction = ((Vector2)nearest.transform.position - hand).normalized;
            if (direction == Vector2.zero)
                direction = Vector2.right;
            float levelScale = 1f + count * 0.01f;
            // 시각 크기는 정수 배율(픽셀 비율 유지) 고정, 레벨에 따라 피해 범위만 증가
            WeaponEffect effect = GameManager.instance.poolManager.Get(prefabId).GetComponent<WeaponEffect>();
            effect.PlayFire(player.HandAnchor, direction, Mathf.Max(1f, Mathf.Round(data.effectScale)), data.attackRange * levelScale, damage, player.scanner.targetLayer);
            return;
        }

        player.PlayAttack(true, enemies[0].transform.position); // 손을 들어 천뢰를 부름
        List<Vector2> positions = new List<Vector2>();
        while (enemies.Count > 0 && positions.Count < count)
        {
            int index = Random.Range(0, enemies.Count);
            Vector2 position = enemies[index].transform.position;
            enemies.RemoveAt(index);
            bool duplicate = false;
            foreach (Vector2 selected in positions)
            {
                if ((selected - position).sqrMagnitude < 0.0001f)
                {
                    duplicate = true;
                    break;
                }
            }
            if (duplicate)
                continue;
            positions.Add(position);
            WeaponEffect effect = GameManager.instance.poolManager.Get(prefabId).GetComponent<WeaponEffect>();
            effect.PlayLightning(position, Mathf.Max(1f, Mathf.Round(data.effectScale)), data.areaRadius, damage, player.scanner.targetLayer);
        }
    }
    void Batch()
    {
        for (int i = 0; i < count; i++)
        {
            Transform bullet;
            if (i < transform.childCount)
            {
                bullet = transform.GetChild(i);
            }
            else
            {
                bullet = GameManager.instance.poolManager.Get(prefabId).transform;
                bullet.parent = transform;
            }

            float angle = i * 360f / count;
            bullet.localPosition = Quaternion.Euler(0f, 0f, angle) * Vector3.up * 0.65f; // 공명(64px) 몸체 바깥으로 회전
            bullet.rotation = Quaternion.identity;
            bullet.GetComponent<Bullet>().Init(damage, -1, bullet.up); // -1 is infinity pierce.
        }
    }
    void Fire()
    {
        if (!(player.scanner.nearestTarget != null))
            return;

        // 손끝에서 발사 + 시전 동작
        Vector3 targetPos = player.scanner.nearestTarget.position;
        player.PlayAttack(true, targetPos);
        Vector3 origin = player.HandPosition;
        Vector3 dir = (targetPos - origin).normalized;
        Transform bullet = GameManager.instance.poolManager.Get(prefabId).transform;
        bullet.position = origin;
        bullet.rotation = Quaternion.FromToRotation(Vector3.up, dir);
        bullet.GetComponent<Bullet>().Init(damage, count, dir);
    }
}
