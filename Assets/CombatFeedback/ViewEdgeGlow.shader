// SpellSign - glow toward the edges of the view, for a sphere centred on the
// player's head (SigilShield's bubble, DamageVignette's red flash).
//
// The head sits at the centre of the sphere, so every point of it is seen
// head-on and an ordinary fresnel rim would be invisible. Instead the glow
// grows toward the edges of the view: _Color straight ahead, _RimColor around
// the periphery.
//
// Drawn from the inside (Cull Front), no depth write, premultiplied alpha so
// the tint also shows over passthrough (straight alpha blending squares the
// alpha written to the eye buffer, which makes faint tints vanish there).
Shader "SpellSign/ViewEdgeGlow"
{
    Properties
    {
        _Color ("Tint (alpha = opacity straight ahead)", Color) = (0.3, 0.75, 1, 0.08)
        _RimColor ("Rim (alpha = opacity at the view edge)", Color) = (0.45, 0.85, 1, 0.55)
        _RimAngle ("Angle from view centre where the rim is full, degrees", Range(10, 80)) = 45
        _RimPower ("Rim falloff", Range(0.5, 8)) = 2
        _Fade ("Fade (set from script)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Bubble"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _RimColor;
                float _RimAngle;
                float _RimPower;
                half _Fade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 toFragment = normalize(input.positionWS - GetCameraPositionWS());
                float3 viewForward = -UNITY_MATRIX_V[2].xyz;
                float offCentre = 1.0 - saturate(dot(toFragment, viewForward));
                float rimAt = 1.0 - cos(radians(_RimAngle));
                half rim = pow(saturate(offCentre / rimAt), _RimPower);

                half4 c = lerp(_Color, _RimColor, rim);
                c.a *= _Fade;
                return half4(c.rgb * c.a, c.a);
            }
            ENDHLSL
        }
    }
}
