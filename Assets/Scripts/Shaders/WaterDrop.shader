// Splash droplets and ripple rings. Drawn from math, so no texture asset: a soft dot,
// or a thin ring when _Ring is on. Colour comes from the particle.
Shader "Weizza/WaterDrop"
{
    Properties
    {
        [Toggle] _Ring ("Ring", Float) = 0
        _RingWidth ("Ring width", Range(0.02, 0.4)) = 0.12
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };

            CBUFFER_START(UnityPerMaterial)
                float _Ring, _RingWidth;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                o.color = input.color;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float d = length(input.uv - 0.5) * 2.0;
                float a;
                if (_Ring > 0.5)
                {
                    // A ring that thins as it widens; the particle shrinks alpha over life.
                    a = 1.0 - smoothstep(0.0, _RingWidth, abs(d - (1.0 - _RingWidth)));
                }
                else
                {
                    a = 1.0 - smoothstep(0.55, 1.0, d);
                }
                return half4(input.color.rgb, input.color.a * a);
            }
            ENDHLSL
        }
    }
}
