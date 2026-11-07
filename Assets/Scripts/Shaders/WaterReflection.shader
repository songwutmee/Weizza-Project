// A mirrored copy of a sprite, laid on a pond. WaterReflection.cs flips each sprite
// near the water across the surface; this draws it wobbling, tinted toward the water,
// fading with distance below the surface, and only where the pond is.
//
// At Fate's End reflects sprites the same way rather than reading the screen back.
// It keeps the drawings sharp and needs no second camera or screen copy, which was
// what made the old Water2D attempt fragile here (the Opaque Texture is empty).
//
// Clipping to the pond: on a camera pitched about the x axis, a point's height on
// screen is dot(position, camera up). The water plane point drawn at the same pixel
// is the one at the surface height with the matching depth, so the depth is solved
// from that and checked against the same painted shore the surface uses.
Shader "Weizza/WaterReflection"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Tint ("Water tint", Color) = (0.2, 0.36, 0.32, 1)
        _TintAmount ("Tint amount", Range(0, 1)) = 0.35
        _Bright ("Brightness", Range(0, 1.5)) = 0.8
        _Fade ("Fade metres", Range(0.2, 10)) = 3.2
        _Opacity ("Opacity", Range(0, 1)) = 0.85
        _Wobble ("Wobble", Range(0, 0.1)) = 0.025
        _WobbleSpeed ("Wobble speed", Range(0, 6)) = 1.6
        [HideInInspector] _Color ("Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "CanUseSpriteAtlas" = "True" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
                float3 waterWS : TEXCOORD5;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"
            #include "WaterCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _Tint;
                float _TintAmount, _Bright, _Fade, _Opacity, _Wobble, _WobbleSpeed;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                Varyings o = CommonUnlitVertex(input);
                o.waterWS = TransformObjectToWorld(input.positionOS.xyz);
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 p = input.waterWS;
                float t = _Time.y * _WobbleSpeed;

                // Where on the water plane this pixel lands, then whether that is inside the pond.
                float3 camUp = UNITY_MATRIX_V[1].xyz;
                float v = dot(p, camUp);
                float zOnWater = (v - _WaterLevel.x * camUp.y - p.x * camUp.x) / max(camUp.z, 1e-3);
                float inside = WaterInside(p.x, zOnWater);
                clip(inside);

                // Wobble grows with distance below the surface, like a real reflection
                // breaking up further from the bank, in bands that roll toward the viewer.
                float below = max(0.0, _WaterLevel.x - p.y);
                float band = sin(p.y * 22.0 + t * 3.0) * 0.35 + (WaterNoise(float2(p.x * 1.5, p.y * 9.0 + t)) - 0.5);
                float2 uv = input.uv + float2(band * _Wobble * saturate(0.3 + below * 0.5), 0.0);

                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * input.color;
                c.rgb = lerp(c.rgb, _Tint.rgb, _TintAmount) * _Bright;
                c.a *= _Opacity * exp(-below / _Fade) * smoothstep(0.0, 0.05, inside);
                return c;
            }
            ENDHLSL
        }
    }
}
