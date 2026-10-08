// Soft round particle for fire embers, smoke and the glow halo (URP, unlit).
// Works with ParticleSystems (vertex colour) and with simple quads.
//   Additive (embers, halo): Src Blend = One,      Dst Blend = One,              Additive = 1
//   Smoke:                   Src Blend = SrcAlpha, Dst Blend = OneMinusSrcAlpha, Additive = 0
// FireVFX sets these for you.
Shader "VFX/FireParticle"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1.5, 0.6, 0.15, 1)
        _Intensity ("Intensity", Float) = 1
        _Power ("Softness Power (higher = tighter dot)", Float) = 1.5
        [Toggle] _Additive ("Additive", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test", Float) = 4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+20"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "FireParticle"
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity;
                float _Power;
                float _Additive;
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
                float a = saturate(1.0 - length(c));
                a = pow(a, _Power);

                half3 rgb = _Color.rgb * IN.color.rgb * _Intensity;
                float alpha = a * IN.color.a * _Color.a;

                // additive: colour is scaled by the dot; alpha blend: colour stays, alpha carries the dot
                return _Additive > 0.5 ? half4(rgb * alpha, alpha) : half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
