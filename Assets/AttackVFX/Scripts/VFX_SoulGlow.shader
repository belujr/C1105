// Soft additive glow for the soul wisps (URP, unlit).
//   _Radial = 0 : a soft strip, for Trail Renderers (bright in the middle, soft edges)
//   _Radial = 1 : a round glowing orb, for the wisp head and the absorb flash
Shader "VFX/SoulGlow"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1.6, 1.0, 0.3, 1)
        _Intensity ("Intensity", Float) = 1
        _Radial ("Radial (0 = strip, 1 = round)", Range(0, 1)) = 0
        _Fade ("Fade (set by script)", Float) = 1
        _ShapeTex ("Head Shape (white on black, optional)", 2D) = "white" {}
        _UseShape ("Use Head Shape (0/1, set by script)", Range(0, 1)) = 0
        _HaloAmount ("Soft Halo Behind The Shape", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+50"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SoulGlow"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_ShapeTex);
            SAMPLER(sampler_ShapeTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _ShapeTex_ST;
                float _UseShape;
                float _HaloAmount;
                half4 _Color;
                float _Intensity;
                float _Radial;
                float _Fade;
            CBUFFER_END

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
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 c = IN.uv * 2.0 - 1.0;

                float strip = saturate(1.0 - abs(c.y));
                strip *= strip;

                float orb = saturate(1.0 - length(c));
                orb = orb * orb * (1.0 + orb * 2.0);   // soft halo with a hot core

                // optional custom head shape (comet, flame, star ...) with a soft glowing halo behind it
                float shape = SAMPLE_TEXTURE2D(_ShapeTex, sampler_ShapeTex, IN.uv).r;
                float shapedHead = saturate(shape * 1.5 + orb * _HaloAmount);
                orb = lerp(orb, shapedHead, _UseShape);

                float a = lerp(strip, orb, _Radial);

                half3 col = _Color.rgb * _Intensity * IN.color.rgb * (a * IN.color.a * _Fade);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
