using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// 픽셀 폰트가 깨지지 않도록 UI 배율을 '정수'로 고정. (PC 전용, 1920x1080 기준)
///  - 카메라 기준 480x270 → 1920x1080 에서 게임 zoom 4, UI 배율 2
///    → UI 1단위 = 화면 2px, 아틀라스/아이콘 1픽셀 = UI 2단위 = 화면 4px = 게임 아트 1픽셀
///  - 글자는 16 / 32 크기만 사용 (DOSMyungjo 원본 16px 의 정수배) → 1080p 에서 32px / 64px, 계단 모양 그대로 선명
/// </summary>
[RequireComponent(typeof(CanvasScaler))]
public class PixelUIScaler : MonoBehaviour
{
    public int refX = 480, refY = 270;

    CanvasScaler scaler;
    int lastW, lastH;

    public static int UIScale { get; private set; } = 1;

    void Awake()
    {
        scaler = GetComponent<CanvasScaler>();
        var ppc = FindAnyObjectByType<PixelPerfectCamera>();
        if (ppc) { refX = ppc.refResolutionX; refY = ppc.refResolutionY; }
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        Apply();
    }

    void Update()
    {
        if (Screen.width != lastW || Screen.height != lastH) Apply();
    }

    void Apply()
    {
        lastW = Screen.width;
        lastH = Screen.height;
        int zoom = Mathf.Max(1, Mathf.Min(Screen.width / Mathf.Max(1, refX), Screen.height / Mathf.Max(1, refY)));
        UIScale = Mathf.Max(1, zoom / 2);
        scaler.scaleFactor = UIScale;
    }
}
