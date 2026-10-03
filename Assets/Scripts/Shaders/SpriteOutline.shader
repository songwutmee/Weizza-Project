// A real outline, sampling neighbour texels against the sprite's own alpha,
// instead of the cheap trick InteractableOutline used before (a second
// SpriteRenderer, same sprite, scaled up, drawn one order behind). That
// version worked but doubled the draw call and vertex count of every pickup
// and needed its own transform to scale for the pulse.
//
// Written by hand rather than as a Shader Graph, same reason as GodRay.shader:
// there is no way to drive the Shader Graph node editor through the editor automation
// bridge this project uses.
//
// The offsets are read from fwidth(uv), the UV's own rate of change over one
// real screen pixel, not a fixed texel count. A fixed texel offset looked
// right on a 512px sheet and turned invisible, sub-pixel, on a 2048px source
// texture, since the same texel count is a completely different fraction of
// a sprite's own footprint depending on how the art was scanned. Screen
// space keeps the outline the same visible thickness regardless of the
// source resolution. It also stays inside the padding Unity's own sprite
// packer leaves around a packed sprite for any reasonable width, the same
// reason a fixed small texel count was chosen in the first draft, just
// measured in the unit that actually matters.
Shader "Weizza/SpriteOutline"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Sprite Tint", Color) = (1, 1, 1, 1)
        _OutlineColor ("Outline Colour", Color) = (1, 1, 1, 1)
        _OutlineWidth ("Outline Width (screen px)", Range(0, 8)) = 2
        _OutlineAlpha ("Outline Alpha", Range(0, 1)) = 1
        _AlphaCutoff ("Alpha Counts As Solid Above", Range(0, 1)) = 0.1
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
            float4 _MainTex_TexelSize;
            half4 _Color;
            half4 _OutlineColor;
            float _OutlineWidth;
            float _OutlineAlpha;
            float _AlphaCutoff;

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color * _Color;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half4 centre = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);

                // Already solid here, draw the sprite itself. The outline
                // only ever shows up in the transparent rim around it.
                if (centre.a >= _AlphaCutoff)
                {
                    return centre * IN.color;
                }

                // fwidth(uv) is how much the UV changes over one real screen
                // pixel, so offsetting by it rather than by a fixed texel
                // count keeps the outline the same thickness on screen no
                // matter the source texture's resolution or the sprite's
                // world scale. A fixed texel offset looked bold on a small
                // sheet and vanished below a screen pixel on a 2048px scan,
                // which is exactly the trap this project's own import
                // settings notes already warn about for measuring art by the
                // wrong unit.
                float2 texel = fwidth(IN.uv) * _OutlineWidth;
                float maxNeighbourAlpha = 0;

                // Eight taps around the pixel rather than four, so a corner
                // of the silhouette gets caught too, not just its flat edges.
                UNITY_UNROLL
                for (int x = -1; x <= 1; x++)
                {
                    UNITY_UNROLL
                    for (int y = -1; y <= 1; y++)
                    {
                        if (x == 0 && y == 0) continue;
                        float2 offsetUV = IN.uv + float2(x, y) * texel;
                        half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, offsetUV).a;
                        maxNeighbourAlpha = max(maxNeighbourAlpha, a);
                    }
                }

                if (maxNeighbourAlpha < _AlphaCutoff) return half4(0, 0, 0, 0);

                half4 result = _OutlineColor;
                result.a *= _OutlineAlpha * IN.color.a;
                return result;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
