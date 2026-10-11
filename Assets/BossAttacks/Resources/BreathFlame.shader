// SpellSign - flame puffs for the dragon's breath (DragonBoss builds the
// particle systems that use it).
//
// Each particle is a soft blob whose edge is eaten away by scrolling noise, so
// a stream of them reads as licking flames instead of round dots. As the
// particle ages (vertex alpha, from Color over Lifetime) the noise eats further
// in, until only wisps are left. The dense middle burns toward white.
//
// The noise is anchored in the world, not to the particle, so flames flicker
// as they travel through it.
//
// Premultiplied alpha with the colour pushed past the alpha (_Glow): it glows
// like additive blending but still writes alpha, which it has to for anything
// to show over passthrough.
Shader "SpellSign/BreathFlame"
{
    Properties
    {
        _NoiseTex ("Noise (tiling, set from script)", 2D) = "gray" {}
        _NoiseScale ("Noise detail", Range(0.2, 4)) = 1.1
        _Scroll ("Flicker speed", Range(0, 4)) = 1.4
        _Glow ("Glow", Range(0.5, 4)) = 1.8
        _Opacity ("Opacity", Range(0, 1)) = 0.9
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST;
                float _NoiseScale;
                float _Scroll;
                half _Glow;
                half _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 noiseUV : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.color = input.color;
                output.uv = input.uv;

                // World-anchored, and always scrolling upward.
                float3 ws = positions.positionWS;
                output.noiseUV = input.uv * _NoiseScale * 0.5
                    + float2(dot(ws.xz, float2(0.7, 0.45)), ws.y * 0.8 - _Time.y * _Scroll);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half blob = saturate(1.0 - length(input.uv - 0.5) * 2.0);
                half noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, input.noiseUV).r * 0.6
                           + SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, input.noiseUV * 2.1 + 0.37).r * 0.4;
                half density = blob * (0.45 + 1.1 * noise);

                // The older the particle, the more of it the noise eats.
                half life = input.color.a;
                half cut = lerp(0.12, 0.6, 1.0 - life);
                half a = smoothstep(cut, cut + 0.25, density);
                half core = smoothstep(0.75, 1.1, density) * life;

                half3 rgb = lerp(input.color.rgb * 0.7, input.color.rgb, a);
                rgb = lerp(rgb, lerp(input.color.rgb, half3(1, 1, 1), 0.75), core);

                half alpha = a * _Opacity * smoothstep(0.0, 0.3, life);
                return half4(rgb * alpha * _Glow, alpha);
            }
            ENDHLSL
        }
    }
}
