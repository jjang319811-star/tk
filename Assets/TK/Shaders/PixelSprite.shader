// TK Pixel Sprite shader (URP 2D, lit)
// Sprite-Lit-Default 를 기반으로 픽셀 아트 효과를 추가한 셰이더.
//  - Pixel Size   : 텍스처 텍셀을 N x N 블록으로 묶어 모자이크(픽셀화)
//  - Color Levels : 색 단계 수 제한 (포스터라이즈, 0 = 끄기)
//  - Outline      : 스프라이트 가장자리 안쪽 1~N 텍셀에 외곽선
//  - Pixel Snap   : 정점을 월드 픽셀 그리드(PPU)에 스냅해 서브픽셀 떨림 방지
//  - Flash        : 피격 등 단색 번쩍임 (스크립트에서 _FlashAmount 제어)
// SpriteRenderer / TilemapRenderer 모두 지원, 2D 라이트(Global Light 2D 등) 적용됨.
Shader "TK/PixelSprite"
{
    Properties
    {
        _MainTex("Diffuse", 2D) = "white" {}
        _MaskTex("Mask", 2D) = "white" {}
        _NormalMap("Normal Map", 2D) = "bump" {}

        [Header(Pixelate)]
        [IntRange] _PixelSize("Pixel Size (texels)", Range(1, 16)) = 1
        [IntRange] _ColorLevels("Color Levels (0 = off)", Range(0, 32)) = 0

        [Header(Color)]
        _Saturation("Saturation", Range(0, 2)) = 1
        _Contrast("Contrast", Range(0, 2)) = 1
        _Brightness("Brightness", Range(-1, 1)) = 0
        _Unlit("Self Illumination (ignore 2D lights)", Range(0, 1)) = 0

        [Header(Outline)]
        [Toggle(_PIXEL_OUTLINE)] _Outline("Outline", Float) = 0
        _OutlineColor("Outline Color", Color) = (0.08, 0.05, 0.05, 1)
        [IntRange] _OutlineThickness("Outline Thickness (texels)", Range(1, 4)) = 1

        [Header(Pixel Snap)]
        [Toggle(_PIXEL_SNAP)] _PixelSnap("Snap To Pixel Grid", Float) = 0
        _PixelsPerUnit("Pixels Per Unit", Float) = 64

        [Header(Flash)]
        _FlashColor("Flash Color", Color) = (1, 1, 1, 1)
        _FlashAmount("Flash Amount", Range(0, 1)) = 0

        [MaterialToggle] _ZWrite("ZWrite", Float) = 0

        // Legacy properties (Sprite-Lit-Default 호환)
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
    }

    HLSLINCLUDE
        // SRP Batcher 호환을 위해 모든 패스에서 동일한 CBUFFER 레이아웃 사용
        #define TK_PIXEL_CBUFFER                    \
        CBUFFER_START(UnityPerMaterial)             \
            half4  _Color;                          \
            float  _PixelSize;                      \
            float  _ColorLevels;                    \
            half   _Saturation;                     \
            half   _Contrast;                       \
            half   _Brightness;                     \
            half   _Unlit;                          \
            half4  _OutlineColor;                   \
            float  _OutlineThickness;               \
            float  _PixelsPerUnit;                  \
            half4  _FlashColor;                     \
            half   _FlashAmount;                    \
        CBUFFER_END
    ENDHLSL

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite [_ZWrite]

        // ---------------------------------------------------------------- 2D Lit
        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex PixelLitVertex
            #pragma fragment PixelLitFragment

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"

            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile _ SKINNED_SPRITE
            #pragma shader_feature_local _PIXEL_OUTLINE
            #pragma shader_feature_local _PIXEL_SNAP

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color        : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_LIT_OUTPUTS
                half4 color        : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            UNITY_TEXTURE_STREAMING_DEBUG_VARS_FOR_TEX(_MainTex);
            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            TK_PIXEL_CBUFFER

            #include "Assets/TK/Shaders/PixelSpriteCommon.hlsl"

            Varyings PixelLitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 positionWS = PixelSnapWS(TransformObjectToWorld(input.positionOS));
                o.positionCS = TransformWorldToHClip(positionWS);
            #if defined(DEBUG_DISPLAY)
                o.positionWS = positionWS;
                o.normalWS = TransformObjectToWorldDir(input.normal);
            #endif
                o.uv = input.uv;
                o.lightingUV = half2(ComputeScreenPos(o.positionCS / o.positionCS.w).xy);
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 PixelLitFragment(Varyings input) : SV_Target
            {
                float2 uv = PixelateUV(input.uv);
                half4 main = PixelShade(uv, input.color);
                const half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv);
                const half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv));

                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(main.rgb, main.a, mask, normalTS, surfaceData);
                InitializeInputData(uv, input.lightingUV, inputData);

            #if defined(DEBUG_DISPLAY)
                SETUP_DEBUG_TEXTURE_DATA_2D_NO_TS(inputData, input.positionWS, input.positionCS, _MainTex);
                surfaceData.normalWS = input.normalWS;
            #endif

                half4 lit = CombinedShapeLightShared(surfaceData, inputData);
                // 자체 발광: 어두운 환경에서도 캐릭터가 묻히지 않고 돋보이게 함
                lit.rgb = lerp(lit.rgb, main.rgb, _Unlit);
                // 플래시는 라이팅 이후에 적용해 어두운 곳에서도 확실히 보이게 함
                lit.rgb = lerp(lit.rgb, _FlashColor.rgb, _FlashAmount);
                return lit;
            }
            ENDHLSL
        }

        // ---------------------------------------------------------------- Normals
        Pass
        {
            Tags { "LightMode" = "NormalsRendering"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex NormalsRenderingVertex
            #pragma fragment NormalsRenderingFragment

            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE
            #pragma shader_feature_local _PIXEL_SNAP

            struct Attributes
            {
                COMMON_2D_NORMALS_INPUTS
                float4 color        : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_NORMALS_OUTPUTS
                half4   color           : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Normals2DCommon.hlsl"

            TK_PIXEL_CBUFFER

            #include "Assets/TK/Shaders/PixelSpriteCommon.hlsl"

            Varyings NormalsRenderingVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonNormalsVertex(input);
                o.positionCS = TransformWorldToHClip(PixelSnapWS(TransformObjectToWorld(input.positionOS)));
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 NormalsRenderingFragment(Varyings input) : SV_Target
            {
                SetUpSpriteInstanceProperties();
                input.uv = PixelateUV(input.uv);
                return CommonNormalsFragment(input, input.color);
            }
            ENDHLSL
        }

        // ---------------------------------------------------------------- Unlit fallback (non-2D renderer)
        Pass
        {
            Tags { "LightMode" = "UniversalForward" "Queue"="Transparent" "RenderType"="Transparent"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex PixelUnlitVertex
            #pragma fragment PixelUnlitFragment

            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE
            #pragma shader_feature_local _PIXEL_OUTLINE
            #pragma shader_feature_local _PIXEL_SNAP

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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TK_PIXEL_CBUFFER

            #include "Assets/TK/Shaders/PixelSpriteCommon.hlsl"

            Varyings PixelUnlitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformWorldToHClip(PixelSnapWS(TransformObjectToWorld(input.positionOS)));
                o.uv = input.uv;
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 PixelUnlitFragment(Varyings input) : SV_Target
            {
                half4 c = PixelShade(PixelateUV(input.uv), input.color);
                c.rgb = lerp(c.rgb, _FlashColor.rgb, _FlashAmount);
                return c;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/2D/Sprite-Lit-Default"
}
