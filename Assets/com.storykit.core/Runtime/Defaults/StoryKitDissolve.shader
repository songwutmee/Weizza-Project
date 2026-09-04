// StoryKit dissolve transition for the fade overlay.
// UI/Canvas shader: works on Built-in, URP and HDRP alike (Canvas shaders
// bypass the SRP lit pipeline). Noise is procedural — no texture needed.
//
// Usage:
//   var mat = new Material(Shader.Find("StoryKit/UIDissolve"));
//   simpleScreenUI.SetOverlayMaterial(mat);
// Then FadeNode drives _Progress through the overlay alpha as usual:
// alpha 0 = fully visible scene, alpha 1 = fully covered, with a burning
// dissolve edge in between instead of a plain crossfade.
Shader "StoryKit/UIDissolve"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Cover Color", Color) = (0,0,0,1)
        _EdgeColor ("Edge Color", Color) = (1,0.55,0.1,1)
        _EdgeWidth ("Edge Width", Range(0.001,0.3)) = 0.08
        _NoiseScale ("Noise Scale", Range(1,64)) = 14
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;      // Image tint; alpha drives progress
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            fixed4 _Color;
            fixed4 _EdgeColor;
            float _EdgeWidth;
            float _NoiseScale;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            // Cheap value noise: hash + bilinear smoothing. Good enough for a
            // dissolve mask and costs a handful of ALU — mobile friendly.
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 uv)
            {
                float2 cell = floor(uv);
                float2 f = frac(uv);
                f = f * f * (3.0 - 2.0 * f);   // smoothstep interpolation

                float a = hash21(cell);
                float b = hash21(cell + float2(1, 0));
                float c = hash21(cell + float2(0, 1));
                float d = hash21(cell + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // progress 0..1 comes from the Image color alpha (set by FadeAsync).
                float progress = i.color.a;

                // Two octaves of noise for a more organic edge.
                float n = valueNoise(i.uv * _NoiseScale) * 0.65
                        + valueNoise(i.uv * _NoiseScale * 2.7) * 0.35;

                // Pixel is covered once progress passes its noise threshold.
                float covered = step(n, progress * (1.0 + _EdgeWidth));
                float edge = covered * step(progress * (1.0 + _EdgeWidth) - _EdgeWidth, n);

                fixed4 result = _Color;
                result.rgb = lerp(result.rgb, _EdgeColor.rgb, edge);
                result.a = covered;

                // Fully covered or fully clear shortcut keeps blending exact.
                if (progress >= 0.999) result.a = 1;
                if (progress <= 0.001) result.a = 0;
                return result;
            }
            ENDCG
        }
    }
    FallBack "UI/Default"
}
