// TK Pixel Glow shader (URP 2D, unlit, HDR)
// 불/마법 FX 전용. 라이트 영향 없이 항상 밝게 그려지고(HDR 값 → Bloom 대상),
// 스프라이트 바깥쪽 투명 영역에 픽셀 헤일로(후광)를 더해 빛나 보이게 함.
//  - Intensity      : 본체 HDR 배율 (1 초과 시 Bloom 이 반응)
//  - Glow Color     : 후광 색 (HDR)
//  - Halo Radius    : 후광 반경 (텍셀). 스프라이트 Mesh Type 이 Full Rect 여야 바깥까지 보임
//  - Flicker        : 시간에 따른 밝기 깜빡임
// 블렌드는 프리멀티플라이드(One, OneMinusSrcAlpha) → 본체는 알파 블렌드, 후광은 가산 블렌드처럼 동작.
Shader "TK/PixelGlow"
{
    Properties
    {
        _MainTex("Sprite", 2D) = "white" {}
        _Intensity("Intensity", Range(0, 6)) = 1.6
        [HDR] _GlowColor("Glow Color", Color) = (2.0, 0.8, 0.25, 1)
        _HaloRadius("Halo Radius (texels)", Range(0, 8)) = 3
        _HaloStrength("Halo Strength", Range(0, 3)) = 0.9
        _FlickerSpeed("Flicker Speed", Range(0, 30)) = 9
        _FlickerAmount("Flicker Amount", Range(0, 1)) = 0.18

        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
    }

    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half  _Intensity;
            half4 _GlowColor;
            float _HaloRadius;
            half  _HaloStrength;
            float _FlickerSpeed;
            half  _FlickerAmount;
        CBUFFER_END

        struct Attributes
        {
            COMMON_2D_INPUTS
            half4 color : COLOR;
            UNITY_SKINNED_VERTEX_INPUTS
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv         : TEXCOORD0;
            half4  color      : COLOR;
            float  phase      : TEXCOORD1;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings GlowVertex(Attributes input)
        {
            UNITY_SKINNED_VERTEX_COMPUTE(input);
            SetUpSpriteInstanceProperties();
            input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            float3 positionWS = TransformObjectToWorld(input.positionOS);
            o.positionCS = TransformWorldToHClip(positionWS);
            o.uv = input.uv;
            o.color = input.color * _Color * unity_SpriteColor;
            // 오브젝트마다 깜빡임 위상이 달라지도록 월드 위치 사용 (배칭돼도 동작)
            o.phase = dot(floor(positionWS.xy * 2.0), float2(1.7, 3.1));
            return o;
        }

        half4 GlowFragment(Varyings input) : SV_Target
        {
            uint w, h;
            _MainTex.GetDimensions(w, h);
            float2 texel = 1.0 / float2(max(w, 1u), max(h, 1u));

            half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
            half4 c = tex * input.color;

            float t = _Time.y * _FlickerSpeed + input.phase;
            half flicker = 1.0h + _FlickerAmount * (half)(0.6 * sin(t) + 0.4 * sin(t * 2.37 + 1.3));

            half3 core = c.rgb * _Intensity * flicker * c.a;

            half halo = 0;
            if (_HaloRadius > 0.0)
            {
                // 12 방향 × 2 링 샘플링으로 주변 알파 평균 → 부드러운 픽셀 후광
                const float2 dirs[12] = {
                    float2(1, 0), float2(0.866, 0.5), float2(0.5, 0.866), float2(0, 1),
                    float2(-0.5, 0.866), float2(-0.866, 0.5), float2(-1, 0), float2(-0.866, -0.5),
                    float2(-0.5, -0.866), float2(0, -1), float2(0.5, -0.866), float2(0.866, -0.5) };
                float r = _HaloRadius;
                [unroll] for (int i = 0; i < 12; i++)
                {
                    halo += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + dirs[i] * texel * r).a * 0.6h;
                    halo += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + dirs[i] * texel * r * 0.5).a * 0.4h;
                }
                halo /= 12.0h;
                // 픽셀 느낌을 살리기 위해 4단계로 양자화
                halo = floor(halo * 4.0h + 0.5h) / 4.0h;
            }
            half3 haloRgb = _GlowColor.rgb * halo * _HaloStrength * (1.0h - c.a) * input.color.a * flicker;

            return half4(core + haloRgb, c.a);
        }
    ENDHLSL

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #pragma vertex GlowVertex
            #pragma fragment GlowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex GlowVertex
            #pragma fragment GlowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/2D/Sprite-Unlit-Default"
}
