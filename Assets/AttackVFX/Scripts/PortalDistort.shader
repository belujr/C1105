Shader "VFX/PortalDistort"
{
    // Bends the picture behind it toward the portal centre.
    // Needs "Opaque Texture" ticked on the URP Asset.
    //  _Mode 0 = soft blob (used on particles that ride along the rays)
    //  _Mode 1 = halo around the portal (inward ripples + opening shockwave)
    Properties
    {
        _Mode ("Mode (0 blob, 1 halo)", Float) = 0
        _Strength ("Pull Strength (screen UV)", Float) = 0.02
        _Falloff ("Blob Falloff", Range(0.5, 6)) = 2
        _RippleFreq ("Ripple Frequency", Float) = 14
        _RippleSpeed ("Ripple Speed", Float) = 2.5
        _Shock ("Shockwave Radius (>1 = off)", Float) = 2
        _ShockStrength ("Shockwave Strength", Float) = 0.03
        _Fade ("Master Fade", Range(0, 1)) = 1
        _CenterWS ("Portal Centre (world)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ZTest ("ZTest (4 = normal, 8 = always)", Float) = 4
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
            Name "PortalDistort"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Mode;
                float _Strength;
                float _Falloff;
                float _RippleFreq;
                float _RippleSpeed;
                float _Shock;
                float _ShockStrength;
                float _Fade;
                float4 _CenterWS;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float2 centerUV : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;

                // where the portal centre is on screen (0..1)
                float4 centerCS = TransformWorldToHClip(_CenterWS.xyz);
                float4 sp = ComputeScreenPos(centerCS);
                OUT.centerUV = sp.xy / max(abs(sp.w), 0.0001);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 d = (IN.uv - 0.5) * 2.0;
                float r = length(d);
                float t = _Time.y;

                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                float2 toCenter = IN.centerUV - screenUV;
                float2 dirN = toCenter / max(length(toCenter), 0.0001);
                float aspect = _ScreenParams.y / _ScreenParams.x; // keeps the pull round on wide screens

                float amount;
                float alpha;

                if (_Mode > 0.5)
                {
                    // halo: ripples that travel inward, plus a ring that pushes outward when the portal opens
                    float body = smoothstep(1.0, 0.15, r);
                    float ripple = 0.5 + 0.5 * sin(r * _RippleFreq + t * _RippleSpeed);
                    float shock = exp(-pow((r - _Shock) / 0.1, 2.0)) * (1.0 - saturate(_Shock)) * step(_Shock, 1.0);
                    amount = body * (0.35 + 0.65 * ripple) * _Strength - shock * _ShockStrength;
                    alpha = smoothstep(1.0, 0.9, r);
                }
                else
                {
                    float mask = pow(saturate(1.0 - r), _Falloff) * IN.color.a;
                    amount = mask * _Strength;
                    alpha = saturate(mask * 3.0);
                }

                amount *= _Fade;
                alpha *= _Fade;

                // sample from slightly further out, so the world looks like it is slipping toward the portal
                float2 sampleUV = saturate(screenUV - dirN * float2(aspect, 1.0) * amount);
                half3 col = SampleSceneColor(sampleUV);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
