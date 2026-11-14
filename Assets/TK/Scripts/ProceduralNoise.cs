using UnityEngine;

/// <summary>
/// PixelNoise.hlsl 과 동일한 정수 해시 value noise (CPU 쪽).
/// 셰이더(PixelBlend)가 그리는 지형 레이어와 장식 배치가 같은 결과를 보도록 공유.
/// </summary>
public static class ProceduralNoise
{
    static uint Pcg(uint v)
    {
        unchecked
        {
            uint state = v * 747796405u + 2891336453u;
            uint word = ((state >> (int)((state >> 28) + 4u)) ^ state) * 277803737u;
            return (word >> 22) ^ word;
        }
    }

    public static float Hash(int x, int y, uint seed)
    {
        unchecked
        {
            uint h = Pcg((uint)x ^ Pcg((uint)y ^ Pcg(seed)));
            return (h & 0x00FFFFFFu) / 16777215f;
        }
    }

    public static float Value(Vector2 p, uint seed)
    {
        float fx = Mathf.Floor(p.x), fy = Mathf.Floor(p.y);
        int ix = (int)fx, iy = (int)fy;
        float tx = p.x - fx, ty = p.y - fy;
        float ux = tx * tx * (3f - 2f * tx), uy = ty * ty * (3f - 2f * ty);
        float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed);
        float c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
    }

    public static float Fbm(Vector2 p, uint seed)
    {
        unchecked
        {
            return 0.5f * Value(p, seed)
                 + 0.3f * Value(p * 2.03f + new Vector2(17.1f, 5.3f), seed + 101u)
                 + 0.2f * Value(p * 4.01f + new Vector2(31.7f, 11.9f), seed + 202u);
        }
    }

    /// <summary>PixelBlend 셰이더 TK_LAYER 와 같은 마스크 값 (0~1, 1 이면 레이어가 완전히 덮음)</summary>
    public static float Layer(Vector2 world, float scale, float cover, uint index, uint seed, float edgeWidth)
    {
        unchecked
        {
            float n = Fbm(world * scale + new Vector2(index * 53.7f, index * 19.3f), seed + index * 977u);
            return Mathf.Clamp01((n - (1f - cover)) / edgeWidth + 0.5f);
        }
    }
}
