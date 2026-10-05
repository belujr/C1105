// URP unlit fire wall (noise version). Put on a Quad (Scale X = wall length, Scale Y = flame height).
// Double sided, no textures needed (noise is procedural).
// Same look and same property names as before. New: Solidity (black colours), Edge Shorten, and the Reveal sweep (driven by BeaconWallBarrier).
// Solidity 0 = identical to the old pure-additive look (black is invisible). Raise it to make black / dark colours show.
Shader "Custom/FireWall"
{
    Properties
    {
        [Header(Colors)]
        [HDR] _ColorCore ("Core Color (hottest, near the floor)", Color) = (3.0, 1.4, 2.0, 1)
        [HDR] _ColorMid ("Flame Color", Color) = (2.2, 0.08, 0.65, 1)
        [HDR] _ColorOuter ("Tip Color", Color) = (0.55, 0.02, 0.4, 1)
        [HDR] _BaseLineColor ("Floor Line Color", Color) = (4.0, 1.1, 2.4, 1)
        _BaseLineIntensity ("Floor Line Intensity", Range(0, 4)) = 1.2
        _BaseLineSharpness ("Floor Line Sharpness (higher = thinner)", Range(2, 60)) = 16

        [Header(Black and Dark Colors)]
        _Solidity ("Solidity (0 = glow only, black invisible | raise it so black shows)", Range(0, 1)) = 0

        [Header(Flame Shape)]
        _NoiseScale ("Noise Scale (cells per world unit)", Float) = 1.6
        _FlameStretch ("Flame Stretch (higher = taller tongues)", Range(1, 6)) = 2.6
        _Speed ("Rise Speed", Float) = 0.9
        _PanSpeed ("Sideways Drift (0 = none)", Float) = 0.3
        _Turbulence ("Turbulence (ragged tips)", Range(0, 3)) = 1.5
        _Cutoff ("Flame Cutoff (higher = shorter flames)", Range(0, 1)) = 0.35
        _Softness ("Edge Softness", Range(0.01, 1)) = 0.3
        _Intensity ("Overall Intensity", Range(0, 5)) = 1.2

        [Header(Flicker)]
        _Flicker ("Flicker Amount", Range(0, 1)) = 0.25
        _FlickerSpeed ("Flicker Speed", Float) = 6

        [Header(Wall Ends)]
        _EdgeFade ("Fade Distance At Wall Ends (world units)", Float) = 0.6
        _EdgeShorten ("Also Shorten Flames Toward Ends (0 = off)", Range(0, 1)) = 0

        [Header(Appear and Disappear  driven by script)]
        _Reveal ("Reveal (1 = fully shown)", Range(0, 1)) = 1
        _RevealCenter ("Reveal Origin (0 = left end, 0.5 = middle, 1 = right end)", Range(0, 1)) = 0.5
        _RevealInvert ("Reveal Inverted (0 = grows from origin, 1 = hole opens at origin)", Range(0, 1)) = 0
        _RevealSoftness ("Reveal Front Width", Range(0.05, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "FireWall"
            Tags { "LightMode"="UniversalForward" }

            // Premultiplied alpha. With alpha = 0 this is exactly the same as the old additive "Blend One One".
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorCore;
                float4 _ColorMid;
                float4 _ColorOuter;
                float4 _BaseLineColor;
                float _BaseLineIntensity;
                float _BaseLineSharpness;
                float _Solidity;
                float _NoiseScale;
                float _FlameStretch;
                float _Speed;
                float _PanSpeed;
                float _Turbulence;
                float _Cutoff;
                float _Softness;
                float _Intensity;
                float _Flicker;
                float _FlickerSpeed;
                float _EdgeFade;
                float _EdgeShorten;
                float _Reveal;
                float _RevealCenter;
                float _RevealInvert;
                float _RevealSoftness;
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
                float2 wall : TEXCOORD1;   // x = distance along the wall (world units), y = height above the bottom (world units)
                float2 size : TEXCOORD2;   // wall width / height in world units
            };

            // ---------- procedural noise ----------
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
                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float v = 0.0;
                float amp = 0.5;
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    v += amp * ValueNoise(p);
                    p = p * 2.03 + 17.7;
                    amp *= 0.5;
                }
                return v;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.uv = IN.uv;

                // Wall size in world units, read from the object's scale (works for any wall length)
                float4x4 m = GetObjectToWorldMatrix();
                float3 axisX = float3(m._m00, m._m10, m._m20);
                float3 axisY = float3(m._m01, m._m11, m._m21);
                float sizeX = max(length(axisX), 0.0001);
                float sizeY = max(length(axisY), 0.0001);

                OUT.size = float2(sizeX, sizeY);
                // Distance along the wall in WORLD space, so wall pieces in a line share one continuous pattern
                OUT.wall = float2(dot(posWS, axisX / sizeX), IN.uv.y * sizeY);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float u = IN.wall.x;
                float h = IN.wall.y;

                // ---- appear / disappear sweep along the wall (1 everywhere when Reveal = 1) ----
                float origin = _RevealCenter;
                float dOrigin = abs(IN.uv.x - origin) / max(max(origin, 1.0 - origin), 0.0001);   // 0 at the origin, 1 at the far end
                float soft = max(_RevealSoftness, 0.01);
                float p = saturate(_Reveal);
                float reveal = _RevealInvert < 0.5
                    ? saturate((p * (1.0 + soft) - dOrigin) / soft)                        // grows outward from the origin
                    : saturate((dOrigin - (1.0 - p) * (1.0 + soft) + soft) / soft);        // a hole opens at the origin
                float sweep = smoothstep(0.0, 1.0, reveal);

                // ---- fade at the wall ends (same linear fade as before) ----
                float edge = saturate(min(IN.uv.x, 1.0 - IN.uv.x) * IN.size.x / max(_EdgeFade, 0.0001));
                // optional: flames also get shorter toward the ends (0 = off, keeps the old look)
                float shorten = lerp(1.0, smoothstep(0.0, 1.0, edge), _EdgeShorten);

                float grow = sweep * shorten;
                float fade = smoothstep(0.0, 0.25, grow);

                // 0 at the floor, 1 at the top of the flames. Small values squash the flames toward the floor.
                float hn = IN.uv.y / max(grow, 0.05);
                float taper = saturate(1.0 - hn);

                // Noise scrolls downward in noise space, so the flames appear to rise
                float2 np = float2(u + _Time.y * _PanSpeed, (h - _Time.y * _Speed) / _FlameStretch) * _NoiseScale;
                float n = Fbm(np);

                // Noise pushes each column's flame tip up or down -> tongues of fire
                float fire = taper + (n - 0.5) * _Turbulence * (1.0 - 0.6 * taper);
                float mask = smoothstep(_Cutoff, _Cutoff + _Softness, fire);
                mask *= saturate((1.0 - hn) * 5.0);               // soft fade at the very top, no hard cut

                // Colour ramp: tips -> flame -> hot core near the floor
                float heat = saturate(taper + (n - 0.5) * 0.6);
                float3 col = lerp(_ColorOuter.rgb, _ColorMid.rgb, smoothstep(0.0, 0.55, heat));
                col = lerp(col, _ColorCore.rgb, smoothstep(0.6, 1.0, heat));

                // Thin bright line where the wall meets the floor
                float baseLine = exp(-h * _BaseLineSharpness);

                float flick = 1.0 + _Flicker * (ValueNoise(float2(_Time.y * _FlickerSpeed, u * 0.35)) * 2.0 - 1.0);

                float3 rgb = col * mask * _Intensity + _BaseLineColor.rgb * baseLine * _BaseLineIntensity;
                rgb *= flick * edge * fade;

                // alpha > 0 lets black / dark colours darken what is behind the fire instead of vanishing
                float cover = saturate(mask + baseLine) * edge * fade * _Solidity;

                return float4(rgb, cover);
            }
            ENDHLSL
        }
    }
}
