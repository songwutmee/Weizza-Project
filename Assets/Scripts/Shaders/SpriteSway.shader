// Wind for still drawings: plants, grass clumps and hanging vines bend a little,
// each on its own beat, so a forest of static props reads as alive without a
// single extra drawn frame (the design bible rule: juice is code, not frames).
//
// The technique every 2D foliage shader shares: push vertices sideways by more
// the further they are from the planted end, so the base never moves. How far
// along the drawing a vertex is comes from its UV inside the sprite's own rect,
// which SpriteSway.cs hands over as _UVRect. UV is used rather than object
// position because it means the same thing whether or not Unity batched the
// sprite into world space.
//
// Mesh Type Tight gives more vertices to bend than Full Rect. Full Rect still
// works, it just leans as one sheet instead of curving.
//
// Built from URP's own Sprite-Unlit-Default so flip, tint and instancing behave
// exactly like every other sprite in the scene.
Shader "Weizza/SpriteSway"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Strength ("Bend Metres", Range(0, 0.6)) = 0.12
        _Speed ("Speed", Range(0, 4)) = 1.1
        [HideInInspector] _UVRect ("UV Rect", Vector) = (0, 0, 1, 1)
        [HideInInspector] _Hanging ("Hanging", Float) = 0
        [HideInInspector] _Color ("Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "CanUseSpriteAtlas" = "True" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
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
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Strength;
                float _Speed;
                float4 _UVRect;
                float _Hanging;
            CBUFFER_END

            float4 _WindA;   // set by WindField.cs, outside the material buffer on purpose

            Varyings Vert(Attributes input)
            {
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                // 0 at the planted end, 1 at the free end.
                float along = saturate((input.uv.y - _UVRect.y) / max(_UVRect.w, 1e-5));
                along = _Hanging > 0.5 ? 1.0 - along : along;
                float bend = along * along;

                // Each plant gets its own phase from where it stands, so a row
                // never sways in unison (the project notes, Plant Animations, same rule).
                float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                float phase = origin.x * 0.37 + origin.y * 0.11;
                float t = _Time.y * _Speed;
                float wave = sin(t + phase) * 0.7 + sin(t * 2.3 + phase * 1.7) * 0.3;

                // Shared wind from WindField.cs. Without one in the scene these globals
                // are zero, so fall back to a mild default instead of a dead forest.
                // x gust speed (m/s), y gust amount, z gust length (m), w direction (+1 right).
                float4 wind = _WindA.z > 0.01 ? _WindA : float4(3.0, 0.7, 16.0, 1.0);
                float travel = (origin.x - _Time.y * wind.x) / wind.z * 6.2831;
                // A gust is a slow bump that rolls along the level: plants lean one after
                // another as it passes, then ease back. Two lengths so it is never regular.
                float g1 = pow(saturate(0.5 + 0.5 * sin(travel)), 2.0);
                float g2 = 0.65 + 0.35 * sin(travel * 0.43 + 1.3);
                float gust = g1 * g2 * wind.y;
                // The wind leans a plant one way, the shake rides on top of the lean.
                float lean = (0.18 + gust) * wind.w;
                float flutter = wave * (0.45 + gust * 0.6);
                float tipFlutter = sin(_Time.y * 6.3 + phase * 3.1) * along * along * along * gust * 0.18;

                // Strength is in metres, so undo the object's own scale.
                float scaleX = max(length(GetObjectToWorldMatrix()._m00_m10_m20), 1e-4);
                input.positionOS.x += (lean + flutter + tipFlutter) * bend * _Strength / scaleX;

                Varyings o = CommonUnlitVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return CommonUnlitFragment(input, input.color);
            }
            ENDHLSL
        }
    }
}
