using UnityEngine;

/// <summary>UI_atlas.png 에서 잘라 만든 UI 스킨 (TKGameBuilder 가 생성).</summary>
public class TKUISkin : ScriptableObject
{
    public Font font;
    [Header("Atlas (9-slice)")]
    public Sprite panel;        // 금테 큰 패널
    public Sprite card;         // 금 모서리 작은 패널 (선택 카드)
    public Sprite ribbon;       // 초록 리본 (제목)
    public Sprite ribbonPaper;  // 종이 리본 (이름표)
    public Sprite slot;         // 금 사각 슬롯
    public Sprite button;       // 주황 버튼
    public Sprite barFrame;     // 점선 게이지 프레임 (XP)
    public Sprite barThin;      // 얇은 게이지 프레임 (HP)
    public Sprite white;
    [Header("Icons")]
    public Sprite skull;
    public Sprite bossPortrait;
    [Header("Font sizes (canvas 단위)")]
    // 폰트 원본 16px 의 정수배만 사용 (깨짐 방지)
    public int headlineSize = 32;
    public int titleSize = 16;
    public int bodySize = 16;
    public int smallSize = 16;
}
