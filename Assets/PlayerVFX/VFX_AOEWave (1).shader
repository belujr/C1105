// Light fire shockwave on the ground for the player's AOE attack (URP, additive, flat quad centred on the player).
// Designed NOT to hide the grass-cutting effect:
//   - the wave front is a thin, BROKEN line (gaps), not a solid band
//   - flame tongues are sparse thin streaks trailing behind it
//   - nothing is filled in: you always see the ground and grass between the streaks
//   - the cone edge is ragged and very soft, so no hard cone outline is ever visible
//   - after the wave: only faint thin glowing cracks that fade
// No textures needed (procedural noise). Runtime values are set by AOEShockwaveVFX.
Shader "VFX/AOEWave"
{
    Properties
    {
        [HDR] _ColorHot ("Hot Color", Color) = (3.2, 2.4, 1.2, 1)
        [HDR] _ColorEdge ("Edge Color", Color) = (2.4, 0.55, 0.12, 1)
        _CrackScale ("Crack Scale (per world unit)", Float) = 1.6
        _CrackSharpness ("Crack Sharpness (higher = thinner)", Float) = 12
        _FlameScale ("Flame Detail", Float) = 1

        [HideInInspector] _Progress ("Wave Progress (0 = no wave yet)", Float) = 0
        [HideInInspector] _GlowFade ("Afterglow", Float) = 1
        [HideInInspector] _HalfAngle ("Half Cone Angle (radians, > 3.2 = full circle)", Float) = 1
        [HideInInspector] _Seed ("Seed", Float) = 0
        [HideInInspector] _Intensity ("Intensity", Float) = 1
        [HideInInspector] _Radius ("Radius (world units)", Float) = 5
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
            Name "AOEWave"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -2, -2

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorHot;
                half4 _ColorEdge;
                float _CrackScale;
                float _CrackSharpness;
                float _FlameScale;
                float _Progress;
                float _GlowFade;
                float _HalfAngle;
                float _Seed;
                float _Intensity;
                float _Radius;
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
                for (int k = 0; k < 4; k++)
                {
                    v += a * ValueNoise(p);
                    p = p * 2.03 + 17.1;
                    a *= 0.5;
                }
                return v;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = IN.uv * 2.0 - 1.0;          // +Y = the player's forward direction
                float r = length(p);
                float2 u = p / max(r, 0.0001);
                float ang = atan2(p.x, p.y);           // 0 = straight ahead
                float aa = abs(ang);

                float prog = saturate(_Progress);
                float front = _Progress > 0.0 ? 1.0 - pow(1.0 - prog, 3.0) : -1.0;   // fast start, slow end
                float behind = front - r;               // > 0 means the wave has already passed this point

                float fullCircle = step(3.2, _HalfAngle);

                // noise used for flames and for roughening the cone edge (periodic around the circle: no seam)
                float fl = Fbm(u * (2.5 * _FlameScale) + float2(r * 7.0 - _Time.y * 4.0, _Seed * 13.0));

                // very soft, ragged cone edge: the cone shape is never visible as a clean outline
                float coneMask = lerp(1.0 - smoothstep(_HalfAngle - 0.45, _HalfAngle + 0.05, aa + (fl - 0.5) * 0.7), 1.0, fullCircle);

                // ---- thin BROKEN wave front (gaps where the noise is low)
                float gaps = smoothstep(0.30, 0.65, Fbm(u * 3.0 + float2(_Seed * 9.0, r * 2.0 - _Time.y * 1.5)));
                float lead = exp(-pow(behind / 0.022, 2.0)) * (0.3 + 0.7 * gaps);

                // ---- sparse thin flame streaks trailing behind the front (stretched along the direction of travel)
                float st = Fbm(u * 7.0 * _FlameScale + float2(r * 1.2 - _Time.y * 2.5, _Seed * 7.0));
                float streak = smoothstep(0.55, 0.82, st);
                float tongue = streak * saturate(1.0 - behind / (0.05 + 0.22 * fl)) * step(0.0, behind);

                float waveFade = 1.0 - smoothstep(0.7, 1.0, prog);
                float flame = (tongue * 0.9 + lead * 1.2) * waveFade;

                // ---- faint thin cracks revealed behind the wave, glowing like embers
                float2 wp = p * _Radius;                // world-unit position, so cracks keep a constant size
                float cn = Fbm(wp * _CrackScale + _Seed * 5.0);
                float crack = pow(saturate(1.0 - abs(cn * 2.0 - 1.0)), _CrackSharpness);
                float passed = step(0.0, behind) * smoothstep(0.0, 0.12, behind);
                float flick = 0.75 + 0.25 * sin(_Time.y * 9.0 + cn * 20.0);
                float glow = crack * 1.1 * passed * _GlowFade * flick;

                float inside = 1.0 - smoothstep(0.93, 1.0, r);
                float g = (flame + glow) * coneMask * inside * _Intensity;

                float heat = saturate(lead * 0.9 + tongue * 1.2);
                half3 col = lerp(_ColorEdge.rgb, _ColorHot.rgb, heat) * g;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
