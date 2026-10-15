// Energy trail for the Imp's projectile (URP, additive). Use it on a TrailRenderer or LineRenderer.
//   - a bright thin core line
//   - a soft glowing body
//   - thin streaks of light that flow along the trail (like the light lines in the reference)
// The color and the fade-out come from the renderer's color gradient (vertex color); no textures needed.
Shader "VFX/ShardTrail"
{
    Properties
    {
        _Intensity ("Intensity", Float) = 1.8
        _CoreWidth ("Core Width", Range(0.05, 0.5)) = 0.16
        _StreakAmount ("Streak Amount", Range(0, 2)) = 0.9
        _StreakStretch ("Streak Stretch (higher = shorter streaks)", Float) = 14
        _FlowSpeed ("Flow Speed", Float) = 3
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
            Name "ShardTrail"
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _CoreWidth;
                float _StreakAmount;
                float _StreakStretch;
                float _FlowSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                OUT.uv = IN.uv;
                return OUT;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float across = abs(IN.uv.y * 2.0 - 1.0);              // 0 in the middle of the ribbon, 1 at its edges

                float core = exp(-pow(across / max(_CoreWidth, 0.01), 2.0));
                float body = pow(saturate(1.0 - across), 2.0);

                // thin streaks of light that flow along the trail
                float n = ValueNoise(float2(IN.uv.x * _StreakStretch - _Time.y * _FlowSpeed, IN.uv.y * 9.0));
                float streaks = smoothstep(0.55, 0.9, n) * saturate(1.0 - across);

                float pattern = core * 1.4 + body * 0.55 + streaks * _StreakAmount;

                // the vertex color gives the tint and the fade (the renderer's color gradient); the core is a bit whiter
                half3 col = IN.color.rgb * pattern + core * 0.5;
                col *= IN.color.a * _Intensity;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
