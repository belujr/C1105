// Ground shockwave distortion for the player's AOE attack (URP).
// A thin ripple that PUSHES THE GRASS and bends the picture as the wave front passes, plus a light heat haze behind it.
// It re-samples the screen instead of drawing anything opaque, so it never hides the grass cutting.
//
// REQUIRED: "Opaque Texture" ON in your URP Asset (the same setting your enemy hit distortion already needs).
// Runtime values are set by AOEShockwaveVFX.
Shader "VFX/AOEDistortion"
{
    Properties
    {
        _Strength ("Strength (screen UV units)", Float) = 0.02
        _Width ("Ripple Width", Range(0.03, 0.4)) = 0.1
        _HazeStrength ("Heat Haze Behind The Wave", Range(0, 1)) = 0.35

        [HideInInspector] _Progress ("Progress", Float) = 0
        [HideInInspector] _HalfAngle ("Half Cone Angle (radians, > 3.2 = full circle)", Float) = 1
        [HideInInspector] _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            // drawn BEFORE the other transparent effects, so they are not covered by the re-sampled screen
            "Queue" = "Transparent-50"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "AOEDistortion"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Strength;
                float _Width;
                float _HazeStrength;
                float _Progress;
                float _HalfAngle;
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
                float4 centerSP : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                // where the player (the middle of the quad) is on the screen
                float4 c = TransformObjectToHClip(float3(0.0, 0.0, 0.0));
                OUT.centerSP = ComputeScreenPos(c);
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

            float Fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                for (int k = 0; k < 3; k++)
                {
                    v += a * ValueNoise(p);
                    p = p * 2.03 + 17.1;
                    a *= 0.5;
                }
                return v;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = IN.uv * 2.0 - 1.0;
                float r = length(p);
                float2 u = p / max(r, 0.0001);
                float aa = abs(atan2(p.x, p.y));

                float prog = saturate(_Progress);
                float front = 1.0 - pow(1.0 - prog, 3.0);
                float behind = front - r;
                float fade = 1.0 - smoothstep(0.7, 1.0, prog);

                float fullCircle = step(3.2, _HalfAngle);
                float fl = Fbm(u * 2.5 + float2(r * 5.0, _Seed * 13.0));
                float coneMask = lerp(1.0 - smoothstep(_HalfAngle - 0.45, _HalfAngle + 0.05, aa + (fl - 0.5) * 0.7), 1.0, fullCircle);

                // ripple at the wave front + a light heat haze behind it
                float band = exp(-pow(behind / _Width, 2.0));
                float haze = step(0.0, behind) * saturate(1.0 - behind / 0.35) * fl * _HazeStrength;

                // push direction: away from the player on the SCREEN
                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                float2 centerUV = IN.centerSP.xy / IN.centerSP.w;
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 d = screenUV - centerUV;
                d.x *= aspect;
                float2 dn = d / max(length(d), 0.0001);

                float2 wob = float2(Fbm(IN.uv * 6.0 + _Time.y * 1.5), Fbm(IN.uv * 6.0 + 7.7 + _Time.y * 1.5)) * 2.0 - 1.0;
                float2 offset = (float2(dn.x / aspect, dn.y) * band + wob * haze * 0.6) * _Strength * fade;

                half3 col = SampleSceneColor(screenUV + offset);

                float inside = 1.0 - smoothstep(0.93, 1.0, r);
                float alpha = saturate((band + haze) * fade * 2.0) * coneMask * inside;
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
