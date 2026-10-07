// Energy ripple that runs over an enemy's body from the point where it was hit (URP, additive).
// It is drawn on a duplicate "shell" of the enemy's mesh, so it works with any enemy material.
//
//   - a hot flash over the whole body for the first instant
//   - a bright wave front that expands from the hit point across the surface
//   - glowing energy cracks (veins) that appear inside the wave and fade
//   - a rim glow on the silhouette
// Runtime values (_HitPos, _Progress, _Intensity, colors) are set by EnemyHitFX.
Shader "VFX/HitGlow"
{
    Properties
    {
        _NoiseTex ("Noise (tileable, Wrap Mode = Repeat)", 2D) = "gray" {}
        _CrackScale ("Crack Scale", Float) = 2.5
        _CrackSharpness ("Crack Sharpness (higher = thinner veins)", Float) = 10
        _RimPower ("Rim Power", Float) = 2.5

        [HideInInspector] _HitPos ("Hit Position", Vector) = (0, 0, 0, 0)
        [HideInInspector] _Progress ("Progress", Float) = 1
        [HideInInspector] _Intensity ("Intensity", Float) = 1
        [HideInInspector] _MaxRadius ("Max Radius", Float) = 1.8
        [HideInInspector] [HDR] _ColorHot ("Hot Color", Color) = (3, 2.4, 1.6, 1)
        [HideInInspector] [HDR] _ColorEdge ("Edge Color", Color) = (2.2, 0.45, 0.2, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "HitGlow"
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
                float _CrackScale;
                float _CrackSharpness;
                float _RimPower;
                float4 _HitPos;
                float _Progress;
                float _Intensity;
                float _MaxRadius;
                half4 _ColorHot;
                half4 _ColorEdge;
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
                float p = saturate(_Progress);

                float dist = distance(IN.positionWS, _HitPos.xyz);

                // wave front: starts fast, slows down
                float R = max(0.05, _MaxRadius * (1.0 - pow(1.0 - p, 3.0)));
                float fade = pow(1.0 - p, 1.6);

                // 1) glow behind the wave front, strongest at the hit point
                float inside = 1.0 - smoothstep(R * 0.6, R, dist);

                // 2) bright rim of the wave
                float ring = exp(-pow((dist - R) / (0.12 + 0.10 * p), 2.0));

                // 3) energy cracks: veins found where a noise value crosses 0.5, mixed from 3 directions
                float3 wp = IN.positionWS * _CrackScale;
                float3 w = abs(N);
                w /= (w.x + w.y + w.z + 0.0001);
                float nn = Noise(wp.xy) * w.z + Noise(wp.zy) * w.x + Noise(wp.xz) * w.y;
                float crack = pow(saturate(1.0 - abs(nn * 2.0 - 1.0)), _CrackSharpness);
                float crackMask = 1.0 - smoothstep(R * 0.9, R * 1.15, dist);

                // 4) rim glow on the silhouette
                float rim = pow(1.0 - saturate(dot(N, V)), _RimPower);

                // 5) quick hot flash over the whole body in the first instant
                float body = (1.0 - smoothstep(0.0, 0.22, p)) * 0.35;

                float g = rim * 0.55 * (0.4 + 0.6 * fade)
                        + inside * fade
                        + ring * 1.6 * (1.0 - p * 0.5)
                        + crack * crackMask * 1.3 * fade
                        + body;

                // hot near the hit point, edge color toward the wave front and later in time
                float t = saturate(dist / max(R, 0.01));
                half3 col = lerp(_ColorHot.rgb, _ColorEdge.rgb, saturate(t * 1.2 + p * 0.6));
                col *= g * _Intensity;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
