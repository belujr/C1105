// Painted ink / brush-stroke shader for SmoothSwingRibbon (URP, unlit, transparent).
//
// Mesh data it reads (written by SmoothSwingRibbon):
//   UV0.x = age along the ribbon  (0 = at the limb / head, 1 = oldest part / tail)
//   UV0.y = across the ribbon     (0..1)
//   UV1.x = stable distance along the path, so noise stays glued to each part of the trail
//   UV1.y = how fast the limb was moving when that part was made (0 slow .. 1 fast)
//   Vertex color = tint and opacity from the script
Shader "VFX/InkRibbon"
{
    Properties
    {
        _NoiseTex ("Noise (tileable, Wrap Mode = Repeat)", 2D) = "gray" {}
        [HDR] _ColorEdge ("Edge Color (ink body)", Color) = (0.12, 0.0, 0.03, 1)
        [HDR] _ColorCore ("Core Color (hot centre)", Color) = (4, 0.7, 0.5, 1)
        _Intensity ("Intensity", Float) = 1
        _Cut ("Cut (higher = thinner, bolder shapes)", Range(0.05, 0.9)) = 0.35
        _Softness ("Softness (edge crispness)", Range(0.01, 0.3)) = 0.08
        _BristleScale ("Bristle Density (across the stroke)", Float) = 3
        _AlongScale ("Noise Length (along the stroke, smaller = longer streaks)", Float) = 0.35
        _EdgeTear ("Edge Tear", Range(0, 1)) = 0.5
        _TailPower ("Tail Erosion Curve (higher = tail breaks later)", Range(0.3, 4)) = 1.2
        _ErodeStrength ("Erosion Strength", Range(0, 2.5)) = 1.3
        _Seed ("Seed (set by script)", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test", Float) = 8
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

            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST;
                half4 _ColorEdge;
                half4 _ColorCore;
                float _Intensity;
                float _Cut;
                float _Softness;
                float _BristleScale;
                float _AlongScale;
                float _EdgeTear;
                float _TailPower;
                float _ErodeStrength;
                float _Seed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float4 color : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.uv2 = IN.uv2;
                OUT.color = IN.color;
                return OUT;
            }

            float Noise(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv).r;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float age = saturate(IN.uv.x);     // 0 head .. 1 tail
                float v = IN.uv.y;
                float s = IN.uv2.x;                // stable distance along the path
                float d = abs(v * 2.0 - 1.0);      // 0 centre .. 1 edge

                // torn edge: the edge position wobbles along the stroke, more toward the tail
                float tear = Noise(float2(s * _AlongScale * 3.0 + _Seed * 1.7, 0.21 + _Seed * 0.13));
                d = saturate(d + (tear - 0.5) * _EdgeTear * (0.35 + age));

                // soft stroke body: thick in the middle, thin at the edges
                float ink = saturate(1.0 - d * d);

                // dry-brush bristles: long streaks along the stroke, dryer toward the tail
                float bristle = Noise(float2(s * _AlongScale + _Seed * 7.13, v * _BristleScale + _Seed * 3.7));
                float dry = saturate(age * 1.3);
                ink *= lerp(1.0, saturate(bristle * 1.8), dry * 0.85);

                // erosion: the tail breaks up first, and every part of the trail breaks at a different moment
                float erode = pow(age, _TailPower);
                float n2 = Noise(float2(s * _AlongScale * 1.7 + _Seed * 2.9 + 0.5, 0.63));
                ink -= erode * _ErodeStrength * (0.45 + n2 * 0.9);

                float alpha = smoothstep(_Cut - _Softness, _Cut + _Softness, ink) * IN.color.a;

                // hot core: bright in the middle of the young part of the stroke
                float hot = saturate((1.0 - d) * (1.0 - age) * 1.6);
                half3 col = lerp(_ColorEdge.rgb, _ColorCore.rgb, hot) * _Intensity * IN.color.rgb;

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
