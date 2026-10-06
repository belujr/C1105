// Painted ink / brush-stroke shader for SmoothSwingRibbon (URP, unlit, transparent). Version 2.
//
// The SHADER now shapes the stroke (pointed head, tapering body), so the mesh can stay wide.
// The extra width is used by "strands": thin lines beside the body that start behind the head and
// disappear LAST, different on every swing.
//
// Mesh data it reads (written by SmoothSwingRibbon):
//   UV0.x = age along the ribbon  (0 = at the limb / head, 1 = oldest part / tail)
//   UV0.y = across the ribbon     (0..1)
//   UV1.x = stable distance along the path (noise stays glued to each part of the trail)
//   UV1.y = limb speed when that part was made (not used here, the mesh width already uses it)
//   Vertex color = tint and opacity from the script
Shader "VFX/InkRibbon"
{
    Properties
    {
        _NoiseTex ("Noise (tileable, Wrap Mode = Repeat)", 2D) = "gray" {}
        [HDR] _ColorEdge ("Body Color (ink)", Color) = (0.12, 0.0, 0.03, 1)
        [HDR] _ColorCore ("Hot Core Color", Color) = (4, 0.7, 0.5, 1)
        [HDR] _ColorStrand ("Strand Color", Color) = (3, 0.25, 0.25, 1)
        _Intensity ("Intensity", Float) = 1

        [Header(Custom stroke texture)]
        _ShapeTex ("Stroke Shape (R = body, G = lingering streaks. Left = head, right = tail)", 2D) = "white" {}
        _ShapeAmount ("Texture Drives The Body (0 = off)", Range(0, 1)) = 0
        _StreakAmount ("Texture Streaks (0 = off)", Range(0, 2)) = 0

        [Header(Body shape)]
        _BodyWidth ("Body Width (fraction of the ribbon)", Range(0.2, 1)) = 0.55
        _HeadLength ("Head Length (world units, the pointed front tip)", Range(0.05, 2)) = 0.6
        _TailLength ("Tail Length (world units, the pointed back end)", Range(0.05, 2)) = 0.6
        _HeadJag ("Head Jaggedness", Range(0, 1)) = 0.5
        _Cut ("Cut (higher = thinner, bolder shapes)", Range(0.05, 0.9)) = 0.35
        _Softness ("Softness (edge crispness)", Range(0.01, 0.3)) = 0.08

        [Header(Brush texture)]
        _BristleScale ("Bristle Density (across the stroke)", Float) = 3
        _AlongScale ("Noise Length (along the stroke, smaller = longer streaks)", Float) = 0.35
        _EdgeTear ("Edge Tear", Range(0, 1)) = 0.5

        [Header(Tail breakup)]
        _TailPower ("Tail Erosion Curve (higher = tail breaks later)", Range(0.3, 4)) = 1.2
        _ErodeStrength ("Erosion Strength", Range(0, 2.5)) = 1.3

        [Header(Strands that outlive the stroke)]
        _StrandAmount ("Strand Chance (0 = none, 1 = always all 4)", Range(0, 1)) = 0.65
        _StrandLife ("Strand Life (1 = lives until the very end)", Range(0.4, 1)) = 1

        [Header(Impact)]
        _FlashBoost ("Hit Flash Brightness", Float) = 3
        _Flash ("Flash (set by script)", Range(0, 1)) = 0

        _Seed ("Seed (set by script)", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test", Float) = 8
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+200"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "InkRibbon"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);
            TEXTURE2D(_ShapeTex);
            SAMPLER(sampler_ShapeTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST;
                float4 _ShapeTex_ST;
                float _ShapeAmount;
                float _StreakAmount;
                half4 _ColorEdge;
                half4 _ColorCore;
                half4 _ColorStrand;
                float _Intensity;
                float _BodyWidth;
                float _HeadLength;
                float _TailLength;
                float _HeadJag;
                float _Cut;
                float _Softness;
                float _BristleScale;
                float _AlongScale;
                float _EdgeTear;
                float _TailPower;
                float _ErodeStrength;
                float _StrandAmount;
                float _StrandLife;
                float _FlashBoost;
                float _Flash;
                float _Seed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float2 uv3 : TEXCOORD2;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float2 uv3 : TEXCOORD2;
                float4 color : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.uv2 = IN.uv2;
                OUT.uv3 = IN.uv3;
                OUT.color = IN.color;
                return OUT;
            }

            float Noise(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv).r;
            }

            // random number 0..1 that is different for every swing (through _Seed)
            float Hash(float x)
            {
                return frac(sin(x * 12.9898 + _Seed * 78.233) * 43758.5453);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float age = saturate(IN.uv.x);      // 0 head .. 1 tail
                float v = IN.uv.y;
                float s = IN.uv2.x;                 // stable distance along the path
                float dRaw = abs(v * 2.0 - 1.0);    // 0 centre .. 1 edge of the whole ribbon

                // distance (world units) from the FRONT end and from the BACK end of the ribbon.
                // The tips are shaped from these, so they stay pointed even after the swing has stopped.
                float frontDist = IN.uv3.x;
                float tailDist = IN.uv3.y;
                float totalLen = frontDist + tailDist;
                float along = (totalLen > 0.001) ? frontDist / totalLen : 0.0;   // 0 front .. 1 back

                // ---------- BODY SHAPE (made here, so the ends are never a straight cut) ----------
                float jag = Noise(float2(s * _AlongScale * 2.5 + _Seed * 1.3, 0.77 + _Seed * 0.1));
                // head: the half-width grows from 0 at the tip, so the front is a point, not a flat edge
                float headT = saturate((frontDist - jag * _HeadJag * 0.15) / max(_HeadLength, 0.01));
                // tail: thins out with age, and the very back end is pointed too
                float tailT = (1.0 - smoothstep(0.30, 1.0, age) * 0.92) * pow(saturate(tailDist / max(_TailLength, 0.01)), 0.8);
                float bodyW = _BodyWidth * headT * tailT;

                float d = dRaw / max(bodyW, 0.001);

                // torn edge: the edge wobbles along the stroke, more toward the tail
                float tear = Noise(float2(s * _AlongScale * 3.0 + _Seed * 1.7, 0.21 + _Seed * 0.13));
                d += (tear - 0.5) * _EdgeTear * (0.35 + age);

                float ink = saturate(1.0 - d * d);

                // dry-brush bristles: long streaks along the stroke, dryer toward the tail
                float bristle = Noise(float2(s * _AlongScale + _Seed * 7.13, v * _BristleScale + _Seed * 3.7));
                float dry = saturate(age * 1.3);
                ink *= lerp(1.0, saturate(bristle * 1.8), dry * 0.85);

                // custom painted stroke: left edge of the texture = head, right edge = tail.
                // Mirrored at random per swing so it never looks identical.
                float flipV = step(0.5, Hash(9.7));
                float4 shp = SAMPLE_TEXTURE2D(_ShapeTex, sampler_ShapeTex, float2(along, lerp(v, 1.0 - v, flipV)));
                ink = lerp(ink, shp.r, _ShapeAmount);

                // erosion: the tail breaks up first, and every part breaks at a different moment
                float erode = pow(age, _TailPower);
                float n2 = Noise(float2(s * _AlongScale * 1.7 + _Seed * 2.9 + 0.5, 0.63));
                ink -= erode * _ErodeStrength * (0.45 + n2 * 0.9);

                // hit flash: the stroke gets fatter for a moment
                ink += _Flash * 0.35 * saturate(headT);

                float alphaBody = smoothstep(_Cut - _Softness, _Cut + _Softness, ink);

                // ---------- STRANDS: thin lines that start behind the head and vanish last ----------
                float strandInk = 0.0;
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    float fi = (float)i;
                    float h0 = Hash(fi * 1.37 + 0.11);
                    float h1 = Hash(fi * 2.71 + 0.53);
                    float h2 = Hash(fi * 3.29 + 0.97);
                    float h3 = Hash(fi * 5.13 + 0.29);

                    float exists = step(1.0 - _StrandAmount, h3);   // some swings get fewer strands
                    float side = (h0 < 0.5) ? -1.0 : 1.0;
                    float c0 = 0.5 + side * lerp(0.14, 0.40, h1);   // where it sits across the ribbon
                    float c = lerp(c0, 0.5 + side * 0.46, age * 0.4); // slowly fans outward as it ages
                    float w = lerp(0.012, 0.03, h2);                 // line thickness
                    float start = lerp(0.04, 0.35, h2);              // starts behind the head
                    float life = lerp(0.60, 1.0, h1) * _StrandLife;  // dies at different times

                    float strandLine = 1.0 - smoothstep(w * 0.5, w, abs(v - c));
                    float fadeIn = smoothstep(start, start + 0.08, age);
                    float fadeOut = 1.0 - smoothstep(life - 0.18, life, age);

                    // break the line into dashes, more and more toward its end
                    float dash = Noise(float2(s * _AlongScale * 1.5 + fi * 3.7 + _Seed * 2.3, fi * 0.31));
                    float dashMask = smoothstep(0.30 + age * 0.25, 0.45 + age * 0.25, dash);

                    strandInk = max(strandInk, strandLine * fadeIn * fadeOut * dashMask * exists);
                }

                // streaks painted in the texture's green channel: the value is the age at which that streak disappears
                float texLife = shp.g;
                float texAlive = step(0.02, texLife) * (1.0 - smoothstep(texLife - 0.12, texLife, age));
                float texDash = smoothstep(0.30 + age * 0.25, 0.45 + age * 0.25,
                                           Noise(float2(s * _AlongScale * 1.5 + _Seed * 2.3, v * 7.0)));
                strandInk = max(strandInk, saturate(texAlive * texDash * _StreakAmount));

                float alpha = max(alphaBody, strandInk) * IN.color.a;

                // ---------- COLOR ----------
                float hot = saturate((1.0 - saturate(d)) * (1.0 - age) * 1.6);
                hot = saturate(hot + (1.0 - smoothstep(0.0, max(_HeadLength * 0.5, 0.05), frontDist)) * 0.9);   // bright white-hot tip

                half3 bodyCol = lerp(_ColorEdge.rgb, _ColorCore.rgb, hot);
                float strandOnly = saturate(strandInk - alphaBody);
                half3 col = lerp(bodyCol, _ColorStrand.rgb, strandOnly);

                col *= _Intensity * IN.color.rgb * (1.0 + _Flash * _FlashBoost);

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
