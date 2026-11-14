using UnityEngine;
using System.Collections.Generic;

public class PoolManager : MonoBehaviour
{
    public GameObject[] prefabs;
    List<GameObject>[] pools;
    // 프리팹 참조 → 인덱스 (TK 이펙트들이 인덱스 없이 기존 풀을 공유하도록)
    readonly Dictionary<GameObject, int> indexOf = new Dictionary<GameObject, int>();

    void Awake()
    {
        pools = new List<GameObject>[prefabs.Length];
        for (int i = 0; i < pools.Length; i++)
        {
            pools[i] = new List<GameObject>();
            if (prefabs[i] != null && !indexOf.ContainsKey(prefabs[i]))
                indexOf.Add(prefabs[i], i);
        }
    }
    public GameObject Get(int prefabIndex)
    {
        GameObject select = null;

        foreach (GameObject item in pools[prefabIndex])
        {
            if (!item.activeSelf)
            {
                select = item;
                select.SetActive(true);
                break;
            }
        }
        if (select == null)
        {
            select = Instantiate(prefabs[prefabIndex], transform);
            pools[prefabIndex].Add(select);
        }
        return select;
    }

    /// <summary>프리팹으로 꺼내기. 등록되지 않은 프리팹은 끝에 자동 등록 (기존 인덱스는 유지)</summary>
    public GameObject Get(GameObject prefab)
    {
        if (!indexOf.TryGetValue(prefab, out int index))
        {
            index = prefabs.Length;
            System.Array.Resize(ref prefabs, index + 1);
            System.Array.Resize(ref pools, index + 1);
            prefabs[index] = prefab;
            pools[index] = new List<GameObject>();
            indexOf.Add(prefab, index);
        }
        return Get(index);
    }
}

/// <summary>TK 이펙트 생성 헬퍼: 기존 PoolManager 재사용 (없으면 Instantiate)</summary>
public static class TKPool
{
    public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        var pm = GameManager.instance ? GameManager.instance.poolManager : null;
        if (pm == null) return Object.Instantiate(prefab, position, rotation);
        // 비활성 상태에서 위치를 잡고 켜야 OnEnable 시점에 올바른 위치
        var go = pm.Get(prefab);
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = prefab.transform.localScale;
        return go;
    }

    public static T Spawn<T>(T prefab, Vector3 position, Quaternion rotation) where T : Component =>
        Spawn(prefab.gameObject, position, rotation).GetComponent<T>();

    public static void Release(GameObject go) => go.SetActive(false);
}
