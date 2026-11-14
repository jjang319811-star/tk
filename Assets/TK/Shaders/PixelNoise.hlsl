#ifndef TK_PIXEL_NOISE
#define TK_PIXEL_NOISE

// C# 쪽 ProceduralNoise 와 비트 단위로 같은 결과를 내는 정수 해시 기반 value noise.
// (장식 배치가 셰이더의 지형 블렌드와 일치하도록)

uint TKPcg(uint v)
{
    uint state = v * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

float TKHash(int x, int y, uint seed)
{
    uint h = TKPcg(asuint(x) ^ TKPcg(asuint(y) ^ TKPcg(seed)));
    return (float)(h & 0x00FFFFFFu) / 16777215.0;
}

float TKValueNoise(float2 p, uint seed)
{
    float2 fl = floor(p);
    int2 i = (int2)fl;
    float2 f = p - fl;
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = TKHash(i.x,     i.y,     seed);
    float b = TKHash(i.x + 1, i.y,     seed);
    float c = TKHash(i.x,     i.y + 1, seed);
    float d = TKHash(i.x + 1, i.y + 1, seed);
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float TKFbm(float2 p, uint seed)
{
    return 0.5 * TKValueNoise(p, seed)
         + 0.3 * TKValueNoise(p * 2.03 + float2(17.1, 5.3), seed + 101u)
         + 0.2 * TKValueNoise(p * 4.01 + float2(31.7, 11.9), seed + 202u);
}

// 4x4 Bayer 행렬 (0~1) — 픽셀 단위 디더링 경계
float TKBayer4(int2 pixel)
{
    const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    int2 q = pixel & 3;
    return (m[q.y * 4 + q.x] + 0.5) / 16.0;
}

#endif
