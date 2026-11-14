using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 뱀서식 보유 아이템 표시: 화면 왼쪽 위, 1줄 = 무기, 2줄 = 패시브(장갑/신발 등).
/// 아이템을 얻거나 레벨업하면 아이콘과 레벨이 갱신됨.
/// </summary>
public class InventoryHUD : MonoBehaviour
{
    public float slotSize = 40f;   // 16px 아이콘 x2 = 32 + 테두리
    public float gap = 2f;
    public int slotsPerRow = 6;

    Item[] items;
    readonly List<(Image icon, Text level)> weaponSlots = new List<(Image, Text)>();
    readonly List<(Image icon, Text level)> passiveSlots = new List<(Image, Text)>();
    int lastSignature = -1;

    void Start()
    {
        items = FindObjectsByType<Item>(FindObjectsInactive.Include);
        var skin = TKUIRoot.I.skin;
        for (int row = 0; row < 2; row++)
            for (int i = 0; i < slotsPerRow; i++)
            {
                var slot = TKUIRoot.NewImage($"Slot{row}_{i}", transform, skin.slot, new Color(1f, 1f, 1f, 0.55f), true); // 빈 슬롯은 흐리게
                var rt = slot.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(slotSize, slotSize);
                rt.anchoredPosition = new Vector2(i * (slotSize + gap), -row * (slotSize + gap));
                var icon = TKUIRoot.NewImage("Icon", rt, null, Color.white, false);
                TKUIRoot.IconOutline(icon);
                icon.enabled = false;
                var lv = TKUIRoot.I.NewText("Lv", rt, skin.smallSize, TextAnchor.LowerRight, Color.white);
                lv.rectTransform.offsetMin = new Vector2(0f, -6f);
                lv.rectTransform.offsetMax = new Vector2(4f, 0f);
                (row == 0 ? weaponSlots : passiveSlots).Add((icon, lv));
            }
    }

    void LateUpdate()
    {
        if (items == null) return;
        int signature = 0;
        foreach (var it in items) signature = signature * 31 + it.level;
        if (signature == lastSignature) return;
        lastSignature = signature;

        var owned = items.Where(i => i.level > 0 && i.data.itemType != ItemData.ItemType.Heal).ToList();
        bool IsWeapon(Item i) => i.data.itemType != ItemData.ItemType.Glove && i.data.itemType != ItemData.ItemType.Shoe;
        Fill(weaponSlots, owned.Where(IsWeapon).ToList());
        Fill(passiveSlots, owned.Where(i => !IsWeapon(i)).ToList());
    }

    static void Fill(List<(Image icon, Text level)> slots, List<Item> list)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            bool has = i < list.Count;
            slots[i].icon.enabled = has;
            slots[i].level.text = has ? list[i].level.ToString() : "";
            if (has) { slots[i].icon.sprite = list[i].data.iconSmall ? list[i].data.iconSmall : list[i].data.itemIcon; TKUIRoot.NativeSize(slots[i].icon); }
        }
    }
}
