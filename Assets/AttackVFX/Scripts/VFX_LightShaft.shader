// A soft beam of light (moonlight / sunlight shaft through the canopy), drawn on a tall camera-facing strip (URP, additive).
//   - soft edges across the beam
//   - fades out at the ground end (no hard line where it meets the floor) and at the sky end
//   - slow streaks that shimmer along the beam
// All noise is procedural. Brightness (_Intensity) and _Seed are set by AmbientVFX.
// Mesh data: UV0.x = across the beam (0..1), UV0.y = 0 at the ground end .. 1 at the sky end.
Shader "VFX/LightShaft"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.55, 0.75, 1.1, 1)
        _EdgePower ("Edge Softness (higher = narrower core)", Float) = 1.6
        _NoiseScale ("Streak Density", Float) = 6
        _NoiseContrast ("Streak Contrast", Range(0, 1)) = 0.55
        _ShimmerSpeed ("Shimmer Speed", Float) = 0.08
        [HideInInspector] _Intensity ("Intensity (set by script)", Float) = 0
        [HideInInspector] _Seed ("Seed", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test", Float) = 4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+5"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "LightShaft"
            Blend One One
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _EdgePower;
                float _NoiseScale;
                float _NoiseContrast;
                float _ShimmerSpeed;
                float _Intensity;
                float _Seed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float VNoise(float2 x)
            {
                float2 i = floor(x);
                float2 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash12(i), Hash12(i + float2(1, 0)), f.x),
                            lerp(Hash12(i + float2(0, 1)), Hash12(i + float2(1, 1)), f.x), f.y);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float u = IN.uv.x;
                float v = IN.uv.y;

                float across = pow(saturate(1.0 - abs(u * 2.0 - 1.0)), _EdgePower);
                float along = smoothstep(0.0, 0.25, v) * (1.0 - smoothstep(0.55, 1.0, v));

                // slow streaks that drift along the beam
                float n = VNoise(float2(u * _NoiseScale + _Seed * 13.1, v * 1.5 - _Time.y * _ShimmerSpeed + _Seed * 5.3));
                float streak = lerp(1.0 - _NoiseContrast, 1.0, n);

                float a = across * along * streak;
                return half4(_Color.rgb * a * _Intensity, 1.0);
            }
            ENDHLSL
        }
    }
}
