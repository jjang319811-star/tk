using UnityEngine;

/// <summary>
/// 무쌍류 대군 이벤트 (뱀서의 웨이브 이벤트 + 무쌍의 '몰려오는 군세')
///  - 대열 돌격(Charge): 화면 한쪽 밖에 밀집 대형으로 한꺼번에 등장해 공명을 추격
///  - 포위진(Encircle): 화면 사방에 원형으로 동시에 등장해 조여옴
///  - 관통 돌진(PassThrough): 일직선으로 화면을 휩쓸고 지나감 (플레이어를 쫓지 않음)
///  보스전 중에도 일정 간격으로 포위진이 와서 긴장감 유지.
/// </summary>
public class EventDirector : MonoBehaviour
{
    public enum Kind { Charge, Encircle, PassThrough }

    [System.Serializable]
    public struct WaveEvent
    {
        public float time;
        public Kind kind;
        public int count;
        public WaveEvent(float t, Kind k, int c) { time = t; kind = k; count = c; }
    }

    public WaveEvent[] events =
    {
        new WaveEvent(4f, Kind.Charge, 18),
        new WaveEvent(9f, Kind.Encircle, 20),
        new WaveEvent(12f, Kind.PassThrough, 16),
        new WaveEvent(16f, Kind.Charge, 28),
        new WaveEvent(20f, Kind.Encircle, 28),
        new WaveEvent(23f, Kind.PassThrough, 20),
        new WaveEvent(26f, Kind.Charge, 36),
    };
    public float passSpeed = 1.8f;
    public float formationSpacing = 0.34f;
    [Tooltip("보스전 중 포위진 간격(초)")]
    public float bossWaveInterval = 12f;
    public int bossWaveCount = 16;

    int next;
    float bossTimer;

    void Update()
    {
        var gm = GameManager.instance;
        if (!gm.isLive) return;

        if (gm.bossFightActive)
        {
            bossTimer += Time.deltaTime;
            if (bossTimer >= bossWaveInterval)
            {
                bossTimer = 0f;
                Spawn(new WaveEvent(0f, Kind.Encircle, bossWaveCount));
            }
            return;
        }

        if (next < events.Length && gm.gameTime >= events[next].time)
            Spawn(events[next++]);
    }

    void Spawn(WaveEvent e)
    {
        var spawner = FindAnyObjectByType<Spawner>(FindObjectsInactive.Include);
        if (spawner == null || spawner.spawnDatas.Length == 0 || !CameraView.TryGetWorldRect(0f, 0.6f, out Rect view)) return;
        switch (e.kind)
        {
            case Kind.Charge: Charge(spawner, view, e.count); break;
            case Kind.Encircle: Encircle(spawner, view, e.count); break;
            default: PassThrough(spawner, view, e.count); break;
        }
    }

    static Vector2 Side(int side) => side switch { 0 => Vector2.right, 1 => Vector2.left, 2 => Vector2.down, _ => Vector2.up };

    Enemy SpawnEnemy(Spawner spawner, Vector2 position, int dataIndex = 0)
    {
        var go = GameManager.instance.poolManager.Get(0);
        go.transform.position = new Vector3(Mathf.Round(position.x * 64f) / 64f, Mathf.Round(position.y * 64f) / 64f, 0f);
        var rb = go.GetComponent<Rigidbody2D>();
        if (rb) rb.position = go.transform.position;
        var enemy = go.GetComponent<Enemy>();
        enemy.Init(spawner.spawnDatas[Mathf.Clamp(dataIndex, 0, spawner.spawnDatas.Length - 1)]);
        return enemy;
    }

    // 대열 돌격: 화면 밖 한쪽에 직사각형 밀집 대형 → 모두 추격
    void Charge(Spawner spawner, Rect view, int count)
    {
        int side = Random.Range(0, 4);
        Vector2 dir = Side(side);             // 진행 방향 (화면 안쪽)
        Vector2 across = new Vector2(-dir.y, dir.x);
        Vector2 edge = view.center - dir * ((dir.x != 0f ? view.width : view.height) * 0.5f + 0.3f);
        int cols = Mathf.CeilToInt(Mathf.Sqrt(count * 2f));
        for (int i = 0; i < count; i++)
        {
            int row = i / cols, col = i % cols;
            Vector2 p = edge + across * ((col - (cols - 1) / 2f) * formationSpacing) - dir * (row * formationSpacing);
            SpawnEnemy(spawner, p, i % 9 == 8 ? 1 : 0); // 9명 중 1명은 궁병
        }
        TKUIRoot.Announce("동탁군 대열이 돌격한다!", 1.8f);
        GameFeel.Shake(0.04f, 0.4f);
    }

    // 포위진: 화면 바깥 원형으로 동시에 등장 → 사방에서 조여옴
    void Encircle(Spawner spawner, Rect view, int count)
    {
        Vector2 c = GameManager.instance.player.transform.position;
        float radius = Mathf.Max(view.width, view.height) * 0.5f + 0.2f;
        float offset = Random.Range(0f, 360f);
        for (int i = 0; i < count; i++)
        {
            float a = (offset + i * 360f / count) * Mathf.Deg2Rad;
            SpawnEnemy(spawner, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
        }
        TKUIRoot.Announce("포위진! 사방에서 몰려온다!", 1.8f);
        GameFeel.Shake(0.05f, 0.45f);
    }

    // 관통 돌진: 일직선으로 화면을 가로지름
    void PassThrough(Spawner spawner, Rect view, int count)
    {
        int side = Random.Range(0, 4);
        Vector2 dir = Side(side);
        Vector2 start = view.center - dir * ((dir.x != 0f ? view.width : view.height) * 0.5f);
        Vector2 across = new Vector2(-dir.y, dir.x);
        float span = (dir.x != 0f ? view.height : view.width) * 0.8f;
        float travel = (dir.x != 0f ? view.width : view.height) + 1.5f;
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0.5f : i / (float)(count - 1);
            Vector2 p = start + across * Mathf.Lerp(-span / 2f, span / 2f, t) - dir * (i % 2) * 0.35f;
            SpawnEnemy(spawner, p).SetSwarm(dir, passSpeed, travel / passSpeed);
        }
        TKUIRoot.Announce("동탁군이 몰려온다!", 1.8f);
        GameFeel.Shake(0.03f, 0.4f);
    }
}
