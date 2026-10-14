// Impact burst shown on the player when an enemy hits them (URP, additive, camera-facing quad).
//   - white-hot core flash
//   - sharp rays that shoot outward
//   - one thin shockwave ring
// Runtime values (_Progress, _Seed, _Intensity) are set by PlayerHitFX.
Shader "VFX/PlayerImpact"
{
    Properties
    {
        [HDR] _ColorCore ("Core Color", Color) = (3, 2.2, 1.8, 1)
        [HDR] _ColorEdge ("Edge Color", Color) = (2.4, 0.12, 0.1, 1)
        _RayCount ("Ray Count", Float) = 9

        [HideInInspector] _Progress ("Progress", Float) = 0
        [HideInInspector] _Seed ("Seed", Float) = 0
        [HideInInspector] _Intensity ("Intensity", Float) = 1
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
            Name "PlayerImpact"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorCore;
                half4 _ColorEdge;
                float _RayCount;
                float _Progress;
                float _Seed;
                float _Intensity;
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

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv * 2.0 - 1.0;
                float r = length(uv);
                float a = atan2(uv.y, uv.x);
                float p = saturate(_Progress);

                float decay = pow(1.0 - p, 1.5);
                float grow = 1.0 - pow(1.0 - p, 3.0);   // fast start, slow end

                // 1) white-hot core flash, gone after the first moments
                float coreR = lerp(0.12, 0.38, grow);
                float core = exp(-pow(r / coreR, 2.0)) * (1.0 - smoothstep(0.0, 0.45, p));

                // 2) rays (integer counts, so there is no seam where the angle wraps)
                float n = max(3.0, round(_RayCount));
                float ph = _Seed * 6.2831853;
                float spikes = pow(saturate(cos(a * n + ph)), 7.0);
                float spikes2 = pow(saturate(cos(a * (n + 3.0) - ph * 1.7)), 9.0) * 0.6;
                float rays = max(spikes, spikes2);
                float lenVar = 0.7 + 0.3 * cos(a * 3.0 + ph);
                float rayLen = lerp(0.2, 0.95, grow) * lenVar;
                float rayMask = (1.0 - smoothstep(rayLen * 0.4, rayLen, r)) * smoothstep(0.03, 0.14, r);
                float rayTerm = rays * rayMask * decay;

                // 3) thin shockwave ring
                float ringPos = lerp(0.12, 0.88, grow);
                float ring = exp(-pow((r - ringPos) / 0.055, 2.0)) * decay;

                // soft round mask so the edge of the quad is never visible
                float mask = 1.0 - smoothstep(0.82, 1.0, r);

                float g = (core * 2.2 + rayTerm * 1.8 + ring * 0.8) * mask * _Intensity;

                // white-hot in the middle, crimson toward the tips and later in time
                float t = saturate(r * 1.3 + p * 0.8);
                half3 col = lerp(_ColorCore.rgb, _ColorEdge.rgb, t) * g;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
