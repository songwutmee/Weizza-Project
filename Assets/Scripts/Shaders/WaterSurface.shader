// The top of a pond, a flat sheet lying on the ground that the pitched camera sees
// from a little above, the way At Fate's End lays its water as a plane in a 3D world.
// Everything is painted from noise in here, no textures:
//
// - colour from shallow at the shore to deep in the middle, lifting toward a pale
//   sky colour at the far edge where a low view sees more reflection (Ameye's depth
//   colour, with the far lift standing in for fresnel);
// - caustic light from two drifting Voronoi patterns, gathered along the cell walls
//   (the Daniel Ilett and Cyanilux caustics, laid on the water instead of under it);
// - long horizontal streaks and a few sparkles on the sun side, the painterly marks
//   a hand drawn pond gets (Cyanilux 2D water: step on noise for sparkle);
// - a broken foam line along the painted shore (Ameye foam from distance to the edge);
// - wave crests from WaterPond.cs's spring row, passed in the vertex colour red.
//
// The reflections draw first and this goes over them partly see through, so they
// read as being in the water. The front strip draws again over the player's feet
// with _Overlay on, so wading feet sink into it.
Shader "Weizza/WaterSurface"
{
    Properties
    {
        [Header(Colour)]
        _Shallow ("Shallow", Color) = (0.42, 0.55, 0.38, 1)
        _Deep ("Deep", Color) = (0.12, 0.26, 0.24, 1)
        _Far ("Far edge sky", Color) = (0.86, 0.9, 0.66, 1)
        _FarAmount ("Far edge amount", Range(0, 1)) = 0.55
        _DeepAt ("Metres to full deep", Range(0.1, 3)) = 0.9
        _AlphaFront ("Opacity near", Range(0, 1)) = 0.55
        _AlphaBack ("Opacity far", Range(0, 1)) = 0.9

        [Header(Light)]
        _Highlight ("Highlight", Color) = (1, 0.97, 0.78, 1)
        _Caustics ("Caustics", Range(0, 1)) = 0.35
        _CausticScale ("Caustic size", Range(0.3, 4)) = 1.4
        _Streaks ("Streaks", Range(0, 1)) = 0.45
        _Sparkle ("Sparkle", Range(0, 2)) = 0.9
        _Crest ("Wave crest light", Range(0, 4)) = 1.6

        [Header(Foam)]
        _Foam ("Foam", Color) = (0.95, 0.96, 0.84, 1)
        _FoamWidth ("Foam width", Range(0, 0.5)) = 0.14
        _FoamBreak ("Foam broken up", Range(0, 1)) = 0.45

        [Header(Wet ground rim)]
        _RimColour ("Rim colour", Color) = (0.08, 0.09, 0.05, 0.4)
        _RimWidth ("Rim width", Range(0, 0.4)) = 0

        [Header(Front face of deep water)]
        _FaceTop ("Opacity at the top", Range(0, 1)) = 0.35
        _FaceBottom ("Opacity deep down", Range(0, 1)) = 0.85
        _Rays ("Light rays", Range(0, 1)) = 0.35

        [HideInInspector] _Overlay ("Overlay", Float) = 0
        [HideInInspector] _OverlayAlpha ("Overlay alpha", Float) = 0.5
        [HideInInspector] _Face ("Face", Float) = 0
        [HideInInspector] _FaceShape ("Face shape", Vector) = (2, 1.4, 0, 0)
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
            #include "WaterCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 waterWS : TEXCOORD0;
                half4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Shallow, _Deep, _Far, _Highlight, _Foam;
                float _FarAmount, _DeepAt, _AlphaFront, _AlphaBack;
                float _Caustics, _CausticScale, _Streaks, _Sparkle, _Crest;
                float _FoamWidth, _FoamBreak;
                half4 _RimColour;
                float _RimWidth, _FaceTop, _FaceBottom, _Rays;
            CBUFFER_END

            float _Overlay, _OverlayAlpha, _Face;
            float4 _FaceShape;   // x depth below the surface, y metres from the bank to full depth

            // The front of a deep pond, seen side on: the player sinks behind it. Darker the
            // deeper, a few soft rays from the surface, caustics on the cut, a bright line
            // where it meets the surface. Its depth follows a bowl, shallow at the banks, so
            // it only covers the basin and not the earth beside it.
            half4 FaceFrag(float3 p, float wave, float t)
            {
                float inside = WaterInside(p.x, _WaterRect.z);
                clip(inside);
                float floorDepth = _FaceShape.x * smoothstep(0.0, _FaceShape.y, inside);
                float below = _WaterLevel.x - p.y;
                clip(floorDepth - below);
                float k = saturate(below / max(floorDepth, 0.05));

                half3 col = lerp(_Shallow.rgb, _Deep.rgb, saturate(below / 1.2)) * lerp(1.0, 0.8, k);
                float e = WaterVoronoiEdge(float2(p.x * 1.1, p.y * 1.5) * _CausticScale + float2(t * 0.1, -t * 0.05), t * 0.6);
                col += _Highlight.rgb * (1.0 - smoothstep(0.02, 0.14, e)) * _Caustics * 0.6 * (1.0 - k);
                // Rays slanting down from the surface, drifting slowly, fading with depth.
                float r = WaterNoise(float2(p.x * 0.55 + p.y * 0.3 - t * 0.04, t * 0.07));
                col += _Highlight.rgb * smoothstep(0.55, 0.85, r) * _Rays * (1.0 - k) * (1.0 - k);
                // The cut through the surface catches light.
                float lip = 1.0 - smoothstep(0.0, 0.05, abs(below - max(0.0, wave) * 0.02));
                col = lerp(col, _Foam.rgb, lip * 0.8);

                float alpha = lerp(_FaceTop, _FaceBottom, k);
                alpha = max(alpha, lip * 0.9);
                alpha *= smoothstep(0.0, 0.12, floorDepth - below) * smoothstep(0.0, 0.05, inside);
                return half4(col, alpha);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.waterWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.waterWS);
                o.color = input.color;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float x = input.waterWS.x;
                float z = input.waterWS.z;
                float t = _Time.y;

                if (_Face > 0.5) return FaceFrag(input.waterWS, input.color.r * 2.0 - 1.0, t);

                float inside = WaterInside(x, z);
                // Ground just outside a puddle stays dark and wet, broken up like soaked soil.
                if (inside < 0.0)
                {
                    clip(_RimWidth - 0.001);
                    clip(_Overlay > 0.5 ? -1.0 : 1.0);
                    float wet = 1.0 - saturate(-inside / _RimWidth);
                    wet *= 0.6 + 0.4 * WaterNoise(float2(x * 9.0, z * 15.0));
                    clip(wet - 0.01);
                    return half4(_RimColour.rgb, _RimColour.a * wet * wet);
                }

                // 0 at the near edge, 1 at the far edge.
                float back = saturate((z - (_WaterRect.z - _WaterRect.w)) / max(_WaterRect.w * 2.0, 1e-3));
                float deep = saturate(inside / _DeepAt);

                half3 col = lerp(_Shallow.rgb, _Deep.rgb, deep);
                col = lerp(col, _Far.rgb, back * back * _FarAmount);

                // Caustics: two drifting cell patterns; light gathers where their walls meet.
                // Depth is squeezed on screen, so the pattern is stretched along z to stay round.
                float2 cp = float2(x, z * 2.2) * _CausticScale;
                float e1 = WaterVoronoiEdge(cp + float2(t * 0.11, t * 0.04), t * 0.7);
                float e2 = WaterVoronoiEdge(cp * 1.37 + float2(-t * 0.07, t * 0.09) + 3.1, t * 0.55 + 1.7);
                float caustic = (1.0 - smoothstep(0.02, 0.16, e1)) * 0.6 + (1.0 - smoothstep(0.02, 0.12, e2)) * 0.4;
                // Brightest in the clear shallows, gone under the far glare.
                caustic *= _Caustics * (1.0 - back * 0.7) * (0.45 + 0.55 * (1.0 - deep));

                // Long thin streaks drifting along the surface, like brush marks of light.
                float s = WaterNoise(float2(x * 0.8 + t * 0.12, z * 16.0 - t * 0.05));
                float streak = smoothstep(0.66, 0.74, s) * (1.0 - smoothstep(0.82, 0.9, s));
                streak *= _Streaks * (0.35 + 0.65 * back);

                // Sparkles where the sun catches, more on the sun side of the screen.
                float2 screen = input.positionCS.xy / _ScreenParams.xy;
                float sunSide = 0.35 + 0.65 * smoothstep(0.2, 0.95, screen.x);
                float n = WaterNoise(float2(x * 9.0 + t * 0.7, z * 30.0 - t * 0.4));
                float twinkle = 0.5 + 0.5 * sin(t * 6.0 + x * 13.0 + z * 7.0);
                float sparkle = smoothstep(0.86, 0.93, n) * twinkle * _Sparkle * sunSide;

                // Wave crests from the spring row catch the light, troughs dim a little.
                float wave = input.color.r * 2.0 - 1.0;
                float crest = saturate(wave) * _Crest;
                col *= 1.0 - saturate(-wave) * 0.35;

                col += _Highlight.rgb * (caustic + streak + sparkle + crest);

                // Foam hugging the painted shore, broken into clumps by noise.
                float clump = WaterNoise(float2(x * 3.4 + t * 0.05, z * 7.0));
                float lap = sin(t * 1.3 + x * 2.0) * 0.25 + 0.75;
                float edgeIn = inside + (WaterNoise(float2(x * 8.0, z * 14.0 + t * 0.2)) - 0.5) * _FoamWidth * 0.9;
                float foam = (1.0 - smoothstep(_FoamWidth * 0.35 * lap, _FoamWidth * lap, edgeIn));
                foam *= lerp(1.0, smoothstep(0.3, 0.55, clump), _FoamBreak);

                float alpha = lerp(_AlphaFront, _AlphaBack, back);
                alpha = saturate(alpha + (caustic + streak + sparkle) * 0.4);
                col = lerp(col, _Foam.rgb, foam);
                alpha = max(alpha, foam * _Foam.a);
                // A hair of soft edge so the shore never aliases.
                alpha *= smoothstep(0.0, 0.03, inside);

                if (_Overlay > 0.5) alpha *= _OverlayAlpha;
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
