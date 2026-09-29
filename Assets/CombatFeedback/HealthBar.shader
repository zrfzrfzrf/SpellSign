// SpellSign - sleek health bar on a single quad (HealthBarView).
//
// Rounded dark pill with a thin border; inside it the white damage trail and,
// on top, the fill. Both end in rounded caps. Everything is drawn from signed
// distances in quad space, so it stays crisp at any size or distance.
// _Aspect (width / height of the quad) is set from script so the caps stay
// round when the quad is stretched.
Shader "SpellSign/HealthBar"
{
    Properties
    {
        _FillColor ("Fill", Color) = (0.89, 0.29, 0.29, 1)
        _TrailColor ("Damage trail", Color) = (1, 1, 1, 0.9)
        _BackColor ("Background", Color) = (0.08, 0.08, 0.08, 0.75)
        _BorderColor ("Border", Color) = (1, 1, 1, 0.35)
        _Border ("Border thickness (fraction of height)", Range(0, 0.4)) = 0.14
        _Fill ("Fill amount", Range(0, 1)) = 1
        _Trail ("Trail amount", Range(0, 1)) = 1
        _Aspect ("Width / height", Float) = 10
        _Alpha ("Overall opacity", Range(0, 1)) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test (Always = never hidden)", Float) = 4
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Bar"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _FillColor;
                half4 _TrailColor;
                half4 _BackColor;
                half4 _BorderColor;
                float _Border;
                float _Fill;
                float _Trail;
                float _Aspect;
                half _Alpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            // Signed distance to a pill (rounded box) centred at c, half-size h.
            float Pill(float2 p, float2 c, float2 h)
            {
                float r = min(h.x, h.y);
                float2 q = abs(p - c) - (h - r);
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            // Pill spanning the inner area from its left edge to `amount` of its width.
            float Bar(float2 p, float halfWidth, float amount)
            {
                float halfHeight = 0.5 - _Border;
                float left = -halfWidth + _Border;
                float right = left + 2.0 * (halfWidth - _Border) * saturate(amount);
                return Pill(p, float2((left + right) * 0.5, 0.0), float2((right - left) * 0.5, halfHeight));
            }

            half Coverage(float d)
            {
                float aa = max(fwidth(d), 1e-4);
                return saturate(0.5 - d / aa);
            }

            half4 Over(half4 dst, half3 rgb, half a)
            {
                return half4(lerp(dst.rgb, rgb, a), dst.a + a * (1.0 - dst.a));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Quad space in units of bar height, origin at the centre.
                float halfWidth = _Aspect * 0.5;
                float2 p = (input.uv - 0.5) * float2(_Aspect, 1.0);

                float outer = Pill(p, 0.0, float2(halfWidth, 0.5));
                float inner = Pill(p, 0.0, float2(halfWidth - _Border, 0.5 - _Border));

                half4 c = 0;
                c = Over(c, _BorderColor.rgb, _BorderColor.a * Coverage(outer));
                c.rgb = lerp(c.rgb, _BackColor.rgb, Coverage(inner));
                c.a = max(c.a, _BackColor.a * Coverage(inner));

                if (_Trail > _Fill)
                    c = Over(c, _TrailColor.rgb, _TrailColor.a * Coverage(Bar(p, halfWidth, _Trail)));

                // Slightly brighter towards the top for a bit of depth.
                half3 fill = _FillColor.rgb * lerp(0.8, 1.2, saturate(input.uv.y));
                if (_Fill > 0.0)
                    c = Over(c, fill, _FillColor.a * Coverage(Bar(p, halfWidth, _Fill)));

                c.a *= _Alpha;
                return half4(c.rgb * c.a, c.a);
            }
            ENDHLSL
        }
    }
}
