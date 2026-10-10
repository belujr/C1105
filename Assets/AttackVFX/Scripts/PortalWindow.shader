Shader "VFX/PortalWindow"
{
    // Fire portal.
    //  - The middle is a glassy view into another world.
    //      No Center Texture: the real scene behind the portal, bent like a crystal ball
    //                         (needs "Opaque Texture" ticked on the URP Asset).
    //      With a Center Texture: your picture, shifted with the viewing angle so it feels like it sits behind the ring.
    //  - The rim is a living fire: flame tongues of different lengths, glowing cracks, a molten lip
    //    at the very edge, and a pulse that runs around the ring.
    Properties
    {
        [NoScaleOffset] _CenterTex ("Center Texture (optional)", 2D) = "black" {}
        [NoScaleOffset] _RimTex ("Rim Pattern (optional, tileable, grayscale)", 2D) = "white" {}
        _UseTexture ("Use Center Texture (set by script)", Float) = 0
        _UseRimTex ("Use Rim Texture (set by script)", Float) = 0
        _RimTexTiling ("Rim Texture Tiling (whole number)", Float) = 6
        [HDR] _CenterTint ("Center Tint", Color) = (1, 1, 1, 1)
        _TexParams ("Texture Tiling XY, Scroll XY", Vector) = (1, 1, 0, 0)
        _Parallax ("Parallax", Range(-0.5, 0.5)) = 0.08
        _LensStrength ("Lens (magnify the middle)", Range(0, 0.6)) = 0.3
        _Chroma ("Chromatic Split", Range(0, 0.05)) = 0.015
        _Warp ("Heat Warp", Range(0, 0.05)) = 0.012
        _Vignette ("Edge Darkening", Range(0, 1)) = 0.5
        _CenterOpacity ("Center Opacity", Range(0, 1)) = 1

        [HDR] _FireHot ("Fire Hot", Color) = (2.5, 1.6, 0.5, 1)
        [HDR] _FireMid ("Fire Mid", Color) = (2.2, 0.55, 0.08, 1)
        [HDR] _FireDark ("Fire Dark", Color) = (0.5, 0.06, 0.02, 1)
        _Intensity ("Fire Intensity", Float) = 1.8
        _FireWidth ("Fire Width", Range(0.02, 0.6)) = 0.24
        _Tongues ("Flame Tongues", Range(0, 1)) = 0.6
        _TongueFreq ("Tongue Count", Float) = 4
        _RunPulse ("Running Pulse", Range(0, 1)) = 0.5
        _CrackScale ("Crack Scale", Float) = 4
        _CrackSharp ("Crack Sharpness", Float) = 2.5
        _FireFlicker ("Flicker", Range(0, 1)) = 0.3
        _FireSpeed ("Fire Speed", Float) = 0.6
        _InnerGlow ("Inner Glow (onto the window)", Range(0, 2)) = 0.5
        _OuterGlow ("Outer Glow (onto the stone)", Range(0, 2)) = 0.7

        _DiscRadius ("Disc Fill (UV)", Range(0.5, 1)) = 0.8
        _Softness ("Edge Softness", Range(0.005, 0.3)) = 0.03
        _Surge ("Surge (0-1, set by script)", Range(0, 1)) = 0
        _Fade ("Master Fade", Range(0, 1)) = 1
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
            Name "PortalWindow"
            Tags { "LightMode" = "UniversalForward" }

            // premultiplied: the window replaces what is behind it, the fire and glow add on top
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_CenterTex);
            SAMPLER(sampler_CenterTex);
            TEXTURE2D(_RimTex);
            SAMPLER(sampler_RimTex);

            CBUFFER_START(UnityPerMaterial)
                float _UseTexture;
                float _UseRimTex;
                float _RimTexTiling;
                float4 _CenterTint;
                float4 _TexParams;
                float _Parallax;
                float _LensStrength;
                float _Chroma;
                float _Warp;
                float _Vignette;
                float _CenterOpacity;
                float4 _FireHot;
                float4 _FireMid;
                float4 _FireDark;
                float _Intensity;
                float _FireWidth;
                float _Tongues;
                float _TongueFreq;
                float _RunPulse;
                float _CrackScale;
                float _CrackSharp;
                float _FireFlicker;
                float _FireSpeed;
                float _InnerGlow;
                float _OuterGlow;
                float _DiscRadius;
                float _Softness;
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
                float4 screenPos : TEXCOORD1;
                float2 centerUV : TEXCOORD2;
                float3 viewOS : TEXCOORD3;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);

                // where the middle of the portal is on screen
                float4 cCS = TransformObjectToHClip(float3(0, 0, 0));
                float4 cSP = ComputeScreenPos(cCS);
                OUT.centerUV = cSP.xy / max(abs(cSP.w), 0.0001);

                // direction from this point to the camera, in the portal's own space (for parallax)
                float3 camOS = TransformWorldToObject(_WorldSpaceCameraPos);
                OUT.viewOS = camOS - IN.positionOS.xyz;
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

            half4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y;

                // p: -1..1 across the disc, length 1 at the edge
                float2 p = (IN.uv - 0.5) * 2.0 / max(_DiscRadius, 0.01);
                float r = length(p);
                float ang = atan2(p.y, p.x);
                float edgeDist = 1.0 - r; // > 0 inside the window
                float centerMask = smoothstep(0.0, _Softness, edgeDist);

                // heat shimmer that wobbles whatever is seen through the window, stronger near the fire
                float nearEdge = exp(-max(edgeDist, 0.0) * 4.0);
                float2 wq = p * 2.5;
                float2 warp = (float2(Fbm(wq + float2(0.0, -t * 0.4)),
                                      Fbm(wq + float2(5.2, 1.3 - t * 0.4))) - 0.5) * _Warp * 2.0 * (1.0 + 3.0 * nearEdge);

                // crystal-ball magnification: strongest in the middle, none at the rim
                float lens = _LensStrength * (1.0 - saturate(r * r));

                // --- view 1: the real scene behind the portal, bent by the lens
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float2 toP = screenUV - IN.centerUV;
                float sc = 1.0 - lens;
                float3 scene;
                scene.r = SampleSceneColor(IN.centerUV + toP * (sc * (1.0 + _Chroma)) + warp).r;
                scene.g = SampleSceneColor(IN.centerUV + toP * sc + warp).g;
                scene.b = SampleSceneColor(IN.centerUV + toP * (sc * (1.0 - _Chroma)) + warp).b;

                // --- view 2: your own picture, shifted with the viewing angle
                float2 par = IN.viewOS.xy / max(abs(IN.viewOS.z), 0.2);
                float2 tuv = p * 0.5 + 0.5;
                tuv = (tuv - 0.5) * (1.0 - lens * 0.5) + 0.5;
                tuv = tuv * _TexParams.xy + _TexParams.zw * t + par * _Parallax + warp;
                float3 tex;
                tex.r = SAMPLE_TEXTURE2D(_CenterTex, sampler_CenterTex, (tuv - 0.5) * (1.0 + _Chroma) + 0.5).r;
                tex.g = SAMPLE_TEXTURE2D(_CenterTex, sampler_CenterTex, tuv).g;
                tex.b = SAMPLE_TEXTURE2D(_CenterTex, sampler_CenterTex, (tuv - 0.5) * (1.0 - _Chroma) + 0.5).b;

                float3 center = lerp(scene, tex, _UseTexture) * _CenterTint.rgb;
                center *= 1.0 - _Vignette * pow(saturate(r), 2.5);

                // --- fire rim
                float2 fq = Rot(p, t * 0.15) * _CrackScale;
                float n1 = Fbm(fq + float2(0.0, -t * _FireSpeed));
                float n2 = Fbm(fq * 1.9 + float2(t * _FireSpeed * 0.7, 3.7));

                // flame tongues: each part of the ring reaches a different depth, and the lengths keep changing.
                // (noise sampled on a circle, so there is no seam)
                float2 circle = float2(cos(ang), sin(ang));
                float tn = Fbm(circle * _TongueFreq + float2(t * _FireSpeed, -t * _FireSpeed * 0.6));
                float reach = _FireWidth * lerp(1.0, 0.3 + 1.6 * tn, _Tongues);

                float bandEdge = edgeDist + (n1 - 0.5) * 0.18;
                float band = smoothstep(reach, 0.0, bandEdge) * smoothstep(-0.07, 0.0, bandEdge);

                // glowing cracks
                float ridge = pow(saturate(1.0 - abs(2.0 * n2 - 1.0)), _CrackSharp);

                // optional custom pattern around the rim (tileable texture, Wrap Mode = Repeat)
                float2 ruv = float2(ang * 0.15915494 * _RimTexTiling,
                                    saturate(bandEdge / max(_FireWidth, 0.01)) - t * _FireSpeed * 0.1);
                float rimTex = SAMPLE_TEXTURE2D_LOD(_RimTex, sampler_RimTex, ruv, 1.0).r;
                ridge = lerp(ridge, rimTex, _UseRimTex);

                // a thin molten lip right at the edge, and a pulse that travels around the ring
                float lip = smoothstep(0.07, 0.0, abs(bandEdge - 0.02));
                float wave = 0.5 + 0.5 * sin(ang * 3.0 - t * 2.2);
                float runMul = lerp(1.0, 0.55 + 0.9 * wave, _RunPulse);

                float flick = 1.0 + _FireFlicker * (sin(t * 9.0 + n1 * 12.0) * 0.5 + sin(t * 5.3 + n2 * 9.0) * 0.5);

                float heat = saturate((band * (0.35 + ridge * 1.3) + lip * 0.6) * runMul);
                float3 fire = lerp(_FireDark.rgb, _FireMid.rgb, saturate(heat * 1.6));
                fire = lerp(fire, _FireHot.rgb, saturate(heat * heat * 2.0 + ridge * band * 0.6 + lip * 0.5));
                fire *= heat * flick * _Intensity * (1.0 + 2.0 * _Surge);

                // warm light spilling onto the window (inside) and onto the stone (outside)
                float innerG = exp(-max(edgeDist, 0.0) * 5.0) * centerMask;
                float outerG = exp(-max(-edgeDist, 0.0) * 18.0) * (1.0 - centerMask);
                float3 glow = _FireMid.rgb * (innerG * _InnerGlow + outerG * _OuterGlow)
                              * (0.8 + 0.4 * flick) * _Intensity * (1.0 + _Surge);

                float3 rgb = (center * centerMask * _CenterOpacity + fire + glow) * _Fade;
                float alpha = centerMask * _CenterOpacity * _Fade;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
