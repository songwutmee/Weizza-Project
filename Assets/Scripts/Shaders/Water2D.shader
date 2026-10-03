// Cyanilux's 2D Water Shader Breakdown, adapted for the reflection technique
// (a second camera into a RenderTexture, see WaterSurface.cs), but Cyanilux's
// own specular is noise-thresholded sparkle with no real lighting at all
// (checked directly against the tutorial's own text: "there's no need for
// actual specular lighting calculations, that would probably require normal
// maps which I haven't used here"). That is exactly what read as flat,
// lifeless glass through four earlier drafts in this file's history.
//
// The fix, researched properly rather than guessed at a fifth time: a real
// stylized water shader (ameye.dev's Gerstner wave breakdown, the standard
// technique behind most Unity ocean shaders) displaces the surface with a
// sum of sine waves and derives an actual surface normal from the wave's own
// slope, then lights it with a real N dot L. This project's water is a flat
// 2D side view, not a 3D mesh, so there is no vertex displacement or
// tangent/binormal cross product here, but the same idea reduces cleanly to
// one dimension: track the surface height h(x) as a sum of sines, take its
// analytic derivative for the local slope, and build a 2D pseudo-normal from
// that slope. The specular highlight this produces travels along a wave
// crest as the wave moves, which is what a coherent lit surface looks like,
// instead of static random sparkle sitting on top of it.
//
// Written by hand rather than as a Shader Graph, same reason as every other
// shader in this folder: there is no way to drive the Shader Graph node
// editor through the editor automation bridge this project uses.
//
// No depth reconstruction anywhere in this shader, so unlike GodRay it needs
// no orthographic-specific branch: ComputeScreenPos works the same for
// orthographic and perspective, since w is already 1 for an orthographic
// vertex and the divide is a no-op.
Shader "Weizza/Water2D"
{
    Properties
    {
        _MainTex ("Sprite Texture (unused, kept for SRP batcher)", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _ReflectionTex ("Reflection RT", 2D) = "black" {}
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 0.7
        _ReflectionExposure ("Reflection Exposure", Range(0.5, 3)) = 1.6
        _DeepColor ("Deep Colour", Color) = (0.03, 0.15, 0.2, 0.9)
        _ShallowColor ("Shallow Colour", Color) = (0.28, 0.62, 0.62, 0.4)

        [Header(Wave shape)]
        _WaveScale ("Wave Scale (base wavenumber)", Float) = 2.4
        _WaveSpeed ("Wave Speed", Float) = 0.6
        _WaveAmount ("Wave Amount (0-1 of height)", Range(0, 0.2)) = 0.045
        _DistortStrength ("Reflection Distort Strength", Range(0, 0.1)) = 0.03

        [Header(Foam at the surface edge)]
        _FoamColor ("Foam Colour", Color) = (1.5, 2.1, 2.0, 1)
        _FoamWidth ("Foam Width (0-1 of height)", Range(0, 0.3)) = 0.06

        [Header(Caustics)]
        _CausticColor ("Caustic Colour (HDR)", Color) = (1.6, 2.0, 1.7, 1)
        _CausticScale ("Caustic Scale", Float) = 16
        _CausticSpeed ("Caustic Speed", Float) = 0.5
        _CausticStrength ("Caustic Strength", Range(0, 2)) = 0.9

        [Header(Specular)]
        _LightDir ("Light Direction (screen XY)", Vector) = (0.5, 0.85, 0, 0)
        _SpecularColor ("Specular Colour (HDR)", Color) = (2.4, 2.5, 2.0, 1)
        _Shininess ("Shininess", Range(1, 128)) = 24
        _SparkleScale ("Micro Sparkle Noise Scale", Float) = 34
        _SparkleThreshold ("Micro Sparkle Threshold", Range(0, 1)) = 0.85

        [Header(Ripple)]
        _RippleCenter ("Ripple Centre (world X)", Float) = 0
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0
        _RippleRadius ("Ripple Radius (m)", Float) = 2.2
        _RippleFrequency ("Ripple Frequency", Float) = 10
        _RippleSpeed ("Ripple Speed", Float) = 4
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
                float4 screenPos : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            TEXTURE2D(_ReflectionTex);
            SAMPLER(sampler_ReflectionTex);
            float4 _MainTex_ST;
            half4 _Color;
            half4 _DeepColor;
            half4 _ShallowColor;
            half4 _FoamColor;
            half4 _CausticColor;
            half4 _SpecularColor;
            float4 _LightDir;
            float _ReflectionStrength;
            float _ReflectionExposure;
            float _WaveScale;
            float _WaveSpeed;
            float _WaveAmount;
            float _DistortStrength;
            float _FoamWidth;
            float _CausticScale;
            float _CausticSpeed;
            float _CausticStrength;
            float _Shininess;
            float _SparkleScale;
            float _SparkleThreshold;
            float _RippleCenter;
            float _RippleStrength;
            float _RippleRadius;
            float _RippleFrequency;
            float _RippleSpeed;

            // Kept only for the small residual micro sparkle on top of the
            // analytic wave, same hash/value noise as Dissolve.shader.
            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 uv)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);
                float a = Hash(i);
                float b = Hash(i + float2(1, 0));
                float c = Hash(i + float2(0, 1));
                float d = Hash(i + float2(1, 1));
                float2 u = f * f * (3 - 2 * f);
                return lerp(a, b, u.x) + (c - a) * u.y * (1 - u.x) + (d - b) * u.x * u.y;
            }

            // A Gerstner wave train reduced to one dimension: the surface is
            // a line, not a mesh, so there is no tangent/binormal cross
            // product, only a height and its slope along X. Four harmonics,
            // each roughly doubling frequency and halving amplitude like a
            // real Gerstner sum's 1/k falloff, each drifting a different
            // speed and direction so no two crests ever stay lined up.
            void WaveHeightAndSlope(float x, float t, out float height, out float slope)
            {
                const float freqMul[4] = { 1.0, 1.9, 3.3, 5.7 };
                const float ampMul[4] = { 1.0, 0.5, 0.28, 0.14 };
                const float speedMul[4] = { 1.0, -1.4, 1.7, -2.3 };
                const float phase[4] = { 0.0, 2.1, 4.6, 1.3 };

                height = 0;
                slope = 0;

                UNITY_UNROLL
                for (int i = 0; i < 4; i++)
                {
                    float k = _WaveScale * freqMul[i];
                    float w = k * x - t * _WaveSpeed * speedMul[i] + phase[i];
                    float a = _WaveAmount * ampMul[i];
                    height += a * sin(w);
                    slope += a * k * cos(w);
                }
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color * _Color;
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float t = _Time.y;

                float height, slope;
                WaveHeightAndSlope(IN.uv.x, t, height, slope);

                // The player's own ripple folds into the same height and
                // slope sum, not a separate bolt-on effect, so it lights up
                // exactly like any other wave rather than only warping the
                // reflection under their feet. Its slope contribution uses
                // sign(x - centre) because rippleDist is abs(x - centre),
                // an approximation that ignores the falloff term's own
                // derivative but is close enough at the scale this reads.
                float toCentre = IN.positionWS.x - _RippleCenter;
                float rippleDist = abs(toCentre);
                float rippleFalloff = saturate(1 - rippleDist / _RippleRadius);
                rippleFalloff *= rippleFalloff;
                float ripplePhase = rippleDist * _RippleFrequency - t * _RippleSpeed * 6.2831;
                float rippleAmp = 0.5 * _RippleStrength * rippleFalloff;
                height += sin(ripplePhase) * rippleAmp;
                slope += sign(toCentre) * _RippleFrequency * cos(ripplePhase) * rippleAmp;

                // A little non-analytic noise on top so the surface does not
                // read as a perfect stack of sines, the same reason a real
                // water line has chop scattered across its dominant swell.
                float microNoise = (Noise(float2(IN.uv.x * _SparkleScale * 0.5, t * 0.4)) - 0.5) * 0.15;
                float depth = saturate(IN.uv.y - height - microNoise * _WaveAmount);

                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                screenUV += float2(-slope, 0) * _DistortStrength;

                half4 reflection = SAMPLE_TEXTURE2D(_ReflectionTex, sampler_ReflectionTex, screenUV);
                reflection.rgb = pow(reflection.rgb * _ReflectionExposure, 0.85);

                half4 baseColour = lerp(_DeepColor, _ShallowColor, depth);

                // Fresnel style curve, steep near the surface and flat
                // through the depth, closer to how a real water surface
                // reflects hardest along its own line and lets you see
                // through it looking straight down.
                float fresnel = pow(depth, 2.2);
                half3 colour = lerp(baseColour.rgb, reflection.rgb, _ReflectionStrength * fresnel);
                half alpha = lerp(baseColour.a, saturate(baseColour.a + 0.15), depth);

                // Caustics, a moving light net from two crossed sine grids,
                // strongest a little below the surface and fading at both
                // the edge and full depth, the way real shallow water light
                // actually behaves.
                float c1 = sin(IN.uv.x * _CausticScale + t * _CausticSpeed
                             + Noise(IN.uv * 3 + t * 0.1) * 2);
                float c2 = sin(IN.uv.x * _CausticScale * 1.3 - t * _CausticSpeed * 1.4
                             + IN.uv.y * _CausticScale * 0.5 + 4.7);
                float caustic = saturate(c1 * c2);
                caustic = pow(caustic, 3) * _CausticStrength;
                caustic *= smoothstep(0, 0.15, depth) * smoothstep(1, 0.55, depth);
                colour += _CausticColor.rgb * caustic;

                // The real specular: a pseudo normal built from the wave's
                // own slope, lit against a fixed light direction the way
                // the game's diagonal light shafts already imply. This is
                // what makes the highlight travel along a wave crest as it
                // moves, rather than sitting as static noise on top of a
                // moving picture, and is the actual fix for "does not read
                // as liquid" over three earlier noise-only attempts.
                float2 normal = normalize(float2(-slope, 1));
                float2 lightDir = normalize(_LightDir.xy);
                float nDotL = saturate(dot(normal, lightDir));
                float specular = pow(nDotL, _Shininess) * depth;
                colour += _SpecularColor.rgb * specular * _SpecularColor.a;

                // A little threshold noise for fine glitter between the big
                // coherent highlight's crests, the finishing touch rather
                // than the whole effect the way it was before.
                float sparkleNoise = Noise(float2(IN.uv.x * _SparkleScale + t * 0.3,
                                                   IN.uv.y * _SparkleScale * 0.4 - t * 0.2));
                float sparkle = smoothstep(_SparkleThreshold, 1, sparkleNoise) * depth * 0.6;
                colour += _SpecularColor.rgb * sparkle;

                // Foam right at the wavy surface line, so its own curve
                // visibly follows the same wave the reflection and the
                // specular do.
                float foamMask = 1 - smoothstep(0, _FoamWidth, depth);
                colour += _FoamColor.rgb * foamMask * _FoamColor.a;

                colour *= IN.color.rgb;
                alpha *= IN.color.a;

                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
