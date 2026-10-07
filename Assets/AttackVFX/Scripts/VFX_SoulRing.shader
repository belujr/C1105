// A thin ring of energy that CONTRACTS into the player's body when a soul is absorbed (URP, additive).
// It is drawn on a flat ring mesh (made by PlayerSoulAbsorbFX): U goes around, V goes from the inner to the outer edge.
// The ring is broken into bright arcs that spin, so it reads as energy being pulled in, not as a solid shield.
// Parts of the ring that pass behind the body are hidden by depth, and when it shrinks into the body it disappears inside.
Shader "VFX/SoulRing"
{
    Properties
    {
        _NoiseTex ("Noise (tileable, Wrap Mode = Repeat)", 2D) = "gray" {}
        _Arcs ("Arcs Around The Ring (whole number)", Float) = 4
        _GapSize ("Gap Size (higher = more broken)", Range(0, 0.9)) = 0.45
        _Sharp ("Streak Sharpness", Float) = 4

        [HideInInspector] [HDR] _ColorHot ("Hot Color", Color) = (2.5, 1.8, 0.6, 1)
        [HideInInspector] [HDR] _ColorEdge ("Edge Color", Color) = (1.6, 0.5, 0.1, 1)
        [HideInInspector] _Intensity ("Intensity", Float) = 0
        [HideInInspector] _Rotate ("Rotate", Float) = 0
        [HideInInspector] _Seed ("Seed", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test", Float) = 4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+60"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SoulRing"
            Blend One One
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
                float _Arcs;
                float _GapSize;
                float _Sharp;
                half4 _ColorHot;
                half4 _ColorEdge;
                float _Intensity;
                float _Rotate;
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
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            float Noise(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv).r;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float u = IN.uv.x;   // around the ring
                float v = IN.uv.y;   // inner edge 0 .. outer edge 1

                // soft bright line along the middle of the ring's width
                float across = 1.0 - abs(v * 2.0 - 1.0);
                across = pow(saturate(across), 1.6);

                // broken, spinning arcs (whole numbers keep it seamless where the ring closes)
                float arcs = max(1.0, round(_Arcs));
                float n = Noise(float2(u * arcs + _Rotate, v * 0.5 + _Seed));
                float arc = smoothstep(_GapSize, _GapSize + 0.25, n);

                // thin fast streaks inside the arcs, spinning the other way
                float n2 = Noise(float2(u * arcs * 2.0 - _Rotate * 1.4 + 0.37, v * 2.0 + _Seed * 1.7));
                float streak = pow(saturate(1.0 - abs(n2 * 2.0 - 1.0)), _Sharp);

                float g = across * saturate(arc * 0.8 + streak * 0.9);

                half3 col = lerp(_ColorEdge.rgb, _ColorHot.rgb, saturate(g * 1.4));
                col *= g * _Intensity;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
