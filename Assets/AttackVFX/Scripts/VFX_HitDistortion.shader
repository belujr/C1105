// Small, local screen distortion at the hit point: an expanding shockwave ring plus a heat-haze wobble in the middle.
// It bends the picture by re-sampling the screen (URP "Opaque Texture") with an offset.
//
// REQUIRED: in your URP Asset turn ON "Opaque Texture" (Project Settings > Graphics > the URP asset > Rendering).
// If you also use a separate preview camera, its "Opaque Texture" must be on or use the pipeline setting.
//
// Runtime values (_Progress, _Strength, _Seed) are set by HitFeedbackManager.
Shader "VFX/HitDistortion"
{
    Properties
    {
        _NoiseTex ("Noise (tileable, Wrap Mode = Repeat)", 2D) = "gray" {}
        _NoiseScale ("Haze Noise Scale", Float) = 2
        _RingWidth ("Ring Width", Range(0.03, 0.4)) = 0.14
        _RingStrength ("Ring Strength", Float) = 1
        _HazeStrength ("Haze Strength", Float) = 0.6

        [HideInInspector] _Progress ("Progress", Float) = 0
        [HideInInspector] _Strength ("Strength (screen UV units)", Float) = 0.03
        [HideInInspector] _Seed ("Seed", Float) = 0
        [HideInInspector] _Debug ("Debug tint (set by script)", Float) = 0
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
            Name "HitDistortion"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST;
                float _NoiseScale;
                float _RingWidth;
                float _RingStrength;
                float _HazeStrength;
                float _Progress;
                float _Strength;
                float _Seed;
                float _Debug;
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
                float2 uv = IN.uv * 2.0 - 1.0;
                float r = length(uv);
                float p = saturate(_Progress);

                float decay = (1.0 - p) * (1.0 - p);

                // shockwave ring: expands fast, then slows
                float ringPos = lerp(0.1, 0.85, 1.0 - pow(1.0 - p, 2.0));
                float ring = exp(-pow((r - ringPos) / _RingWidth, 2.0));
                float2 dirOut = r > 0.001 ? uv / r : float2(0.0, 0.0);

                // heat haze: wobbling noise that rises and is strongest in the centre
                float2 nUV = IN.uv * _NoiseScale + float2(_Seed * 3.1, -p * 2.5);
                float nx = Noise(nUV) * 2.0 - 1.0;
                float ny = Noise(nUV + float2(0.37, 0.71)) * 2.0 - 1.0;
                float inner = saturate(1.0 - r);
                inner *= inner;

                float2 offset = (dirOut * ring * _RingStrength + float2(nx, ny) * inner * _HazeStrength) * _Strength * decay;

                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                half3 col = SampleSceneColor(screenUV + offset);

                // soft round mask so the edge of the quad is never visible
                float mask = 1.0 - smoothstep(0.75, 1.0, r);

                // debug: tints the whole distortion area magenta, so you can see where it is even if nothing bends
                col = lerp(col, half3(1.0, 0.0, 1.0), _Debug * 0.35 * mask);

                return half4(col, mask);
            }
            ENDHLSL
        }
    }
}
