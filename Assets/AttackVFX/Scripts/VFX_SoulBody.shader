// Energy that flows over the PLAYER's body when a soul is absorbed (URP, additive).
// Drawn on a hidden duplicate "shell" of the player's mesh by PlayerSoulAbsorbFX:
//   - a glowing rim around the silhouette
//   - thin energy veins that flow upward over the body
//   - a faint overall tint
// Runtime values (_Intensity, _Scroll, colors) are set by the script.
Shader "VFX/SoulBody"
{
    Properties
    {
        _NoiseTex ("Noise (tileable, Wrap Mode = Repeat)", 2D) = "gray" {}
        _NoiseScale ("Noise Scale", Float) = 2.2
        _RimPower ("Rim Power", Float) = 2.2
        _FlowSharpness ("Flow Sharpness (higher = thinner veins)", Float) = 4

        [HideInInspector] [HDR] _ColorHot ("Hot Color", Color) = (2.5, 1.8, 0.6, 1)
        [HideInInspector] [HDR] _ColorEdge ("Edge Color", Color) = (1.6, 0.5, 0.15, 1)
        [HideInInspector] _Intensity ("Intensity", Float) = 0
        [HideInInspector] _Scroll ("Scroll", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SoulBody"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST;
                float _NoiseScale;
                float _RimPower;
                float _FlowSharpness;
                half4 _ColorHot;
                half4 _ColorEdge;
                float _Intensity;
                float _Scroll;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            float Noise(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv).r;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(_WorldSpaceCameraPos - IN.positionWS);

                float rim = pow(1.0 - saturate(dot(N, V)), _RimPower);

                // veins of energy that flow upward over the body (noise scrolls down, so the pattern rises)
                float3 wp = IN.positionWS * _NoiseScale;
                wp.y -= _Scroll;
                float3 w = abs(N);
                w /= (w.x + w.y + w.z + 0.0001);
                float nn = Noise(wp.xy) * w.z + Noise(wp.zy) * w.x + Noise(wp.xz) * w.y;
                float flow = pow(saturate(1.0 - abs(nn * 2.0 - 1.0)), _FlowSharpness);

                float g = rim * 1.3 + flow * 0.9 + 0.12;
                half3 col = lerp(_ColorHot.rgb, _ColorEdge.rgb, saturate(rim * 0.8 + (1.0 - nn) * 0.3));
                col *= g * _Intensity;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
