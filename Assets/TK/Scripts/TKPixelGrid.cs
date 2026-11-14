using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 월드를 따라다니는 UI(데미지 숫자·체력바)를 게임 아트 픽셀 격자에 맞추는 유틸.
/// 화면 좌표를 'Pixel Perfect Camera 의 배율(아트 1px = 화면 N px)' 단위로 스냅 → 월드와 같은 박자로 움직여 흔들림이 없음.
/// </summary>
public static class TKPixelGrid
{
    static PixelPerfectCamera ppc;

    public static int ArtPixel
    {
        get
        {
            if (ppc == null && Camera.main) ppc = Camera.main.GetComponent<PixelPerfectCamera>();
            return ppc && ppc.isActiveAndEnabled ? Mathf.Max(1, ppc.pixelRatio) : 1;
        }
    }

    public static Vector2 SnapScreen(Vector2 screen)
    {
        float n = ArtPixel;
        return new Vector2(Mathf.Round(screen.x / n) * n, Mathf.Round(screen.y / n) * n);
    }
}
