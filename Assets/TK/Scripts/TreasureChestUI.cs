using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 뱀서식 보물상자 룰렛: 게임 정지 → 슬롯들이 아이템 아이콘을 빠르게 돌리다 하나씩 멈추며 업그레이드 지급,
/// 금빛 파편이 분수처럼 튐 → 확인(또는 자동 닫힘) 후 재개.
/// </summary>
public static class TreasureChestUI
{
    public static void Open(System.Action onClosed)
    {
        if (TKUIRoot.I == null) { onClosed?.Invoke(); return; }
        TKUIRoot.I.StartCoroutine(Run(onClosed));
    }

    static IEnumerator Run(System.Action onClosed)
    {
        var gm = GameManager.instance;
        var ui = TKUIRoot.I;
        var skin = ui.skin;
        gm.Stop();

        var root = TKUIRoot.Stretch("TreasureChest", ui.canvas.transform);
        var dim = root.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.6f);

        var panel = TKUIRoot.NewImage("Panel", root, skin.panel, Color.white, true);
        panel.rectTransform.sizeDelta = new Vector2(340f, 216f);

        var ribbon = TKUIRoot.NewImage("Ribbon", panel.transform, skin.ribbon, Color.white, true);
        var rr = ribbon.rectTransform;
        rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 1f);
        rr.sizeDelta = new Vector2(200f, skin.ribbon.rect.height * TKUIRoot.UnitsPerPixel);
        rr.anchoredPosition = new Vector2(0f, -4f);
        var title = ui.NewText("Title", ribbon.transform, skin.titleSize, TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.6f));
        title.text = "황제의 하사품!";

        // 지급할 업그레이드 결정
        var items = Object.FindObjectsByType<Item>(FindObjectsInactive.Include).ToList();
        var candidates = items.Where(i => i.data.itemType != ItemData.ItemType.Heal && i.level < i.data.damages.Length).ToList();
        var heal = items.FirstOrDefault(i => i.data.itemType == ItemData.ItemType.Heal);
        int count = Random.value < 0.25f ? 5 : 3;
        var results = new List<Item>();
        for (int i = 0; i < count; i++)
        {
            var pool = candidates.Where(c => c.level + results.Count(r => r == c) < c.data.damages.Length).ToList();
            results.Add(pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : heal);
        }
        var allIcons = items.Where(i => i.data.itemIcon).Select(i => i.data.itemIcon).ToArray();

        // 슬롯
        float slotSize = 52f, gap = 8f, total = count * slotSize + (count - 1) * gap; // 아이콘 32px x2 = 64 가 들어가도록 슬롯은 9-slice
        panel.rectTransform.sizeDelta = new Vector2(Mathf.Max(340f, count * 90f + 60f), 216f);
        var icons = new Image[count];
        var slots = new RectTransform[count];
        for (int i = 0; i < count; i++)
        {
            var slot = TKUIRoot.NewImage("Slot", panel.transform, skin.slot, Color.white, true);
            slot.rectTransform.sizeDelta = new Vector2(slotSize, slotSize);
            slot.rectTransform.sizeDelta = new Vector2(76f, 76f);
            slot.rectTransform.anchoredPosition = new Vector2((i - (count - 1) / 2f) * 90f, 8f);
            slots[i] = slot.rectTransform;
            var icon = TKUIRoot.NewImage("Icon", slot.transform, allIcons.Length > 0 ? allIcons[0] : null, Color.white, false);
            TKUIRoot.NativeSize(icon); // 32px 아이콘 → 64 단위 (정수 배율, 비율 유지)
            TKUIRoot.IconOutline(icon);
            icons[i] = icon;
        }

        var resultText = ui.NewText("Result", panel.transform, skin.smallSize, TextAnchor.MiddleCenter, Color.white);
        resultText.rectTransform.anchorMin = new Vector2(0f, 0f);
        resultText.rectTransform.anchorMax = new Vector2(1f, 0f);
        resultText.rectTransform.sizeDelta = new Vector2(-16f, 20f);
        resultText.rectTransform.anchoredPosition = new Vector2(0f, 62f);
        resultText.horizontalOverflow = HorizontalWrapMode.Wrap;

        // 금빛 파편
        var coins = new List<(RectTransform rt, Vector2 vel)>();
        for (int i = 0; i < 28; i++)
        {
            var c = TKUIRoot.NewImage("Coin", root, skin.white, new Color(1f, Random.Range(0.7f, 0.9f), 0.2f), false);
            c.rectTransform.sizeDelta = Vector2.one * TKUIRoot.UnitsPerPixel * Random.Range(1, 3); // 1~2px 금 조각
            c.rectTransform.anchoredPosition = new Vector2(0f, -10f);
            coins.Add((c.rectTransform, new Vector2(Random.Range(-160f, 160f), Random.Range(180f, 380f))));
        }

        var stopFlash = new float[count];
        float t = 0f, cycle = 0f;
        int stopped = 0;
        var names = new List<string>();
        while (stopped < count)
        {
            float dt = Time.unscaledDeltaTime;
            t += dt;
            cycle += dt;
            if (cycle > 0.06f)
            {
                cycle = 0f;
                for (int i = stopped; i < count; i++)
                    if (allIcons.Length > 0) { icons[i].sprite = allIcons[Random.Range(0, allIcons.Length)]; TKUIRoot.NativeSize(icons[i]); }
            }
            if (t > 0.9f + stopped * 0.35f)
            {
                var item = results[stopped];
                icons[stopped].sprite = item.data.itemIcon;
                TKUIRoot.NativeSize(icons[stopped]);
                stopFlash[stopped] = 0.12f; // 멈출 때 슬롯 번쩍 (크기 변화 대신)
                item.OnClick();
                names.Add(item.data.itemName);
                resultText.text = string.Join("  ", names);
                stopped++;
            }
            for (int i = 0; i < count; i++)
            {
                stopFlash[i] -= dt;
                slots[i].GetComponent<Image>().color = stopFlash[i] > 0f ? new Color(1f, 0.95f, 0.5f) : Color.white;
            }
            for (int i = 0; i < coins.Count; i++)
            {
                var (rt, vel) = coins[i];
                vel.y -= 620f * dt;
                rt.anchoredPosition += vel * dt;
                rt.anchoredPosition = new Vector2(Mathf.Round(rt.anchoredPosition.x / 2f) * 2f, Mathf.Round(rt.anchoredPosition.y / 2f) * 2f);
                coins[i] = (rt, vel);
            }
            yield return null;
        }

        // 확인 버튼 (3초 후 자동 닫힘)
        var button = TKUIRoot.NewImage("Button", panel.transform, skin.button, Color.white, true);
        button.raycastTarget = true;
        var br = button.rectTransform;
        br.anchorMin = br.anchorMax = new Vector2(0.5f, 0f);
        br.sizeDelta = new Vector2(120f, 44f);
        br.anchoredPosition = new Vector2(0f, 26f);
        var bt = ui.NewText("Text", button.transform, skin.bodySize, TextAnchor.MiddleCenter, Color.white);
        bt.text = "받는다";
        bool closed = false;
        button.gameObject.AddComponent<Button>().onClick.AddListener(() => closed = true);
        float wait = 0f;
        while (!closed && wait < 3f)
        {
            wait += Time.unscaledDeltaTime;
            yield return null;
        }

        Object.Destroy(root.gameObject);
        if (!gm.ShowPendingLevelUp()) gm.Resume();
        onClosed?.Invoke();
    }
}
