// The whole frame's colour and light in one full screen pass, run by a Full Screen
// Pass Renderer Feature on PC_Renderer before post processing, so bloom catches the sun.
//
// The art is grey silhouettes. Colour comes from here: brightness is mapped through a
// three stop ramp (deep green shadow, olive middle, pale yellow light), then the frame is
// lit from the upper right, which also makes the left side the dark side, so the eye
// is pulled toward where the player has to go. Saturated things (the player, enemies,
// pickups) keep their own colour; only grey art is graded.
//
// Every value is a global set by SceneLook.cs, so a scene without that component is
// passed through untouched.
Shader "Weizza/SceneLook"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "SceneLook"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _LookOn;
            float4 _LookShadow, _LookMid, _LookLight;
            float _LookMapStrength, _LookGain, _LookBias, _LookKeepSaturated;
            float4 _LookSunPos;      // xy screen uv, z radius (screen heights), w unused
            float4 _LookSunColor;    // rgb, a = intensity
            float _LookLeftMul, _LookRightMul, _LookBottomMul, _LookBottomReach;
            float4 _LookShaft;       // x intensity, y angle in degrees, z count, w drift speed
            float _LookWorldBrightness;
            float4 _LookTintLeft, _LookTintRight;
            float _LookContrast, _LookSaturation, _LookShimmer, _LookBreath;

            half Luma(half3 c) { return dot(c, half3(0.299, 0.587, 0.114)); }

            half3 Ramp(half t)
            {
                t = saturate(t);
                half3 a = lerp(_LookShadow.rgb, _LookMid.rgb, saturate(t * 2.0));
                half3 b = lerp(_LookMid.rgb, _LookLight.rgb, saturate(t * 2.0 - 1.0));
                return t < 0.5 ? a : b;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                if (_LookOn < 0.5) return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                // Air shimmering around the sun: the picture wobbles a little where the light is
                // strongest, the way it does over warm ground. Nothing at all far from the sun.
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 fromSun = (uv - _LookSunPos.xy) * float2(aspect, 1.0);
                float near = exp(-dot(fromSun, fromSun) / (max(0.01, _LookSunPos.z) * max(0.01, _LookSunPos.z) * 0.7));
                float2 wobble = float2(sin(uv.y * 38.0 + _Time.y * 1.7), cos(uv.x * 30.0 + _Time.y * 1.3)) * 0.0022 * _LookShimmer * near;
                half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + wobble);

                half3 col = src.rgb;
                half L = Luma(col);

                // 1. Grade the grey art through the ramp, leave anything with real colour alone.
                half sat = max(col.r, max(col.g, col.b)) - min(col.r, min(col.g, col.b));
                half keep = saturate(sat * _LookKeepSaturated);
                half3 graded = Ramp(L * _LookGain + _LookBias);
                col = lerp(col, graded, _LookMapStrength * (1.0 - keep));

                // 2. Light across the frame: dark left, bright right, dark ground.
                half across = smoothstep(0.0, 1.0, uv.x);
                half light = lerp(_LookLeftMul, _LookRightMul, across) * _LookWorldBrightness;
                half ground = lerp(_LookBottomMul, 1.0, smoothstep(0.0, _LookBottomReach, uv.y));
                col *= lerp(_LookTintLeft.rgb, _LookTintRight.rgb, across);
                // Richer: push colour away from grey, and open the contrast so shadows are dark
                // and lit parts glow, instead of everything sitting in the middle.
                half grey = Luma(col);
                col = lerp(grey.xxx, col, _LookSaturation);
                col = saturate((col - 0.4) * _LookContrast + 0.4);
                col *= light * ground;

                // 3. Sunbeams from the sun, fading with distance. Strongest on the bright
                //    background, almost nothing on black silhouettes, like real haze.
                float2 toSun = uv - _LookSunPos.xy;
                float angle = radians(_LookShaft.y);
                float2 dir = float2(cos(angle), sin(angle));
                float t = _Time.y;
                float along = dot(toSun, dir) * _LookShaft.z;
                float across2 = dot(toSun, float2(-dir.y, dir.x)) * _LookShaft.z * aspect * 0.6;
                // Two sets of beams sliding across each other at different speeds, so the
                // light is never still: it drifts, swells and thins like sun through moving leaves.
                float b1 = sin(across2 * 6.2831 + t * _LookShaft.w * 6.0 + sin(along * 1.7 + t * 0.4) * 0.9) * 0.5 + 0.5;
                float b2 = sin(across2 * 9.4 - t * _LookShaft.w * 4.0 + sin(along * 2.3 - t * 0.3) * 0.7) * 0.5 + 0.5;
                float beams = pow(saturate(b1), 3.0) * 0.65 + pow(saturate(b2), 4.0) * 0.45;
                float breath = 1.0 + _LookBreath * (sin(t * 0.7) * 0.6 + sin(t * 1.9 + 1.3) * 0.4);
                float reach = exp(-length(toSun * float2(aspect, 1.0)) * 1.1);
                half haze = saturate(L * 1.6 + 0.15);
                col += _LookSunColor.rgb * beams * reach * _LookShaft.x * haze * breath;

                // 4. The sun itself: a hot core and a wide glow, past 1 so bloom blows it out.
                float2 d = (uv - _LookSunPos.xy) * float2(aspect, 1.0);
                float r2 = dot(d, d);
                float rad = max(0.01, _LookSunPos.z);
                float core = exp(-r2 / (rad * rad * 0.08));
                float glow = exp(-r2 / (rad * rad));
                col += _LookSunColor.rgb * _LookSunColor.a * (core * 2.5 + glow * 0.22) * (0.35 + L) * breath;

                return half4(col, src.a);
            }
            ENDHLSL
        }
    }
}
