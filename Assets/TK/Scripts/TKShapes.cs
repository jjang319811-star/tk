using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 장판·팔진도 결계 같은 도형을 '필요한 실제 픽셀 크기'로 생성 (스케일로 늘리지 않음 → 선 두께 1px 유지).
/// 같은 크기는 캐시해서 재사용.
/// </summary>
public static class TKShapes
{
    const float PPU = 64f;
    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    static Sprite Make(string key, int w, int h, Vector2 pivot, System.Func<int, int, float> alpha)
    {
        if (cache.TryGetValue(key, out var s) && s) return s;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(x, y)) * 255f));
        tex.SetPixels32(px);
        tex.Apply(false, true);
        s = Sprite.Create(tex, new Rect(0, 0, w, h), pivot, PPU, 0, SpriteMeshType.FullRect);
        s.name = key;
        cache[key] = s;
        return s;
    }

    /// <summary>채워진 원 (안쪽 반투명 + 1px 테두리)</summary>
    public static Sprite Circle(float radiusUnits)
    {
        int r = Mathf.Max(4, Mathf.RoundToInt(radiusUnits * PPU));
        int d = r * 2;
        return Make($"circle{r}", d, d, new Vector2(0.5f, 0.5f), (x, y) =>
        {
            float dx = x + 0.5f - r, dy = y + 0.5f - r, dist = Mathf.Sqrt(dx * dx + dy * dy);
            return dist > r ? 0f : dist > r - 1.2f ? 1f : 0.4f;
        });
    }

    /// <summary>왼쪽 가운데가 피벗인 직사각형 (돌진 경로)</summary>
    public static Sprite Line(float lengthUnits, float widthUnits)
    {
        int w = Mathf.Max(2, Mathf.RoundToInt(lengthUnits * PPU));
        int h = Mathf.Max(2, Mathf.RoundToInt(widthUnits * PPU));
        return Make($"line{w}x{h}", w, h, new Vector2(0f, 0.5f), (x, y) =>
            (x == 0 || y == 0 || x == w - 1 || y == h - 1) ? 1f : 0.4f);
    }

    /// <summary>팔진도 결계: 바깥 링 2px + 안쪽 링 1px + 팔괘 막대 8개 (홀수는 음효로 끊김)</summary>
    public static Sprite TrigramRing(float radiusUnits)
    {
        int r = Mathf.Max(16, Mathf.RoundToInt(radiusUnits * PPU));
        int d = r * 2;
        return Make($"trigram{r}", d, d, new Vector2(0.5f, 0.5f), (x, y) =>
        {
            float dx = x + 0.5f - r, dy = y + 0.5f - r, dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist <= r && dist >= r - 2f) return 1f;
            if (dist <= r - 6f && dist >= r - 7f) return 0.55f;
            float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
            float inner = r * 0.5f, outer = r * 0.72f;
            for (int k = 0; k < 8; k++)
            {
                float diff = Mathf.Abs(Mathf.DeltaAngle(ang, k * 45f));
                float halfWidthDeg = Mathf.Rad2Deg * (3f / Mathf.Max(dist, 1f)); // 막대 폭 약 6px
                if (diff < halfWidthDeg * 2f && dist >= inner && dist <= outer)
                {
                    bool yin = k % 2 == 1 && Mathf.Abs(dist - (inner + outer) * 0.5f) < 2f;
                    return yin ? 0f : 1f;
                }
            }
            return 0f;
        });
    }
}
