// Sparkle / glint for particles and glows (URP, additive). No textures needed.
//   _StarStrength 1 = a four-point star glint (twinkles when the particle rotates)
//   _StarStrength 0 = just a soft round glow
// The color and the fade come from the particle's color (vertex color), multiplied by _Tint.
Shader "VFX/ShardSparkle"
{
    Properties
    {
        [HDR] _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1.5
        _StarStrength ("Star Strength (0 = soft glow)", Range(0, 1.5)) = 1
        _StarSharpness ("Star Sharpness", Range(6, 40)) = 18
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "ShardSparkle"
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _Intensity;
                float _StarStrength;
                float _StarSharpness;
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

            half4 frag(Varyings IN) : SV_Target
            {
                float2 d = IN.uv * 2.0 - 1.0;
                float r2 = dot(d, d);

                float core = exp(-r2 * 14.0);
                float hStreak = exp(-abs(d.x) * _StarSharpness) * exp(-abs(d.y) * 2.5);
                float vStreak = exp(-abs(d.y) * _StarSharpness) * exp(-abs(d.x) * 2.5);

                float pattern = core * 1.2 + (hStreak + vStreak) * _StarStrength;
                pattern *= 1.0 - smoothstep(0.85, 1.0, sqrt(r2));   // never show the edge of the quad

                half3 col = IN.color.rgb * _Tint.rgb * pattern * IN.color.a * _Intensity;
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
