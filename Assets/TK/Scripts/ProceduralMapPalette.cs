using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>절차적 맵이 쓰는 타일·장식·재질 묶음 (TKGameBuilder 가 생성).</summary>
[CreateAssetMenu(fileName = "ProceduralMapPalette", menuName = "TK/Procedural Map Palette")]
public class ProceduralMapPalette : ScriptableObject
{
    [Header("Ground tiles (32px, tile_01)")]
    public TileBase[] plainTiles;
    public TileBase[] lightTiles;
    public TileBase[] heavyTiles;

    [Header("Decor sprites")]
    public Sprite[] rocksSmall;
    public Sprite[] rocksBig;
    public Sprite[] crystalsBlue;
    public Sprite[] crystalsGreen;
    public Sprite[] mushrooms;
    [Tooltip("plantFrameCount 장씩 끊어서 애니메이션 1개")]
    public Sprite[] plantFrames;
    public int plantFrameCount = 6;

    [Header("Fire")]
    public Sprite[] bonfireFrames;
    public Sprite[] smallFireFrames;
    public Sprite[] blueFireFrames;

    [Header("Materials / prefabs")]
    public Material decorMaterial;
    public Material fireMaterial;
    public Material fireBlueMaterial;
    public GameObject fireLightPrefab;
}
