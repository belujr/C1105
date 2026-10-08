// Stylized, cel-shaded 3D flame (URP, additive). It is drawn on tapered flame-tongue MESHES (made by FireVFX),
// so the fire has real volume from every side instead of being a flat plane.
//
//   - noise scrolls upward and eats the flame, so the tip and edges are ragged and never the same twice
//   - the colour goes from a hot core, through orange, to a dark red tip, in a few hard "cel" bands (ink / anime look)
//   - the middle of the tongue is brighter where it faces the camera, the silhouette is softer
//   - the tip sways and flickers
// All noise is procedural, no textures needed.
//
// Mesh data: UV0.x = around the tongue, UV0.y = 0 at the base .. 1 at the tip. The mesh is 1 unit tall.
Shader "VFX/FireFlame"
{
    Properties
    {
        [HDR] _ColorCore ("Core Color (hottest)", Color) = (1.7, 0.75, 0.15, 1)
        [HDR] _ColorMid ("Mid Color", Color) = (1.3, 0.24, 0.02, 1)
        [HDR] _ColorTip ("Tip Color (coolest)", Color) = (0.38, 0.02, 0.01, 1)
        _Intensity ("Intensity", Float) = 0.6

        [Header(Shape)]
        _NoiseScale ("Noise Scale", Float) = 2.2
        _RiseSpeed ("Rise Speed", Float) = 1.6
        _Erosion ("Erosion (how ragged)", Range(0, 2)) = 1.1
        _Cut ("Cut (higher = thinner flame)", Range(0, 0.9)) = 0.34
        _Softness ("Edge Softness", Range(0.01, 0.5)) = 0.08
        _Sway ("Sway (tip movement)", Float) = 0.12

        [Header(Cel shading)]
        _Bands ("Cel Bands", Range(1, 6)) = 3
        _Cel ("Cel Amount (0 = smooth, 1 = hard bands)", Range(0, 1)) = 0.7

        [HideInInspector] _Seed ("Seed", Float) = 0
        [HideInInspector] _Flicker ("Flicker (set by script)", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test", Float) = 4
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
            Name "FireFlame"
            Blend One One
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorCore;
                half4 _ColorMid;
                half4 _ColorTip;
                float _Intensity;
                float _NoiseScale;
                float _RiseSpeed;
                float _Erosion;
                float _Cut;
                float _Softness;
                float _Sway;
                float _Bands;
                float _Cel;
                float _Seed;
                float _Flicker;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float v = IN.uv.y;
                float3 pos = IN.positionOS.xyz;

                // the tip sways, more and more toward the top
                float t = _Time.y * _RiseSpeed * 0.5 + _Seed * 3.0;
                float swayX = sin(t * 1.7 + v * 3.0 + _Seed * 2.0) + 0.5 * sin(t * 3.1 + v * 5.0);
                float swayZ = cos(t * 1.3 + v * 2.5 + _Seed) + 0.5 * sin(t * 2.7 + v * 4.0);
                pos.x += swayX * _Sway * v * v;
                pos.z += swayZ * _Sway * v * v;

                // flicker makes the whole flame a little taller and shorter
                pos.y *= lerp(1.0, _Flicker, v);

                float3 posWS = TransformObjectToWorld(pos);
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = _WorldSpaceCameraPos - posWS;
                OUT.uv = IN.uv;
                return OUT;
            }

            // ---- procedural 3D value noise ----
            float Hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float VNoise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash13(i);
                float n100 = Hash13(i + float3(1, 0, 0));
                float n010 = Hash13(i + float3(0, 1, 0));
                float n110 = Hash13(i + float3(1, 1, 0));
                float n001 = Hash13(i + float3(0, 0, 1));
                float n101 = Hash13(i + float3(1, 0, 1));
                float n011 = Hash13(i + float3(0, 1, 1));
                float n111 = Hash13(i + float3(1, 1, 1));

                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                            lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            float Fbm(float3 p)
            {
                float a = 0.5;
                float s = 0.0;
                [unroll]
                for (int i = 0; i < 3; i++)
                {
                    s += a * VNoise(p);
                    p = p * 2.03 + 17.1;
                    a *= 0.5;
                }
                return saturate(s * 1.14);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float v = saturate(IN.uv.y);

                // noise that rises through the flame
                float3 np = float3(IN.positionOS.x, IN.positionOS.y * 0.6 - _Time.y * _RiseSpeed, IN.positionOS.z) * _NoiseScale;
                np += _Seed * 7.3;
                float n = Fbm(np);

                // brighter where the surface faces the camera, softer silhouette
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(IN.viewDirWS);
                float rim = pow(saturate(abs(dot(N, V))), 0.7);

                // "heat": high at the base and in the middle, eaten by noise toward the tip
                float e = (1.0 - v) * lerp(0.55, 1.0, rim) + (n - 0.5) * _Erosion * (0.25 + v);
                float mask = smoothstep(_Cut, _Cut + _Softness, e);

                float heat = saturate((e - _Cut) / max(1.0 - _Cut, 0.01));

                // hard colour bands for the painted / anime look
                float steps = max(_Bands, 1.0);
                float banded = saturate(floor(heat * steps) / max(steps - 1.0, 1.0));
                heat = lerp(heat, banded, _Cel);

                half3 col = lerp(_ColorTip.rgb, _ColorMid.rgb, smoothstep(0.1, 0.5, heat));
                col = lerp(col, _ColorCore.rgb, smoothstep(0.72, 0.97, heat));

                col *= mask * _Intensity * _Flicker;
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
