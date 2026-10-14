// Procedural particle shader for the player hit effect (URP). No textures needed.
//   _Softness : 0.05 = hard-edged droplet, 1 = soft glow
//   _Additive : 1 = pure additive light (sparks), 0 = normal alpha blending (blood, mist)
// Uses a fixed premultiplied blend state, so it does not depend on any blend property.
Shader "VFX/PlayerParticle"
{
    Properties
    {
        _Intensity ("Intensity", Float) = 1
        _Softness ("Edge Softness (0.05 = hard droplet, 1 = soft glow)", Range(0.02, 1)) = 0.15
        _Additive ("Additive (1 = glow, 0 = normal alpha)", Range(0, 1)) = 0
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
            Name "PlayerParticle"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _Softness;
                float _Additive;
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
                float r = length(d);

                float a = saturate((1.0 - r) / max(_Softness, 0.001));
                a = a * a * (3.0 - 2.0 * a);

                float alpha = IN.color.a * a;
                // premultiplied: the alpha channel is reduced by _Additive, so 1 = pure additive
                return half4(IN.color.rgb * _Intensity * alpha, alpha * (1.0 - _Additive));
            }
            ENDHLSL
        }
    }
}
