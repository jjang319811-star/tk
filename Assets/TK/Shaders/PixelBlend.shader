// TK Pixel Blend shader (URP 2D, lit)
// 바닥 타일맵 위에 덮는 '지형 블렌드 레이어'. 화면을 덮는 사각형 하나로 무한 맵 전체를 처리.
//  - 월드 좌표를 픽셀 그리드(PPU)에 스냅 → 블렌드 경계도 픽셀아트 해상도로 계단지게
//  - 레이어 3개(A/B/C)를 각자 fbm 노이즈 마스크로 배치, 경계는 4x4 Bayer 디더링으로 섞음
//  - 경계 안쪽 몇 픽셀을 어둡게(Rim) 해서 타일 가장자리처럼 자연스럽게
//  - Harmonize: 레이어 색을 바탕(모래) 팔레트 쪽으로 당겨 색감 통일
// 노이즈는 C# ProceduralNoise 와 동일 → 장식 오브젝트가 같은 지형 위에 배치됨.
Shader "TK/PixelBlend"
{
    Properties
    {
        [HideInInspector] _MainTex("Unused (quad sprite)", 2D) = "white" {}
        _PPU("Pixels Per Unit", Float) = 64
        [IntRange] _Seed("Seed", Range(0, 9999)) = 1234

        [Header(Layer A)]
        [NoScaleOffset] _LayerA("Texture A", 2D) = "black" {}
        _ScaleA("Noise Scale A (per unit)", Float) = 0.18
        _CoverA("Coverage A", Range(0, 1)) = 0.25
        [Header(Layer B)]
        [NoScaleOffset] _LayerB("Texture B", 2D) = "black" {}
        _ScaleB("Noise Scale B", Float) = 0.22
        _CoverB("Coverage B", Range(0, 1)) = 0.15
        [Header(Layer C)]
        [NoScaleOffset] _LayerC("Texture C", 2D) = "black" {}
        _ScaleC("Noise Scale C", Float) = 0.3
        _CoverC("Coverage C", Range(0, 1)) = 0.06

        [Header(Edge)]
        _EdgeWidth("(CPU biome only) Edge Width", Range(0.005, 0.3)) = 0.06
        _EdgePixels("Dither Edge (pixels)", Range(0.5, 6)) = 2
        _RimPixels("Rim Width (pixels)", Range(0, 6)) = 2
        _RimDarken("Rim Darken", Range(0, 1)) = 0.7

        [Header(Color)]
        _HarmonizeColor("Harmonize Toward", Color) = (0.93, 0.72, 0.38, 1)
        _Harmonize("Harmonize Amount", Range(0, 1)) = 0.3
        _Brightness("Brightness", Range(0, 2)) = 1.15
    }

    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
        #include "Assets/TK/Shaders/PixelNoise.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _PPU;
            float _Seed;
            float _ScaleA, _CoverA;
            float _ScaleB, _CoverB;
            float _ScaleC, _CoverC;
            float _EdgeWidth, _EdgePixels, _RimPixels;
            half  _RimDarken;
            half4 _HarmonizeColor;
            half  _Harmonize, _Brightness;
        CBUFFER_END

        TEXTURE2D(_LayerA); SAMPLER(sampler_LayerA);
        TEXTURE2D(_LayerB); SAMPLER(sampler_LayerB);
        TEXTURE2D(_LayerC); SAMPLER(sampler_LayerC);

        // 레이어 하나 평가.
        // 노이즈 값을 '한 픽셀당 변화량(기울기)'으로 나눠 경계까지의 거리(픽셀)로 바꿈
        // → 노이즈 주파수와 상관없이 항상 _EdgePixels 폭의 디더 + _RimPixels 폭의 어두운 테두리
        #define TK_LAYER(TEX, SCALE, COVER, IDX, OUTCOL, OUTA)                                          \
        {                                                                                                \
            float2 off = float2(IDX * 53.7, IDX * 19.3);                                                 \
            uint ls = seed + IDX * 977u;                                                                 \
            float n  = TKFbm(p * SCALE + off, ls);                                                       \
            float nx = TKFbm((p + float2(1.0 / _PPU, 0)) * SCALE + off, ls);                             \
            float ny = TKFbm((p + float2(0, 1.0 / _PPU)) * SCALE + off, ls);                             \
            float g = max(length(float2(nx - n, ny - n)), 1e-6);                                          \
            float d = (n - (1.0 - COVER)) / g;                                                           \
            float v = saturate(d / _EdgePixels + 0.5);                                                    \
            if (v > dither)                                                                              \
            {                                                                                            \
                uint tw, th; TEX.GetDimensions(tw, th);                                                  \
                half3 c = SAMPLE_TEXTURE2D(TEX, sampler##TEX, pix / float2(tw, th)).rgb;                 \
                if (d < _RimPixels) c *= _RimDarken;                                                     \
                OUTCOL = c; OUTA = 1.0;                                                                  \
            }                                                                                            \
        }

        half4 TKBlendColor(float3 positionWS)
        {
            float2 pix = floor(positionWS.xy * _PPU) + 0.5;   // 픽셀 그리드 스냅
            float2 p = pix / _PPU;
            uint seed = (uint)_Seed;
            float dither = TKBayer4((int2)floor(pix));

            half3 col = 0; half a = 0;
            TK_LAYER(_LayerA, _ScaleA, _CoverA, 1u, col, a)
            TK_LAYER(_LayerB, _ScaleB, _CoverB, 2u, col, a)
            TK_LAYER(_LayerC, _ScaleC, _CoverC, 3u, col, a)

            col = lerp(col, col * _HarmonizeColor.rgb * 1.6h, _Harmonize) * _Brightness;
            return half4(col, a);
        }
    ENDHLSL

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #pragma vertex BlendVertex
            #pragma fragment BlendFragment
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half2  lightingUV : TEXCOORD1;
            };

            Varyings BlendVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                o.positionWS = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.lightingUV = half2(ComputeScreenPos(o.positionCS / o.positionCS.w).xy);
                return o;
            }

            half4 BlendFragment(Varyings input) : SV_Target
            {
                half4 c = TKBlendColor(input.positionWS);
                if (c.a <= 0.0h) discard;

                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(c.rgb, c.a, half4(1, 1, 1, 1), half3(0, 0, 1), surfaceData);
                InitializeInputData(input.positionWS.xy, input.lightingUV, inputData);
                return CombinedShapeLightShared(surfaceData, inputData);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #pragma vertex BlendVertex
            #pragma fragment BlendFragment

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings BlendVertex(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 BlendFragment(Varyings input) : SV_Target
            {
                half4 c = TKBlendColor(input.positionWS);
                if (c.a <= 0.0h) discard;
                return c;
            }
            ENDHLSL
        }
    }
}
