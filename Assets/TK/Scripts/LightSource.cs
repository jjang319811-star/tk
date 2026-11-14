using UnityEngine;

/// <summary>
/// 뱀서의 '광원(화로)'. 맵의 불(모닥불/불돌)을 무기로 맞히면 꺼지면서 아이템을 떨어뜨림.
/// ProceduralMap 이 불 오브젝트에 붙이고, 청크를 다시 불러오면 새로 생김.
/// </summary>
[RequireComponent(typeof(CircleCollider2D))]
public class LightSource : MonoBehaviour
{
    public GameObject lightObject;
    bool broken;

    public void ResetSource(GameObject light)
    {
        lightObject = light;
        broken = false;
        var col = GetComponent<CircleCollider2D>();
        col.enabled = true;
        enabled = true;
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (broken || !collision.CompareTag("Bullet") || !GameManager.instance.isLive) return;
        Break();
    }

    public void Break()
    {
        broken = true;
        GetComponent<CircleCollider2D>().enabled = false;
        var sr = GetComponent<SpriteRenderer>();
        if (sr) sr.enabled = false;
        if (lightObject) lightObject.SetActive(false);
        GameFeel.DeathPop(transform.position);
        GameFeel.Shake(0.03f, 0.12f);
        PickupSpawner.DropFromLightSource(transform.position);
    }
}
