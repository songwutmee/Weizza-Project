// A light shaft that fades where something solid stands between it and the
// camera, instead of the flat additive quad Weizza > Atmosphere used to draw.
//
// Written by hand rather than as a Shader Graph, because there is no way to
// drive the Shader Graph node editor through the editor automation bridge this project
// uses. The technique matches Cyanilux's God Rays breakdown: sample the
// depth texture, fade the beam out near whatever is standing in front of it.
//
// Weizza's camera is always orthographic (ViewRig locks
// Lens.ModeOverride to Orthographic), so this only implements the
// orthographic depth reconstruction. The usual LinearEyeDepth macro assumes
// a perspective camera and gives the wrong answer here; for orthographic,
// view space Z already is linear distance from the camera, so the fragment's
// own depth is read straight from view space rather than reconstructed from
// NDC at all. Only the depth texture sample (the occluder) needs the
// orthographic-specific conversion.
Shader "Weizza/GodRay"
{
    Properties
    {
        _MainTex ("Beam Sprite", 2D) = "white" {}
        // White by design, not the actual beam tint. The tint lives on
        // renderer.color per instance (AtmosphereWindow.shaftColour), the
        // same as it did on the old Sprites/Default material. A coloured
        // default here would multiply against that tint a second time and
        // quietly crush the alpha, which is exactly the bug that made a
        // freshly placed beam invisible during testing.
        _Color ("Tint (HDR)", Color) = (1, 1, 1, 1)
        _OcclusionFade ("Occlusion Fade Distance (m)", Float) = 1.2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        // Additive, same reasoning as the old AtmosphereWindow material:
        // light adds to what is behind it, alpha blending would dim the
        // scene and read as fog instead of light.
        Blend SrcAlpha One
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

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
                float viewZ : TEXCOORD2;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            half4 _Color;
            float _OcclusionFade;

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positions.positionCS;
                OUT.screenPos = ComputeScreenPos(positions.positionCS);

                // View space Z is already the beam's true distance from the
                // camera under an orthographic projection, no NDC
                // reconstruction needed. positionVS.z is negative going into
                // the screen in Unity's convention, so this flips it positive
                // to match SampleSceneDepth's own convention below.
                OUT.viewZ = -positions.positionVS.z;

                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color * _Color;
                return OUT;
            }

            // Unity's built in LinearEyeDepth assumes a perspective camera.
            // Orthographic depth is already linear in the buffer, so the
            // only work here is undoing the reversed Z convention and
            // remapping the 0 to 1 range onto the near and far clip planes.
            float OrthographicEyeDepth(float rawDepth)
            {
                #if UNITY_REVERSED_Z
                rawDepth = 1.0 - rawDepth;
                #endif
                return lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half4 texColour = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);

                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float rawSceneDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = OrthographicEyeDepth(rawSceneDepth);

                // Positive once something solid sits between the beam and
                // the camera at this pixel, zero or negative in open air.
                float intoOccluder = IN.viewZ - sceneEyeDepth;

                // Soft fade over _OcclusionFade metres rather than a hard
                // cut, so a beam crossing a branch dims rather than snaps
                // off at a visible edge.
                float visibility = saturate(1.0 - intoOccluder / max(_OcclusionFade, 0.001));

                half4 result = texColour * IN.color;
                result.a *= visibility;
                return result;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
