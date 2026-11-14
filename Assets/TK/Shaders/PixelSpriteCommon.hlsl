#ifndef TK_PIXEL_SPRITE_COMMON
#define TK_PIXEL_SPRITE_COMMON

// 이 파일을 include 하기 전에 _MainTex / sampler_MainTex 와 TK_PIXEL_CBUFFER 가 선언되어 있어야 함.

// _MainTex 의 실제 크기(텍셀). SpriteRenderer/TilemapRenderer 가 바인딩한 텍스처 기준.
float2 PixelTexSize()
{
    uint w, h;
    _MainTex.GetDimensions(w, h);
    return float2(max(w, 1u), max(h, 1u));
}

// 정점을 월드 픽셀 그리드에 스냅 (서브픽셀 떨림 방지)
float3 PixelSnapWS(float3 positionWS)
{
#if defined(_PIXEL_SNAP)
    float ppu = max(_PixelsPerUnit, 1.0);
    positionWS.xy = round(positionWS.xy * ppu) / ppu;
#endif
    return positionWS;
}

// UV 를 _PixelSize 텍셀 블록의 중심으로 스냅 → 모자이크/픽셀화
float2 PixelateUV(float2 uv)
{
    float2 size = PixelTexSize();
    float block = max(floor(_PixelSize), 1.0);
    float2 texel = uv * size;
    texel = (floor(texel / block) + 0.5) * block;
    return texel / size;
}

half3 PixelColorGrade(half3 rgb)
{
    half luma = dot(rgb, half3(0.299h, 0.587h, 0.114h));
    rgb = lerp(luma.xxx, rgb, _Saturation);
    rgb = (rgb - 0.5h) * _Contrast + 0.5h + _Brightness;
    rgb = saturate(rgb);
    if (_ColorLevels >= 2.0)
    {
        half levels = (half)floor(_ColorLevels) - 1.0h;
        rgb = floor(rgb * levels + 0.5h) / levels;
    }
    return rgb;
}

half PixelAlphaAt(float2 uv)
{
    return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;
}

// 텍스처 샘플 + 컬러 그레이딩 + 포스터라이즈 + (선택) 안쪽 외곽선
half4 PixelShade(float2 uv, half4 vertexColor)
{
    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
    half4 c = tex * vertexColor;
    c.rgb = PixelColorGrade(c.rgb);

#if defined(_PIXEL_OUTLINE)
    if (tex.a > 0.5h)
    {
        float2 o = max(floor(_PixelSize), 1.0) * max(floor(_OutlineThickness), 1.0) / PixelTexSize();
        half minA = min(min(PixelAlphaAt(uv + float2(o.x, 0)), PixelAlphaAt(uv - float2(o.x, 0))),
                        min(PixelAlphaAt(uv + float2(0, o.y)), PixelAlphaAt(uv - float2(0, o.y))));
        if (minA < 0.5h)
        {
            // 외곽선도 SpriteRenderer.color 의 알파(페이드)를 따르도록 함
            c.rgb = _OutlineColor.rgb;
            c.a = _OutlineColor.a * vertexColor.a;
        }
    }
#endif

    return c;
}

#endif // TK_PIXEL_SPRITE_COMMON
