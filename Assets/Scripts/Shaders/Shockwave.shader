// Full screen ripple for a roar or a blast: a ring that bends the picture as it
// passes, with the colour channels pulled apart a little at its edge. Driven by
// ScreenShockwave through global properties, so nothing holds a material.
// Run by a Full Screen Pass Renderer Feature on PC_Renderer.
Shader "Weizza/Shockwave"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "Shockwave"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // Up to two rings at once, so a second roar burst can overlap the first.
            // xy centre in screen uv, z radius in screen heights, w strength.
            float4 _ShockA;
            float4 _ShockB;
            float _ShockWidth;

            float2 Offset(float2 uv, float4 shock)
            {
                if (shock.w <= 0.0001) return 0;
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 d = uv - shock.xy;
                d.x *= aspect;
                float dist = length(d);
                // Signed distance from the ring, in ring widths. The offset pushes
                // outward just inside the ring and pulls inward just outside it,
                // which is what reads as a lens moving across the picture.
                float band = (dist - shock.z) / _ShockWidth;
                float push = band * exp(-band * band * 2.0);
                float2 dir = dist > 0.0001 ? d / dist : 0;
                dir.x /= aspect;
                return -dir * push * shock.w;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 offset = Offset(uv, _ShockA) + Offset(uv, _ShockB);

                // Red bends a little more and blue a little less: a thin rainbow
                // fringe on the ring, the cheap version of real refraction.
                half r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + offset * 1.25).r;
                half2 ga = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + offset).ga;
                half b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + offset * 0.75).b;
                return half4(r, ga.x, b, ga.y);
            }
            ENDHLSL
        }
    }
}
