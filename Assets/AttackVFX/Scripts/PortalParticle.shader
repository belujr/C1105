Shader "VFX/PortalParticle"
{
    Properties
    {
        [HDR] _Tint ("Tint (HDR)", Color) = (1, 1, 1, 1)
        _Softness ("Falloff", Range(0.5, 6)) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "PortalParticle"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _Softness;
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
                float fogFactor : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // soft round falloff; on a stretched billboard this becomes a soft streak
                float2 d = (IN.uv - 0.5) * 2.0;
                float m = pow(saturate(1.0 - length(d)), _Softness);

                float4 c = IN.color * _Tint;
                float3 rgb = MixFogColor(c.rgb, float3(0, 0, 0), IN.fogFactor); // additive: fog fades toward black
                return half4(rgb, m * c.a);
            }
            ENDHLSL
        }
    }
}
