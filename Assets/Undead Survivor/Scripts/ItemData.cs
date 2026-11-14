using UnityEngine;

[CreateAssetMenu(fileName = "Item", menuName = "ScriptableObjects/ItemData")]
public class ItemData : ScriptableObject
{
    public enum ItemType
    {
        Melee, Ranged, Glove, Shoe, Heal, FireBreath, LightningStrike
    }
    //아이템 데이터 관리
    [Header("# Main Info")]
    public ItemType itemType;
    public int itemId;
    public string itemName;
    [TextArea]
    public string itemDesc;
    [Tooltip("처음 얻을 때(신규) 보여 줄 소개문. 비어 있으면 itemDesc 사용")]
    [TextArea]
    public string introDesc;
    public Sprite itemIcon;
    [Tooltip("16px 소형 아이콘 (인벤토리 슬롯용, 원본에서 픽셀 단위로 축소 생성)")]
    public Sprite iconSmall;
    [Header("# Level Data")]
    public float baseDamage;
    public int baseCount;
    public float[] damages;
    public int[] counts;
    [Header("# Weapon")]
    public GameObject projectile;
    public Sprite hand;
    [Header("# Area Weapon")]
    public float attackInterval;
    public float attackRange;
    public float areaRadius;
    public float effectScale = 1f;

}
