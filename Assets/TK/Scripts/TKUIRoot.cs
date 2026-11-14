using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 런타임 UI 공용 진입점: 스킨, 캔버스, 화면 알림 텍스트, UI 생성 헬퍼.
/// </summary>
public class TKUIRoot : MonoBehaviour
{
    public static TKUIRoot I { get; private set; }

    public TKUISkin skin;
    public Canvas canvas;

    Text announce;
    float announceTimer;

    void Awake()
    {
        I = this;
        if (canvas == null)
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Include))
                if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay) { canvas = c; break; }
    }

    void Start()
    {
        announce = NewText("Announce", canvas.transform, skin.bodySize, TextAnchor.MiddleCenter, new Color(1f, 0.88f, 0.5f));
        var r = announce.rectTransform;
        r.anchorMin = new Vector2(0f, 0.7f);
        r.anchorMax = new Vector2(1f, 0.7f);
        r.sizeDelta = new Vector2(-20f, 40f);
        announce.horizontalOverflow = HorizontalWrapMode.Wrap;
        announce.enabled = false;
    }

    void Update()
    {
        if (!announce) return;
        announceTimer -= Time.unscaledDeltaTime;
        announce.enabled = announceTimer > 0f && (announceTimer > 0.6f || Mathf.Repeat(announceTimer, 0.2f) > 0.1f);
    }

    public static void Announce(string message, float duration = 2.2f)
    {
        if (I == null || I.announce == null) return;
        I.announce.text = message;
        I.announceTimer = duration;
        I.announce.transform.SetAsLastSibling();
    }

    // ------------------------------------------------------------------ factory

    public static RectTransform NewRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    public static RectTransform Stretch(string name, Transform parent)
    {
        var rt = NewRect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        return rt;
    }

    public static Image NewImage(string name, Transform parent, Sprite sprite, Color color, bool sliced)
    {
        var rt = NewRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.type = sliced && sprite && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        img.raycastTarget = false;
        img.pixelsPerUnitMultiplier = PixelMultiplier(sprite);
        return img;
    }

    /// <summary>UI 픽셀 규격: 원본 1픽셀 = UI 2단위 (= 게임 아트 1픽셀). 9-slice 테두리도 같은 비율</summary>
    public const float UnitsPerPixel = 2f;

    /// <summary>이미지를 원본 픽셀 크기 x UnitsPerPixel 로 (늘리거나 찌그러뜨리지 않음)</summary>
    public static void NativeSize(Image img)
    {
        if (img && img.sprite) img.rectTransform.sizeDelta = img.sprite.rect.size * UnitsPerPixel;
    }

    /// <summary>9-slice 테두리의 아틀라스 1픽셀 = UI 2단위가 되도록 하는 배율</summary>
    public static float PixelMultiplier(Sprite sprite)
    {
        if (!sprite) return 1f;
        float refPpu = 100f;
        if (I && I.canvas)
        {
            var scaler = I.canvas.GetComponent<CanvasScaler>();
            refPpu = scaler ? scaler.referencePixelsPerUnit : I.canvas.referencePixelsPerUnit;
        }
        return refPpu / (sprite.pixelsPerUnit * UnitsPerPixel);
    }

    /// <summary>아이콘 가시성: UI 내장 Outline 으로 1px 어두운 테두리 + 그림자 (별도 셰이더 불필요)</summary>
    public static void IconOutline(Graphic g)
    {
        if (!g || g.GetComponent<Outline>()) return;
        var o = g.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0.04f, 0.02f, 0.02f, 0.75f);
        o.effectDistance = new Vector2(1f, -1f); // 얇은 테두리 (UI 1단위)
    }

    public Text NewText(string name, Transform parent, int size, TextAnchor anchor, Color color)
    {
        var rt = Stretch(name, parent);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = skin.font;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = color;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        var o = rt.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0f, 0f, 0f, 0.9f);
        o.effectDistance = new Vector2(1f, -1f);
        return t;
    }
}
