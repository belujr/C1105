Shader "VFX/PortalSwirl"
{
    Properties
    {
        [HDR] _ColorVoid ("Void Color", Color) = (0.03, 0.0, 0.12, 1)
        [HDR] _ColorMid  ("Swirl Color", Color) = (0.35, 0.25, 2.2, 1)
        [HDR] _ColorCore ("Core Color", Color) = (0.6, 0.9, 2.2, 1)
        [HDR] _ColorRim  ("Rim Color", Color) = (1.6, 0.6, 2.6, 1)
        _Intensity   ("Intensity", Float) = 1.3
        _CoreGlow    ("Core Glow", Float) = 0.6
        _VoidAlpha   ("Void Opacity", Range(0, 1)) = 0.6
        _DiscRadius  ("Disc Fill (UV)", Range(0.5, 1)) = 0.8
        _RimWidth    ("Rim Width", Range(0.01, 0.5)) = 0.16
        _Softness    ("Edge Softness", Range(0.005, 0.5)) = 0.08
        _Aspect      ("Aspect (width / height)", Range(0.5, 1)) = 1
        _EdgeRagged  ("Ragged Edge", Range(0, 0.4)) = 0.1
        _EdgeFreq    ("Ragged Edge Frequency", Float) = 2.5
        _EdgeSpeed   ("Ragged Edge Speed", Float) = 0.8
        _Arms        ("Spiral Arms", Float) = 5
        _Twist       ("Spiral Twist", Float) = 8
        _SwirlSpeed  ("Swirl / Inflow Speed", Float) = 1.2
        _NoiseScale  ("Noise Scale", Float) = 3.2
        _InflowSpeed ("Noise Inflow Speed", Float) = 0.8
        _Pulse       ("Pulse Amount", Range(0, 1)) = 0.15
        _PulseSpeed  ("Pulse Speed", Float) = 1.8
        _Surge       ("Surge (0-1, set by script)", Range(0, 1)) = 0
        _Fade        ("Master Fade", Range(0, 1)) = 1
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
            Name "PortalSwirl"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorVoid;
                float4 _ColorMid;
                float4 _ColorCore;
                float4 _ColorRim;
                float _Intensity;
                float _CoreGlow;
                float _VoidAlpha;
                float _DiscRadius;
                float _RimWidth;
                float _Softness;
                float _Aspect;
                float _EdgeRagged;
                float _EdgeFreq;
                float _EdgeSpeed;
                float _Arms;
                float _Twist;
                float _SwirlSpeed;
                float _NoiseScale;
                float _InflowSpeed;
                float _Pulse;
                float _PulseSpeed;
                float _Surge;
                float _Fade;
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
                float fogFactor : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float VNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                for (int k = 0; k < 4; k++)
                {
                    v += a * VNoise(p);
                    p = p * 2.03 + float2(17.1, 9.7);
                    a *= 0.5;
                }
                return v;
            }

            float2 Rot(float2 p, float ang)
            {
                float s, c;
                sincos(ang, s, c);
                return float2(c * p.x - s * p.y, s * p.x + c * p.y);
            }

            // Noise that keeps zooming out toward the middle. Two copies half a cycle apart
            // are cross-faded, so the loop never pops.
            float FlowLayer(float2 q, float phase)
            {
                float s = lerp(0.5, 1.8, phase);
                float w = 1.0 - abs(2.0 * phase - 1.0);
                return w * Fbm(q * _NoiseScale * s + phase * 3.1);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // p: -1..1 across the disc, length 1 at the disc edge (narrower than tall when Aspect < 1)
                float2 p = (IN.uv - 0.5) * 2.0 / max(_DiscRadius, 0.01);
                p.x /= max(_Aspect, 0.3);
                float t = _Time.y;

                // living edge: the outline wobbles and flares (more during a surge).
                // The noise is sampled on a circle, so it has no seam.
                float ang = atan2(p.y, p.x);
                float2 circle = float2(cos(ang), sin(ang));
                float edgeN = Fbm(circle * _EdgeFreq + float2(t * _EdgeSpeed, -t * _EdgeSpeed * 0.7));
                float ragged = _EdgeRagged * (1.0 + 1.5 * _Surge);
                float r = length(p) / max(0.25, 1.0 + (edgeN - 0.5) * 2.0 * ragged);

                float pulse = 1.0 + _Pulse * sin(t * _PulseSpeed);

                // noise that rotates faster near the middle and flows inward
                float spin = 6.0 * (1.0 - saturate(r)) + t * 0.35;
                float2 q = Rot(p, spin);
                float ph0 = frac(t * _InflowSpeed * 0.25);
                float ph1 = frac(ph0 + 0.5);
                float n = FlowLayer(q, ph0) + FlowLayer(q, ph1);
                n = smoothstep(0.28, 0.72, n);

                // spiral arms (whole number of arms, so there is no seam) flowing toward the centre
                float arm = 0.5 + 0.5 * sin(_Arms * ang + _Twist * r + t * _SwirlSpeed);
                arm = arm * arm;
                float body = saturate(arm * (0.5 + n));

                float core = pow(saturate(1.0 - r), 3.0) * pulse * (1.0 + _Surge);
                float edgeDist = 1.0 - r;
                float disc = smoothstep(0.0, _Softness, edgeDist + (n - 0.5) * 0.05);

                float halfRim = _RimWidth * 0.5;
                float rim = saturate(1.0 - abs(edgeDist - halfRim) / max(halfRim, 0.0001));
                rim = rim * rim * disc * (1.0 + 2.0 * _Surge);

                float3 col = _ColorVoid.rgb;
                col = lerp(col, _ColorMid.rgb, body);
                col += _ColorCore.rgb * core * _CoreGlow;
                col += _ColorRim.rgb * rim * (0.7 + 0.8 * n) * pulse;
                col *= _Intensity;

                float alpha = disc * saturate(_VoidAlpha + body * 0.5 + core * 0.6);
                alpha = saturate(alpha + rim * 0.6) * _Fade;

                col = MixFog(col, IN.fogFactor);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
