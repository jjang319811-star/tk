using UnityEngine;

/// <summary>바닥 아이템 프리팹 보관 + 드롭 규칙 (씬에 1개).</summary>
public class PickupSpawner : MonoBehaviour
{
    public static PickupSpawner I { get; private set; }

    public FloorPickup healPrefab;
    public FloorPickup magnetPrefab;
    public FloorPickup chestPrefab;
    [Range(0f, 1f)] public float healChance = 0.55f;
    [Range(0f, 1f)] public float magnetChance = 0.25f;

    void Awake() => I = this;

    // 광원 파괴: 만두 55% / 자석 25% / 경험치 보석 묶음 20%
    public static void DropFromLightSource(Vector3 position)
    {
        if (I == null) return;
        float r = Random.value;
        if (r < I.healChance) Instantiate(I.healPrefab, position, Quaternion.identity);
        else if (r < I.healChance + I.magnetChance) Instantiate(I.magnetPrefab, position, Quaternion.identity);
        else for (int i = 0; i < 3; i++) XpGemSystem.Drop(position + (Vector3)(Random.insideUnitCircle * 0.25f), 2);
    }

    public static FloorPickup DropChest(Vector3 position, System.Action onOpened)
    {
        if (I == null || I.chestPrefab == null) { onOpened?.Invoke(); return null; }
        var chest = Instantiate(I.chestPrefab, position, Quaternion.identity);
        chest.Opened = onOpened;
        return chest;
    }
}
