using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보스전 진행
///  1) spawnTime 도달 → 게임 정지 + 등장 컷인 (화면 암전, 붉은 띠, 여포 실루엣이 미끄러져 들어오며 이름 표시)
///  2) 여포 등장, 일반 스폰 중지, 화면 하단에 보스 체력바 (UI_atlas 프레임)
///  3) 처치 → 보물상자 드롭(뱀서 룰) → 상자 룰렛을 닫으면 승리
/// </summary>
public class BossDirector : MonoBehaviour
{
    public LuBuBoss bossPrefab;
    public float spawnTime = 30f;
    public string bossName = "여포 봉선";
    public string bossTitle = "인중여포 마중적토";

    LuBuBoss boss;
    bool spawned;
    RectTransform barRoot, fill;
    Text statusText;

    void Update()
    {
        var gm = GameManager.instance;
        if (!spawned && gm.isLive && gm.gameTime >= spawnTime)
        {
            spawned = true;
            StartCoroutine(SpawnRoutine());
        }

        if (boss && fill)
        {
            fill.anchorMax = new Vector2(boss.Health01, 1f);
            statusText.text = boss.Stunned ? "기절!" : boss.ShieldHits > 0 ? $"무쌍 갑옷 {boss.ShieldHits}" : (boss.Phase2 ? "분노" : "");
        }
    }

    IEnumerator SpawnRoutine()
    {
        var gm = GameManager.instance;
        gm.bossFightActive = true;
        var spawner = FindAnyObjectByType<Spawner>();
        if (spawner) spawner.enabled = false;

        yield return CutIn();

        Vector3 pos = gm.player.transform.position + Vector3.up * 2f;
        if (CameraView.TryGetWorldRect(0f, 0.4f, out Rect view))
            pos = new Vector3(gm.player.transform.position.x, view.yMax, 0f);
        boss = Instantiate(bossPrefab, pos, Quaternion.identity);
        boss.Announce = msg => TKUIRoot.Announce(msg, 2.2f);
        boss.Died = OnBossDied;
        BuildBar();
        GameFeel.Shake(0.06f, 0.5f);
    }

    // 등장 컷인 (실제 시간 기준, 게임은 정지)
    // 여포 초상은 원본 픽셀 비율 그대로 정수 2배 (원본 1px = 게임 아트 2px), 이동도 화면 픽셀 단위로 스냅
    IEnumerator CutIn()
    {
        var gm = GameManager.instance;
        var ui = TKUIRoot.I;
        if (ui == null) yield break;
        var skin = ui.skin;
        gm.Stop();

        var root = TKUIRoot.Stretch("BossCutIn", ui.canvas.transform);
        var dim = root.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0f);

        var band = TKUIRoot.NewImage("Band", root, skin.white, new Color(0.55f, 0.05f, 0.05f, 0.92f), false);
        var br = band.rectTransform;
        br.anchorMin = new Vector2(0f, 0.5f);
        br.anchorMax = new Vector2(1f, 0.5f);
        br.sizeDelta = new Vector2(0f, 120f);
        var bandEdge = TKUIRoot.NewImage("Edge", band.transform, skin.white, new Color(1f, 0.75f, 0.3f, 1f), false);
        bandEdge.rectTransform.anchorMin = new Vector2(0f, 0f);
        bandEdge.rectTransform.anchorMax = new Vector2(1f, 0f);
        bandEdge.rectTransform.sizeDelta = new Vector2(0f, TKUIRoot.UnitsPerPixel * 2f);

        var portrait = TKUIRoot.NewImage("Portrait", root, skin.bossPortrait, new Color(0.15f, 0.02f, 0.02f, 1f), false);
        TKUIRoot.IconOutline(portrait);
        var pr = portrait.rectTransform;
        pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
        pr.pivot = new Vector2(0.5f, 0.5f);
        // 비율 유지 + 정수 2배 확대 (원본 93x114 → 찌그러짐 없음)
        pr.sizeDelta = skin.bossPortrait.rect.size * TKUIRoot.UnitsPerPixel * 2f;

        var title = ui.NewText("Title", band.transform, skin.bodySize, TextAnchor.UpperLeft, new Color(1f, 0.8f, 0.5f));
        title.rectTransform.offsetMin = new Vector2(16f, 0f);
        title.rectTransform.offsetMax = new Vector2(0f, -18f);
        title.text = bossTitle;
        var name = ui.NewText("Name", band.transform, skin.headlineSize, TextAnchor.LowerLeft, Color.white);
        name.rectTransform.offsetMin = new Vector2(16f, 22f);
        name.text = bossName;

        float px = TKUIRoot.UnitsPerPixel;
        float Snap(float v) => Mathf.Round(v / px) * px;
        float t = 0f;
        while (t < 2.4f)
        {
            t += Time.unscaledDeltaTime;
            // 미끄러지는 보간 대신 단계(0.05초 박자)로 끊어 들어오기
            float step = Mathf.Floor(t / 0.05f) * 0.05f;
            float inK = Mathf.Clamp01(step / 0.3f);
            float outK = Mathf.Clamp01((step - 2.0f) / 0.3f);
            dim.color = new Color(0f, 0f, 0f, 0.7f * inK * (1f - outK));
            br.anchoredPosition = new Vector2(Snap(Mathf.Lerp(-900f, 0f, inK) + outK * 900f), 0f);
            float pk = Mathf.Clamp01((step - 0.15f) / 0.35f);
            pr.anchoredPosition = new Vector2(Snap(Mathf.Lerp(700f, 120f, pk) - outK * 900f), Snap(40f));
            // 등장 순간 실루엣 → 원래 색으로 번쩍 (단계 전환)
            portrait.color = t > 0.9f && outK < 1f ? Color.white : new Color(0.15f, 0.02f, 0.02f, 1f);
            yield return null;
        }
        Destroy(root.gameObject);
        gm.Resume();
    }

    void BuildBar()
    {
        var ui = TKUIRoot.I;
        if (ui == null || barRoot) return;
        var skin = ui.skin;
        float px = TKUIRoot.UnitsPerPixel;

        // 화면 하단 중앙: [이름표 리본] + [아틀라스 게이지 프레임 안 붉은 바] — 테두리는 원본 1px = 2단위
        barRoot = TKUIRoot.NewRect("BossBar", ui.canvas.transform, new Vector2(0.08f, 0f), new Vector2(0.92f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 12f * px), new Vector2(0f, skin.barFrame.rect.height * px));
        var frame = TKUIRoot.NewImage("Frame", barRoot, skin.barFrame, Color.white, true);
        frame.rectTransform.anchorMin = Vector2.zero;
        frame.rectTransform.anchorMax = Vector2.one;
        frame.rectTransform.sizeDelta = Vector2.zero;
        var track = TKUIRoot.NewRect("Track", barRoot, Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        track.offsetMin = new Vector2(3f * px, 3f * px);
        track.offsetMax = new Vector2(-3f * px, -3f * px);
        fill = TKUIRoot.NewRect("Fill", track, Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        var fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.sprite = skin.white;
        fillImg.color = new Color(0.86f, 0.14f, 0.12f, 1f);
        fillImg.raycastTarget = false;

        var tag = TKUIRoot.NewImage("NameTag", barRoot, skin.ribbonPaper, Color.white, true);
        var tr = tag.rectTransform;
        tr.anchorMin = tr.anchorMax = new Vector2(0f, 1f);
        tr.pivot = new Vector2(0f, 0f);
        tr.sizeDelta = new Vector2(64f * px, skin.ribbonPaper.rect.height * px);
        tr.anchoredPosition = new Vector2(0f, 0f);
        var name = ui.NewText("Name", tag.transform, skin.bodySize, TextAnchor.MiddleCenter, new Color(0.25f, 0.12f, 0.05f));
        foreach (var e in name.GetComponents<Shadow>()) Destroy(e); // 종이 이름표 위 글자는 효과 없이
        name.text = bossName;

        statusText = ui.NewText("Status", barRoot, skin.bodySize, TextAnchor.LowerRight, new Color(1f, 0.85f, 0.4f));
        statusText.rectTransform.offsetMin = new Vector2(0f, 16f * px);
        statusText.rectTransform.offsetMax = new Vector2(-4f, 16f * px);
    }
    void OnBossDied()
    {
        TKUIRoot.Announce("여포를 토벌했다!", 3f);
        if (barRoot) barRoot.gameObject.SetActive(false);
        GameFeel.Shake(0.12f, 0.6f);
        GameFeel.HitStop(0.25f);
        StartCoroutine(DropChestLater());
    }

    IEnumerator DropChestLater()
    {
        yield return new WaitForSeconds(1f);
        var gm = GameManager.instance;
        Vector3 pos = boss ? boss.transform.position : gm.player.transform.position + Vector3.up;
        // 상자는 플레이어 근처로 (멀리 떨어지면 줍기 번거로움)
        pos = Vector3.Lerp(pos, gm.player.transform.position, 0.6f);
        if (XpGemSystem.I) XpGemSystem.I.VacuumAll();
        PickupSpawner.DropChest(pos, () => StartCoroutine(WinLater()));
    }

    IEnumerator WinLater()
    {
        yield return new WaitForSeconds(0.8f);
        GameManager.instance.bossFightActive = false;
        GameManager.instance.GameWin();
    }
}
