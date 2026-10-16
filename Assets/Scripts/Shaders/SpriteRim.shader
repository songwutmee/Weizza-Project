// A thin line of sunlight along the side of a character that faces the sun.
//
// Drawn as a second sprite on top of the character, additive, so it never fights
// the flashes and dissolves that already own the character's own material. A pixel
// is rim when it is solid and the pixel a few texels toward the sun is empty: that
// is exactly the edge the light would catch. No drawn mask is needed.
//
// The sun is SceneLook's own sun (_LookSunPos, _LookSunColor), so the rim always
// agrees with the beams in the frame, and it dims in the shade of a dark stretch
// through _LookWorldBrightness. Without SceneLook the light comes from the upper right.
Shader "Weizza/SpriteRim"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _RimWidth ("Width In Texels", Range(1, 12)) = 4
        _RimStrength ("Strength", Range(0, 4)) = 1.2
        _RimTint ("Tint", Color) = (1, 0.92, 0.75, 1)
        // Written per renderer by RimLight.cs. A SpriteRenderer only passes on values
        // the shader declares here, so both have to be listed even though no one edits them.
        [HideInInspector] _RimTexel ("Texel", Vector) = (0.002, 0.002, 0, 0)
        [HideInInspector] _RimFlip ("Flip", Float) = 1
        [HideInInspector] _Color ("Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "CanUseSpriteAtlas" = "True" }

        Blend One One
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
                float3 light : TEXCOORD5;   // xy direction to the sun in uv space, z brightness
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                // One texel of the sprite's texture. Unity does not fill _MainTex_TexelSize
                // for a sprite's own texture, so RimLight.cs writes it.
                float4 _RimTexel;
                // -1 when the sprite is flipped. The renderer's own flip value was not
                // reaching this shader, which put the rim on the shadow side.
                float _RimFlip;
                float _RimWidth;
                float _RimStrength;
                half4 _RimTint;
            CBUFFER_END

            // SceneLook's globals. Zero when SceneLook is not in the scene.
            float _LookOn;
            float4 _LookSunPos;
            float4 _LookSunColor;
            float _LookWorldBrightness;

            Varyings Vert(Attributes input)
            {
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                Varyings o = CommonUnlitVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;

                // Where the character is on screen, in the same 0..1 space SceneLook uses.
                float4 centre = TransformObjectToHClip(float3(0.0, 0.0, 0.0));
                float2 screen = centre.xy / max(centre.w, 1e-5);
                screen.y *= _ProjectionParams.x;
                screen = screen * 0.5 + 0.5;

                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 toSun = _LookOn > 0.5 ? (_LookSunPos.xy - screen) * float2(aspect, 1.0) : float2(0.7, 0.7);
                float distance = length(toSun);
                float2 direction = toSun / max(distance, 1e-4);
                // A flipped sprite is mirrored, so its texture runs the other way on screen.
                direction.x *= _RimFlip;

                float near = 0.6 + 0.4 * exp(-distance * 1.1);
                float shade = _LookOn > 0.5 ? lerp(0.45, 1.0, saturate(_LookWorldBrightness)) : 1.0;
                o.light = float3(direction, near * shade);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 step = input.light.xy * _RimTexel.xy * _RimWidth;
                half solid = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                // Two samples toward the sun: the far one gives the width, the near one
                // keeps the line from breaking up on thin parts like hair and fingers.
                half far = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + step).a;
                half nearSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + step * 0.5).a;
                half rim = solid * saturate(1.0 - (far * 0.6 + nearSample * 0.4));

                half3 sun = _LookOn > 0.5 ? _LookSunColor.rgb : half3(1.0, 0.95, 0.8);
                half3 colour = _RimTint.rgb * sun * rim * _RimStrength * input.light.z * input.color.a;
                return half4(colour, 0.0);
            }
            ENDHLSL
        }
    }
}
