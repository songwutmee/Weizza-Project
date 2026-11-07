// Water seen side on, the way the rest of the world is drawn: the hollow in the ground
// filled up to a level line. WaterPond.cs builds the shape from the ground itself, so
// the water meets the banks exactly and its floor is the real floor.
//
// One body, top to bottom, no separate sheet for the surface:
// - a thin bright line where the surface is, riding the spring row's waves;
// - a soft sheen just under it, where a real surface seen nearly edge on turns pale;
// - colour deepening with depth, more see through near the top (Ori's pools, Rayman's);
// - a few short light dashes drifting just under the surface, like painted glints;
// - fine caustic threads and slanting rays only in deep water, kept faint;
// - a darker, softer edge against the floor so it settles into the ground.
//
// Drawn over the player, so a wading foot or a swimmer's body sits inside the water.
Shader "Weizza/WaterBody"
{
    Properties
    {
        [Header(Colour)]
        _Shallow ("Near the surface", Color) = (0.48, 0.6, 0.46, 1)
        _Deep ("Deep", Color) = (0.12, 0.24, 0.2, 1)
        _DeepAt ("Metres to full deep", Range(0.05, 3)) = 1.2
        _AlphaTop ("Opacity at the surface", Range(0, 1)) = 0.45
        _AlphaDeep ("Opacity deep", Range(0, 1)) = 0.9

        [Header(Surface)]
        _Line ("Surface line", Color) = (0.95, 0.96, 0.82, 1)
        _LineWidth ("Line width (metres)", Range(0.005, 0.08)) = 0.025
        _Sheen ("Sheen under the line", Color) = (0.8, 0.88, 0.7, 1)
        _SheenDepth ("Sheen depth (metres)", Range(0, 0.6)) = 0.16
        _SheenAmount ("Sheen amount", Range(0, 1)) = 0.55
        _Glints ("Glints", Range(0, 1)) = 0.5

        [Header(Deep water)]
        _Caustics ("Caustics", Range(0, 1)) = 0.18
        _CausticScale ("Caustic size", Range(0.5, 8)) = 3.2
        _Rays ("Light rays", Range(0, 1)) = 0.3

        [Header(Floor)]
        _FloorShade ("Darkening at the floor", Range(0, 1)) = 0.35
        _FloorSoft ("Floor softness (metres)", Range(0.005, 0.3)) = 0.06
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
                float2 heights : TEXCOORD0;   // x the surface above this column, y the floor under it (world)
                half4 color : COLOR;          // r wave: 0.5 flat, above is a crest
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 heights : TEXCOORD1;
                half wave : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Shallow, _Deep, _Line, _Sheen;
                float _DeepAt, _AlphaTop, _AlphaDeep;
                float _LineWidth, _SheenDepth, _SheenAmount, _Glints;
                float _Caustics, _CausticScale, _Rays;
                float _FloorShade, _FloorSoft;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.heights = input.heights;
                o.wave = input.color.r * 2.0 - 1.0;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 p = input.positionWS;
                float t = _Time.y;
                float below = max(0.0, input.heights.x - p.y);
                float aboveFloor = p.y - input.heights.y;
                float depth = max(input.heights.x - input.heights.y, 1e-3);
                float k = saturate(below / _DeepAt);

                half3 col = lerp(_Shallow.rgb, _Deep.rgb, k);
                float alpha = lerp(_AlphaTop, _AlphaDeep, k);

                // Sheen: a real surface seen nearly edge on reflects the pale sky in a band.
                float sheen = pow(1.0 - saturate(below / max(_SheenDepth, 1e-3)), 2.0) * _SheenAmount;
                sheen *= 0.8 + 0.2 * sin(p.x * 3.0 + t * 1.2);
                col = lerp(col, _Sheen.rgb, sheen);
                alpha = max(alpha, sheen * 0.85);

                // Short glints drifting just under the surface, brighter on a crest.
                float g = WaterNoise(float2(p.x * 2.6 + t * 0.35, below * 22.0 + t * 0.15));
                float glint = smoothstep(0.7, 0.78, g) * (1.0 - smoothstep(0.86, 0.94, g));
                glint *= (1.0 - smoothstep(0.02, 0.3, below)) * _Glints * (0.7 + 0.6 * saturate(input.wave));
                col += _Line.rgb * glint * 0.6;

                // Deep water only: fine caustic threads and soft slanting rays, both fading down.
                float deepOnly = smoothstep(0.35, 0.9, depth);
                float e = WaterVoronoiEdge(float2(p.x, p.y * 1.3) * _CausticScale + float2(t * 0.12, -t * 0.06), t * 0.7);
                float caustic = (1.0 - smoothstep(0.015, 0.07, e)) * _Caustics * deepOnly * (1.0 - k * 0.8);
                float r = WaterNoise(float2(p.x * 0.6 + p.y * 0.35 - t * 0.04, t * 0.06));
                float rays = smoothstep(0.58, 0.88, r) * _Rays * deepOnly * (1.0 - k);
                col += _Sheen.rgb * (caustic + rays * 0.6);

                // Settle into the ground: darker and softer right at the floor.
                float atFloor = 1.0 - smoothstep(0.0, _FloorSoft * 3.0, aboveFloor);
                col *= 1.0 - atFloor * _FloorShade;
                alpha *= smoothstep(0.0, _FloorSoft, aboveFloor);

                // The surface line itself, a hair thicker on crests.
                float lw = _LineWidth * (1.0 + 0.6 * saturate(input.wave));
                float lineK = 1.0 - smoothstep(lw * 0.5, lw, below);
                col = lerp(col, _Line.rgb, lineK);
                alpha = lerp(alpha, _Line.a, lineK);

                return half4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
