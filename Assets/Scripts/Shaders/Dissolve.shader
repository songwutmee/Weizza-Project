// Dissolves a sprite in or out through a noise mask instead of a plain alpha
// fade, with a bright rim along the dissolving edge. Built for
// Common_Revive, the body reassembling out of butterflies that
// `weizza.md`'s art plan draws and nothing in code owned yet.
//
// Written by hand rather than as a Shader Graph, same reason as GodRay.shader
// and SpriteOutline.shader: there is no way to drive the Shader Graph node
// editor through the editor automation bridge this project uses.
//
// The noise is generated in the shader itself, not sampled from a texture
// asset, the same "needs no art" rule the rest of the game's juice already
// follows (HitSpark's burst, the Spore dot, SkyGradient). A hash based value
// noise is cheap enough for a sprite sized quad and never needs an artist to
// hand it a texture.
Shader "Weizza/Dissolve"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _DissolveAmount ("Dissolve Amount", Range(0, 1)) = 0
        _NoiseScale ("Noise Scale", Float) = 18
        _EdgeWidth ("Edge Width", Range(0.01, 0.4)) = 0.08
        _EdgeColor ("Edge Colour (HDR)", Color) = (1.6, 1.3, 0.6, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "CanUseSpriteAtlas" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            half4 _Color;
            float _DissolveAmount;
            float _NoiseScale;
            float _EdgeWidth;
            half4 _EdgeColor;

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color * _Color;
                return OUT;
            }

            // Value noise built from a hash, not sampled. Cheap enough for
            // one sprite sized quad and needs no texture asset.
            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 uv)
            {
                float2 cell = floor(uv);
                float2 fraction = frac(uv);
                fraction = fraction * fraction * (3.0 - 2.0 * fraction);

                float bottomLeft = Hash(cell);
                float bottomRight = Hash(cell + float2(1, 0));
                float topLeft = Hash(cell + float2(0, 1));
                float topRight = Hash(cell + float2(1, 1));

                float bottom = lerp(bottomLeft, bottomRight, fraction.x);
                float top = lerp(topLeft, topRight, fraction.x);
                return lerp(bottom, top, fraction.y);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half4 texColour = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                half4 result = texColour * IN.color;

                if (_DissolveAmount <= 0.0001) return result;

                float n = Noise(IN.uv * _NoiseScale);

                // Fully gone once the noise falls under the dissolve amount.
                clip(n - _DissolveAmount);

                // A bright rim just above the cutoff, so the edge looks like
                // it is burning or reassembling rather than a plain alpha
                // wipe. Widens as _EdgeWidth grows, always sits just inside
                // the still solid side of the cut.
                float edge = 1.0 - smoothstep(_DissolveAmount, _DissolveAmount + _EdgeWidth, n);
                result.rgb += _EdgeColor.rgb * edge * _EdgeColor.a;

                return result;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
